using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using OpenVP.Core;

namespace OpenVP.Panels;

/// <summary>
/// A browser panel (XPanel-style test page or a CH5 project using openvp-crcomlib.js) connected over
/// WebSocket. Messages are small JSON objects:
///   {"t":"b","j":1,"v":true}     digital   (t may be "b" or "d")
///   {"t":"n","j":1,"v":32768}    analog    ("n" or "a")
///   {"t":"s","j":1,"v":"text"}   serial
/// The server sends the same shape for feedback, a {"t":"hello",...} on connect, then a snapshot of every
/// feedback value already set.
/// </summary>
public sealed class WebPanelSession
{
    private readonly WebSocket _socket;
    private readonly Processor _processor;
    private readonly uint _ipId;
    private readonly int? _slot;
    // Feedback is queued as (key, value) and turned into JSON in SendLoop, so the slot thread only pays for an enqueue.
    // Control messages (hello, offline, pong, error) travel as ready-made text in the same queue to keep their order.
    private readonly struct Out(JoinKey key, object? value, string? raw)
    {
        public readonly JoinKey Key = key;
        public readonly object? Value = value;
        public readonly string? Raw = raw;
    }

    private readonly Channel<Out> _outbox = Channel.CreateUnbounded<Out>(new UnboundedChannelOptions { SingleReader = true });
    private JoinDevice? _device;
    private readonly DeviceConnection _conn;

    public WebPanelSession(WebSocket socket, Processor processor, uint ipId, int? slot)
        : this(socket, processor, ipId, slot, new DeviceConnection("Web panel")) { }

    /// <param name="connection">Who the client is (address, User-Agent); the server fills it from the HTTP request.</param>
    public WebPanelSession(WebSocket socket, Processor processor, uint ipId, int? slot, DeviceConnection connection)
    {
        _socket = socket;
        _processor = processor;
        _ipId = ipId;
        _slot = slot;
        _conn = connection;
        _conn.SetDetail("IP ID requested", ipId.ToString("X2"));
    }

    public DeviceConnection Connection => _conn;

    public async Task RunAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _processor.DeviceUnregistered += OnUnregistered;
        ClientIdentifier.Begin(_conn);
        Attach();
        var sender = SendLoop(cts.Token);
        try { await ReceiveLoop(cts.Token); }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            _processor.DeviceUnregistered -= OnUnregistered;
            Detach();
            _outbox.Writer.TryComplete();
            cts.Cancel();
            try { await sender; } catch { }
            if (_socket.State == WebSocketState.Open)
                try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
    }

    private void Attach()
    {
        _device = DeviceDirectory.Resolve(_processor, _ipId, _slot)!;
        _device.FeedbackChanged += OnFeedback;
        _device.AttachConnection(_conn);
        Queue(new JsonObject { ["t"] = "hello", ["ipid"] = _ipId, ["slot"] = _device.Slot, ["type"] = _device.Type, ["connection"] = _conn.Id });
        foreach (var (k, v) in _device.FeedbackSnapshot()) OnFeedback(_device, k, v);
    }

    private void Detach()
    {
        if (_device == null) return;
        _device.FeedbackChanged -= OnFeedback;
        _device.DetachConnection(_conn);
        _device = null;
    }

    /// <summary>The program was unloaded or reloaded: re-attach to the new device object.</summary>
    private void OnUnregistered(JoinDevice d)
    {
        if (d != _device) return;
        Detach();
        Queue(new JsonObject { ["t"] = "offline" });
        // Re-resolve shortly after so the panel follows the reloaded program.
        _ = Task.Delay(200).ContinueWith(_ => { if (_socket.State == WebSocketState.Open) Attach(); });
    }

    private void OnFeedback(JoinDevice dev, JoinKey key, object value)
    {
        Interlocked.Increment(ref _conn.JoinsOut);
        _outbox.Writer.TryWrite(new Out(key, value, null));
    }

    private void Queue(JsonObject msg) => _outbox.Writer.TryWrite(new Out(default, null, msg.ToJsonString()));

    private static void WriteFeedback(Utf8JsonWriter w, in Out o)
    {
        w.WriteStartObject();
        w.WriteString("t", o.Key.Kind switch { SigKind.Digital => "b", SigKind.Analog => "n", _ => "s" });
        w.WriteNumber("j", o.Key.Join);
        w.WritePropertyName("v");
        switch (o.Value)
        {
            case bool b: w.WriteBooleanValue(b); break;
            case ushort u: w.WriteNumberValue(u); break;
            case string str: w.WriteStringValue(str); break;
            default: JsonSerializer.Serialize(w, o.Value); break;
        }
        if (o.Key.SmartObject != 0) w.WriteNumber("so", o.Key.SmartObject);
        w.WriteEndObject();
    }

    private async Task SendLoop(CancellationToken ct)
    {
        // One JSON object per frame (the documented format); the buffer is reused between frames.
        var buf = new ArrayBufferWriter<byte>(256);
        using var w = new Utf8JsonWriter(buf);
        await foreach (var o in _outbox.Reader.ReadAllAsync(ct))
        {
            if (_socket.State != WebSocketState.Open) break;
            if (o.Raw != null)
            {
                var bytes = Encoding.UTF8.GetBytes(o.Raw);
                _conn.RecordOut(bytes.Length);
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
            else
            {
                buf.Clear();
                w.Reset(buf);
                WriteFeedback(w, o);
                w.Flush();
                _conn.RecordOut(buf.WrittenCount);
                await _socket.SendAsync(buf.WrittenMemory, WebSocketMessageType.Text, true, ct);
            }
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var r = await _socket.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) break;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            if (!r.EndOfMessage) continue;
            var text = sb.ToString();
            _conn.RecordIn(Encoding.UTF8.GetByteCount(text));
            Handle(text);
            sb.Clear();
        }
    }

    /// <summary>
    /// What the panel page says about itself: {"t":"info","screen":"1920x1080","platform":"Win32",...}.
    /// Only short strings are kept; a page can't flood the IP table.
    /// </summary>
    private void ApplyClientInfo(JsonElement root)
    {
        var labels = new Dictionary<string, string>
        {
            ["screen"] = "Screen", ["viewport"] = "Viewport", ["pixelRatio"] = "Pixel ratio", ["platform"] = "Platform",
            ["language"] = "Language", ["timezone"] = "Time zone", ["page"] = "Page", ["touch"] = "Touch points",
            ["cores"] = "CPU cores", ["memoryGb"] = "Device memory (GB)", ["connection"] = "Network type", ["title"] = "Page title"
        };
        foreach (var p in root.EnumerateObject())
        {
            if (!labels.TryGetValue(p.Name, out var label)) continue;
            var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
            if (v is { Length: > 0 }) _conn.SetDetail(label, v.Length > 200 ? v[..200] : v);
        }
    }

    private void Handle(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var t = root.GetProperty("t").GetString() ?? "";
            if (t == "ping")
            {
                Interlocked.Increment(ref _conn.Heartbeats);
                if (root.TryGetProperty("rtt", out var rtt) && rtt.ValueKind == JsonValueKind.Number) _conn.LatencyMs = rtt.GetDouble();
                Queue(new JsonObject { ["t"] = "pong" });
                return;
            }
            if (t == "info") { ApplyClientInfo(root); return; }
            var kind = JoinKey.ParseKind(t);
            var join = root.GetProperty("j").GetUInt32();
            var v = root.GetProperty("v");
            object value = kind switch
            {
                SigKind.Digital => v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetInt32() != 0),
                SigKind.Analog => (ushort)Math.Clamp(v.GetInt32(), 0, 65535),
                _ => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString()
            };
            var so = root.TryGetProperty("so", out var soEl) && soEl.ValueKind == JsonValueKind.Number ? soEl.GetUInt32() : 0u;
            Interlocked.Increment(ref _conn.JoinsIn);
            _device?.ReceiveInput(new JoinKey(kind, join, so), value);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _conn.Errors);
            Queue(new JsonObject { ["t"] = "error", ["message"] = "Bad message: " + (text.Length > 200 ? text[..200] : text) });
        }
    }
}
