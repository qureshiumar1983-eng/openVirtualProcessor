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
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private JoinDevice? _device;

    public WebPanelSession(WebSocket socket, Processor processor, uint ipId, int? slot)
    {
        _socket = socket;
        _processor = processor;
        _ipId = ipId;
        _slot = slot;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _processor.DeviceUnregistered += OnUnregistered;
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
        _device = DeviceDirectory.Resolve(_processor, _ipId, _slot);
        _device.FeedbackChanged += OnFeedback;
        _device.AttachConnection();
        Queue(new JsonObject { ["t"] = "hello", ["ipid"] = _ipId, ["slot"] = _device.Slot, ["type"] = _device.Type });
        foreach (var (k, v) in _device.FeedbackSnapshot()) OnFeedback(_device, k, v);
    }

    private void Detach()
    {
        if (_device == null) return;
        _device.FeedbackChanged -= OnFeedback;
        _device.DetachConnection();
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
        var t = key.Kind switch { SigKind.Digital => "b", SigKind.Analog => "n", _ => "s" };
        var msg = new JsonObject { ["t"] = t, ["j"] = key.Join, ["v"] = JsonValue.Create(value) };
        if (key.SmartObject != 0) msg["so"] = key.SmartObject;
        Queue(msg);
    }

    private void Queue(JsonObject msg) => _outbox.Writer.TryWrite(msg.ToJsonString());

    private async Task SendLoop(CancellationToken ct)
    {
        await foreach (var msg in _outbox.Reader.ReadAllAsync(ct))
        {
            if (_socket.State != WebSocketState.Open) break;
            await _socket.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ct);
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
            Handle(sb.ToString());
            sb.Clear();
        }
    }

    private void Handle(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var t = root.GetProperty("t").GetString() ?? "";
            if (t == "ping") { Queue(new JsonObject { ["t"] = "pong" }); return; }
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
            _device?.ReceiveInput(new JoinKey(kind, join, so), value);
        }
        catch (Exception)
        {
            Queue(new JsonObject { ["t"] = "error", ["message"] = "Bad message: " + (text.Length > 200 ? text[..200] : text) });
        }
    }
}
