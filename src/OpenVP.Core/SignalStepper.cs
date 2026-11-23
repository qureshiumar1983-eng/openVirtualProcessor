namespace OpenVP.Core;

/// <summary>Where a stepping slot is stopped: the change that just happened, before anything reacts to it.</summary>
public sealed record StepPause(long Step, DateTimeOffset Time, string Signal, SigKind Kind, object Value, string Source,
    string? Symbol = null, string? Cause = null, int Depth = 0, long Chain = 0, string? Reason = null);

/// <summary>A consistent copy of the stepper's state. Version goes up on every change, so clients can drop stale copies.</summary>
public sealed record StepState(long Version, bool Enabled, bool OnlyBreakSignals, IReadOnlyList<string> BreakSignals, StepPause? Paused,
    IReadOnlyList<string>? Conditions = null);

/// <summary>
/// Step-through debugging for one slot. When enabled, every signal change (or only changes to the
/// chosen break signals) halts the thread that made it, right after the new value is stored and
/// before any logic listening to that signal runs. <see cref="Next"/> lets exactly one change through;
/// <see cref="Run"/> turns stepping off and lets the program run freely again.
/// </summary>
public sealed class SignalStepper
{
    private readonly object _gate = new();
    private readonly HashSet<string> _breakOn = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _enabled; // written under _gate, read lock-free by ProgramSlot.TraceWanted
    private bool _onlyBreakSignals;
    private StepPause? _paused;
    private long _step, _version;
    // Conditional breakpoints: pause when a change to a signal the expression reads makes it true.
    private List<SignalExpression> _conditions = new();
    private Dictionary<string, List<SignalExpression>> _condBySignal = new(StringComparer.Ordinal);
    private string? _forceReason; // a watch asked to stop at the next change

    /// <summary>Where conditions read signal values (set by the slot).</summary>
    public Func<string, object?>? ValueLookup { get; set; }
    public bool HasConditions { get { lock (_gate) return _conditions.Count > 0; } }
    public IReadOnlyList<string> Conditions { get { lock (_gate) return _conditions.Select(c => c.Text).ToList(); } }

    /// <summary>Raised (outside the lock) whenever the mode, break list or paused change changes.</summary>
    public event Action<SignalStepper>? Changed;

    public bool Enabled => _enabled;
    /// <summary>True: pause only on <see cref="BreakSignals"/>. False: pause on every change.</summary>
    public bool OnlyBreakSignals { get { lock (_gate) return _onlyBreakSignals; } }
    public IReadOnlyList<string> BreakSignals { get { lock (_gate) return _breakOn.Order(StringComparer.OrdinalIgnoreCase).ToList(); } }
    public StepPause? Paused { get { lock (_gate) return _paused; } }
    public bool IsPaused { get { lock (_gate) return _paused != null; } }

    public StepState Snapshot()
    {
        lock (_gate) return new StepState(_version, _enabled, _onlyBreakSignals, _breakOn.Order(StringComparer.OrdinalIgnoreCase).ToList(), _paused,
            _conditions.Select(c => c.Text).ToList());
    }

    /// <summary>Change the mode and/or break list. Null arguments leave that setting alone.</summary>
    /// <param name="conditions">Conditional breakpoints (see <see cref="SignalExpression"/>); null leaves them alone. Throws ArgumentException for one that doesn't parse.</param>
    public void Configure(bool? enabled = null, bool? onlyBreakSignals = null, IEnumerable<string>? breakSignals = null, IEnumerable<string>? conditions = null)
    {
        List<SignalExpression>? parsed = null;
        if (conditions != null)
        {
            parsed = new();
            foreach (var c in conditions.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (!SignalExpression.TryParse(c, out var e, out var err)) throw new ArgumentException($"Condition '{c.Trim()}': {err}");
                parsed.Add(e!);
            }
            if (parsed.Count > 50) throw new ArgumentException("At most 50 conditions.");
        }
        lock (_gate)
        {
            if (parsed != null)
            {
                _conditions = parsed;
                _condBySignal = new(StringComparer.Ordinal);
                foreach (var c in parsed) foreach (var sig in c.Signals) { if (!_condBySignal.TryGetValue(sig, out var l)) _condBySignal[sig] = l = new(); l.Add(c); }
            }
            if (onlyBreakSignals is bool o) _onlyBreakSignals = o;
            if (breakSignals != null) { _breakOn.Clear(); _breakOn.UnionWith(breakSignals.Where(s => !string.IsNullOrWhiteSpace(s))); }
            if (enabled is bool e)
            {
                _enabled = e;
                if (!e) { _paused = null; _forceReason = null; Monitor.PulseAll(_gate); }
            }
            _version++;
        }
        Changed?.Invoke(this);
    }

    /// <summary>Stop at the next change: turns stepping on and pauses on the very next signal change (a watch that asked for it).</summary>
    public void BreakNext(string reason)
    {
        lock (_gate) { _enabled = true; _forceReason = reason; _version++; }
        Changed?.Invoke(this);
    }

    /// <summary>Let the paused change through; the next matching change pauses again.</summary>
    public bool Next()
    {
        lock (_gate)
        {
            if (_paused == null) return false;
            _paused = null;
            _version++;
            Monitor.PulseAll(_gate);
        }
        Changed?.Invoke(this);
        return true;
    }

    /// <summary>Turn stepping off and release any paused change.</summary>
    public void Run() => Configure(enabled: false);

    /// <summary>
    /// Called for every signal change, on the thread that made it. Blocks while stepping says this
    /// change should wait. Only one change is held at a time; others queue behind it.
    /// </summary>
    internal void OnChange(TraceEvent e)
    {
        StepPause mine;
        lock (_gate)
        {
            if (!Matches(e)) return;
            // Another change is already being held (e.g. from a device thread): wait our turn.
            while (_paused != null && _enabled) Monitor.Wait(_gate);
            if (!Matches(e)) return;
            var why = _forceReason ?? ConditionReason(e.Signal);
            _forceReason = null;
            mine = _paused = new StepPause(++_step, e.Time, e.Signal, e.Kind, e.Value, e.Source, e.Symbol, e.Cause, e.Depth, e.Chain, why);
            _version++;
        }
        Changed?.Invoke(this);
        lock (_gate)
            while (ReferenceEquals(_paused, mine)) Monitor.Wait(_gate);
    }

    private bool Matches(TraceEvent e) =>
        _enabled && (!_onlyBreakSignals || _forceReason != null || _breakOn.Contains(e.Signal) || ConditionReason(e.Signal) != null);

    // The first condition that reads this signal and is true now (null if none). Held under _gate; expressions are cheap.
    private string? ConditionReason(string signal)
    {
        if (_condBySignal.Count == 0 || !_condBySignal.TryGetValue(signal, out var list) || ValueLookup is not { } look) return null;
        foreach (var c in list) if (c.Evaluate(look)) return "condition " + c.Text;
        return null;
    }
}
