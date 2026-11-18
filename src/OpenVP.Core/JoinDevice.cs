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

/// <summary>
/// A device that talks to a program through joins: a touch panel, XPanel, CH5 project or EISC.
/// One instance per IP ID per slot. Transports (WebSocket, CIP) attach to it; program runtimes
/// (SIMPL#, SIMPL Windows) drive it.
/// </summary>
public sealed class JoinDevice
{
    private readonly ConcurrentDictionary<JoinKey, object> _toDevice = new();
    private readonly ConcurrentDictionary<JoinKey, object> _fromDevice = new();
    private int _connections;

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

    /// <summary>Raised when the program sets a feedback join. Transports subscribe.</summary>
    public event Action<JoinDevice, JoinKey, object>? FeedbackChanged;

    /// <summary>Raised when a device sends a press/value. Program runtimes subscribe.</summary>
    public event Action<JoinDevice, JoinKey, object>? InputReceived;

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
        var old = GetFeedback(key);
        // Serials fire on every set. Anything else that hasn't changed needs no write (interlocks and
        // buffers re-set many outputs to the value they already hold).
        if (key.Kind != SigKind.Serial && SigValue.AreEqual(old, value)) return;
        _toDevice[key] = value;
        FeedbackChanged?.Invoke(this, key, value);
    }

    /// <summary>Transport side: the device pressed a button or sent a value.</summary>
    public void ReceiveInput(JoinKey key, object value)
    {
        value = SigValue.Coerce(key.Kind, value);
        var old = GetInput(key);
        _fromDevice[key] = value;
        if (!SigValue.AreEqual(old, value) || key.Kind == SigKind.Serial)
            InputReceived?.Invoke(this, key, value);
    }

    public void AttachConnection()
    {
        if (Interlocked.Increment(ref _connections) == 1)
            OnlineChanged?.Invoke(this, true);
    }

    public void DetachConnection()
    {
        var n = Interlocked.Decrement(ref _connections);
        if (n < 0) { Interlocked.Exchange(ref _connections, 0); return; }
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
