using System.Net;
using System.Net.Sockets;

namespace OpenVP.Core.Integrations;

public sealed record ArtNetStatus(bool Sending, bool Listening, string? Error, string Target, int RateHz, int Universes, long PacketsSent, long PacketsReceived, long Rejected,
    DateTimeOffset? LastOut, DateTimeOffset? LastIn);

/// <summary>
/// Art-Net. Sending: analog signals mapped to DMX channels are sent as ArtDMX at a steady rate (<see cref="ArtNetSettings.RateHz"/>), whether or not
/// they changed, to the target address, one packet per universe in use. A signal's value is kept up to date by the slot's change
/// stream; the frames are built and sent on this service's own thread. Receiving (optional): ArtDMX on UDP 6454 sets signals whose
/// mapping says <c>receive</c>, only when the value actually changes.
/// </summary>
public sealed class ArtNetService : IDisposable
{
    private sealed class Cell { public int Value; }
    private sealed record Map(Cell Cell, int Channel, bool Wide);
    private sealed record RecvMap(int Slot, string Signal, int Channel, bool Wide, int Universe);

    private readonly Processor _processor;
    private readonly IntegrationStore _store;
    private readonly SignalDirectory _directory;
    private readonly object _gate = new();
    private readonly List<IDisposable> _subs = new();
    private readonly List<(int Slot, Action<ProgramSlot> Handler)> _stateHooks = new();
    private volatile Dictionary<int, Map[]> _frames = new();
    private volatile RecvMap[] _recv = [];
    private readonly Dictionary<string, int> _lastRecv = new();
    private readonly byte[] _seq = new byte[32768];
    private UdpClient? _sender, _listener;
    private IPEndPoint? _target;
    private CancellationTokenSource? _cts;
    private string? _error;
    private ArtNetSettings _settings = new();
    private long _sent, _received, _rejected;
    private DateTimeOffset? _lastOut, _lastIn;
    private bool _disposed;

    public ArtNetService(Processor processor, IntegrationStore store, SignalDirectory directory)
    {
        _processor = processor; _store = store; _directory = directory;
    }

    public ArtNetStatus Status()
    {
        lock (_gate)
            return new ArtNetStatus(_cts != null && _frames.Count > 0, _listener != null, _error, _target?.ToString() ?? _settings.Target, _settings.RateHz, _frames.Count,
                Interlocked.Read(ref _sent), Interlocked.Read(ref _received), Interlocked.Read(ref _rejected), _lastOut, _lastIn);
    }

    public void Apply()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Stop();
            _error = null;
            var g = _settings = _store.Global.ArtNet;
            if (!g.Enabled) return;

            var frames = new Dictionary<int, List<Map>>();
            var recv = new List<RecvMap>();
            foreach (var slot in _processor.Slots)
            {
                var list = _store.Slot(slot.Number).ArtNet;
                if (list.Count == 0) continue;
                var n = slot.Number;
                var cells = new Dictionary<string, Cell>(StringComparer.Ordinal);
                foreach (var c in list.Where(x => x.Send))
                {
                    if (!cells.TryGetValue(c.Signal, out var cell)) cells[c.Signal] = cell = new Cell();
                    if (!frames.TryGetValue(c.Universe, out var f)) frames[c.Universe] = f = new List<Map>();
                    f.Add(new Map(cell, c.Channel, c.Mode == "16"));
                }
                foreach (var c in list.Where(x => x.Receive)) recv.Add(new RecvMap(n, c.Signal, c.Channel, c.Mode == "16", c.Universe));
                if (cells.Count == 0) continue;
                _subs.Add(slot.Hub.Subscribe(cells.Keys, e =>
                {
                    if (cells.TryGetValue(e.Signal, out var cell)) Volatile.Write(ref cell.Value, ToInt(e.Value));
                }));
                void Seed()
                {
                    foreach (var s in _directory.All(n)) if (cells.TryGetValue(s.Name, out var cell)) Volatile.Write(ref cell.Value, ToInt(s.Value));
                }
                Seed();
                Action<ProgramSlot> hook = s => { if (s.State == SlotState.Running) Seed(); };
                slot.StateChanged += hook;
                _stateHooks.Add((n, hook));
            }
            _frames = frames.ToDictionary(k => k.Key, k => k.Value.ToArray());
            _recv = recv.ToArray();

            if (_frames.Count > 0)
            {
                try
                {
                    _target = IPAddress.TryParse(g.Target, out var ip) ? new IPEndPoint(ip, g.Port) : new IPEndPoint(Dns.GetHostAddresses(g.Target).First(a => a.AddressFamily == AddressFamily.InterNetwork), g.Port);
                    _sender = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
                    _cts = new CancellationTokenSource();
                    var t = _cts.Token; var rate = g.RateHz;
                    _ = Task.Run(() => SendLoop(rate, t));
                }
                catch (Exception e) when (e is SocketException or InvalidOperationException or ArgumentException) { _error = $"Can't send to {g.Target}: {e.Message}"; }
            }
            if (g.Listen && _recv.Length > 0) StartListener();
        }
    }

    private static int ToInt(object? v) => v switch
    {
        bool b => b ? 65535 : 0,
        ushort u => u,
        IConvertible c when v is not string => (int)Math.Clamp(Convert.ToInt64(c), 0, 65535),
        _ => 0
    };

    private void StartListener()
    {
        try
        {
            var l = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
            l.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            l.Client.Bind(new IPEndPoint(IPAddress.Any, ArtNetCodec.Port));
            _listener = l;
            _ = Task.Run(() => ReceiveLoop(l));
        }
        catch (SocketException e) { _error = $"Can't listen on UDP {ArtNetCodec.Port}: {e.Message}"; }
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

    /// <summary>Applies one received datagram. Public so tests can feed bytes without a socket.</summary>
    public void HandlePacket(byte[] packet)
    {
        if (!ArtNetCodec.TryDecodeDmx(packet, out var dmx)) { Interlocked.Increment(ref _rejected); return; }
        Interlocked.Increment(ref _received);
        lock (_gate) _lastIn = DateTimeOffset.Now;
        foreach (var m in _recv)
        {
            if (m.Universe != dmx!.Universe) continue;
            var i = m.Channel - 1;
            int value;
            if (m.Wide) { if (i + 1 >= dmx.Data.Length) continue; value = (dmx.Data[i] << 8) | dmx.Data[i + 1]; }
            else { if (i >= dmx.Data.Length) continue; value = ArtNetCodec.FromByte(dmx.Data[i]); }
            var key = $"{m.Slot}|{m.Signal}|{m.Universe}|{m.Channel}";
            lock (_lastRecv) { if (_lastRecv.TryGetValue(key, out var old) && old == value) continue; _lastRecv[key] = value; }
            _directory.SetFromText(m.Slot, m.Signal, value.ToString());
        }
    }

    /// <summary>The packet this service would send for a universe now (null if nothing is mapped there). The sender uses it; tests read it.</summary>
    public byte[]? BuildFrame(int universe, byte sequence)
    {
        if (!_frames.TryGetValue(universe, out var maps)) return null;
        var data = new byte[512];
        var top = 0;
        foreach (var m in maps)
        {
            var v = Volatile.Read(ref m.Cell.Value);
            var i = m.Channel - 1;
            if (m.Wide) { data[i] = (byte)(v >> 8); data[i + 1] = (byte)(v & 0xFF); top = Math.Max(top, i + 2); }
            else { data[i] = ArtNetCodec.ToByte(v); top = Math.Max(top, i + 1); }
        }
        return ArtNetCodec.EncodeDmx(universe, sequence, data.AsSpan(0, top));
    }

    private async Task SendLoop(int rate, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / rate));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                UdpClient? s; IPEndPoint? to;
                lock (_gate) { s = _sender; to = _target; }
                if (s == null || to == null) return;
                foreach (var u in _frames.Keys)
                {
                    var seq = ++_seq[u]; if (seq == 0) seq = ++_seq[u]; // 1-255; 0 would turn sequencing off
                    var p = BuildFrame(u, seq);
                    if (p == null) continue;
                    try { await s.SendAsync(p, p.Length, to); Interlocked.Increment(ref _sent); lock (_gate) _lastOut = DateTimeOffset.Now; }
                    catch (SocketException e) { lock (_gate) _error = "Send failed: " + e.Message; }
                    catch (ObjectDisposedException) { return; }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // Called with the lock held.
    private void Stop()
    {
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
        foreach (var (n, h) in _stateHooks) _processor.Slot(n).StateChanged -= h;
        _stateHooks.Clear();
        _cts?.Cancel(); _cts = null;
        try { _sender?.Dispose(); } catch { }
        try { _listener?.Dispose(); } catch { }
        _sender = null; _listener = null; _target = null;
        _frames = new(); _recv = [];
        lock (_lastRecv) _lastRecv.Clear();
    }

    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; Stop(); } }
}
