using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>Where a value change came from.</summary>
public enum JoinDirection
{
    /// <summary>Program to device: feedback (lights a button, sets text).</summary>
    ToDevice,
    /// <summary>Device to program: a press, slider value or text entry.</summary>
    FromDevice
}

/// <summary>Who sent an input to a device: a panel (browser, CIP, or a program's own device) or the other end of an EISC link.</summary>
public enum InputSource { Panel, Eisc }

/// <summary>
/// A device that talks to a program through joins: a touch panel, XPanel, CH5 project or EISC.
/// One instance per IP ID per slot. Transports (WebSocket, CIP) attach to it; program runtimes
/// (SIMPL#, SIMPL Windows) drive it.
/// </summary>
public sealed class JoinDevice
{
    private readonly ConcurrentDictionary<JoinKey, object> _toDevice = new();
    private readonly ConcurrentDictionary<JoinKey, object> _fromDevice = new();
    private readonly ConcurrentDictionary<string, DeviceConnection> _clients = new();
    private readonly object _connGate = new();
    private int _connections;
    private long _totalConnects, _inputsReceived, _feedbackSent;

    public JoinDevice(int slot, uint ipId, string type, string description)
    {
        Slot = slot;
        IpId = ipId;
        Type = type;
        Description = description;
    }

    public int Slot { get; }
    public uint IpId { get; }
    public string Type { get; set; }
    public string Description { get; set; }

    /// <summary>True while at least one transport connection is attached.</summary>
    public bool Online => _connections > 0;

    public int ConnectionCount => _connections;

    /// <summary>Who is attached right now: address, hostname, what kind of client, counters.</summary>
    public IReadOnlyList<DeviceConnection> Connections => _clients.Values.OrderBy(c => c.ConnectedAt).ToList();

    public DeviceConnection? FindConnection(string id) => _clients.TryGetValue(id, out var c) ? c : null;

    /// <summary>When the device last came online or went offline (null = never).</summary>
    public DateTimeOffset? LastOnline { get; private set; }
    public DateTimeOffset? LastOffline { get; private set; }
    public DateTimeOffset RegisteredAt { get; } = DateTimeOffset.Now;
    /// <summary>Connections made to this IP ID since it was registered.</summary>
    public long TotalConnects => Interlocked.Read(ref _totalConnects);
    public long InputsReceived => Interlocked.Read(ref _inputsReceived);
    public long FeedbackSent => Interlocked.Read(ref _feedbackSent);
    public int FeedbackJoinCount => _toDevice.Count;
    public int InputJoinCount => _fromDevice.Count;

    /// <summary>Raised when the program sets a feedback join. Transports subscribe.</summary>
    public event Action<JoinDevice, JoinKey, object>? FeedbackChanged;

    /// <summary>Raised when a device sends a press/value. Program runtimes subscribe.</summary>
    public event Action<JoinDevice, JoinKey, object>? InputReceived;

    /// <summary>
    /// Raised for every input just before <see cref="InputReceived"/>, saying where it came from. The recorder uses it, so
    /// its timestamp comes before anything the program does in response. Handlers can run on any thread and must be cheap.
    /// </summary>
    public event Action<JoinDevice, JoinKey, object, InputSource>? InputObserved;

    public event Action<JoinDevice, bool>? OnlineChanged;

    public object GetFeedback(JoinKey key) => _toDevice.TryGetValue(key, out var v) ? v : SigValue.Default(key.Kind);

    public object GetInput(JoinKey key) => _fromDevice.TryGetValue(key, out var v) ? v : SigValue.Default(key.Kind);

    /// <summary>Snapshot of every feedback value set so far; sent to a transport when it connects.</summary>
    public IReadOnlyDictionary<JoinKey, object> FeedbackSnapshot() => new Dictionary<JoinKey, object>(_toDevice);

    public IReadOnlyDictionary<JoinKey, object> InputSnapshot() => new Dictionary<JoinKey, object>(_fromDevice);

    /// <summary>Program side: set a feedback join. Fires only on change, like a real processor.</summary>
    public void SetFeedback(JoinKey key, object value)
    {
        value = SigValue.Coerce(key.Kind, value);
        var stored = _toDevice.TryGetValue(key, out var old);
        if (!stored) old = SigValue.Default(key.Kind);
        var changed = !SigValue.AreEqual(old!, value);
        // Serials fire on every set. A value that is already stored needs no write (interlocks and buffers
        // re-set many outputs to the value they already hold). The first set of a join is always stored,
        // even at its default, so a snapshot lists every join the program drives: a panel that reconnects
        // after a reload relies on it to clear what the previous program left lit.
        if (stored && !changed && key.Kind != SigKind.Serial) return;
        _toDevice[key] = value;
        if (changed || key.Kind == SigKind.Serial)
        {
            Interlocked.Increment(ref _feedbackSent);
            FeedbackChanged?.Invoke(this, key, value);
        }
    }

    /// <summary>Transport side: the device pressed a button or sent a value.</summary>
    public void ReceiveInput(JoinKey key, object value) => ReceiveInput(key, value, InputSource.Panel);

    /// <summary>Transport side, saying where the input came from (a panel, or the other end of an EISC link).</summary>
    public void ReceiveInput(JoinKey key, object value, InputSource source)
    {
        value = SigValue.Coerce(key.Kind, value);
        var old = GetInput(key);
        _fromDevice[key] = value;
        if (!SigValue.AreEqual(old, value) || key.Kind == SigKind.Serial)
        {
            Interlocked.Increment(ref _inputsReceived);
            InputObserved?.Invoke(this, key, value, source);
            InputReceived?.Invoke(this, key, value);
        }
    }

    /// <summary>Attach a client without details (kept for callers that only count connections).</summary>
    public void AttachConnection() => AttachConnection(new DeviceConnection("Internal"));

    /// <summary>Detach one client attached with <see cref="AttachConnection()"/>.</summary>
    public void DetachConnection()
    {
        DeviceConnection? anon;
        lock (_connGate) anon = _clients.Values.Where(c => c.Transport == "Internal").OrderBy(c => c.ConnectedAt).FirstOrDefault();
        if (anon != null) DetachConnection(anon);
        else DetachCore();
    }

    /// <summary>A transport attached: the device is online while at least one client is attached.</summary>
    public void AttachConnection(DeviceConnection client)
    {
        bool first;
        lock (_connGate)
        {
            if (!_clients.TryAdd(client.Id, client)) return;
            first = ++_connections == 1;
            _totalConnects++;
            if (first) LastOnline = DateTimeOffset.Now;
        }
        if (first) OnlineChanged?.Invoke(this, true);
    }

    public void DetachConnection(DeviceConnection client)
    {
        lock (_connGate)
            if (!_clients.TryRemove(client.Id, out _)) return;
        DetachCore();
    }

    private void DetachCore()
    {
        int n;
        lock (_connGate)
        {
            n = --_connections;
            if (n < 0) { _connections = 0; return; }
            if (n == 0) LastOffline = DateTimeOffset.Now;
        }
        if (n == 0)
        {
            // Release any held digitals so a dropped panel doesn't leave a press stuck high.
            foreach (var (k, v) in _fromDevice)
                if (k.Kind == SigKind.Digital && v is true)
                    ReceiveInput(k, false);
            OnlineChanged?.Invoke(this, false);
        }
    }

    public override string ToString() => $"IPID {IpId:X2} ({Type}) slot {Slot}";
}
