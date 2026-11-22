using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenVP.Core;

/// <summary>The last known value of each signal, kept only while something (a watch, a breakpoint condition) needs it.</summary>
public sealed class SignalValueCache
{
    private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);

    public void Set(string name, object value) => _values[name] = value;

    /// <summary>Add only what isn't known yet, so a seed read can't overwrite a newer traced value.</summary>
    public void Seed(IEnumerable<SignalInfo> signals) { foreach (var s in signals) _values.TryAdd(s.Name, s.Value); }

    public object? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;
    public bool Has(string name) => _values.ContainsKey(name);
    public int Count => _values.Count;
    public void Clear() => _values.Clear();
}

/// <summary>A watch: an expression (see <see cref="SignalExpression"/>) that is reported when it becomes true.</summary>
/// <param name="Id">Stable id, made by the server.</param>
/// <param name="Expression">e.g. <c>Room_On=1 &amp;&amp; Proj_Power=0 for 2s</c>.</param>
/// <param name="PauseStep">Also stop the program at the change that made it true (turns step mode on).</param>
public sealed record WatchDef(string Id, string Expression, bool PauseStep = false, bool Enabled = true, string? Name = null);

/// <summary>One time a watch came true.</summary>
public sealed record WatchHit(string WatchId, string Expression, DateTimeOffset Time, string Message);

/// <summary>What a watch is doing now, for the Watches panel.</summary>
public sealed record WatchStatus(string Id, string Expression, bool PauseStep, bool Enabled, string? Name, string? Error, string State,
    DateTimeOffset? Since, int Hits, DateTimeOffset? LastHit, IReadOnlyList<string> Unknown);

/// <summary>
/// Evaluates watches against a stream of signal changes. A watch fires when its expression turns true (and, with
/// <c>for 2s</c>, has stayed true that long), once per time it becomes true: it fires again only after it went false.
/// The engine reads time only from the events and from <see cref="Tick"/>, so it can be tested without waiting.
/// Safe to use from several threads.
/// </summary>
public sealed class WatchEngine
{
    private sealed class W
    {
        public WatchDef Def = null!;
        public SignalExpression? Expr;
        public string? Error;
        public bool Truth, Fired;
        public DateTimeOffset? Since;
        public int Hits;
        public DateTimeOffset? LastHit;
    }

    private readonly object _gate = new();
    private readonly SignalValueCache _values;
    private readonly List<W> _watches = new();
    private readonly Dictionary<string, List<W>> _bySignal = new(StringComparer.Ordinal);
    private readonly List<WatchHit> _hits = new();
    private const int MaxHits = 200;
    private int _pendingDurations;

    public WatchEngine(SignalValueCache values) => _values = values;

    /// <summary>Raised (outside the lock) when a watch fires.</summary>
    public event Action<WatchHit, WatchDef>? Fired;

    /// <summary>True when at least one enabled watch is set, so the caller knows whether to feed it changes.</summary>
    public bool Active { get { lock (_gate) return _watches.Any(w => w.Def.Enabled && w.Expr != null); } }

    /// <summary>True while a <c>for</c> watch is waiting out its time, so a timer should keep calling <see cref="Tick"/>.</summary>
    public bool NeedsTick { get { lock (_gate) return _pendingDurations > 0; } }

    public IReadOnlyList<WatchDef> Definitions { get { lock (_gate) return _watches.Select(w => w.Def).ToList(); } }

    /// <summary>Replace the watches. A bad expression is kept (with its error shown) but never fires.</summary>
    public void Set(IEnumerable<WatchDef> defs)
    {
        lock (_gate)
        {
            var old = _watches.ToDictionary(w => w.Def.Id);
            _watches.Clear(); _bySignal.Clear(); _pendingDurations = 0;
            foreach (var d in defs)
            {
                var w = new W { Def = d };
                if (old.TryGetValue(d.Id, out var prev) && prev.Def.Expression == d.Expression) { w.Hits = prev.Hits; w.LastHit = prev.LastHit; }
                if (SignalExpression.TryParse(d.Expression, out var e, out var err)) w.Expr = e; else w.Error = err;
                _watches.Add(w);
                if (w.Expr == null || !d.Enabled) continue;
                foreach (var s in w.Expr.Signals)
                {
                    if (!_bySignal.TryGetValue(s, out var l)) _bySignal[s] = l = new List<W>();
                    l.Add(w);
                }
                // Already true when set: it counts from now, and fires after its time (or at once).
                if (Eval(w)) { w.Truth = true; w.Since = DateTimeOffset.MinValue; w.Fired = true; }
            }
        }
    }

    private bool Eval(W w) => w.Expr!.Evaluate(_values.Get);

    /// <summary>A signal changed. <paramref name="value"/> must already be in the value cache.</summary>
    public void OnChange(string signal, DateTimeOffset time)
    {
        List<(WatchHit, WatchDef)>? fire = null;
        lock (_gate)
        {
            if (!_bySignal.TryGetValue(signal, out var list)) return;
            foreach (var w in list) Update(w, time, ref fire);
        }
        Raise(fire);
    }

    /// <summary>Check the watches that are waiting out a <c>for</c> time.</summary>
    public void Tick(DateTimeOffset now)
    {
        List<(WatchHit, WatchDef)>? fire = null;
        lock (_gate)
        {
            if (_pendingDurations == 0) return;
            foreach (var w in _watches)
                if (w.Truth && !w.Fired && w.Expr != null && w.Def.Enabled) Update(w, now, ref fire);
        }
        Raise(fire);
    }

    private void Update(W w, DateTimeOffset now, ref List<(WatchHit, WatchDef)>? fire)
    {
        var t = Eval(w);
        if (!t)
        {
            if (w.Truth && !w.Fired) _pendingDurations--;
            w.Truth = false; w.Fired = false; w.Since = null;
            return;
        }
        if (!w.Truth) { w.Truth = true; w.Since = now; w.Fired = false; if (w.Expr!.ForMs > 0) _pendingDurations++; }
        if (w.Fired) return;
        if (w.Expr!.ForMs > 0 && (now - w.Since!.Value).TotalMilliseconds < w.Expr.ForMs) return;
        if (w.Expr.ForMs > 0) _pendingDurations--;
        w.Fired = true; w.Hits++; w.LastHit = now;
        var label = string.IsNullOrWhiteSpace(w.Def.Name) ? w.Def.Expression : $"{w.Def.Name} ({w.Def.Expression})";
        var hit = new WatchHit(w.Def.Id, w.Def.Expression, now, $"Watch true: {label}");
        _hits.Add(hit);
        if (_hits.Count > MaxHits) _hits.RemoveAt(0);
        (fire ??= new()).Add((hit, w.Def));
    }

    private void Raise(List<(WatchHit, WatchDef)>? fire)
    {
        if (fire == null) return;
        foreach (var (h, d) in fire) Fired?.Invoke(h, d);
    }

    public IReadOnlyList<WatchHit> RecentHits(int max = 100) { lock (_gate) return _hits.TakeLast(max).Reverse().ToList(); }
    public void ClearHits() { lock (_gate) _hits.Clear(); }

    public IReadOnlyList<WatchStatus> Status()
    {
        lock (_gate)
            return _watches.Select(w =>
            {
                var state = w.Error != null ? "error" : !w.Def.Enabled ? "off" : w.Fired ? "true" : w.Truth ? "waiting" : "idle";
                var unknown = w.Expr == null ? Array.Empty<string>() : w.Expr.UnknownSignals(_values.Has).ToArray();
                return new WatchStatus(w.Def.Id, w.Def.Expression, w.Def.PauseStep, w.Def.Enabled, w.Def.Name, w.Error, state,
                    w.Truth && w.Since != DateTimeOffset.MinValue ? w.Since : null, w.Hits, w.LastHit, unknown);
            }).ToList();
    }
}

/// <summary>Settings for the oscillation detector and the never-changed tracker.</summary>
public sealed record MonitorSettings
{
    /// <summary>Look for signals that change too often. Off by default: it needs every change traced.</summary>
    public bool Oscillation { get; init; }
    /// <summary>A signal that changes more than this many times in one burst (one press and everything it causes) raises an alert.</summary>
    public int MaxChangesPerBurst { get; init; } = 6;
    /// <summary>A digital signal that changes this many times inside <see cref="OscillationWindowMs"/> raises an alert.</summary>
    public int OscillationChanges { get; init; } = 20;
    public int OscillationWindowMs { get; init; } = 1000;
    /// <summary>Remember which signals have changed, for the Never changed view.</summary>
    public bool TrackChanges { get; init; }

    public MonitorSettings Validated() => this with
    {
        MaxChangesPerBurst = Math.Clamp(MaxChangesPerBurst, 2, 100_000),
        OscillationChanges = Math.Clamp(OscillationChanges, 3, 100_000),
        OscillationWindowMs = Math.Clamp(OscillationWindowMs, 10, 600_000)
    };
}

/// <summary>What is saved for one slot in <c>debug.json</c>: its watches and monitor settings.</summary>
public sealed record DebugConfig
{
    public List<WatchDef> Watches { get; init; } = new();
    public MonitorSettings Monitor { get; init; } = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static DebugConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new DebugConfig();
            return JsonSerializer.Deserialize<DebugConfig>(File.ReadAllText(path), Json) ?? new DebugConfig();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return new DebugConfig(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
