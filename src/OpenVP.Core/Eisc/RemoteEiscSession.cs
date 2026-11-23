using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenVP.Core.Eisc;

/// <summary>
/// One live remote-EISC link: a WebSocket between an EISC on this OpenVP and an EISC on another one. Both ends run the same code
/// (the outbound side connects, the inbound side accepts), so joins flow both ways the way they do between two slots on the
/// <see cref="IntersystemBus"/>: whatever this program sets on the EISC's feedback appears as an input on the far EISC.
///
/// Wire format: JSON text frames. <c>hello</c> (instance id, version), <c>sync</c> (every feedback join, sent right after hello so a
/// reconnect brings both sides up to date), <c>j</c> (one join: k = d|a|s, n = join number, v = value), <c>ping</c>/<c>pong</c>.
/// Loops: a value that arrives is applied as an input and is never sent back by the link itself. Two programs that bounce a
/// serial between them are cut by an echo rule (a serial equal to the one that just arrived is not returned for a moment), and
/// any join that changes faster than <see cref="MaxJoinsPerSecond"/> is paused for a few seconds with a warning.
/// </summary>
public sealed class RemoteEiscSession : IDisposable
{
    public const string Transport = "Remote EISC";
    public const int Version = 1;
    public static int MaxJoinsPerSecond = 200;
    public static TimeSpan EchoWindow = TimeSpan.FromMilliseconds(150);
    public static TimeSpan PauseFor = TimeSpan.FromSeconds(3);
    public static TimeSpan PingEvery = TimeSpan.FromSeconds(5);
    private const int MaxMessage = 1 << 20;
    private const int MaxQueue = 50_000;

    private readonly WebSocket _ws;
    private readonly JoinDevice _device;
    private readonly string _instance, _appVersion;
    private readonly Action<string, string>? _log;
    private readonly Channel<string> _out = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<bool> _hello = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<JoinKey, (long Tick, string Value)> _received = new();
    private readonly Dictionary<(JoinKey, bool), Rate> _rates = new();
    private readonly object _gate = new();
    private int _queued;
    private int _disposed;
    private CancellationTokenSource? _cts;
    private long _pings;

    private sealed class Rate { public long WindowStart; public int Count; public long BlockedUntil; public bool Warned; }

    /// <param name="log">Called with a level and a message; the owner puts it in the slot's log.</param>
    public RemoteEiscSession(WebSocket ws, JoinDevice device, string localInstance, string appVersion, string peerLabel, string remoteAddress, int remotePort, Action<string, string>? log = null)
    {
        _ws = ws; _device = device; _instance = localInstance; _appVersion = appVersion; _log = log;
        Connection = new DeviceConnection(Transport, remoteAddress, remotePort)
        {
            Peer = peerLabel,
            ClientKind = "Remote EISC link",
            ProbeStatus = "connecting",
            QueueDepth = () => Volatile.Read(ref _queued)
        };
    }

    public DeviceConnection Connection { get; }
    public JoinDevice Device => _device;
    /// <summary>Completes with true when the other end's hello arrives, false if the link ends first.</summary>
    public Task<bool> HelloReceived => _hello.Task;
    public string? PeerInstance { get; private set; }
    public string? PeerVersion { get; private set; }
    /// <summary>Why the session ended, when it ended because of something to tell the person (a link to itself, an overloaded queue).</summary>
    public string? EndReason { get; private set; }
    public DateTimeOffset? ConnectedSince { get; private set; }
    public double LatencyMs => Connection.LatencyMs;

    /// <summary>Runs until the socket closes, the token is cancelled or the session decides to end. Never throws for a network problem.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        _device.AttachConnection(Connection);
        _device.FeedbackChanged += OnFeedback;
        try
        {
            Enqueue(JsonSerializer.Serialize(new { t = "hello", v = Version, instance = _instance, version = _appVersion, slot = _device.Slot, ipid = $"{_device.IpId:X2}" }));
            EnqueueSync();
            var writer = Task.Run(() => WriteLoop(token), CancellationToken.None);
            var pinger = Task.Run(() => PingLoop(token), CancellationToken.None);
            try { await ReadLoop(token); }
            finally
            {
                _cts.Cancel();
                try { await Task.WhenAll(writer, pinger).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            }
        }
        finally
        {
            _device.FeedbackChanged -= OnFeedback;
            _device.DetachConnection(Connection);
            _hello.TrySetResult(false);
            try
            {
                if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await _ws.CloseOutputAsync(EndReason != null ? WebSocketCloseStatus.PolicyViolation : WebSocketCloseStatus.NormalClosure, EndReason ?? "bye", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch { }
        }
    }

    /// <summary>Ends the session from outside (the link was edited, switched off, or its device went away).</summary>
    public void Stop(string? reason = null) { if (reason != null) EndReason ??= reason; try { _cts?.Cancel(); } catch (ObjectDisposedException) { } }

    // ------------------------------------------------------------------ sending

    private void Enqueue(string json)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Increment(ref _queued) > MaxQueue) { EndReason ??= "The link fell too far behind and was dropped; it reconnects and resyncs."; Stop(); return; }
        _out.Writer.TryWrite(json);
    }

    private void EnqueueSync()
    {
        var joins = _device.FeedbackSnapshot().Select(kv => Wire(kv.Key, kv.Value)).ToList();
        Enqueue(JsonSerializer.Serialize(new { t = "sync", joins }));
    }

    private static object Wire(JoinKey k, object v) => k.SmartObject == 0
        ? new { k = JoinKey.KindLetter(k.Kind).ToString(), n = k.Join, v = Val(v) }
        : new { k = JoinKey.KindLetter(k.Kind).ToString(), n = k.Join, so = k.SmartObject, v = Val(v) };

    private static object Val(object v) => v switch { bool b => b ? 1 : 0, ushort u => u, _ => v.ToString() ?? "" };

    private void OnFeedback(JoinDevice d, JoinKey key, object value)
    {
        var now = Environment.TickCount64;
        if (key.Kind == SigKind.Serial)
        {
            lock (_gate)
                if (_received.TryGetValue(key, out var r) && r.Value == (string)value && now - r.Tick < EchoWindow.TotalMilliseconds) return; // echo of what just arrived
        }
        if (!Allow(key, true, now)) return;
        Enqueue(JsonSerializer.Serialize(new { t = "j", k = JoinKey.KindLetter(key.Kind).ToString(), n = key.Join, so = key.SmartObject == 0 ? (uint?)null : key.SmartObject, v = Val(value) }, NoNulls));
        Interlocked.Increment(ref Connection.JoinsOut);
    }

    private static readonly JsonSerializerOptions NoNulls = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>True unless this join is changing too fast (a loop between the two programs, or a flood).</summary>
    private bool Allow(JoinKey key, bool outgoing, long now)
    {
        lock (_gate)
        {
            if (!_rates.TryGetValue((key, outgoing), out var r)) _rates[(key, outgoing)] = r = new Rate { WindowStart = now };
            if (now < r.BlockedUntil) return false;
            if (now - r.WindowStart >= 1000) { r.WindowStart = now; r.Count = 0; r.Warned = false; }
            if (++r.Count <= MaxJoinsPerSecond) return true;
            r.BlockedUntil = now + (long)PauseFor.TotalMilliseconds;
            _log?.Invoke("Warning", $"Remote EISC {_device.IpId:X2}: {key} is changing more than {MaxJoinsPerSecond} times a second {(outgoing ? "going out" : "coming in")}; it is paused for {PauseFor.TotalSeconds:0} s. Two programs may be feeding each other.");
            Interlocked.Increment(ref Connection.Errors);
            return false;
        }
    }

    private async Task WriteLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _out.Reader.ReadAllAsync(ct))
            {
                Interlocked.Decrement(ref _queued);
                var bytes = Encoding.UTF8.GetBytes(msg);
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                Connection.RecordOut(bytes.Length);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or ObjectDisposedException or InvalidOperationException) { }
        finally { _cts?.Cancel(); }
    }

    private async Task PingLoop(CancellationToken ct)
    {
        try
        {
            await _hello.Task.WaitAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                Enqueue(JsonSerializer.Serialize(new { t = "ping", ts = Stopwatch.GetTimestamp(), n = Interlocked.Increment(ref _pings) }));
                await Task.Delay(PingEvery, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ------------------------------------------------------------------ receiving

    private async Task ReadLoop(CancellationToken ct)
    {
        var buf = new byte[8192];
        using var ms = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (ms.Length + r.Count > MaxMessage) { EndReason = "The other end sent a message that is too large."; return; }
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);
                if (r.MessageType != WebSocketMessageType.Text) continue;
                Connection.RecordIn((int)ms.Length);
                try { Handle(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length)); }
                catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
                {
                    Interlocked.Increment(ref Connection.Errors); // one bad message doesn't end the link
                }
                if (EndReason != null && PeerInstance == _instance) return;
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
    }

    private void Handle(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        switch (root.GetProperty("t").GetString())
        {
            case "hello":
                PeerInstance = root.TryGetProperty("instance", out var inst) ? inst.GetString() : null;
                PeerVersion = root.TryGetProperty("version", out var ver) ? ver.GetString() : null;
                if (PeerInstance == _instance) { EndReason = "That address is this same OpenVP. A link needs two different machines."; _hello.TrySetResult(false); return; }
                if (root.TryGetProperty("v", out var pv) && pv.TryGetInt32(out var protocol) && protocol > Version) { EndReason = $"The other OpenVP speaks a newer link protocol ({protocol}); update this one."; _hello.TrySetResult(false); return; }
                ConnectedSince = DateTimeOffset.Now;
                Connection.ProbeStatus = "linked";
                Connection.SetDetail("Remote OpenVP version", PeerVersion);
                if (root.TryGetProperty("slot", out var rs) && rs.TryGetInt32(out var rslot)) Connection.SetDetail("Remote slot", rslot.ToString());
                if (root.TryGetProperty("ipid", out var rip)) Connection.SetDetail("Remote IP ID", rip.GetString());
                _hello.TrySetResult(true);
                break;
            case "sync":
                foreach (var j in root.GetProperty("joins").EnumerateArray()) Apply(j, sync: true);
                break;
            case "j":
                Apply(root, sync: false);
                break;
            case "ping":
                Enqueue(JsonSerializer.Serialize(new { t = "pong", ts = root.GetProperty("ts").GetInt64() }));
                break;
            case "pong":
                var ms = Stopwatch.GetElapsedTime(root.GetProperty("ts").GetInt64()).TotalMilliseconds;
                if (ms is >= 0 and < 60_000) Connection.RecordLatency(ms, "link ping");
                break;
        }
    }

    private void Apply(JsonElement j, bool sync)
    {
        var kind = JoinKey.ParseKind(j.GetProperty("k").GetString() ?? "");
        var n = j.GetProperty("n").GetUInt32();
        if (n == 0 || n > 65535) return;
        var so = j.TryGetProperty("so", out var s) && s.TryGetUInt32(out var sv) ? sv : 0u;
        var key = new JoinKey(kind, n, so);
        var v = j.GetProperty("v");
        object value = kind switch
        {
            SigKind.Digital => v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Number => v.GetDouble() != 0, _ => false },
            SigKind.Analog => v.ValueKind == JsonValueKind.Number ? (ushort)Math.Clamp(v.GetDouble(), 0, ushort.MaxValue) : (ushort)0,
            _ => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
        };
        if (sync && kind == SigKind.Serial && Equals(_device.GetInput(key), value)) return; // a resync is a state transfer, not new text
        if (!sync && !Allow(key, false, Environment.TickCount64)) return;
        if (kind == SigKind.Serial) lock (_gate) _received[key] = (Environment.TickCount64, (string)value);
        Interlocked.Increment(ref Connection.JoinsIn);
        _device.ReceiveInput(key, value, InputSource.Eisc);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _out.Writer.TryComplete();
    }
}
