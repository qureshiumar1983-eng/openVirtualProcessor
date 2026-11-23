using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using OpenVP.Core.Integrations;
using OpenVP.Core.Ops;

namespace OpenVP.Core.Eisc;

/// <summary>An EISC on this OpenVP linked to an EISC on another one. The other machine is a push target (its address, certificate pin and token).</summary>
public sealed record RemoteEiscLink
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The push target that names the other machine. Its key must be an API token with scope eisc-link (or an admin token).</summary>
    public string TargetId { get; init; } = "";
    public int LocalSlot { get; init; }
    public string LocalIpId { get; init; } = "";
    public int RemoteSlot { get; init; }
    public string RemoteIpId { get; init; } = "";
    public bool Enabled { get; init; } = true;
}

/// <summary>The links in <c>data/eisc-links.json</c>.</summary>
public sealed class RemoteEiscStore
{
    public const int MaxLinks = 32;
    private sealed class FileDto { public List<RemoteEiscLink> Links { get; set; } = new(); }
    private readonly string? _file;
    private readonly object _gate = new();
    private List<RemoteEiscLink> _links;

    public event Action? Changed;

    public RemoteEiscStore(string? dataDirectory)
    {
        _file = dataDirectory == null ? null : Path.Combine(dataDirectory, "eisc-links.json");
        _links = _file == null ? new() : IntegrationFiles.Load<FileDto>(_file).Links.Where(l => l.Id.Length > 0).ToList();
    }

    public IReadOnlyList<RemoteEiscLink> List() { lock (_gate) return _links.ToList(); }
    public RemoteEiscLink? Get(string? id) { lock (_gate) return _links.FirstOrDefault(l => l.Id == id); }

    public static bool TryIpId(string? s, out uint id) => uint.TryParse((s ?? "").Trim(), System.Globalization.NumberStyles.HexNumber, null, out id) && id is > 0 and <= 0xFE;

    private static RemoteEiscLink Check(RemoteEiscLink l, int slotCount, Func<string, bool> targetExists)
    {
        var name = (l.Name ?? "").Trim();
        if (name.Length is 0 or > 60 || name.Any(char.IsControl)) throw new ArgumentException("Give the link a name (up to 60 characters).");
        if (!targetExists(l.TargetId)) throw new ArgumentException("Choose the other OpenVP: add it under Push targets first.");
        if (l.LocalSlot < 1 || l.LocalSlot > slotCount) throw new ArgumentException($"This machine's slot is 1-{slotCount}.");
        if (l.RemoteSlot is < 1 or > 99) throw new ArgumentException("The other machine's slot is a number from 1 up.");
        if (!TryIpId(l.LocalIpId, out var a)) throw new ArgumentException("This machine's EISC IP ID is hex, 03 to FE.");
        if (!TryIpId(l.RemoteIpId, out var b)) throw new ArgumentException("The other machine's EISC IP ID is hex, 03 to FE.");
        return l with { Name = name, LocalIpId = $"{a:X2}", RemoteIpId = $"{b:X2}" };
    }

    public RemoteEiscLink Add(RemoteEiscLink l, int slotCount, Func<string, bool> targetExists)
    {
        var c = Check(l, slotCount, targetExists) with { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() };
        lock (_gate)
        {
            if (_links.Count >= MaxLinks) throw new ArgumentException($"At most {MaxLinks} links.");
            if (_links.Any(x => x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"There's already a link called '{c.Name}'.");
            if (_links.Any(x => x.LocalSlot == c.LocalSlot && x.LocalIpId == c.LocalIpId)) throw new ArgumentException($"Slot {c.LocalSlot} EISC {c.LocalIpId} already has a link. One EISC links to one remote EISC.");
            _links.Add(c); Save();
        }
        Changed?.Invoke();
        return c;
    }

    public RemoteEiscLink? Update(string id, RemoteEiscLink l, int slotCount, Func<string, bool> targetExists)
    {
        RemoteEiscLink c;
        lock (_gate)
        {
            var old = _links.FirstOrDefault(x => x.Id == id);
            if (old == null) return null;
            c = Check(l, slotCount, targetExists) with { Id = id };
            if (_links.Any(x => x.Id != id && x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"There's already a link called '{c.Name}'.");
            if (_links.Any(x => x.Id != id && x.LocalSlot == c.LocalSlot && x.LocalIpId == c.LocalIpId)) throw new ArgumentException($"Slot {c.LocalSlot} EISC {c.LocalIpId} already has a link.");
            _links[_links.IndexOf(old)] = c; Save();
        }
        Changed?.Invoke();
        return c;
    }

    public bool Remove(string id)
    {
        lock (_gate) { if (_links.RemoveAll(x => x.Id == id) == 0) return false; Save(); }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Drops the links that name a push target that was deleted.</summary>
    public void RemoveForTarget(string targetId)
    {
        bool any;
        lock (_gate) { any = _links.RemoveAll(x => x.TargetId == targetId) > 0; if (any) Save(); }
        if (any) Changed?.Invoke();
    }

    private void Save() { if (_file != null) { try { IntegrationFiles.Save(_file, new FileDto { Links = _links }); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } } }
}

/// <summary>What the UI shows for one link. <see cref="State"/>: off, waiting (no EISC in that slot yet), connecting, connected, reconnecting, error.</summary>
public sealed record RemoteEiscStatus(string Id, string State, string Message, double? LatencyMs, DateTimeOffset? ConnectedSince, int Reconnects, DateTimeOffset? NextRetry, long JoinsIn, long JoinsOut, string? PeerVersion, string? PeerAddress);

/// <summary>An incoming link: another OpenVP opened the WebSocket to one of this machine's EISCs.</summary>
public sealed record RemoteEiscInboundInfo(int Slot, string IpId, string Remote, double? LatencyMs, DateTimeOffset Since, long JoinsIn, long JoinsOut, string? PeerVersion);

/// <summary>
/// Keeps every enabled link connected: finds the local EISC, opens the WebSocket to the other machine, and when it drops reconnects with a
/// growing wait (1 s doubling to 30 s, with jitter). Every connection starts with a full resync both ways. Also tracks the incoming
/// sessions, one per EISC (a newer one replaces an older one). Nothing runs while the Labs feature is off.
/// </summary>
public sealed class RemoteEiscManager : IDisposable
{
    private readonly Processor _processor;
    private readonly RemoteEiscStore _store;
    private readonly TargetStore _targets;
    private readonly Func<bool> _enabled;
    private readonly string _instance;
    private readonly string _version;
    private readonly object _gate = new();
    private readonly Dictionary<string, Runner> _runners = new();
    private readonly Dictionary<JoinDevice, RemoteEiscSession> _inbound = new();
    private readonly CancellationTokenSource _cts = new();

    public static TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    public static TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    public RemoteEiscManager(Processor processor, RemoteEiscStore store, TargetStore targets, Func<bool> enabled, string instanceId, string version)
    {
        _processor = processor; _store = store; _targets = targets; _enabled = enabled; _instance = instanceId; _version = version;
        _store.Changed += Reconcile;
        _processor.DeviceUnregistered += OnDeviceGone;
    }

    public string Instance => _instance;
    public string Version => _version;
    public bool Enabled => _enabled();

    /// <summary>A stable id for this installation, kept in the data folder. The two ends compare ids so a link to oneself is refused.</summary>
    public static string LoadOrCreateInstanceId(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "instance-id.txt");
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: >= 16 } id) return id;
            var created = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(path, created);
            return created;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(); }
    }

    /// <summary>Starts a runner for every link in the store (call once at start-up, and again after the Labs switch changes).</summary>
    public void Start() => Reconcile();

    public void Reconcile()
    {
        lock (_gate)
        {
            if (_cts.IsCancellationRequested) return;
            var links = _store.List();
            foreach (var (id, r) in _runners.ToList())
                if (links.All(l => l.Id != id) || r.Link != links.First(l => l.Id == id))
                {
                    r.Cancel();
                    _runners.Remove(id);
                }
            foreach (var l in links)
                if (!_runners.ContainsKey(l.Id))
                {
                    var r = new Runner(this, l);
                    _runners[l.Id] = r;
                    r.Start();
                }
        }
    }

    public RemoteEiscStatus? Status(string id)
    {
        lock (_gate) return _runners.TryGetValue(id, out var r) ? r.Snapshot() : null;
    }

    public IReadOnlyList<RemoteEiscInboundInfo> Inbound()
    {
        lock (_gate)
            return _inbound.Values.Where(s => s.Connection.ProbeStatus == "linked").Select(s => new RemoteEiscInboundInfo(s.Device.Slot, $"{s.Device.IpId:X2}", s.Connection.Endpoint,
                s.LatencyMs < 0 ? null : s.LatencyMs, s.ConnectedSince ?? s.Connection.ConnectedAt, Interlocked.Read(ref s.Connection.JoinsIn), Interlocked.Read(ref s.Connection.JoinsOut), s.PeerVersion)).ToList();
    }

    /// <summary>The incoming WebSocket for one of this machine's EISCs. Replaces an older incoming session for the same EISC. Returns when the link ends.</summary>
    public async Task RunInboundAsync(WebSocket ws, JoinDevice device, string remoteAddress, int remotePort, CancellationToken ct)
    {
        var session = new RemoteEiscSession(ws, device, _instance, _version, "another OpenVP", remoteAddress, remotePort, (lvl, msg) => _processor.Slot(device.Slot).Log(lvl, msg));
        RemoteEiscSession? old;
        lock (_gate) { _inbound.TryGetValue(device, out old); _inbound[device] = session; }
        old?.Stop("Replaced by a newer link to the same EISC.");
        _ = session.HelloReceived.ContinueWith(t =>
        {
            if (t.Result) _processor.Slot(device.Slot).Log("Notice", $"Remote EISC {device.IpId:X2}: linked to another OpenVP at {remoteAddress} (version {session.PeerVersion}).");
        }, TaskScheduler.Default);
        try { await session.RunAsync(ct); }
        finally
        {
            lock (_gate) if (_inbound.TryGetValue(device, out var cur) && cur == session) _inbound.Remove(device);
            if (session.ConnectedSince != null) _processor.Slot(device.Slot).Log("Notice", $"Remote EISC {device.IpId:X2}: link from {remoteAddress} closed" + (session.EndReason != null ? ": " + session.EndReason : "."));
            session.Dispose();
        }
    }

    /// <summary>Ends every incoming link (Remote EISC was switched off). The other machines see a closed socket and keep retrying.</summary>
    public void StopInbound(string reason)
    {
        List<RemoteEiscSession> all;
        lock (_gate) all = _inbound.Values.ToList();
        foreach (var s in all) s.Stop(reason);
    }

    private void OnDeviceGone(JoinDevice d)
    {
        RemoteEiscSession? s;
        lock (_gate) _inbound.TryGetValue(d, out s);
        s?.Stop("The EISC went away (its program was replaced or unloaded).");
    }

    public void Dispose()
    {
        _store.Changed -= Reconcile;
        _processor.DeviceUnregistered -= OnDeviceGone;
        _cts.Cancel();
        lock (_gate)
        {
            foreach (var r in _runners.Values) r.Cancel();
            _runners.Clear();
            foreach (var s in _inbound.Values) s.Stop();
        }
    }

    // ------------------------------------------------------------------ outbound

    private sealed class Runner
    {
        private readonly RemoteEiscManager _m;
        private readonly CancellationTokenSource _cts;
        private readonly object _s = new();
        private string _state = "connecting", _message = "";
        private readonly double? _latency = null;
        private DateTimeOffset? _since, _next;
        private int _reconnects;
        private RemoteEiscSession? _session;
        private string? _peerVersion, _peerAddress;
        private long _inBase, _outBase;

        public RemoteEiscLink Link { get; }

        public Runner(RemoteEiscManager m, RemoteEiscLink link) { _m = m; Link = link; _cts = CancellationTokenSource.CreateLinkedTokenSource(m._cts.Token); }

        public void Start() => Task.Run(Loop);
        public void Cancel() { try { _cts.Cancel(); } catch (ObjectDisposedException) { } _session?.Stop(); }

        public RemoteEiscStatus Snapshot()
        {
            lock (_s)
            {
                var sess = _session;
                return new RemoteEiscStatus(Link.Id, _state, _message, sess?.Connection.LatencyMs is { } l && l >= 0 ? l : _latency, _since, _reconnects, _next,
                    sess == null ? _inBase : _inBase + Interlocked.Read(ref sess.Connection.JoinsIn), sess == null ? _outBase : _outBase + Interlocked.Read(ref sess.Connection.JoinsOut), _peerVersion, _peerAddress);
            }
        }

        private void Set(string state, string message, DateTimeOffset? next = null) { lock (_s) { _state = state; _message = message; _next = next; if (state != "connected") { _since = null; } } }

        private async Task Loop()
        {
            var ct = _cts.Token;
            var backoff = InitialBackoff;
            var first = true;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!Link.Enabled) { Set("off", "This link is switched off."); await Task.Delay(1000, ct); continue; }
                    if (!_m._enabled()) { Set("off", "Remote EISC is switched off in Labs."); await Task.Delay(1000, ct); continue; }
                    var target = _m._targets.Get(Link.TargetId);
                    if (target == null) { Set("error", "The push target for this link was deleted."); await Task.Delay(3000, ct); continue; }
                    var ok = RemoteEiscStore.TryIpId(Link.LocalIpId, out var ipid);
                    var device = ok && _m._processor.IsValidSlot(Link.LocalSlot) ? _m._processor.Slot(Link.LocalSlot).FindDevice(ipid) : null;
                    if (device == null) { Set("waiting", $"Waiting for an EISC with IP ID {Link.LocalIpId} in slot {Link.LocalSlot} (load and start its program)."); await Task.Delay(1000, ct); continue; }

                    Set(first ? "connecting" : "reconnecting", $"Connecting to {target.Name}…");
                    var lasted = await ConnectOnce(target, device, ct);
                    first = false;
                    if (lasted > TimeSpan.FromSeconds(5)) backoff = InitialBackoff;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Set("error", e.Message); }

                if (ct.IsCancellationRequested) break;
                var wait = backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)(backoff.TotalMilliseconds * 0.2) + 1));
                lock (_s) { _next = DateTimeOffset.Now + wait; if (_state == "connected") _state = "reconnecting"; }
                try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; }
                backoff = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, backoff.Ticks * 2));
                lock (_s) _reconnects++;
            }
        }

        private async Task<TimeSpan> ConnectOnce(PushTarget target, JoinDevice device, CancellationToken ct)
        {
            var started = DateTimeOffset.Now;
            var uri = new UriBuilder(target.Url) { Scheme = target.Url.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws", Path = "/ws/eisc", Query = $"slot={Link.RemoteSlot}&ipid={Link.RemoteIpId}" }.Uri;
            using var ws = new ClientWebSocket();
            if (_m._targets.KeyOf(target.Id) is { Length: > 0 } key) ws.Options.SetRequestHeader("Authorization", "Bearer " + key);
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            var fp = target.Fingerprint;
            if (uri.Scheme == "wss") ws.Options.RemoteCertificateValidationCallback = (_, cert, _, errors) => PushClient.ValidateCertificate(cert, errors, fp);
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(10));
                await ws.ConnectAsync(uri, connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Set("reconnecting", $"No answer from {target.Name}."); return TimeSpan.Zero; }
            catch (WebSocketException e)
            {
                Set(ws.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "error" : "reconnecting", Explain(ws.HttpStatusCode, e, target));
                return TimeSpan.Zero;
            }

            var slot = _m._processor.Slot(Link.LocalSlot);
            using var session = new RemoteEiscSession(ws, device, _m._instance, _m._version, $"{target.Name} slot {Link.RemoteSlot} EISC {Link.RemoteIpId}", uri.Host, uri.Port, (l, msg) => slot.Log(l, msg));
            session.Connection.SetDetail("Link", Link.Name);
            lock (_s) _session = session;
            var run = session.RunAsync(ct);
            // Watch for the local EISC being replaced (its program was reloaded), the link being switched off, or Labs turning off: end the session so it reconnects against the new device.
            using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watcher = Task.Run(async () =>
            {
                while (!watchCts.IsCancellationRequested)
                {
                    try { await Task.Delay(300, watchCts.Token); } catch (OperationCanceledException) { return; }
                    var ok = RemoteEiscStore.TryIpId(Link.LocalIpId, out var ip);
                    if (!_m._enabled()) { session.Stop(); return; }
                    if (!ok || _m._processor.Slot(Link.LocalSlot).FindDevice(ip) != device) { session.Stop("The local EISC was replaced."); return; }
                }
            });
            try
            {
                var hello = await session.HelloReceived.WaitAsync(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
                if (hello)
                {
                    lock (_s) { _state = "connected"; _message = $"Linked to {target.Name}."; _since = DateTimeOffset.Now; _next = null; _peerVersion = session.PeerVersion; _peerAddress = uri.Authority; }
                    slot.Log("Notice", $"Remote EISC {Link.LocalIpId}: linked to {target.Name} slot {Link.RemoteSlot} EISC {Link.RemoteIpId} (OpenVP {session.PeerVersion}).");
                }
                await run;
            }
            catch (TimeoutException) { session.Stop(); Set("reconnecting", $"{target.Name} didn't say hello."); try { await run; } catch { } }
            finally
            {
                watchCts.Cancel();
                lock (_s) { _session = null; _inBase += Interlocked.Read(ref session.Connection.JoinsIn); _outBase += Interlocked.Read(ref session.Connection.JoinsOut); }
            }
            var wasLinked = session.ConnectedSince != null;
            if (session.EndReason != null) Set("error", session.EndReason);
            else if (!ct.IsCancellationRequested) Set("reconnecting", wasLinked ? $"The link to {target.Name} dropped." : $"{target.Name} closed the connection.");
            if (wasLinked) slot.Log("Warning", $"Remote EISC {Link.LocalIpId}: link to {target.Name} ended" + (session.EndReason != null ? ": " + session.EndReason : "; reconnecting."));
            return wasLinked ? DateTimeOffset.Now - session.ConnectedSince!.Value : TimeSpan.Zero;
        }

        private static string Explain(HttpStatusCode? status, WebSocketException e, PushTarget t) => status switch
        {
            HttpStatusCode.Unauthorized => $"{t.Name} refused the token. Put an API token with scope eisc-link (made on that machine) in the push target.",
            HttpStatusCode.Forbidden => $"{t.Name} says this token may not open an EISC link. Its scope must be eisc-link or admin, and allow that slot.",
            HttpStatusCode.NotFound => $"{t.Name} has no such EISC, or Remote EISC is switched off in its Labs page, or it is an older OpenVP.",
            HttpStatusCode.Conflict => $"{t.Name}: the IP ID there isn't an EISC.",
            null => "Couldn't connect: " + (e.InnerException?.Message ?? e.Message),
            _ => $"{t.Name} answered {(int)status}."
        };
    }
}
