using System.Diagnostics;

namespace OpenVP.Core;

/// <summary>
/// Marks the current thread as "inside a debugger force", so the recorder doesn't also capture the device input a force
/// can cause (forcing <c>IPID03.out.d1</c> in a SIMPL# slot calls <see cref="JoinDevice.ReceiveInput(JoinKey, object)"/>).
/// The force is recorded once, as a force.
/// </summary>
internal static class ForceScope
{
    [ThreadStatic] private static int _depth;
    public static bool Active => _depth > 0;
    public static IDisposable Enter() { _depth++; return new Exit(); }
    private sealed class Exit : IDisposable { public void Dispose() => _depth--; }
}

/// <summary>
/// Follows every device in a slot, including ones that register later or are registered again after a reload,
/// and reports their inputs and feedback. Handlers run on whatever thread the event comes from (a panel's socket, the slot
/// thread, another slot's thread for EISC) and must be cheap and thread-safe.
/// </summary>
internal sealed class DeviceTap : IDisposable
{
    private readonly ProgramSlot _slot;
    private readonly Action<JoinDevice, JoinKey, object, InputSource>? _onInput;
    private readonly Action<JoinDevice, JoinKey, object>? _onFeedback;
    private readonly HashSet<JoinDevice> _attached = new();
    private readonly object _gate = new();
    private bool _disposed;

    public DeviceTap(ProgramSlot slot, Action<JoinDevice, JoinKey, object, InputSource>? onInput, Action<JoinDevice, JoinKey, object>? onFeedback)
    {
        _slot = slot;
        _onInput = onInput;
        _onFeedback = onFeedback;
        slot.Processor.DeviceRegistered += Registered;
        slot.Processor.DeviceUnregistered += Unregistered;
        foreach (var d in slot.Devices) Attach(d);
    }

    private void Registered(JoinDevice d) { if (d.Slot == _slot.Number) Attach(d); }
    private void Unregistered(JoinDevice d) { if (d.Slot == _slot.Number) Detach(d); }

    private void Attach(JoinDevice d)
    {
        lock (_gate)
        {
            if (_disposed || !_attached.Add(d)) return;
            if (_onInput != null) d.InputObserved += _onInput;
            if (_onFeedback != null) d.FeedbackChanged += _onFeedback;
        }
    }

    private void Detach(JoinDevice d)
    {
        lock (_gate)
        {
            if (!_attached.Remove(d)) return;
            if (_onInput != null) d.InputObserved -= _onInput;
            if (_onFeedback != null) d.FeedbackChanged -= _onFeedback;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _slot.Processor.DeviceRegistered -= Registered;
            _slot.Processor.DeviceUnregistered -= Unregistered;
            foreach (var d in _attached)
            {
                if (_onInput != null) d.InputObserved -= _onInput;
                if (_onFeedback != null) d.FeedbackChanged -= _onFeedback;
            }
            _attached.Clear();
        }
    }

    /// <summary>Every device's feedback right now, for the start and end of a recording or replay.</summary>
    public static List<RecordedDevice> Snapshot(ProgramSlot slot, bool inputs = false) =>
        slot.Devices.OrderBy(d => d.IpId).Select(d => new RecordedDevice
        {
            Ip = d.IpId,
            Type = d.Type,
            Joins = (inputs ? d.InputSnapshot() : d.FeedbackSnapshot())
                .OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.SmartObject).ThenBy(p => p.Key.Join)
                .Select(p => RecordedJoin.From(p.Key, p.Value)).ToList()
        }).ToList();
}

/// <summary>
/// Captures what a slot's program is sent from outside (panel and EISC inputs, signals forced in the debugger) and what it
/// sends back (feedback), with times relative to the start. Created and owned by <see cref="SlotRecordings"/>; one per slot at a time.
/// </summary>
public sealed class SlotRecorder : IDisposable
{
    private readonly ProgramSlot _slot;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private readonly List<RecordedInput> _inputs = new();
    private readonly List<RecordedForce> _forces = new();
    private readonly List<RecordedFeedback> _feedback = new();
    private readonly List<RecordedDevice> _start, _startInputs;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly string? _program, _programFile, _programKind;
    private DeviceTap? _tap;
    private int _seq;
    private bool _stopped, _limitReached;
    private long _endMs;

    internal SlotRecorder(ProgramSlot slot)
    {
        _slot = slot;
        _program = slot.Runtime?.ProgramName;
        _programFile = slot.ProgramPath is null ? null : Path.GetFileName(slot.ProgramPath);
        _programKind = slot.Runtime?.Kind;
        _start = DeviceTap.Snapshot(slot);
        _startInputs = DeviceTap.Snapshot(slot, inputs: true);
        _tap = new DeviceTap(slot, OnInput, OnFeedback);
        _slot.ActiveRecorder = this;
        _clock.Restart();
    }

    public DateTimeOffset StartedAt => _startedAt;
    public long ElapsedMs => _stopped ? _endMs : _clock.ElapsedMilliseconds;
    public bool LimitReached => Volatile.Read(ref _limitReached);

    /// <summary>Counts so far: panel inputs, EISC inputs, forces, feedback.</summary>
    public RecordingCounts Counts
    {
        get
        {
            lock (_gate)
                return new RecordingCounts
                {
                    Inputs = _inputs.Count(i => i.Source == RecordedInput.PanelSource),
                    EiscInputs = _inputs.Count(i => i.Source == RecordedInput.EiscSource),
                    Forces = _forces.Count,
                    Feedback = _feedback.Count
                };
        }
    }

    /// <summary>Inputs and forces captured (the number the console shows).</summary>
    public int InputCount { get { lock (_gate) return _inputs.Count + _forces.Count; } }

    internal long Now() => _clock.ElapsedMilliseconds;

    private void OnInput(JoinDevice d, JoinKey key, object value, InputSource source)
    {
        if (ForceScope.Active) return;
        lock (_gate)
        {
            if (_stopped) return;
            if (_inputs.Count >= Recording.MaxEvents) { _limitReached = true; return; }
            _inputs.Add(new RecordedInput
            {
                T = _clock.ElapsedMilliseconds, Seq = _seq++, Ip = d.IpId,
                Source = source == InputSource.Eisc ? RecordedInput.EiscSource : RecordedInput.PanelSource,
                Kind = JoinKey.KindLetter(key.Kind).ToString(), Join = key.Join, So = key.SmartObject, Value = value
            });
        }
    }

    private void OnFeedback(JoinDevice d, JoinKey key, object value)
    {
        lock (_gate)
        {
            if (_stopped) return;
            if (_feedback.Count >= Recording.MaxEvents) { _limitReached = true; return; }
            _feedback.Add(new RecordedFeedback
            {
                T = _clock.ElapsedMilliseconds, Seq = _seq++, Ip = d.IpId,
                Kind = JoinKey.KindLetter(key.Kind).ToString(), Join = key.Join, So = key.SmartObject, Value = value
            });
        }
    }

    /// <summary>A signal was forced (the time is when the force was made, taken from <see cref="Now"/>).</summary>
    internal void RecordForce(long t, string signal, bool pulse, object? value)
    {
        lock (_gate)
        {
            if (_stopped) return;
            if (_forces.Count >= Recording.MaxEvents) { _limitReached = true; return; }
            _forces.Add(new RecordedForce { T = t, Seq = _seq++, Signal = signal, Pulse = pulse, Value = pulse ? null : value });
        }
    }

    /// <summary>Stops capturing and returns the recording (no ID or name yet; <see cref="RecordingStore"/> adds them).</summary>
    /// <param name="deriveEnd">Work the final state out from the start state and the feedback changes instead of reading the devices
    /// (they are gone once the program has been unloaded).</param>
    internal Recording Finish(string endedBy, bool deriveEnd = false)
    {
        Recording r;
        lock (_gate)
        {
            if (_stopped) throw new InvalidOperationException("Already stopped.");
            _stopped = true;
            _endMs = _clock.ElapsedMilliseconds;
            _slot.ActiveRecorder = null;
            _tap?.Dispose();
            _tap = null;
            r = new Recording
            {
                Slot = _slot.Number, Program = _program, ProgramFile = _programFile, ProgramKind = _programKind,
                RecordedAt = _startedAt, DurationMs = _endMs,
                EndedBy = _limitReached ? "limit reached" : endedBy,
                Start = _start, StartInputs = _startInputs, End = deriveEnd ? new() : DeviceTap.Snapshot(_slot),
                Inputs = _inputs.ToList(), Forces = _forces.ToList(), Feedback = _feedback.ToList()
            };
        }
        if (deriveEnd) r.End = FeedbackState.ToDevices(FeedbackState.Apply(FeedbackState.ToMap(r.Start), r.Feedback), r.Start);
        r.Name = RecordingIds.DefaultName(_program, _startedAt);
        r.UpdateCounts();
        return r;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            if (ReferenceEquals(_slot.ActiveRecorder, this)) _slot.ActiveRecorder = null;
            _tap?.Dispose();
            _tap = null;
        }
    }
}
