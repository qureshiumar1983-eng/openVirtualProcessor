using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace OpenVP.Core.Integrations;

public sealed record OscStatus(bool Listening, int ListenPort, string? Error, bool Sending, string Target, long Received, long Sent, long Rejected, long Dropped,
    DateTimeOffset? LastIn, DateTimeOffset? LastOut, string? LastAddress);

/// <summary>
/// OSC over UDP. Incoming: <c>/openvp/slotNN/&lt;signal&gt; value</c> sets a signal that the slot's OSC list marks as received.
/// A digital signal takes an int (non-zero is high), a float (above 0.5 is high), <c>T</c> or <c>F</c>, or no argument at all
/// (a pulse); <c>/pulse</c> on the end of the address pulses. An analog signal takes an int (0-65535), or a float of 0 to 1 as a
/// fraction of full scale (a float above 1 is a plain number). A serial signal takes a string. Outgoing: each changed signal
/// marked for sending goes to the target as one message (digital and analog as an int, serial as a string), from a bounded queue.
/// </summary>
public sealed class OscService : IDisposable
{
    private readonly Processor _processor;
    private readonly IntegrationStore _store;
    private readonly SignalDirectory _directory;
    private readonly object _gate = new();
    private readonly List<IDisposable> _subs = new();
    private readonly Channel<(OscMessage Msg, IPEndPoint To)> _out;
    private Dictionary<string, (int Slot, string Signal)> _receive = new(StringComparer.Ordinal);
    private UdpClient? _listener, _sender;
    private CancellationTokenSource? _cts;
    private Task? _outTask;
    private int _listenPort;
    private string? _error;
    private IPEndPoint? _target;
    private long _in, _out_, _rejected, _dropped;
    private DateTimeOffset? _lastIn, _lastOut;
    private string? _lastAddress;
    private long _lastWarn;
    private bool _disposed;

    public OscService(Processor processor, IntegrationStore store, SignalDirectory directory)
    {
        _processor = processor; _store = store; _directory = directory;
        _out = Channel.CreateBounded<(OscMessage, IPEndPoint)>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>The port the listener actually has (useful when the setting was 0 in a test that started one itself).</summary>
    public int ListeningPort { get { lock (_gate) return _listenPort; } }

    public OscStatus Status()
    {
        lock (_gate)
            return new OscStatus(_listener != null, _listenPort, _error, _target != null && _subs.Count > 0, _target?.ToString() ?? "", Interlocked.Read(ref _in), Interlocked.Read(ref _out_),
                Interlocked.Read(ref _rejected), Interlocked.Read(ref _dropped), _lastIn, _lastOut, _lastAddress);
    }

    public static string AddressOf(int slot, string segment) => $"/openvp/slot{slot:00}/{segment}";

    public void Apply()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var g = _store.Global.Osc;
            // Listener
            if (_listener != null && (g.ListenPort == 0 || _listenPort != g.ListenPort)) { StopListener(); }
            _error = null;
            if (g.ListenPort > 0 && _listener == null) StartListener(g.ListenPort);

            // Target and sending
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            _target = null;
            if (g.TargetHost.Length > 0)
            {
                try { _target = Resolve(g.TargetHost, g.TargetPort); _sender ??= new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true }; }
                catch (Exception e) when (e is SocketException or ArgumentException) { _error = $"Can't resolve {g.TargetHost}: {e.Message}"; }
            }
            if (_target != null && _outTask == null) { _cts = new CancellationTokenSource(); var t = _cts.Token; _outTask = Task.Run(() => SendLoop(t)); }

            var recv = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
            foreach (var slot in _processor.Slots)
            {
                var list = _store.Slot(slot.Number).Osc;
                if (list.Count == 0) continue;
                var segs = SignalValues.Segments(list.Select(x => x.Signal));
                foreach (var o in list.Where(x => x.Receive)) recv[AddressOf(slot.Number, segs[o.Signal])] = (slot.Number, o.Signal);
                var send = list.Where(x => x.Send).ToList();
                if (_target == null || send.Count == 0) continue;
                var to = _target; var n = slot.Number;
                var addr = send.ToDictionary(x => x.Signal, x => AddressOf(n, segs[x.Signal]), StringComparer.Ordinal);
                _subs.Add(slot.Hub.Subscribe(addr.Keys, e =>
                {
                    if (!addr.TryGetValue(e.Signal, out var a)) return;
                    object? arg = e.Kind == SigKind.Serial ? SignalValues.Format(e.Value) : (int)Convert.ToInt64(SignalValues.ForJson(e.Kind, e.Value)); // a bool converts to 1 or 0
                    _out.Writer.TryWrite((new OscMessage(a, [arg]), to));
                }));
            }
            _receive = recv;
        }
    }

    private static IPEndPoint Resolve(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ip)) return new IPEndPoint(ip, port);
        var ip4 = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? throw new ArgumentException("No IPv4 address.");
        return new IPEndPoint(ip4, port);
    }

    private void StartListener(int port)
    {
        try
        {
            var l = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            _listener = l;
            _listenPort = ((IPEndPoint)l.Client.LocalEndPoint!).Port;
            _ = Task.Run(() => ReceiveLoop(l));
        }
        catch (SocketException e) { _listener = null; _listenPort = 0; _error = $"Can't listen on UDP {port}: {e.Message}"; }
    }

    private void StopListener()
    {
        try { _listener?.Dispose(); } catch { }
        _listener = null; _listenPort = 0;
    }

    private async Task ReceiveLoop(UdpClient l)
    {
        while (true)
        {
            UdpReceiveResult r;
            try { r = await l.ReceiveAsync(); }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (_listener != l) return; continue; }
            try { HandlePacket(r.Buffer); } catch { Interlocked.Increment(ref _rejected); }
        }
    }

    /// <summary>Applies the messages of one datagram. Public so tests can feed bytes without a socket.</summary>
    public void HandlePacket(byte[] data)
    {
        foreach (var m in OscCodec.DecodePacket(data))
        {
            Interlocked.Increment(ref _in);
            lock (_gate) { _lastIn = DateTimeOffset.Now; _lastAddress = m.Address; }
            Apply1(m);
        }
    }

    private void Apply1(OscMessage m)
    {
        var map = _receive;
        var addr = m.Address;
        var pulse = false;
        if (!map.TryGetValue(addr, out var target))
        {
            if (addr.EndsWith("/pulse", StringComparison.Ordinal) && map.TryGetValue(addr[..^6], out target)) pulse = true;
            else { Interlocked.Increment(ref _rejected); return; }
        }
        if (!_directory.TryGet(target.Slot, target.Signal, out var info)) { Reject(target.Slot, $"OSC {addr}: the program has no signal '{target.Signal}'."); return; }
        string? text = null;
        var a = m.Args.Count > 0 ? m.Args[0] : null;
        if (pulse || (m.Args.Count == 0 && info.Kind == SigKind.Digital)) text = "pulse";
        else if (m.Args.Count == 0) { Reject(target.Slot, $"OSC {addr}: no value."); return; }
        else
        {
            switch (info.Kind)
            {
                case SigKind.Digital:
                    text = a switch { bool b => b ? "1" : "0", int i => i != 0 ? "1" : "0", long l => l != 0 ? "1" : "0", float f => f > 0.5f ? "1" : "0", double d => d > 0.5 ? "1" : "0", string s => s, _ => null };
                    break;
                case SigKind.Analog:
                    text = a switch
                    {
                        int i => i.ToString(), long l => l.ToString(),
                        float f => Scale(f), double d => Scale(d),
                        bool b => b ? "65535" : "0", string s => s, _ => null
                    };
                    break;
                default:
                    text = a switch { string s => s, null => "", _ => SignalValues.Format(a) };
                    break;
            }
        }
        if (text == null) { Reject(target.Slot, $"OSC {addr}: can't use that value."); return; }
        var err = _directory.SetFromText(target.Slot, target.Signal, text);
        if (err != null) Reject(target.Slot, $"OSC {addr}: {err}");
    }

    private static string Scale(double v) => (v <= 1.0 ? Math.Round(Math.Clamp(v, 0, 1) * 65535) : Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Reject(int slot, string why)
    {
        Interlocked.Increment(ref _rejected);
        var now = Environment.TickCount64; var last = Interlocked.Read(ref _lastWarn);
        if (now - last < 5000 || Interlocked.CompareExchange(ref _lastWarn, now, last) != last) return;
        _processor.Slot(slot).Log("Warning", why);
    }

    private async Task SendLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var (msg, to) in _out.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var bytes = OscCodec.Encode(msg);
                    UdpClient? s; lock (_gate) s = _sender;
                    if (s == null) continue;
                    await s.SendAsync(bytes, bytes.Length, to);
                    Interlocked.Increment(ref _out_);
                    lock (_gate) { _lastOut = DateTimeOffset.Now; _lastAddress = msg.Address; }
                }
                catch (SocketException e) { lock (_gate) _error = "Send failed: " + e.Message; }
                catch (ObjectDisposedException) { return; }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            StopListener();
            _cts?.Cancel();
            try { _sender?.Dispose(); } catch { }
            _sender = null;
        }
    }
}
