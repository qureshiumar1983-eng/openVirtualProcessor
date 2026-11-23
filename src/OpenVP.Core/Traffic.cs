using System.Text;

namespace OpenVP.Core;

/// <summary>Which way the data went, from the program's point of view: <c>Tx</c> is sent by the program, <c>Rx</c> is received by it.</summary>
public enum TrafficDir { Tx, Rx }

/// <summary>One piece of traffic. <see cref="Text"/> holds the bytes as Latin-1 text, as the program sees them.</summary>
public sealed record TrafficEntry(long Seq, DateTimeOffset Time, TrafficDir Dir, string Text, string Origin);

/// <summary>
/// The last bytes of a COM port or socket, newest last. <see cref="Capacity"/> entries are kept; entries longer than
/// <see cref="MaxText"/> are cut. With <see cref="Reduced"/> (nobody is watching) the limits are much smaller, so an idle
/// socket costs next to nothing.
/// </summary>
public sealed class TrafficBuffer
{
    private readonly object _gate = new();
    private readonly LinkedList<TrafficEntry> _entries = new();
    private long _seq;

    public int Capacity { get; init; } = 1000;
    public int MaxText { get; init; } = 4096;
    public int ReducedCapacity { get; init; } = 30;
    public int ReducedMaxText { get; init; } = 256;
    /// <summary>True while nobody is watching: keep only a few short entries.</summary>
    public Func<bool>? Reduced { get; init; }

    public long LastSeq { get { lock (_gate) return _seq; } }

    public TrafficEntry Add(TrafficDir dir, string text, string origin)
    {
        var reduced = Reduced?.Invoke() ?? false;
        var max = reduced ? ReducedMaxText : MaxText;
        if (text.Length > max) text = text[..max];
        lock (_gate)
        {
            var e = new TrafficEntry(++_seq, DateTimeOffset.Now, dir, text, origin);
            _entries.AddLast(e);
            var cap = reduced ? ReducedCapacity : Capacity;
            while (_entries.Count > cap) _entries.RemoveFirst();
            return e;
        }
    }

    /// <summary>Entries after <paramref name="afterSeq"/> (0 for all), oldest first.</summary>
    public List<TrafficEntry> Since(long afterSeq = 0, int max = 1000)
    {
        lock (_gate) return _entries.Where(e => e.Seq > afterSeq).TakeLast(max).ToList();
    }

    public void Clear() { lock (_gate) _entries.Clear(); }
}

/// <summary>
/// A COM port of a program (IR ports aren't listed): what the program sent (TX) and received (RX), and a way to give it RX.
/// A simulator attached to the port answers what the program sends.
/// </summary>
public sealed class PortChannel
{
    private readonly SlotIo _io;
    private long _tx, _rx;

    internal PortChannel(SlotIo io, string key, string label)
    {
        _io = io;
        Key = key;
        Label = label;
        Traffic = new TrafficBuffer { Capacity = 2000 };
    }

    /// <summary>The path as comports.json spells it, e.g. <c>IP-ID-11/Slot-08/COM-01</c>.</summary>
    public string Key { get; }
    public string Label { get; }
    public TrafficBuffer Traffic { get; }
    public bool Open { get; internal set; }
    public DateTimeOffset? OpenedAt { get; internal set; }
    public long TxBytes => Interlocked.Read(ref _tx);
    public long RxBytes => Interlocked.Read(ref _rx);
    internal Action<string>? ToProgram { get; set; }

    /// <summary>The program sent text out of the port. Recorded, then handed to a simulator attached to it.</summary>
    public void FromProgram(string text)
    {
        if (text.Length == 0) return;
        Interlocked.Add(ref _tx, text.Length);
        Traffic.Add(TrafficDir.Tx, text, "program");
        _io.Simulators.OnComTx(this, text);
    }

    /// <summary>Text arrived for the program (from a simulator, the terminal or a real device): recorded and delivered.
    /// False if the port isn't open (the program isn't running).</summary>
    public bool ToProgramRx(string text, string origin)
    {
        if (text.Length == 0) return true;
        var deliver = ToProgram;
        if (!Open || deliver == null) return false;
        Interlocked.Add(ref _rx, text.Length);
        Traffic.Add(TrafficDir.Rx, text, origin);
        deliver(text);
        return true;
    }

    /// <summary>Note text that reached the program some other way (a serial device server), so the terminal shows it.</summary>
    public void RecordRx(string text, string origin = "device")
    {
        if (text.Length == 0) return;
        Interlocked.Add(ref _rx, text.Length);
        Traffic.Add(TrafficDir.Rx, text, origin);
    }
}

/// <summary>The COM ports a slot's program has opened. Runtimes call <see cref="Open"/> and <see cref="Close"/>.</summary>
public sealed class PortHub
{
    private readonly SlotIo _io;
    private readonly object _gate = new();
    private readonly Dictionary<string, PortChannel> _ports = new(StringComparer.OrdinalIgnoreCase);

    internal PortHub(SlotIo io) { _io = io; }

    public IReadOnlyList<PortChannel> All { get { lock (_gate) return _ports.Values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList(); } }

    public PortChannel? Find(string key)
    {
        lock (_gate)
        {
            if (_ports.TryGetValue(key, out var p)) return p;
            // "COM-01" on its own matches the port of that name if only one has it.
            var tail = _ports.Values.Where(x => x.Key.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase)).ToList();
            return tail.Count == 1 ? tail[0] : null;
        }
    }

    /// <summary>A program started a COM port: list it and get the channel that carries its traffic. <paramref name="received"/>
    /// delivers text to the program.</summary>
    public PortChannel Open(string key, string label, Action<string> received)
    {
        PortChannel ch;
        lock (_gate)
        {
            if (!_ports.TryGetValue(key, out ch!)) _ports[key] = ch = new PortChannel(_io, key, label);
        }
        ch.ToProgram = received;
        ch.Open = true;
        ch.OpenedAt = DateTimeOffset.Now;
        _io.Simulators.OnComOpened(ch);
        return ch;
    }

    public void Close(string key)
    {
        PortChannel? ch;
        lock (_gate) _ports.TryGetValue(key, out ch);
        if (ch == null) return;
        ch.Open = false;
        ch.ToProgram = null;
        _io.Simulators.OnComClosed(ch);
    }

    /// <summary>A program was unloaded: forget its ports.</summary>
    public void Clear()
    {
        List<PortChannel> all;
        lock (_gate) { all = _ports.Values.ToList(); _ports.Clear(); }
        foreach (var p in all) { p.Open = false; p.ToProgram = null; _io.Simulators.OnComClosed(p); }
    }
}

/// <summary>One socket a program opened (a TCP or UDP client or server).</summary>
public sealed class SocketRecord
{
    private readonly SocketTable _table;
    private long _in, _out;

    internal SocketRecord(SocketTable table, long id, string kind, string role)
    {
        _table = table;
        Id = id;
        Kind = kind;
        Role = role;
        Opened = DateTimeOffset.Now;
        Traffic = new TrafficBuffer { Reduced = () => !table.Watched };
    }

    public long Id { get; }
    /// <summary>The API class: <c>TCPClient</c>, <c>TCPServer</c>, <c>UDPServer</c>, <c>SecureTCPClient</c>, <c>SecureTCPServer</c>, <c>SIMPL+ TCP client</c>...</summary>
    public string Kind { get; }
    /// <summary><c>client</c> or <c>server</c>.</summary>
    public string Role { get; }
    public string Protocol { get; init; } = "tcp";
    public string Local { get; set; } = "";
    /// <summary>Where the program asked to connect (or the peer of a server's client).</summary>
    public string Remote { get; set; } = "";
    /// <summary>If a network redirect sent it somewhere else, the address it really connected to.</summary>
    public string? RedirectedTo { get; set; }
    public string State { get; private set; } = "opening";
    public DateTimeOffset Opened { get; }
    public DateTimeOffset? Closed { get; private set; }
    public string? Note { get; set; }
    public long BytesIn => Interlocked.Read(ref _in);
    public long BytesOut => Interlocked.Read(ref _out);
    public TrafficBuffer Traffic { get; }

    public void SetState(string state) { State = state; }
    public void Connected(string? local = null, string? remote = null)
    {
        if (local != null) Local = local;
        if (remote != null) Remote = remote;
        State = Role == "server" ? "listening" : "connected";
    }

    /// <summary>The program sent bytes.</summary>
    public void Sent(byte[] data, int offset, int count)
    {
        if (count <= 0) return;
        Interlocked.Add(ref _out, count);
        Traffic.Add(TrafficDir.Tx, Encoding.Latin1.GetString(data, offset, count), "program");
    }

    /// <summary>The program received bytes.</summary>
    public void Received(byte[] data, int offset, int count)
    {
        if (count <= 0) return;
        Interlocked.Add(ref _in, count);
        Traffic.Add(TrafficDir.Rx, Encoding.Latin1.GetString(data, offset, count), "remote");
    }

    public void Close(string state = "closed")
    {
        if (Closed != null) return;
        Closed = DateTimeOffset.Now;
        State = state;
        _table.Retire(this);
    }
}

/// <summary>
/// Every socket the slot's program has opened, live ones first and the last 200 closed ones after. Counting bytes is always
/// on; the text of what went over each socket is only kept in full while somebody is looking (<see cref="Watch"/>).
/// </summary>
public sealed class SocketTable
{
    private const int KeepClosed = 200;
    private readonly object _gate = new();
    private readonly List<SocketRecord> _all = new();
    private long _nextId;
    private long _watchedUntil;

    /// <summary>Somebody asked to see sockets in the last 30 seconds.</summary>
    public bool Watched => Environment.TickCount64 < Interlocked.Read(ref _watchedUntil);

    /// <summary>The sockets view calls this on each poll.</summary>
    public void Watch() => Interlocked.Exchange(ref _watchedUntil, Environment.TickCount64 + 30_000);

    public SocketRecord Open(string kind, string role, string protocol = "tcp", string local = "", string remote = "")
    {
        lock (_gate)
        {
            var r = new SocketRecord(this, ++_nextId, kind, role) { Protocol = protocol, Local = local, Remote = remote };
            _all.Add(r);
            Trim();
            return r;
        }
    }

    public IReadOnlyList<SocketRecord> All { get { lock (_gate) return _all.ToList(); } }
    public SocketRecord? Find(long id) { lock (_gate) return _all.FirstOrDefault(s => s.Id == id); }

    internal void Retire(SocketRecord r) { lock (_gate) Trim(); }

    private void Trim()
    {
        var closed = _all.Where(s => s.Closed != null).ToList();
        for (var i = 0; i < closed.Count - KeepClosed; i++) _all.Remove(closed[i]);
    }

    /// <summary>The program was unloaded: whatever is still open is gone.</summary>
    public void CloseAll(string state = "closed (program stopped)")
    {
        foreach (var s in All) s.Close(state);
    }

    public void Clear() { lock (_gate) _all.Clear(); }
}
