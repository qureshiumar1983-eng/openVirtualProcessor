namespace OpenVP.Core;

/// <summary>A signal that changes too often.</summary>
/// <param name="Kind"><c>burst</c> (too many changes inside one burst) or <c>oscillation</c> (too many changes in a short time).</param>
public sealed record OscillationAlert(string Signal, string Kind, int Count, DateTimeOffset Time, string Message);

/// <summary>
/// Finds signals that race or oscillate. Two checks, both from the trace stream:
/// a signal that changes more than <see cref="MonitorSettings.MaxChangesPerBurst"/> times in one burst (the chain id
/// the runtime gives every external change, so SIMPL# programs, which have none, only get the second check), and a
/// digital signal that changes <see cref="MonitorSettings.OscillationChanges"/> times inside
/// <see cref="MonitorSettings.OscillationWindowMs"/>. A signal is reported once, then again only after it has been
/// quiet for a window. Time comes from the events, so tests don't wait. Not thread safe: call from the slot thread.
/// </summary>
public sealed class OscillationDetector
{
    private sealed class State
    {
        public DateTimeOffset[] Times = Array.Empty<DateTimeOffset>();
        public int Head, Filled;
        public long Chain;
        public int InChain;
        public bool Alerted;           // already reported, and the signal hasn't been quiet since
        public DateTimeOffset LastChange = DateTimeOffset.MinValue;
    }

    private const int MaxTracked = 20_000;
    private readonly Dictionary<string, State> _state = new(StringComparer.Ordinal);
    private readonly List<OscillationAlert> _alerts = new();
    private readonly object _alertGate = new();
    private MonitorSettings _s = new();

    public void Configure(MonitorSettings s) { _s = s.Validated(); }

    /// <summary>Raised for each new alert, on the thread that fed the change.</summary>
    public event Action<OscillationAlert>? Alert;

    public void Reset() { _state.Clear(); lock (_alertGate) _alerts.Clear(); }

    /// <summary>Alerts so far, newest first.</summary>
    public IReadOnlyList<OscillationAlert> Alerts { get { lock (_alertGate) return _alerts.AsEnumerable().Reverse().ToList(); } }

    /// <summary>Signals alerted inside the last <paramref name="withinSeconds"/> seconds, counted at <paramref name="now"/>.</summary>
    public int Recent(DateTimeOffset now, double withinSeconds = 30)
    {
        lock (_alertGate) return _alerts.Where(a => (now - a.Time).TotalSeconds <= withinSeconds).Select(a => a.Signal).Distinct().Count();
    }

    public void OnChange(string signal, SigKind kind, DateTimeOffset time, long chain)
    {
        if (!_state.TryGetValue(signal, out var st))
        {
            if (_state.Count >= MaxTracked) _state.Clear();
            _state[signal] = st = new State();
        }
        // A signal that has been quiet since it was last reported may be reported again.
        if (st.Alerted && (time - st.LastChange).TotalMilliseconds >= Math.Max(_s.OscillationWindowMs, 1000)) st.Alerted = false;
        st.LastChange = time;

        // Too many changes in one burst.
        if (chain != 0)
        {
            if (st.Chain != chain) { st.Chain = chain; st.InChain = 0; }
            if (++st.InChain > _s.MaxChangesPerBurst) Raise(st, signal, "burst", st.InChain, time,
                $"{signal} changed {st.InChain} times in one burst (limit {_s.MaxChangesPerBurst}): a race or a feedback loop in the logic.");
        }

        // Too many changes in a short time (digital: it flips back and forth).
        if (kind != SigKind.Digital) return;
        var n = _s.OscillationChanges;
        if (st.Times.Length != n) { st.Times = new DateTimeOffset[n]; st.Head = st.Filled = 0; }
        st.Times[st.Head] = time;
        st.Head = (st.Head + 1) % n;
        if (st.Filled < n) st.Filled++;
        if (st.Filled == n)
        {
            var oldest = st.Times[st.Head];   // the next slot to be overwritten holds the oldest of the last n changes
            if ((time - oldest).TotalMilliseconds <= _s.OscillationWindowMs)
                Raise(st, signal, "oscillation", n, time,
                    $"{signal} changed {n} times in {Math.Max(1, (long)(time - oldest).TotalMilliseconds)} ms: it is oscillating.");
        }
    }

    private void Raise(State st, string signal, string kind, int count, DateTimeOffset time, string message)
    {
        // Once per quiet period, so a loop that runs for seconds is one alert, not thousands.
        if (st.Alerted) return;
        st.Alerted = true;
        var a = new OscillationAlert(signal, kind, count, time, message);
        lock (_alertGate) { _alerts.Add(a); if (_alerts.Count > 100) _alerts.RemoveAt(0); }
        Alert?.Invoke(a);
    }
}

/// <summary>Which signals have changed since tracking started or was reset, for the Never changed view.</summary>
public sealed class ChangeTracker
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private DateTimeOffset _since = DateTimeOffset.Now;

    public DateTimeOffset Since => _since;
    public int ChangedCount => _counts.Count;

    public void OnChange(string signal) => _counts.AddOrUpdate(signal, 1, static (_, n) => n + 1);

    public int Count(string signal) => _counts.TryGetValue(signal, out var n) ? n : 0;

    public void Reset(DateTimeOffset now) { _counts.Clear(); _since = now; }

    /// <summary>The signals in <paramref name="all"/> that have not changed since the reset point.</summary>
    public IReadOnlyList<SignalInfo> NeverChanged(IEnumerable<SignalInfo> all) => all.Where(s => !_counts.ContainsKey(s.Name)).ToList();
}
