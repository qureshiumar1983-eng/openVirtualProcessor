using System.Globalization;
using System.Text.Json;

namespace OpenVP.Core.Alerts;

/// <summary>One slot's numbers in a history point. Averages and the highest value seen, over <see cref="N"/> seconds the slot held a program.</summary>
public sealed record SlotPoint(int Slot, int N, double Busy, double BusyMax, double Eps, double EpsMax, double Queue, double QueueMax, double EventMsMax);

/// <summary>Process and slot numbers over <see cref="Seconds"/> seconds starting at <see cref="Time"/>: one second, a minute or fifteen minutes.</summary>
public sealed record HistoryPoint(DateTimeOffset Time, int Seconds, double Cpu, double CpuMax, double Mem, double MemMax, double Threads, double PoolMax,
    double Offline, double OfflineMax, SlotPoint[] Slots);

/// <summary>What the monitor loop hands over each second.</summary>
public sealed record MetricSample(DateTimeOffset Time, double Cpu, double Mem, double Threads, double PoolPending, double Offline, IReadOnlyList<SlotSample> Slots);
public sealed record SlotSample(int Slot, double Busy, double Eps, double Queue, double EventMs);

public sealed record HistorySeries(string Range, int StepSeconds, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<HistoryPoint> Points);

/// <summary>
/// Keeps metrics for days: the last five minutes second by second (memory only), 1-minute averages for 7 days and 15-minute averages for
/// 90 days, on disk in <c>data/history/metrics-1m-yyyyMMdd.jsonl</c> and <c>metrics-15m-yyyyMM.jsonl</c> (one compact JSON line per point;
/// UTC). Each point holds the average and the highest value, so a short spike isn't averaged away. The live once-a-second path is unchanged.
/// </summary>
public sealed class MetricsHistory : IDisposable
{
    public const int RawSeconds = 300, KeepMinuteDays = 7, KeepQuarterDays = 90;
    public static readonly TimeSpan Minute = TimeSpan.FromMinutes(1), Quarter = TimeSpan.FromMinutes(15);

    private readonly string _dir;
    private readonly object _gate = new();
    private readonly List<HistoryPoint> _raw = new();
    private readonly List<HistoryPoint> _min = new();
    private readonly List<HistoryPoint> _quarter = new();
    private HistoryPoint? _curMin, _curQuarter;   // the bucket being filled (merged so far)
    private DateOnly _pruned;
    private readonly Func<DateTimeOffset> _clock;

    public MetricsHistory(string dataDirectory, Func<DateTimeOffset>? clock = null, bool load = true)
    {
        _dir = Path.Combine(dataDirectory, "history");
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (load) Load();
    }

    // ------------------------------------------------------------------ adding samples

    public static HistoryPoint FromSample(MetricSample s) => new(s.Time, 1, R(s.Cpu), R(s.Cpu), R(s.Mem), R(s.Mem), R(s.Threads), R(s.PoolPending), s.Offline, s.Offline,
        s.Slots.Select(x => new SlotPoint(x.Slot, 1, R(x.Busy), R(x.Busy), R(x.Eps), R(x.Eps), R(x.Queue), R(x.Queue), R(x.EventMs))).ToArray());

    private static double R(double v) => Math.Round(v, 2);

    /// <summary>The point with every number rounded to two decimals, as it is stored and shown.</summary>
    public static HistoryPoint Rounded(HistoryPoint p) => p with
    {
        Cpu = R(p.Cpu), CpuMax = R(p.CpuMax), Mem = R(p.Mem), MemMax = R(p.MemMax), Threads = R(p.Threads), PoolMax = R(p.PoolMax), Offline = R(p.Offline), OfflineMax = R(p.OfflineMax),
        Slots = p.Slots.Select(s => s with { Busy = R(s.Busy), BusyMax = R(s.BusyMax), Eps = R(s.Eps), EpsMax = R(s.EpsMax), Queue = R(s.Queue), QueueMax = R(s.QueueMax), EventMsMax = R(s.EventMsMax) }).ToArray()
    };

    /// <summary>Adds one second's sample. Whenever a minute (or quarter hour) is complete it is written to disk. Never throws.</summary>
    public void Add(MetricSample sample)
    {
        var p = FromSample(sample);
        lock (_gate)
        {
            _raw.Add(p);
            if (_raw.Count > RawSeconds) _raw.RemoveRange(0, _raw.Count - RawSeconds);
            var bucket = Floor(p.Time, Minute);
            if (_curMin != null && _curMin.Time != bucket) CloseMinute();
            _curMin = _curMin == null ? p with { Time = bucket } : Merge(_curMin, p, bucket);
        }
    }

    private void CloseMinute()
    {
        var m = Rounded(_curMin!);
        _curMin = null;
        AppendMerged(_min, m);
        Write("1m", m);
        var qb = Floor(m.Time, Quarter);
        if (_curQuarter != null && _curQuarter.Time != qb) CloseQuarter();
        _curQuarter = _curQuarter == null ? m with { Time = qb } : Merge(_curQuarter, m, qb);
        TrimMemory();
    }

    private void CloseQuarter()
    {
        var q = Rounded(_curQuarter!);
        _curQuarter = null;
        AppendMerged(_quarter, q);
        Write("15m", q);
    }

    /// <summary>Writes out the buckets being filled (when OpenVP stops).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_curMin != null) CloseMinute();
            if (_curQuarter != null) CloseQuarter();
        }
    }

    private static void AppendMerged(List<HistoryPoint> list, HistoryPoint p)
    {
        if (list.Count > 0 && list[^1].Time == p.Time) list[^1] = Merge(list[^1], p, p.Time);
        else list.Add(p);
    }

    private void TrimMemory()
    {
        var now = _clock();
        var m = now - TimeSpan.FromDays(KeepMinuteDays); var q = now - TimeSpan.FromDays(KeepQuarterDays);
        _min.RemoveAll(p => p.Time < m);
        _quarter.RemoveAll(p => p.Time < q);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (_pruned != today) { _pruned = today; Prune(); }
    }

    public static DateTimeOffset Floor(DateTimeOffset t, TimeSpan step) => new(t.UtcDateTime.Ticks / step.Ticks * step.Ticks, TimeSpan.Zero);

    /// <summary>Combines two points, weighting averages by the seconds each covers; maximums are the larger of the two.</summary>
    public static HistoryPoint Merge(HistoryPoint a, HistoryPoint b, DateTimeOffset time)
    {
        double wa = a.Seconds, wb = b.Seconds, w = wa + wb;
        double Avg(double x, double y) => w <= 0 ? 0 : (x * wa + y * wb) / w;
        var slots = new List<SlotPoint>();
        foreach (var n in a.Slots.Select(s => s.Slot).Union(b.Slots.Select(s => s.Slot)).Order())
        {
            var x = a.Slots.FirstOrDefault(s => s.Slot == n); var y = b.Slots.FirstOrDefault(s => s.Slot == n);
            if (x == null) { slots.Add(y!); continue; }
            if (y == null) { slots.Add(x); continue; }
            double xa = x.N, ya = y.N, t = xa + ya;
            double A(double u, double v) => t <= 0 ? 0 : (u * xa + v * ya) / t;
            slots.Add(new SlotPoint(n, x.N + y.N, A(x.Busy, y.Busy), Math.Max(x.BusyMax, y.BusyMax), A(x.Eps, y.Eps), Math.Max(x.EpsMax, y.EpsMax),
                A(x.Queue, y.Queue), Math.Max(x.QueueMax, y.QueueMax), Math.Max(x.EventMsMax, y.EventMsMax)));
        }
        return new HistoryPoint(time, a.Seconds + b.Seconds, Avg(a.Cpu, b.Cpu), Math.Max(a.CpuMax, b.CpuMax), Avg(a.Mem, b.Mem), Math.Max(a.MemMax, b.MemMax),
            Avg(a.Threads, b.Threads), Math.Max(a.PoolMax, b.PoolMax), Avg(a.Offline, b.Offline), Math.Max(a.OfflineMax, b.OfflineMax), slots.ToArray());
    }

    // ------------------------------------------------------------------ querying

    /// <summary>
    /// The series for the last <paramref name="range"/>. Five minutes or less is second by second; up to 7 days comes from the 1-minute points,
    /// longer from the 15-minute ones. More than <paramref name="maxPoints"/> points are merged into wider steps. The bucket being filled is the last point.
    /// </summary>
    public HistorySeries Query(string label, TimeSpan range, int maxPoints = 600)
    {
        var now = _clock();
        var from = now - range;
        List<HistoryPoint> pts;
        int step;
        lock (_gate)
        {
            if (range <= TimeSpan.FromSeconds(RawSeconds)) { pts = _raw.Where(p => p.Time >= from).ToList(); step = 1; }
            else if (range <= TimeSpan.FromDays(KeepMinuteDays))
            {
                pts = _min.Where(p => p.Time >= from).ToList();
                if (_curMin != null && _curMin.Time >= from) AppendMerged(pts, _curMin);
                step = 60;
            }
            else
            {
                pts = _quarter.Where(p => p.Time >= from).ToList();
                if (_curQuarter != null || _curMin != null)
                {
                    // The quarter hour being filled is the finished minutes in it plus the minute in progress.
                    var tail = _curQuarter;
                    if (_curMin != null) { var qb = Floor(_curMin.Time, Quarter); tail = tail == null || tail.Time != qb ? _curMin with { Time = qb } : Merge(tail, _curMin, qb); }
                    if (tail != null && tail.Time >= from) AppendMerged(pts, tail);
                }
                step = 900;
            }
        }
        if (pts.Count > maxPoints)
        {
            var group = (int)Math.Ceiling(pts.Count / (double)maxPoints);
            var merged = new List<HistoryPoint>();
            for (var i = 0; i < pts.Count; i += group)
            {
                var g = pts[i];
                for (var j = i + 1; j < Math.Min(i + group, pts.Count); j++) g = Merge(g, pts[j], g.Time);
                merged.Add(g);
            }
            pts = merged; step *= group;
        }
        return new HistorySeries(label, step, from, now, pts);
    }

    /// <summary>The highest CPU and memory (and when) seen in a window, from the 1-minute or 15-minute points.</summary>
    public (double Cpu, double Mem, double Threads) Peaks(TimeSpan range)
    {
        var s = Query("peaks", range, int.MaxValue);
        return s.Points.Count == 0 ? (0, 0, 0) : (s.Points.Max(p => p.CpuMax), s.Points.Max(p => p.MemMax), s.Points.Max(p => p.Threads));
    }

    // ------------------------------------------------------------------ files

    private string File1m(DateTimeOffset t) => Path.Combine(_dir, $"metrics-1m-{t.UtcDateTime:yyyyMMdd}.jsonl");
    private string File15m(DateTimeOffset t) => Path.Combine(_dir, $"metrics-15m-{t.UtcDateTime:yyyyMM}.jsonl");

    private void Write(string tier, HistoryPoint p)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.AppendAllText(tier == "1m" ? File1m(p.Time) : File15m(p.Time), Line(p) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* history is best effort */ }
    }

    public static string Line(HistoryPoint p)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("t", p.Time.ToUnixTimeSeconds());
            w.WriteNumber("n", p.Seconds);
            w.WriteNumber("c", p.Cpu); w.WriteNumber("cx", p.CpuMax);
            w.WriteNumber("m", p.Mem); w.WriteNumber("mx", p.MemMax);
            w.WriteNumber("th", p.Threads); w.WriteNumber("pl", p.PoolMax);
            w.WriteNumber("o", p.Offline); w.WriteNumber("ox", p.OfflineMax);
            w.WriteStartArray("s");
            foreach (var s in p.Slots)
            {
                w.WriteStartArray();
                w.WriteNumberValue(s.Slot); w.WriteNumberValue(s.N); w.WriteNumberValue(s.Busy); w.WriteNumberValue(s.BusyMax); w.WriteNumberValue(s.Eps); w.WriteNumberValue(s.EpsMax);
                w.WriteNumberValue(s.Queue); w.WriteNumberValue(s.QueueMax); w.WriteNumberValue(s.EventMsMax);
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static HistoryPoint? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            double D(string p) => r.GetProperty(p).GetDouble();
            var slots = r.GetProperty("s").EnumerateArray().Select(a => new SlotPoint(a[0].GetInt32(), a[1].GetInt32(), a[2].GetDouble(), a[3].GetDouble(), a[4].GetDouble(), a[5].GetDouble(),
                a[6].GetDouble(), a[7].GetDouble(), a[8].GetDouble())).ToArray();
            return new HistoryPoint(DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("t").GetInt64()), r.GetProperty("n").GetInt32(), D("c"), D("cx"), D("m"), D("mx"), D("th"), D("pl"), D("o"), D("ox"), slots);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    private void Load()
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            var now = _clock();
            ReadFiles("metrics-1m-*.jsonl", _min, now - TimeSpan.FromDays(KeepMinuteDays));
            ReadFiles("metrics-15m-*.jsonl", _quarter, now - TimeSpan.FromDays(KeepQuarterDays));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void ReadFiles(string pattern, List<HistoryPoint> into, DateTimeOffset since)
    {
        foreach (var f in Directory.GetFiles(_dir, pattern).Order())
        {
            foreach (var line in File.ReadLines(f))
                if (Parse(line) is { } p && p.Time >= since) AppendMerged(into, p);   // a restart can leave two lines for one bucket
        }
        into.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private void Prune()
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            var now = _clock();
            foreach (var f in Directory.GetFiles(_dir, "metrics-1m-*.jsonl"))
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f)[11..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) && d < now.UtcDateTime.AddDays(-KeepMinuteDays - 1)) File.Delete(f);
            foreach (var f in Directory.GetFiles(_dir, "metrics-15m-*.jsonl"))
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f)[12..], "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) && d.AddMonths(1) < now.UtcDateTime.AddDays(-KeepQuarterDays)) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => Flush();
}
