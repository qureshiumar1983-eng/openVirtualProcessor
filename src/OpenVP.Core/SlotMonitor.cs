namespace OpenVP.Core;

/// <summary>
/// The server-side watchers of one slot: watch expressions, the oscillation detector, the never-changed tracker and
/// the value cache they and conditional breakpoints read. It listens to the slot's trace stream only while one of them
/// is switched on, so a slot with none of them pays nothing (see <see cref="ProgramSlot.TraceWanted"/>).
/// Settings are kept per slot in <c>debug.json</c> in the slot's folder.
/// </summary>
public sealed class SlotMonitor : IDisposable
{
    private readonly ProgramSlot _slot;
    private readonly object _gate = new();
    private readonly object _feed = new();
    private DebugConfig _config = new();
    private volatile bool _stopped;
    private bool _loaded, _subscribed, _needValues, _osc, _track, _watch, _buf;
    private int _leases;
    private Timer? _tick;
    private volatile bool _disposed;

    public SlotMonitor(ProgramSlot slot)
    {
        _slot = slot;
        Values = new SignalValueCache();
        Watches = new WatchEngine(Values);
        Oscillation = new OscillationDetector();
        Changes = new ChangeTracker();
        Watches.Fired += OnWatchFired;
        Oscillation.Alert += OnAlert;
    }

    public SignalValueCache Values { get; }
    public WatchEngine Watches { get; }
    public OscillationDetector Oscillation { get; }
    public ChangeTracker Changes { get; }

    /// <summary>The slot's recent changes; filled while a debugger is attached (see <see cref="AcquireTrace"/>).</summary>
    public TraceBuffer Buffer { get; } = new();

    private sealed class Lease(SlotMonitor m) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) { Interlocked.Decrement(ref m._leases); m.Refresh(); } }
    }

    /// <summary>Keep the recent-changes buffer filled until the returned handle is disposed (a debugger window does this while it is open).</summary>
    public IDisposable AcquireTrace()
    {
        Interlocked.Increment(ref _leases);
        Refresh();
        return new Lease(this);
    }

    private string ConfigPath => Path.Combine(_slot.Directory, "debug.json");

    public MonitorSettings Settings { get { EnsureLoaded(); return _config.Monitor; } }

    /// <summary>True while this monitor is listening to the slot's trace stream (it costs the slot a little then).</summary>
    public bool Listening => _subscribed;

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            _config = DebugConfig.Load(ConfigPath);
            Watches.Set(_config.Watches);
            Oscillation.Configure(_config.Monitor);
            _loaded = true;
        }
    }

    private void Save()
    {
        try { _config.Save(ConfigPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _slot.Log("Warning", $"Couldn't save the debugger settings: {e.Message}"); }
    }

    // ---------------------------------------------------------------- watches

    public IReadOnlyList<WatchStatus> WatchStatus() { EnsureLoaded(); return Watches.Status(); }

    /// <summary>Replace the slot's watches (and save them). Throws ArgumentException if one doesn't parse.</summary>
    public IReadOnlyList<WatchStatus> SetWatches(IEnumerable<WatchDef> defs)
    {
        EnsureLoaded();
        var list = new List<WatchDef>();
        foreach (var d in defs)
        {
            if (!SignalExpression.TryParse(d.Expression, out _, out var err)) throw new ArgumentException($"Watch '{d.Expression}': {err}");
            list.Add(d with { Id = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("N")[..8] : d.Id, Expression = d.Expression.Trim() });
        }
        if (list.Count > 100) throw new ArgumentException("At most 100 watches per slot.");
        lock (_gate)
        {
            _config = _config with { Watches = list };
            Save();
            Watches.Set(list);
        }
        Refresh();
        return Watches.Status();
    }

    // ---------------------------------------------------------------- settings

    public MonitorSettings SetSettings(MonitorSettings s)
    {
        EnsureLoaded();
        s = s.Validated();
        lock (_gate)
        {
            var wasTracking = _config.Monitor.TrackChanges;
            _config = _config with { Monitor = s };
            Save();
            Oscillation.Configure(s);
            if (s.TrackChanges && !wasTracking) Changes.Reset(DateTimeOffset.Now);
        }
        Refresh();
        return s;
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>The program is about to start: listen from the first change if watches or detectors are on.</summary>
    internal void ProgramStarting() { EnsureLoaded(); _stopped = false; Refresh(); }

    /// <summary>The program has started: baseline values, and the reset point for the never-changed view.</summary>
    internal void ProgramStarted()
    {
        EnsureLoaded();
        Oscillation.Reset();
        Changes.Reset(DateTimeOffset.Now);
        Buffer.Clear();
        Values.Clear();
        Refresh(reseed: true);
    }

    internal void ProgramStopped()
    {
        _stopped = true;
        Refresh();
    }

    /// <summary>Work out whether anything needs the trace stream, and subscribe or unsubscribe.</summary>
    public void Refresh(bool reseed = false)
    {
        if (_disposed) return;
        EnsureLoaded();
        bool subscribe, needValues;
        lock (_gate)
        {
            var cfg = _config.Monitor;
            var running = _slot.Runtime != null && !_stopped;
            _watch = running && Watches.Active;
            _osc = running && cfg.Oscillation;
            _track = running && cfg.TrackChanges;
            needValues = _watch || (running && _slot.Stepper.HasConditions);
            _buf = running && Volatile.Read(ref _leases) > 0;
            subscribe = _watch || _osc || _track || needValues || _buf;
            _needValues = needValues;
            if (subscribe && !_subscribed) { _subscribed = true; _slot.Traced += OnTrace; reseed = true; }
            else if (!subscribe && _subscribed) { _subscribed = false; _slot.Traced -= OnTrace; Values.Clear(); }
        }
        if (reseed && needValues)
        {
            try { if (_slot.Runtime is { } rt) Values.Seed(rt.GetSignals()); } catch { /* a runtime that can't list its signals: values fill in as they change */ }
            Watches.Set(Watches.Definitions);   // re-evaluate against the baseline
        }
        // A timer for "for 2s" watches only runs while one is waiting.
        if (_watch) _tick ??= new Timer(_ => { try { Watches.Tick(DateTimeOffset.Now); } catch { } }, null, 100, 100);
        else if (_tick != null) { _tick.Dispose(); _tick = null; }
    }

    private void OnTrace(TraceEvent e)
    {
        if (_buf) Buffer.Add(e);
        if (_needValues) Values.Set(e.Signal, e.Value);
        if (_track) Changes.OnChange(e.Signal);
        if (_osc) lock (_feed) Oscillation.OnChange(e.Signal, e.Kind, e.Time, e.Chain);
        if (_watch) Watches.OnChange(e.Signal, e.Time);
    }

    private void OnWatchFired(WatchHit hit, WatchDef def)
    {
        var values = string.Join(", ", ValuesOf(def.Expression).Take(6));
        _slot.Log("Warning", values.Length > 0 ? $"{hit.Message}  [{values}]" : hit.Message);
        if (def.PauseStep) _slot.Stepper.BreakNext($"watch {(string.IsNullOrWhiteSpace(def.Name) ? def.Expression : def.Name)}");
    }

    private IEnumerable<string> ValuesOf(string expression)
    {
        if (!SignalExpression.TryParse(expression, out var e, out _)) yield break;
        foreach (var s in e!.Signals)
        {
            var v = Values.Get(s);
            yield return $"{s}={(v is bool b ? (b ? "1" : "0") : v is string str ? "\"" + str + "\"" : v)}";
        }
    }

    private void OnAlert(OscillationAlert a) => _slot.Log("Warning", a.Message);

    public void Dispose()
    {
        _disposed = true;
        _tick?.Dispose(); _tick = null;
        if (_subscribed) { _subscribed = false; _slot.Traced -= OnTrace; }
    }
}
