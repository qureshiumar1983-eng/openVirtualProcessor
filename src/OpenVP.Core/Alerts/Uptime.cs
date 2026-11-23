using System.Globalization;
using System.Text.Json;

namespace OpenVP.Core.Alerts;

public enum UState { None, Up, Down }

/// <summary>A slot (<c>'s'</c>) or a device at an IP ID (<c>'d'</c>).</summary>
public readonly record struct EntityKey(char Kind, int Slot, int IpId);

/// <summary>One line of the uptime history. <c>up</c> and <c>down</c> mark OpenVP starting and stopping, <c>hb</c> says it is still there, <c>cp</c> is a checkpoint of every state written at the top of each day's file.</summary>
public sealed record UptimeEvent(DateTimeOffset Time, string Kind, int Slot = 0, int IpId = 0, string? Value = null,
    IReadOnlyList<(int Slot, string State)>? CpSlots = null, IReadOnlyList<(int Slot, int IpId, string State)>? CpDevices = null);

public sealed record Outage(DateTimeOffset From, DateTimeOffset To);

/// <summary>How long one slot or device was up and down in a window, with a timeline for the bar.</summary>
public sealed record EntityUptime(EntityKey Key, double UpSeconds, double DownSeconds, int Drops, double[] Buckets, IReadOnlyList<Outage> Outages)
{
    public double ObservedSeconds => UpSeconds + DownSeconds;
    /// <summary>Percent of the observed time it was up; null if it was never observed.</summary>
    public double? Availability => ObservedSeconds > 0 ? Math.Round(UpSeconds / ObservedSeconds * 100, 2) : null;
}

public sealed record UptimeReport(DateTimeOffset From, DateTimeOffset To, DateTimeOffset? Since, IReadOnlyList<EntityUptime> Entities);

/// <summary>One row of the offline heat map: minutes offline in each cell and how many times the device dropped there.</summary>
public sealed record HeatRow(int Slot, int IpId, double[] Minutes, int[] Drops, double TotalMinutes);
public sealed record HeatMap(string Mode, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<string> Columns, IReadOnlyList<HeatRow> Rows);

/// <summary>
/// Works out availability from the recorded events. A slot is <i>up</i> while it is Running and <i>down</i> while it holds a program that
/// isn't running (stopped, faulted, or OpenVP itself not running); an empty slot is not counted. A device is <i>up</i> while a panel is connected
/// and <i>down</i> while it is registered and nobody is, only while its slot is running. Time before the first record is not counted.
/// </summary>
public static class UptimeCalculator
{
    /// <summary>After the last record, OpenVP is taken to have stopped if no heartbeat came for this long.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(150);

    private sealed class Walker
    {
        public readonly Dictionary<int, string> Slots = new();
        public readonly Dictionary<(int, int), string> Devices = new();
        public readonly Dictionary<EntityKey, UState> Last = new();
        public bool Alive;
        public DateTimeOffset? Cursor;
        public DateTimeOffset LastAlive;

        public UState Eff(EntityKey k)
        {
            if (k.Kind == 's')
            {
                if (!Slots.TryGetValue(k.Slot, out var raw) || raw == "Empty") return UState.None;
                return Alive && raw == "Running" ? UState.Up : UState.Down;
            }
            if (!Alive || !Devices.TryGetValue((k.Slot, k.IpId), out var v)) return UState.None;
            if (!Slots.TryGetValue(k.Slot, out var s) || s != "Running") return UState.None;
            return v == "on" ? UState.Up : v == "off" ? UState.Down : UState.None;
        }

        public IEnumerable<EntityKey> Keys()
        {
            foreach (var s in Slots.Keys) yield return new EntityKey('s', s, 0);
            foreach (var (s, i) in Devices.Keys) yield return new EntityKey('d', s, i);
        }
    }

    /// <summary>
    /// Replays <paramref name="events"/> (in time order) and calls <paramref name="interval"/> for each stretch of time inside [from, to]
    /// where an entity was up or down, and <paramref name="change"/> each time an entity's state changes (at any time, inside the window or not).
    /// Returns the time of the first record, or null if there was none.
    /// </summary>
    public static DateTimeOffset? Walk(IEnumerable<UptimeEvent> events, DateTimeOffset from, DateTimeOffset to,
        Action<EntityKey, DateTimeOffset, DateTimeOffset, UState> interval, Action<EntityKey, DateTimeOffset, UState, UState>? change = null, TimeSpan? grace = null)
    {
        var w = new Walker();
        DateTimeOffset? first = null;

        void Advance(DateTimeOffset t)
        {
            if (w.Cursor is not { } c) { w.Cursor = t; return; }
            var a = c > from ? c : from; var b = t < to ? t : to;
            if (b > a)
                foreach (var k in w.Keys().ToList())
                {
                    var st = w.Eff(k);
                    if (st != UState.None) interval(k, a, b, st);
                }
            if (t > c) w.Cursor = t;
        }

        void Settle(DateTimeOffset t)
        {
            var keys = w.Keys().Concat(w.Last.Keys).Distinct().ToList();
            foreach (var k in keys)
            {
                var now = w.Eff(k);
                var was = w.Last.GetValueOrDefault(k, UState.None);
                if (now != was) { change?.Invoke(k, t, was, now); }
                if (now == UState.None) w.Last.Remove(k); else w.Last[k] = now;
            }
        }

        foreach (var e in events)
        {
            first ??= e.Time;
            switch (e.Kind)
            {
                case "up":
                    if (w.Alive) { Advance(w.LastAlive); w.Alive = false; Settle(w.LastAlive); }
                    Advance(e.Time);
                    w.Slots.Clear(); w.Devices.Clear();
                    w.Alive = true; w.LastAlive = e.Time;
                    Settle(e.Time);
                    break;
                case "down":
                    Advance(e.Time);
                    w.Alive = false;
                    Settle(e.Time);
                    break;
                default:
                    Advance(e.Time);
                    w.Alive = true; w.LastAlive = e.Time;
                    switch (e.Kind)
                    {
                        case "s": w.Slots[e.Slot] = e.Value ?? "Empty"; break;
                        case "d":
                            if (e.Value is "on" or "off") w.Devices[(e.Slot, e.IpId)] = e.Value; else w.Devices.Remove((e.Slot, e.IpId));
                            break;
                        case "cp":
                            w.Slots.Clear(); w.Devices.Clear();
                            foreach (var (s, st) in e.CpSlots ?? []) w.Slots[s] = st;
                            foreach (var (s, i, st) in e.CpDevices ?? []) w.Devices[(s, i)] = st;
                            break;
                    }
                    Settle(e.Time);
                    break;
            }
        }
        if (first == null) return null;
        if (w.Alive && to - w.LastAlive > (grace ?? Grace))
        {
            Advance(w.LastAlive); w.Alive = false; Settle(w.LastAlive);
        }
        Advance(to);
        return first;
    }

    private sealed class Acc
    {
        public double Up, Down; public int Drops; public double[] BUp = [], BDown = [];
        public List<Outage> Outages = new();
    }

    /// <summary>Availability, drops, a timeline of <paramref name="buckets"/> segments and the outages of every slot and device seen in [from, to].</summary>
    public static UptimeReport Compute(IEnumerable<UptimeEvent> events, DateTimeOffset from, DateTimeOffset to, int buckets = 96, TimeSpan? grace = null)
    {
        var acc = new Dictionary<EntityKey, Acc>();
        var span = (to - from).TotalSeconds;
        buckets = Math.Clamp(buckets, 1, 1000);
        Acc Get(EntityKey k)
        {
            if (!acc.TryGetValue(k, out var a)) acc[k] = a = new Acc { BUp = new double[buckets], BDown = new double[buckets] };
            return a;
        }
        var since = Walk(events, from, to, (k, a, b, st) =>
        {
            var x = Get(k);
            var secs = (b - a).TotalSeconds;
            if (st == UState.Up) x.Up += secs; else x.Down += secs;
            if (span > 0)
            {
                var i0 = Math.Clamp((int)((a - from).TotalSeconds / span * buckets), 0, buckets - 1);
                var i1 = Math.Clamp((int)(((b - from).TotalSeconds - 1e-9) / span * buckets), 0, buckets - 1);
                for (var i = i0; i <= i1; i++)
                {
                    var bs = from.AddSeconds(span * i / buckets); var be = from.AddSeconds(span * (i + 1) / buckets);
                    var s = (a > bs ? a : bs); var e = (b < be ? b : be);
                    var d = Math.Max(0, (e - s).TotalSeconds);
                    if (st == UState.Up) x.BUp[i] += d; else x.BDown[i] += d;
                }
            }
            if (st == UState.Down)
            {
                if (x.Outages.Count > 0 && x.Outages[^1].To == a) x.Outages[^1] = x.Outages[^1] with { To = b };
                else x.Outages.Add(new Outage(a, b));
            }
        }, (k, t, was, now) =>
        {
            if (t >= from && t <= to && was == UState.Up && now == UState.Down) Get(k).Drops++;
        }, grace);

        var list = acc.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Slot).ThenBy(p => p.Key.IpId)
            .Select(p => new EntityUptime(p.Key, Math.Round(p.Value.Up, 1), Math.Round(p.Value.Down, 1), p.Value.Drops,
                Enumerable.Range(0, buckets).Select(i => { var t = p.Value.BUp[i] + p.Value.BDown[i]; return t < 0.5 ? -1 : Math.Round(p.Value.BUp[i] / t, 3); }).ToArray(),
                p.Value.Outages.Count > 20 ? p.Value.Outages.TakeLast(20).ToList() : p.Value.Outages)).ToList();
        return new UptimeReport(from, to, since == null ? null : (since > from ? since : from), list);
    }

    /// <summary>
    /// Offline minutes for each device, by hour of the day (24 columns, all days of the window added up) or by day. <paramref name="offsetMinutes"/>
    /// is the viewer's offset from UTC, so "hour of the day" is their clock.
    /// </summary>
    public static HeatMap Heat(IEnumerable<UptimeEvent> events, DateTimeOffset from, DateTimeOffset to, string mode, int offsetMinutes, TimeSpan? grace = null)
    {
        var byDay = mode == "day";
        var off = TimeSpan.FromMinutes(offsetMinutes);
        var firstDay = (from + off).UtcDateTime.Date;
        var days = (int)((to + off).UtcDateTime.Date - firstDay).TotalDays + 1;
        var n = byDay ? Math.Clamp(days, 1, 400) : 24;
        var minutes = new Dictionary<EntityKey, double[]>();
        var drops = new Dictionary<EntityKey, int[]>();
        int Cell(DateTimeOffset local) => byDay ? Math.Clamp((int)(local.UtcDateTime.Date - firstDay).TotalDays, 0, n - 1) : local.UtcDateTime.Hour;

        Walk(events, from, to, (k, a, b, st) =>
        {
            if (k.Kind != 'd' || st != UState.Down) return;
            if (!minutes.TryGetValue(k, out var m)) { minutes[k] = m = new double[n]; drops[k] = new int[n]; }
            var t = a;
            while (t < b)
            {
                var local = t + off;
                var nextHour = new DateTimeOffset(local.UtcDateTime.Date.AddHours(local.UtcDateTime.Hour + 1), TimeSpan.Zero) - off;
                var e = nextHour < b ? nextHour : b;
                m[Cell(local)] += (e - t).TotalMinutes;
                t = e;
            }
        }, (k, t, was, now) =>
        {
            if (k.Kind != 'd' || was != UState.Up || now != UState.Down || t < from || t > to) return;
            if (!drops.TryGetValue(k, out var d)) { minutes[k] = new double[n]; drops[k] = d = new int[n]; }
            d[Cell(t + off)]++;
        }, grace);

        var cols = byDay
            ? Enumerable.Range(0, n).Select(i => firstDay.AddDays(i).ToString("MMM d", CultureInfo.InvariantCulture)).ToList()
            : Enumerable.Range(0, 24).Select(h => $"{h:00}").ToList();
        var rows = minutes.OrderBy(p => p.Key.Slot).ThenBy(p => p.Key.IpId)
            .Select(p => new HeatRow(p.Key.Slot, p.Key.IpId, p.Value.Select(x => Math.Round(x, 1)).ToArray(), drops[p.Key], Math.Round(p.Value.Sum(), 1))).ToList();
        return new HeatMap(byDay ? "day" : "hour", from, to, cols, rows);
    }
}

/// <summary>
/// Records slot state changes and device online/offline changes to <c>data/history/uptime-yyyyMMdd.jsonl</c> (one file per UTC day, one JSON
/// line per event, 90 days kept), and reads them back for <see cref="UptimeCalculator"/>. Each day's file starts with a checkpoint of every
/// state, so a window never has to look further back than one file. A heartbeat every minute shows OpenVP was running.
/// </summary>
public sealed class UptimeStore : IDisposable
{
    public const int KeepDays = 90;

    private readonly string _dir;
    private readonly Processor? _processor;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _slots = new();
    private readonly Dictionary<(int, int), string> _devices = new();
    private readonly HashSet<JoinDevice> _hooked = new();
    private readonly HashSet<(int, int)> _seen = new();     // devices that have been online since OpenVP started
    private readonly Action<ProgramSlot> _onState;
    private readonly Action<JoinDevice> _onReg, _onUnreg;
    private readonly Action<JoinDevice, bool> _onOnline;
    private Timer? _beat;
    private DateOnly _day;
    private bool _disposed;

    /// <param name="processor">Hooks its slots and devices; null for a store that is only written to by <see cref="Record"/> (tests).</param>
    public UptimeStore(string dataDirectory, Processor? processor = null, Func<DateTimeOffset>? clock = null, bool heartbeat = true)
    {
        _dir = Path.Combine(dataDirectory, "history");
        _processor = processor;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _onState = s => Record("s", s.Number, 0, s.State.ToString());
        _onOnline = (d, on) => Record("d", d.Slot, (int)d.IpId, on ? "on" : "off");
        _onReg = d => { Hook(d); Record("d", d.Slot, (int)d.IpId, d.Online ? "on" : "off"); };   // "off" becomes "new" until it has been online once
        _onUnreg = d => { Unhook(d); Record("d", d.Slot, (int)d.IpId, "gone"); };
        lock (_gate)
        {
            Append(new UptimeEvent(_clock(), "up"), checkpointFirst: false);
            if (processor != null)
            {
                foreach (var s in processor.Slots) _slots[s.Number] = s.State.ToString();
                foreach (var s in processor.Slots) foreach (var d in s.Devices) { SetDevice(d.Slot, (int)d.IpId, d.Online ? "on" : "off"); Hook(d); }
                WriteCheckpoint();
                foreach (var s in processor.Slots) s.StateChanged += _onState;
                processor.DeviceRegistered += _onReg;
                processor.DeviceUnregistered += _onUnreg;
            }
        }
        Prune();
        if (heartbeat) _beat = new Timer(_ => Record("hb", 0, 0, null), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    private void Hook(JoinDevice d) { lock (_hooked) if (_hooked.Add(d)) d.OnlineChanged += _onOnline; }
    private void Unhook(JoinDevice d) { lock (_hooked) if (_hooked.Remove(d)) d.OnlineChanged -= _onOnline; }

    private string FileFor(DateOnly day) => Path.Combine(_dir, $"uptime-{day:yyyyMMdd}.jsonl");

    /// <summary>Writes one event (and keeps the current states for the next checkpoint). Never throws.</summary>
    public void Record(string kind, int slot, int ipId, string? value)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (kind == "s") _slots[slot] = value ?? "Empty";
            else if (kind == "d") value = SetDevice(slot, ipId, value);
            Append(new UptimeEvent(_clock(), kind, slot, ipId, value), checkpointFirst: true);
        }
    }

    // A device that has never been online isn't "down", it just hasn't connected (yet): it is recorded as "new" and not counted until it first connects.
    // Called with the lock held. Returns the value to write.
    private string? SetDevice(int slot, int ipId, string? value)
    {
        var k = (slot, ipId);
        if (value == "on") _seen.Add(k);
        else if (value == "off" && !_seen.Contains(k)) value = "new";
        if (value is "on" or "off" or "new") _devices[k] = value; else { _devices.Remove(k); _seen.Remove(k); }
        return value;
    }

    private void Append(UptimeEvent e, bool checkpointFirst)
    {
        try
        {
            var day = DateOnly.FromDateTime(e.Time.UtcDateTime);
            Directory.CreateDirectory(_dir);
            if (day != _day)
            {
                var rolled = _day != default;
                _day = day;
                if (checkpointFirst && !File.Exists(FileFor(day))) WriteCheckpoint(e.Time);
                if (rolled) Prune();
            }
            File.AppendAllText(FileFor(day), Line(e) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* history is best effort */ }
    }

    private void WriteCheckpoint(DateTimeOffset? at = null)
    {
        var t = at ?? _clock();
        var cp = new UptimeEvent(t, "cp", CpSlots: _slots.Select(p => (p.Key, p.Value)).ToList(), CpDevices: _devices.Select(p => (p.Key.Item1, p.Key.Item2, p.Value)).ToList());
        try
        {
            Directory.CreateDirectory(_dir);
            var day = DateOnly.FromDateTime(t.UtcDateTime);
            if (_day == default) _day = day;
            File.AppendAllText(FileFor(day), Line(cp) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------ file format

    private static string Line(UptimeEvent e)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("t", e.Time.ToUnixTimeMilliseconds());
            w.WriteString("k", e.Kind);
            if (e.Kind is "s" or "d") w.WriteNumber("n", e.Slot);
            if (e.Kind == "d") w.WriteNumber("i", e.IpId);
            if (e.Value != null) w.WriteString("v", e.Value);
            if (e.Kind == "cp")
            {
                w.WriteStartArray("s");
                foreach (var (s, st) in e.CpSlots ?? []) { w.WriteStartArray(); w.WriteNumberValue(s); w.WriteStringValue(st); w.WriteEndArray(); }
                w.WriteEndArray();
                w.WriteStartArray("d");
                foreach (var (s, i, st) in e.CpDevices ?? []) { w.WriteStartArray(); w.WriteNumberValue(s); w.WriteNumberValue(i); w.WriteStringValue(st); w.WriteEndArray(); }
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static UptimeEvent? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            var t = DateTimeOffset.FromUnixTimeMilliseconds(r.GetProperty("t").GetInt64());
            var k = r.GetProperty("k").GetString() ?? "";
            int I(string p) => r.TryGetProperty(p, out var v) ? v.GetInt32() : 0;
            string? V() => r.TryGetProperty("v", out var v) ? v.GetString() : null;
            if (k == "cp")
            {
                var ss = r.GetProperty("s").EnumerateArray().Select(a => (a[0].GetInt32(), a[1].GetString() ?? "Empty")).ToList();
                var dd = r.GetProperty("d").EnumerateArray().Select(a => (a[0].GetInt32(), a[1].GetInt32(), a[2].GetString() ?? "off")).ToList();
                return new UptimeEvent(t, k, CpSlots: ss, CpDevices: dd);
            }
            return new UptimeEvent(t, k, I("n"), I("i"), V());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>
    /// The events needed for [from, to]: the newest file dated on or before <paramref name="from"/> (it starts with a checkpoint) and every
    /// file after it up to <paramref name="to"/>, in order.
    /// </summary>
    public IReadOnlyList<UptimeEvent> Read(DateTimeOffset from, DateTimeOffset to)
    {
        var list = new List<UptimeEvent>();
        try
        {
            if (!Directory.Exists(_dir)) return list;
            var files = Directory.GetFiles(_dir, "uptime-*.jsonl")
                .Select(f => (File: f, Day: ParseDay(f))).Where(x => x.Day != null).OrderBy(x => x.Day).ToList();
            var fromDay = DateOnly.FromDateTime(from.UtcDateTime); var toDay = DateOnly.FromDateTime(to.UtcDateTime);
            var start = files.FindLastIndex(x => x.Day <= fromDay);
            if (start < 0) start = 0;
            for (var i = start; i < files.Count && files[i].Day <= toDay; i++)
            {
                IEnumerable<string> lines;
                lock (_gate)
                {
                    // The day being written may be mid-append; read what is there.
                    using var fs = new FileStream(files[i].File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }
                foreach (var l in lines) if (Parse(l) is { } e) list.Add(e);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list.OrderBy(e => e.Time).ToList();
    }

    private static DateOnly? ParseDay(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name.Length == 15 && DateOnly.TryParseExact(name[7..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public UptimeReport Report(DateTimeOffset from, DateTimeOffset to, int buckets = 96) => UptimeCalculator.Compute(Read(from, to), from, to, buckets);
    public HeatMap Heat(DateTimeOffset from, DateTimeOffset to, string mode, int offsetMinutes) => UptimeCalculator.Heat(Read(from, to), from, to, mode, offsetMinutes);

    /// <summary>The earliest time anything was recorded, or null.</summary>
    public DateTimeOffset? FirstRecord()
    {
        try
        {
            var f = Directory.Exists(_dir) ? Directory.GetFiles(_dir, "uptime-*.jsonl").Where(x => ParseDay(x) != null).OrderBy(x => x).FirstOrDefault() : null;
            if (f == null) return null;
            using var sr = new StreamReader(new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            return sr.ReadLine() is { } l ? Parse(l)?.Time : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private void Prune()
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            var oldest = DateOnly.FromDateTime(_clock().UtcDateTime).AddDays(-KeepDays);
            foreach (var f in Directory.GetFiles(_dir, "uptime-*.jsonl"))
                if (ParseDay(f) is { } d && d < oldest) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Append(new UptimeEvent(_clock(), "down"), checkpointFirst: true);
            _disposed = true;
        }
        _beat?.Dispose();
        if (_processor != null)
        {
            foreach (var s in _processor.Slots) s.StateChanged -= _onState;
            _processor.DeviceRegistered -= _onReg;
            _processor.DeviceUnregistered -= _onUnreg;
        }
        lock (_hooked) { foreach (var d in _hooked) d.OnlineChanged -= _onOnline; _hooked.Clear(); }
    }
}
