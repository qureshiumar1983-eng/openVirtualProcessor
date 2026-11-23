namespace OpenVP.Core.Alerts;

public enum ThresholdLevel { Ok = 0, Warning = 1, Critical = 2 }

/// <summary>A limit on one metric. It must stay at or above the warning (or critical) value for <see cref="ForSeconds"/> before it counts.</summary>
public sealed record ThresholdDef
{
    public string Id { get; init; } = "";
    public string Metric { get; init; } = "";
    /// <summary>Limit it to one slot. Null: every slot for a per-slot metric, the whole processor for the others.</summary>
    public int? Slot { get; init; }
    public double? Warn { get; init; }
    public double? Crit { get; init; }
    public int ForSeconds { get; init; } = 30;
    public bool Enabled { get; init; } = true;
}

/// <summary>The metrics a threshold can watch.</summary>
public static class ThresholdMetrics
{
    public const string Cpu = "cpu", Memory = "memory", SlotBusy = "slotBusy", Queue = "queue", LongestEvent = "longestEvent", EventsPerSecond = "eventsPerSecond", DevicesOffline = "devicesOffline";

    /// <param name="Each">Measured for every slot on its own (unless the threshold names one slot).</param>
    public sealed record Info(string Id, string Label, string Unit, bool Each, double Max);

    public static readonly Info[] All =
    [
        new(Cpu, "CPU of the OpenVP process", "%", false, 100),
        new(Memory, "Memory in use", "MB", false, 0),
        new(SlotBusy, "Slot thread busy", "%", true, 100),
        new(Queue, "Events waiting for a slot", "events", true, 0),
        new(LongestEvent, "Longest event running now", "ms", true, 0),
        new(EventsPerSecond, "Events per second", "events/s", true, 0),
        new(DevicesOffline, "Devices offline", "devices", false, 0),
    ];

    public static Info? Find(string id) => All.FirstOrDefault(m => m.Id == id);
}

public static class ThresholdRules
{
    public static List<ThresholdDef> Validate(IEnumerable<ThresholdDef> defs)
    {
        var list = new List<ThresholdDef>();
        foreach (var d0 in defs)
        {
            var info = ThresholdMetrics.Find(d0.Metric ?? "") ?? throw new ArgumentException($"'{d0.Metric}' isn't a metric a threshold can watch.");
            if (d0.Warn == null && d0.Crit == null) throw new ArgumentException($"{info.Label}: set a warning level, a critical level, or both.");
            if (d0.Warn is < 0 || d0.Crit is < 0) throw new ArgumentException($"{info.Label}: levels can't be negative.");
            if (d0.Warn != null && d0.Crit != null && d0.Warn >= d0.Crit) throw new ArgumentException($"{info.Label}: the warning level must be below the critical level.");
            if (d0.ForSeconds is < 0 or > 86400) throw new ArgumentException($"{info.Label}: \"for\" must be 0-86400 seconds.");
            if (d0.Slot is < 1) throw new ArgumentException($"{info.Label}: the slot must be 1 or more.");
            var id = string.IsNullOrWhiteSpace(d0.Id) ? AlertConfigValidator.NewId() : d0.Id.Trim();
            if (list.Any(x => x.Id == id)) throw new ArgumentException($"Two thresholds have the id '{id}'.");
            list.Add(d0 with { Id = id, Metric = info.Id });
        }
        if (list.Count > 60) throw new ArgumentException("At most 60 thresholds.");
        return list;
    }
}

public sealed record MetricReading(string Metric, int? Slot, double Value);
public sealed record ThresholdChange(ThresholdDef Def, int? Slot, ThresholdLevel Level, ThresholdLevel Previous, double Value);

/// <summary>
/// Turns readings (one set a second) into level changes. A level is reached only after the value has stayed at or above it for the
/// threshold's "for" time, and is left after the value has stayed below it for <see cref="ClearSeconds"/>, so a metric hovering at
/// the line doesn't flap. A reading that disappears (an unloaded slot) ends its alert. Not thread-safe: one thread calls it.
/// </summary>
public sealed class ThresholdEvaluator
{
    public double ClearSeconds { get; set; } = 5;

    private sealed class State
    {
        public ThresholdLevel Level;
        public DateTimeOffset? WarnSince, CritSince, LowerSince;
        public double Last;
        public bool Seen;
    }

    private readonly Dictionary<(string Def, int? Slot), State> _states = new();

    public static ThresholdLevel LevelOf(ThresholdDef d, double v) =>
        d.Crit is { } c && v >= c ? ThresholdLevel.Critical : d.Warn is { } w && v >= w ? ThresholdLevel.Warning : ThresholdLevel.Ok;

    public static bool Matches(ThresholdDef d, MetricReading r)
    {
        if (!d.Enabled || d.Metric != r.Metric) return false;
        if (d.Slot != null) return r.Slot == d.Slot;
        var each = ThresholdMetrics.Find(d.Metric)?.Each ?? false;
        return each ? r.Slot != null : r.Slot == null;
    }

    public List<ThresholdChange> Evaluate(IReadOnlyList<ThresholdDef> defs, IReadOnlyList<MetricReading> readings, DateTimeOffset now)
    {
        var changes = new List<ThresholdChange>();
        foreach (var s in _states.Values) s.Seen = false;
        var byId = defs.ToDictionary(d => d.Id);
        foreach (var d in defs)
        {
            foreach (var r in readings)
            {
                if (!Matches(d, r)) continue;
                var key = (d.Id, r.Slot);
                if (!_states.TryGetValue(key, out var st)) _states[key] = st = new State();
                st.Seen = true; st.Last = r.Value;
                var inst = LevelOf(d, r.Value);
                // How long the value has stayed at or above each level.
                st.WarnSince = d.Warn is { } w && r.Value >= w ? st.WarnSince ?? now : null;
                st.CritSince = d.Crit is { } c && r.Value >= c ? st.CritSince ?? now : null;
                var sustained = ThresholdLevel.Ok;
                if (st.WarnSince is { } ws && (now - ws).TotalSeconds >= d.ForSeconds) sustained = ThresholdLevel.Warning;
                if (st.CritSince is { } cs && (now - cs).TotalSeconds >= d.ForSeconds) sustained = ThresholdLevel.Critical;

                var prev = st.Level;
                if (sustained > st.Level) { st.Level = sustained; st.LowerSince = null; }
                else if (inst < st.Level)
                {
                    st.LowerSince ??= now;
                    if ((now - st.LowerSince.Value).TotalSeconds >= ClearSeconds) { st.Level = inst; st.LowerSince = null; }
                }
                else st.LowerSince = null;
                if (st.Level != prev) changes.Add(new ThresholdChange(d, r.Slot, st.Level, prev, r.Value));
            }
        }
        // Readings that went away, and thresholds that were removed or switched off.
        foreach (var key in _states.Keys.ToList())
        {
            var st = _states[key];
            var def = byId.GetValueOrDefault(key.Def);
            if (def != null && def.Enabled && st.Seen) continue;
            if (st.Level != ThresholdLevel.Ok && def != null) changes.Add(new ThresholdChange(def, key.Slot, ThresholdLevel.Ok, st.Level, st.Last));
            else if (st.Level != ThresholdLevel.Ok) changes.Add(new ThresholdChange(new ThresholdDef { Id = key.Def }, key.Slot, ThresholdLevel.Ok, st.Level, st.Last));
            _states.Remove(key);
        }
        return changes;
    }

    /// <summary>The alert key for a threshold on a metric (and slot): the same one every time, so it is de-duplicated and resolved.</summary>
    public static string KeyFor(ThresholdDef d, int? slot) => $"threshold:{d.Id}:{slot?.ToString() ?? "all"}";
}
