using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OpenVP.Core;

/// <summary>
/// A running simulator: its <see cref="SimEngine"/> plus whatever carries it. For a COM simulator that is the program's COM
/// port (<see cref="PortChannel"/>); for a TCP or UDP simulator it is a listener on 127.0.0.1 that the program connects to,
/// directly or through a network redirect. Listeners bind to loopback only.
/// </summary>
public sealed class SimHost : IDisposable
{
    private sealed class Conn
    {
        public int Id; public TcpClient? Tcp; public IPEndPoint? Udp; public DateTimeOffset Since = DateTimeOffset.Now;
    }

    private readonly SlotIo _io;
    private readonly ConcurrentDictionary<int, Conn> _conns = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private TcpListener? _tcp;
    private UdpClient? _udp;
    private int _nextConn;
    private long _in, _out;
    private SimDef _def;
    private bool _comActive;

    internal SimHost(SlotIo io, SimDef def, ISimClock? clock)
    {
        _io = io;
        _def = def;
        Engine = new SimEngine(def, Send, clock);
    }

    public SimDef Def => _def;
    internal void SetDef(SimDef d) => _def = d;
    public SimEngine Engine { get; }
    /// <summary>Why it isn't listening (port in use...), or null.</summary>
    public string? Error { get; private set; }
    /// <summary>The port it really listens on (for tcp/udp), or 0.</summary>
    public int ListenPort { get; private set; }
    public bool Listening => ListenPort > 0 && (_tcp != null || _udp != null);
    public long BytesIn => Interlocked.Read(ref _in);
    public long BytesOut => Interlocked.Read(ref _out);
    public int Connections => _def.Kind == "tcp" ? _conns.Count : 0;
    /// <summary>For a COM simulator: the program has the port open right now.</summary>
    public bool ComActive => _comActive;

    public bool MatchesCom(string channelKey)
    {
        if (_def.Kind != "com" || !_def.Enabled || _def.Com.Length == 0) return false;
        var want = _def.Com.Trim();
        return string.Equals(want, channelKey, StringComparison.OrdinalIgnoreCase)
            || channelKey.EndsWith("/" + want, StringComparison.OrdinalIgnoreCase);
    }

    public void Start()
    {
        lock (_gate)
        {
            Error = null;
            if (!_def.Enabled) return;
            switch (_def.Kind)
            {
                case "tcp": StartTcp(); break;
                case "udp": StartUdp(); break;
                case "com":
                    if (_io.Ports.Find(_def.Com) is { Open: true } ch && MatchesCom(ch.Key)) ComOpened();
                    break;
            }
        }
    }

    private void StartTcp()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _tcp = new TcpListener(IPAddress.Loopback, _def.Port);
            _tcp.Start();
            ListenPort = ((IPEndPoint)_tcp.LocalEndpoint).Port;
            Engine.Start();
            _ = AcceptLoop(_tcp, _cts.Token);
        }
        catch (Exception e)
        {
            Error = $"Couldn't listen on 127.0.0.1:{_def.Port}: {e.Message}";
            _tcp?.Stop(); _tcp = null; ListenPort = 0;
        }
    }

    private void StartUdp()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, _def.Port));
            ListenPort = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
            Engine.Start();
            _ = UdpLoop(_udp, _cts.Token);
        }
        catch (Exception e)
        {
            Error = $"Couldn't listen on 127.0.0.1:{_def.Port}: {e.Message}";
            _udp?.Dispose(); _udp = null; ListenPort = 0;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel(); _cts = null;
            try { _tcp?.Stop(); } catch { }
            try { _udp?.Dispose(); } catch { }
            _tcp = null; _udp = null; ListenPort = 0;
            foreach (var c in _conns.Values) { try { c.Tcp?.Dispose(); } catch { } }
            _conns.Clear();
            Engine.Stop();
            _comActive = false;
        }
    }

    // ---------------------------------------------------------------- COM

    internal void ComOpened()
    {
        if (_def.Kind != "com" || _comActive) return;
        _comActive = true;
        Engine.Start();
        Engine.Connected(0);
    }

    internal void ComClosed()
    {
        if (!_comActive) return;
        _comActive = false;
        Engine.Stop();
        Engine.Disconnected(0);
    }

    internal void ComTx(string text) { if (_comActive) { Interlocked.Add(ref _in, text.Length); Engine.Receive(0, text); } }

    // ---------------------------------------------------------------- sockets

    private async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(ct); }
            catch { break; }
            var conn = new Conn { Id = Interlocked.Increment(ref _nextConn), Tcp = c };
            c.NoDelay = true;
            _conns[conn.Id] = conn;
            _ = Serve(conn, ct);
        }
    }

    private async Task Serve(Conn conn, CancellationToken ct)
    {
        try
        {
            Engine.Connected(conn.Id);
            var stream = conn.Tcp!.GetStream();
            var buf = new byte[4096];
            while (!ct.IsCancellationRequested)
            {
                var n = await stream.ReadAsync(buf, ct);
                if (n == 0) break;
                Interlocked.Add(ref _in, n);
                Engine.Receive(conn.Id, Encoding.Latin1.GetString(buf, 0, n));
            }
        }
        catch { }
        finally
        {
            _conns.TryRemove(conn.Id, out _);
            Engine.Disconnected(conn.Id);
            try { conn.Tcp?.Dispose(); } catch { }
        }
    }

    private async Task UdpLoop(UdpClient u, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await u.ReceiveAsync(ct); }
            catch { break; }
            var conn = _conns.Values.FirstOrDefault(c => c.Udp != null && c.Udp.Equals(r.RemoteEndPoint));
            if (conn == null)
            {
                conn = new Conn { Id = Interlocked.Increment(ref _nextConn), Udp = r.RemoteEndPoint };
                _conns[conn.Id] = conn;
                if (_conns.Count > 50) { var old = _conns.Values.OrderBy(c => c.Since).First(); _conns.TryRemove(old.Id, out _); }
                Engine.Connected(conn.Id);
            }
            Interlocked.Add(ref _in, r.Buffer.Length);
            Engine.Receive(conn.Id, Encoding.Latin1.GetString(r.Buffer));
        }
    }

    /// <summary>Where the engine's output goes.</summary>
    private void Send(int connection, string text)
    {
        if (text.Length == 0) return;
        var bytes = Encoding.Latin1.GetBytes(text);
        switch (_def.Kind)
        {
            case "com":
                if (_io.Ports.Find(_def.Com) is { } ch && ch.ToProgramRx(text, "simulator")) Interlocked.Add(ref _out, text.Length);
                break;
            case "tcp":
                foreach (var c in connection == 0 ? _conns.Values.ToList() : _conns.Values.Where(c => c.Id == connection).ToList())
                {
                    try { c.Tcp?.GetStream().Write(bytes, 0, bytes.Length); Interlocked.Add(ref _out, bytes.Length); } catch { }
                }
                break;
            case "udp":
                var u = _udp;
                if (u == null) break;
                foreach (var c in connection == 0 ? _conns.Values.ToList() : _conns.Values.Where(c => c.Id == connection).ToList())
                {
                    try { if (c.Udp != null) { u.Send(bytes, bytes.Length, c.Udp); Interlocked.Add(ref _out, bytes.Length); } } catch { }
                }
                break;
        }
    }

    public void Dispose() { Stop(); Engine.Dispose(); }
}

/// <summary>
/// The simulators of one slot. They are saved in <c>devices/simulators.json</c> in the slot's folder (so snapshots include
/// them) and started when OpenVP starts, as long as they are enabled.
/// </summary>
public sealed class SlotSimulators : IDisposable
{
    public const int MaxPerSlot = 20;
    private readonly SlotIo _io;
    private readonly string _file;
    private readonly object _gate = new();
    private readonly List<SimHost> _hosts = new();
    private readonly ISimClock? _clock;

    internal SlotSimulators(SlotIo io, string file, ISimClock? clock = null)
    {
        _io = io;
        _file = file;
        _clock = clock;
        Load();
    }

    public IReadOnlyList<SimHost> All { get { lock (_gate) return _hosts.ToList(); } }

    public SimHost? Find(string idOrName)
    {
        lock (_gate)
            return _hosts.FirstOrDefault(h => h.Def.Id == idOrName)
                ?? _hosts.FirstOrDefault(h => string.Equals(h.Def.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public SimHost Add(SimDef def)
    {
        Prepare(def);
        SimHost host;
        lock (_gate)
        {
            if (_hosts.Count >= MaxPerSlot) throw new ArgumentException($"At most {MaxPerSlot} simulators per slot.");
            def.Id = NewId();
            host = new SimHost(_io, def, _clock);
            _hosts.Add(host);
            Save();
        }
        host.Start();
        return host;
    }

    /// <summary>Replace a simulator's definition. The state it has now is kept unless <paramref name="resetState"/>.</summary>
    public SimHost? Update(string id, SimDef def, bool resetState = false)
    {
        Prepare(def);
        SimHost? old;
        lock (_gate) old = _hosts.FirstOrDefault(h => h.Def.Id == id);
        if (old == null) return null;
        def.Id = id;
        var same = old.Def.Kind == def.Kind && old.Def.Com == def.Com && old.Def.Port == def.Port && old.Def.Enabled == def.Enabled;
        if (same)
        {
            old.Engine.Update(def);
            old.SetDef(def);
            if (resetState) old.Engine.ResetState();
            lock (_gate) Save();
            return old;
        }
        SimHost host;
        var state = old.Engine.State;
        lock (_gate)
        {
            var i = _hosts.IndexOf(old);
            host = new SimHost(_io, def, _clock);
            if (!resetState) foreach (var kv in state) host.Engine.SetState(kv.Key, kv.Value);
            _hosts[i] = host;
            Save();
        }
        old.Dispose();
        host.Start();
        return host;
    }

    public bool Remove(string id)
    {
        SimHost? h;
        lock (_gate)
        {
            h = _hosts.FirstOrDefault(x => x.Def.Id == id);
            if (h == null) return false;
            _hosts.Remove(h);
            Save();
        }
        h.Dispose();
        return true;
    }

    private static void Prepare(SimDef def)
    {
        def.Name = (def.Name ?? "").Trim();
        if (def.Name.Length == 0) def.Name = "Simulator";
        def.Kind = (def.Kind ?? "com").Trim().ToLowerInvariant();
        def.Com = (def.Com ?? "").Trim();
        SimEngine.Validate(def);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..6];

    /// <summary>Where a <c>sim:ID</c> redirect target listens, if it does.</summary>
    internal (string Host, int Port)? Endpoint(string idOrName) =>
        Find(idOrName) is { Listening: true } h ? ("127.0.0.1", h.ListenPort) : null;

    public void StartAll() { foreach (var h in All) h.Start(); }

    // ---- COM hooks (called by PortChannel / PortHub) ----

    internal void OnComTx(PortChannel ch, string text)
    {
        foreach (var h in All) if (h.MatchesCom(ch.Key)) h.ComTx(text);
    }

    internal void OnComOpened(PortChannel ch)
    {
        foreach (var h in All) if (h.MatchesCom(ch.Key)) h.ComOpened();
    }

    internal void OnComClosed(PortChannel ch)
    {
        foreach (var h in All) if (h.MatchesCom(ch.Key)) h.ComClosed();
    }

    // ---------------------------------------------------------------- saving

    private sealed class FileShape { public List<SimDef> Simulators { get; set; } = new(); }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(new FileShape { Simulators = _hosts.Select(h => h.Def).ToList() }, SlotIo.Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Couldn't save the simulators: {e.Message}", e);
        }
    }

    /// <summary>Read the file again (after a snapshot was restored). Running simulators stop; enabled ones start.</summary>
    public void Load()
    {
        List<SimHost> old;
        lock (_gate) { old = _hosts.ToList(); _hosts.Clear(); }
        foreach (var h in old) h.Dispose();
        try
        {
            if (!File.Exists(_file)) return;
            var f = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_file), SlotIo.Json);
            lock (_gate)
                foreach (var d in f?.Simulators ?? new())
                {
                    try { Prepare(d); if (d.Id.Length == 0) d.Id = NewId(); _hosts.Add(new SimHost(_io, d, _clock)); }
                    catch (ArgumentException) { }
                }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        foreach (var h in All) h.Dispose();
    }
}
