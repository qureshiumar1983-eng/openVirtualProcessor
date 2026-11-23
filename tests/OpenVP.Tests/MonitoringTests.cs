using OpenVP.Core;
using OpenVP.Core.Alerts;

namespace OpenVP.Tests;

/// <summary>Threshold evaluation with durations, uptime percentages across restarts and day boundaries, metrics downsampling, and report content.</summary>
public static class MonitoringTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double sec) => T0.AddSeconds(sec);

    // ------------------------------------------------------------------ thresholds

    private static List<ThresholdChange> Eval(ThresholdEvaluator ev, ThresholdDef d, double value, double at, int? slot = null) =>
        ev.Evaluate([d], [new MetricReading(d.Metric, slot, value)], At(at));

    [Test]
    public static void A_threshold_fires_only_after_the_value_has_stayed_over_it_for_the_whole_duration()
    {
        var d = new ThresholdDef { Id = "t", Metric = ThresholdMetrics.Cpu, Warn = 70, Crit = 90, ForSeconds = 10 };
        var ev = new ThresholdEvaluator();
        Assert.Equal(0, Eval(ev, d, 50, 0).Count, "below");
        Assert.Equal(0, Eval(ev, d, 80, 1).Count, "over the warning level, but not for long enough");
        Assert.Equal(0, Eval(ev, d, 80, 10).Count, "9 s");
        var c = Eval(ev, d, 80, 11);
        Assert.Equal(1, c.Count, "10 s over: it fires");
        Assert.Equal(ThresholdLevel.Warning, c[0].Level, "warning");
        Assert.Equal(0, Eval(ev, d, 85, 12).Count, "still warning: no new change");
    }

    [Test]
    public static void A_dip_below_the_level_restarts_the_clock()
    {
        var d = new ThresholdDef { Id = "t", Metric = ThresholdMetrics.Cpu, Warn = 70, ForSeconds = 10 };
        var ev = new ThresholdEvaluator();
        Eval(ev, d, 80, 0); Eval(ev, d, 80, 8);
        Eval(ev, d, 60, 9);                       // dips under
        Assert.Equal(0, Eval(ev, d, 80, 10).Count, "the 8 s before the dip don't count");
        Assert.Equal(0, Eval(ev, d, 80, 19).Count, "9 s since");
        Assert.Equal(1, Eval(ev, d, 80, 20).Count, "10 s since the dip");
    }

    [Test]
    public static void Critical_needs_its_own_duration_and_levels_step_down_after_the_clear_time()
    {
        var d = new ThresholdDef { Id = "t", Metric = ThresholdMetrics.Cpu, Warn = 70, Crit = 90, ForSeconds = 5 };
        var ev = new ThresholdEvaluator { ClearSeconds = 3 };
        Eval(ev, d, 75, 0);
        Assert.Equal(ThresholdLevel.Warning, Eval(ev, d, 75, 5)[0].Level, "warning after 5 s");
        Eval(ev, d, 95, 6);
        Assert.Equal(0, Eval(ev, d, 95, 10).Count, "over critical for only 4 s");
        var up = Eval(ev, d, 95, 11);
        Assert.Equal(ThresholdLevel.Critical, up[0].Level, "critical after 5 s over the critical level");
        Assert.Equal(ThresholdLevel.Warning, up[0].Previous, "from warning");
        Eval(ev, d, 75, 12);
        Assert.Equal(0, Eval(ev, d, 75, 14).Count, "back under critical, but the clear time hasn't passed");
        var down = Eval(ev, d, 75, 15);
        Assert.Equal(ThresholdLevel.Warning, down[0].Level, "steps down to warning");
        Eval(ev, d, 10, 16);
        var gone = Eval(ev, d, 10, 19);
        Assert.Equal(ThresholdLevel.Ok, gone[0].Level, "and clears");
    }

    [Test]
    public static void A_zero_duration_fires_at_once_and_each_slot_has_its_own_state()
    {
        var d = new ThresholdDef { Id = "b", Metric = ThresholdMetrics.SlotBusy, Warn = 80, ForSeconds = 0 };
        var ev = new ThresholdEvaluator();
        var c = ev.Evaluate([d], [new MetricReading("slotBusy", 1, 90), new MetricReading("slotBusy", 2, 10), new MetricReading("cpu", null, 99)], At(0));
        Assert.Equal(1, c.Count, "only slot 1 is over, and the cpu reading isn't this threshold's");
        Assert.Equal(1, c[0].Slot, "slot 1");
        var only2 = d with { Slot = 2 };
        Assert.Equal(0, new ThresholdEvaluator().Evaluate([only2], [new MetricReading("slotBusy", 1, 90), new MetricReading("slotBusy", 2, 10)], At(0)).Count, "a threshold for slot 2 ignores slot 1");
    }

    [Test]
    public static void A_reading_that_goes_away_or_a_removed_threshold_ends_the_alert()
    {
        var d = new ThresholdDef { Id = "q", Metric = ThresholdMetrics.Queue, Warn = 100, ForSeconds = 0 };
        var ev = new ThresholdEvaluator();
        ev.Evaluate([d], [new MetricReading("queue", 3, 500)], At(0));
        var gone = ev.Evaluate([d], [], At(1));
        Assert.Equal(1, gone.Count, "the slot was unloaded");
        Assert.Equal(ThresholdLevel.Ok, gone[0].Level, "so the alert ends");
        ev.Evaluate([d], [new MetricReading("queue", 3, 500)], At(2));
        var removed = ev.Evaluate([], [new MetricReading("queue", 3, 500)], At(3));
        Assert.Equal(ThresholdLevel.Ok, removed[0].Level, "a removed threshold ends too");
        Assert.Equal("threshold:q:3", ThresholdEvaluator.KeyFor(d, 3), "alert key per slot"); Assert.Equal("threshold:q:all", ThresholdEvaluator.KeyFor(d, null), "or for the whole processor");
    }

    [Test]
    public static void Thresholds_are_checked_when_saved()
    {
        Assert.Throws<ArgumentException>(() => ThresholdRules.Validate([new ThresholdDef { Metric = "weather", Warn = 1 }]));
        Assert.Throws<ArgumentException>(() => ThresholdRules.Validate([new ThresholdDef { Metric = "cpu" }]));
        Assert.Throws<ArgumentException>(() => ThresholdRules.Validate([new ThresholdDef { Metric = "cpu", Warn = 90, Crit = 80 }]));
        Assert.Throws<ArgumentException>(() => ThresholdRules.Validate([new ThresholdDef { Metric = "cpu", Warn = 50, ForSeconds = -1 }]));
        var ok = ThresholdRules.Validate([new ThresholdDef { Metric = "cpu", Warn = 50, Crit = 90 }]);
        Assert.True(ok[0].Id.Length > 0, "an id is made");
    }

    // ------------------------------------------------------------------ uptime

    private static UptimeEvent Ev(double sec, string kind, int slot = 0, int ip = 0, string? v = null) => new(At(sec), kind, slot, ip, v);
    private static EntityUptime Slot(UptimeReport r, int n) => r.Entities.Single(e => e.Key.Kind == 's' && e.Key.Slot == n);

    [Test]
    public static void Slot_availability_is_the_running_share_of_the_time_it_held_a_program()
    {
        var ev = new[] { Ev(0, "up"), Ev(0, "s", 1, 0, "Empty"), Ev(100, "s", 1, 0, "Loaded"), Ev(100, "s", 1, 0, "Running"), Ev(700, "s", 1, 0, "Error"), Ev(760, "s", 1, 0, "Running"), Ev(999, "hb") };
        var r = UptimeCalculator.Compute(ev, At(0), At(1000), 10);
        var s = Slot(r, 1);
        Assert.Equal(840, (int)s.UpSeconds, "running 100-700 and 760-1000 = 840 s");
        Assert.Equal(60, (int)s.DownSeconds, "in error for 60 s");
        Assert.Equal(93.33, s.Availability!.Value, "availability, with the empty first 100 s not counted");
        Assert.Equal(1, s.Drops, "one drop (running to error)");
        Assert.Equal(1, s.Outages.Count, "one outage"); Assert.Equal(At(700), s.Outages[0].From, "from"); Assert.Equal(At(760), s.Outages[0].To, "to");
        Assert.Equal(-1, s.Buckets[0], "the empty stretch has no data"); Assert.Equal(1, s.Buckets[2], "a fully-up bucket"); Assert.True(s.Buckets[7] is > 0 and < 1, "bucket 700-800 is part up part down");
    }

    [Test]
    public static void A_restart_counts_as_downtime_whether_or_not_OpenVP_said_goodbye()
    {
        // Clean stop at 300, back at 400.
        var clean = new[] { Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(300, "down"), Ev(400, "up"), Ev(400, "cp"), Ev(400, "s", 1, 0, "Running"), Ev(999, "hb") };
        var a = Slot(UptimeCalculator.Compute(clean, At(0), At(1000), 10), 1);
        Assert.Equal(900, (int)a.UpSeconds, "up 0-300 and 400-1000 = 900"); Assert.Equal(100, (int)a.DownSeconds, "100 s with OpenVP stopped");
        // A crash: last heartbeat at 240, nothing until the new start at 400.
        var crash = new[] { Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(60, "hb"), Ev(120, "hb"), Ev(180, "hb"), Ev(240, "hb"), Ev(400, "up"), Ev(400, "s", 1, 0, "Running"), Ev(999, "hb") };
        var b = Slot(UptimeCalculator.Compute(crash, At(0), At(1000), 10), 1);
        Assert.Equal(240 + 600, (int)b.UpSeconds, "up until the last heartbeat, then from the restart");
        Assert.Equal(160, (int)b.DownSeconds, "down from the last heartbeat to the restart");
        Assert.Equal(1, b.Drops, "the crash is a drop");
        // OpenVP is not running now: after the last record plus the grace time it is down.
        var dead = new[] { Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(60, "hb") };
        var c = Slot(UptimeCalculator.Compute(dead, At(0), At(1000), 10), 1);
        Assert.Equal(60, (int)c.UpSeconds, "up while it was alive"); Assert.Equal(940, (int)c.DownSeconds, "down since");
        // Still alive: a recent heartbeat means up to now.
        var alive = new[] { Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(900, "hb") };
        var d = Slot(UptimeCalculator.Compute(alive, At(0), At(1000), 10), 1);
        Assert.Equal(1000, (int)d.UpSeconds, "a heartbeat 100 s ago is within the grace time");
    }

    [Test]
    public static void The_window_can_start_in_the_middle_of_what_was_recorded_and_ignores_time_before_the_first_record()
    {
        var ev = new[] { Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(500, "s", 1, 0, "Stopped"), Ev(600, "s", 1, 0, "Running"), Ev(999, "hb") };
        var r = UptimeCalculator.Compute(ev, At(450), At(1000), 5);
        var s = Slot(r, 1);
        Assert.Equal(400 + 50, (int)s.UpSeconds, "450-500 and 600-1000"); Assert.Equal(100, (int)s.DownSeconds, "stopped 500-600");
        var early = UptimeCalculator.Compute(ev, At(-5000), At(1000), 5);
        Assert.Equal(At(0), early.Since, "uptime is counted from the first record");
        Assert.Equal(900, (int)Slot(early, 1).UpSeconds, "nothing is invented for the time before it");
        Assert.True(UptimeCalculator.Compute([], At(0), At(10), 5).Entities.Count == 0, "no records, no rows");
    }

    [Test]
    public static void Device_availability_only_counts_while_the_slot_is_running_and_drops_are_counted()
    {
        var ev = new[]
        {
            Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(0, "d", 1, 3, "on"),
            Ev(100, "d", 1, 3, "off"), Ev(160, "d", 1, 3, "on"),           // dropped for 60 s
            Ev(300, "s", 1, 0, "Stopped"), Ev(300, "d", 1, 3, "off"),      // slot stopped: the panel's state doesn't count
            Ev(400, "s", 1, 0, "Running"),                                  // back running, still offline
            Ev(450, "d", 1, 3, "on"), Ev(499, "hb")
        };
        var r = UptimeCalculator.Compute(ev, At(0), At(500), 10);
        var d = r.Entities.Single(e => e.Key.Kind == 'd');
        Assert.Equal(3, d.Key.IpId, "ip id");
        Assert.Equal(100 + 140 + 50, (int)d.UpSeconds, "up 0-100, 160-300, then 450-500");
        Assert.Equal(60 + 50, (int)d.DownSeconds, "down 100-160 and 400-450");
        Assert.Equal(1, d.Drops, "one drop while running (the off at 300 happened while the slot was stopping)");
    }

    [Test]
    public static void The_offline_heat_map_adds_minutes_by_hour_of_day_in_the_viewers_time_and_splits_across_hours()
    {
        // 11:50 to 12:20 UTC offline = 10 min in the 11 o'clock hour and 20 in the 12 o'clock hour (UTC).
        var ev = new[] { Ev(-3600, "up"), Ev(-3600, "s", 1, 0, "Running"), Ev(-3600, "d", 1, 3, "on"), Ev(-600, "d", 1, 3, "off"), Ev(1200, "d", 1, 3, "on") };
        var utc = UptimeCalculator.Heat(ev, At(-3600), At(7200), "hour", 0);
        var row = utc.Rows.Single();
        Assert.Equal(24, row.Minutes.Length, "24 columns");
        Assert.Equal(10, (int)row.Minutes[11], "10 min in hour 11"); Assert.Equal(20, (int)row.Minutes[12], "20 min in hour 12"); Assert.Equal(30, (int)row.TotalMinutes, "30 min");
        Assert.Equal(1, row.Drops[11], "the drop is counted where it started");
        var plus2 = UptimeCalculator.Heat(ev, At(-3600), At(7200), "hour", 120);
        Assert.Equal(10, (int)plus2.Rows[0].Minutes[13], "in a viewer at UTC+2 it is hours 13 and 14"); Assert.Equal(20, (int)plus2.Rows[0].Minutes[14], "hour 14");
        // by day: an outage across local midnight is split between two days
        var ev2 = new[] { Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(0, "d", 1, 3, "on"), Ev(0.5 * 3600, "d", 1, 3, "off"), Ev(3.5 * 3600, "d", 1, 3, "on") };
        var day = UptimeCalculator.Heat(ev2, At(0), At(86400 * 2), "day", 0);
        Assert.Equal("day", day.Mode, "mode"); Assert.True(day.Columns.Count >= 2, "a column for each day");
        Assert.Equal(180, (int)day.Rows[0].Minutes[0], "three hours on the first day");
    }

    [Test]
    public static void The_uptime_store_writes_daily_files_with_checkpoints_and_reads_across_midnight()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-uptime").FullName;
        var now = new DateTimeOffset(2026, 3, 4, 23, 0, 0, TimeSpan.Zero);
        var store = new UptimeStore(dir, null, () => now, heartbeat: false);
        store.Record("s", 1, 0, "Running");
        now = now.AddMinutes(30); store.Record("hb", 0, 0, null);
        now = new DateTimeOffset(2026, 3, 5, 1, 0, 0, TimeSpan.Zero);     // the next day
        store.Record("s", 1, 0, "Error");
        now = now.AddMinutes(30); store.Record("s", 1, 0, "Running");
        now = now.AddHours(2); store.Record("hb", 0, 0, null);
        var files = Directory.GetFiles(Path.Combine(dir, "history"), "uptime-*.jsonl").Select(Path.GetFileName).Order().ToArray();
        Assert.Equal("uptime-20260304.jsonl,uptime-20260305.jsonl", string.Join(",", files!), "one file per day");
        var second = File.ReadAllLines(Path.Combine(dir, "history", "uptime-20260305.jsonl"));
        Assert.True(second[0].Contains("\"k\":\"cp\""), "the new day's file starts with a checkpoint");
        var r = store.Report(new DateTimeOffset(2026, 3, 4, 23, 0, 0, TimeSpan.Zero), now, 12);
        var s = Slot(r, 1);
        Assert.Equal(1800, (int)s.DownSeconds, "the 30 minutes in Error after midnight");
        Assert.Equal(1, s.Drops, "one drop");
        // A fresh store reading the same folder sees the same history.
        store.Dispose();
        var again = new UptimeStore(dir, null, () => now.AddMinutes(1), heartbeat: false);
        var r2 = again.Report(new DateTimeOffset(2026, 3, 5, 0, 30, 0, TimeSpan.Zero), now, 4);
        Assert.True(Slot(r2, 1).UpSeconds > 0, "readable from a new instance");
        again.Dispose();
        Directory.Delete(dir, true);
    }

    [Test]
    public static void A_device_that_never_connected_is_not_counted_as_down_but_one_that_dropped_is()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-uptime3").FullName;
        var p = new Processor(Path.Combine(dir, "p"), 2);
        var now = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);
        var store = new UptimeStore(Path.Combine(dir, "u"), p, () => now, heartbeat: false);
        var never = p.Slot(1).RegisterDevice(5, "Panel", "never connects");
        var panel = p.Slot(1).RegisterDevice(3, "Panel", "Lobby");
        now = now.AddMinutes(10);
        panel.AttachConnection(new DeviceConnection("Internal", "127.0.0.1", 1234));
        now = now.AddMinutes(10); panel.DetachConnection();            // dropped for ten minutes
        now = now.AddMinutes(10); store.Dispose();
        var lines = File.ReadAllLines(Directory.GetFiles(Path.Combine(dir, "u", "history"), "uptime-*.jsonl")[0]);
        Assert.True(lines.Any(l => l.Contains("\"i\":5") && l.Contains("\"v\":\"new\"")), "the device that never connected is recorded as new, not off");
        Assert.True(lines.Any(l => l.Contains("\"i\":3") && l.Contains("\"v\":\"off\"")), "the one that dropped is recorded as off");
        p.Dispose();
        Directory.Delete(dir, true);
    }

    [Test]
    public static void Uptime_files_older_than_90_days_are_deleted()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-uptime2").FullName;
        var h = Path.Combine(dir, "history"); Directory.CreateDirectory(h);
        File.WriteAllText(Path.Combine(h, "uptime-20251101.jsonl"), "{\"t\":1,\"k\":\"up\"}\n");
        File.WriteAllText(Path.Combine(h, "uptime-20260301.jsonl"), "{\"t\":1,\"k\":\"up\"}\n");
        new UptimeStore(dir, null, () => new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero), heartbeat: false).Dispose();
        Assert.True(!File.Exists(Path.Combine(h, "uptime-20251101.jsonl")), "old file gone");
        Assert.True(File.Exists(Path.Combine(h, "uptime-20260301.jsonl")), "recent file kept");
        Directory.Delete(dir, true);
    }

    // ------------------------------------------------------------------ metrics history

    private static MetricSample Sample(double t, double cpu, double busy = 0, double queue = 0, int slot = 1) =>
        new(T0.AddSeconds(t), cpu, 200 + cpu, 30, 0, 0, [new SlotSample(slot, busy, 5, queue, 0)]);

    [Test]
    public static void Seconds_are_averaged_into_minutes_keeping_the_peak()
    {
        var clock = T0.AddMinutes(10);
        var h = new MetricsHistory(Directory.CreateTempSubdirectory("openvp-mh").FullName, () => clock, load: false);
        for (var i = 0; i < 120; i++) h.Add(Sample(i, i < 60 ? 10 : 30, busy: i == 30 ? 100 : 0, queue: i == 90 ? 500 : 0));
        h.Add(Sample(120, 99));                           // the third minute has begun: the first two are closed
        var s = h.Query("1h", TimeSpan.FromHours(1));
        Assert.Equal(60, s.StepSeconds, "one point a minute");
        Assert.Equal(3, s.Points.Count, "two finished minutes and the one in progress");
        Assert.Equal(10, s.Points[0].Cpu, "first minute average"); Assert.Equal(30, s.Points[1].Cpu, "second minute average");
        Assert.Equal(60, s.Points[0].Seconds, "60 seconds in a point");
        Assert.Equal(100, s.Points[0].Slots[0].BusyMax, "the peak is kept"); Assert.Equal(1.67, s.Points[0].Slots[0].Busy, "while the average is small");
        Assert.Equal(500, s.Points[1].Slots[0].QueueMax, "queue peak in the second minute");
        Assert.Equal(99, s.Points[2].Cpu, "the point in progress is shown");
        clock = T0.AddSeconds(125);
        Assert.Equal(121, h.Query("5m", TimeSpan.FromMinutes(5)).Points.Count, "a five-minute range is second by second");
    }

    [Test]
    public static void Minutes_are_merged_into_fifteen_minute_points_weighted_by_time_and_persisted()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-mh2").FullName;
        var now = T0.AddDays(10);
        var h = new MetricsHistory(dir, () => now, load: false);
        var start = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);
        for (var m = 0; m < 31; m++)
            for (var s = 0; s < 60; s += 20)    // sparse seconds are fine: the weights are by samples
                h.Add(new MetricSample(start.AddMinutes(m).AddSeconds(s), m < 15 ? 20 : 40, 200, 30, 0, 0, []));
        h.Flush();
        var q = h.Query("30d", TimeSpan.FromDays(30));
        Assert.Equal(900, q.StepSeconds, "fifteen-minute points for a long range");
        Assert.True(q.Points.Count >= 2, "at least two quarter hours");
        Assert.Equal(20, q.Points[0].Cpu, "first quarter hour averages 20"); Assert.Equal(40, q.Points[1].Cpu, "second 40");
        Assert.True(File.Exists(Path.Combine(dir, "history", "metrics-1m-20260304.jsonl")), "minute file");
        Assert.True(File.Exists(Path.Combine(dir, "history", "metrics-15m-202603.jsonl")), "quarter-hour file");
        // A new instance reads them back.
        var again = new MetricsHistory(dir, () => now);
        var q2 = again.Query("30d", TimeSpan.FromDays(30));
        Assert.Equal(q.Points.Count, q2.Points.Count, "same points after a restart");
        Assert.Equal(20, q2.Points[0].Cpu, "same values");
        var wk = again.Query("7d", TimeSpan.FromDays(7));
        Assert.Equal(0, wk.Points.Count, "the minutes are 10 days old, out of the 7-day tier");
        Assert.Equal(40, again.Peaks(TimeSpan.FromDays(30)).Cpu, "peaks are readable");
    }

    [Test]
    public static void Long_ranges_are_merged_down_to_a_drawable_number_of_points()
    {
        var now = T0.AddDays(2);
        var h = new MetricsHistory(Directory.CreateTempSubdirectory("openvp-mh3").FullName, () => now, load: false);
        for (var m = 0; m < 2000; m++) h.Add(new MetricSample(T0.AddMinutes(m), m, 100, 20, 0, 0, []));
        h.Flush();
        var s = h.Query("24h", TimeSpan.FromHours(48), maxPoints: 200);
        Assert.True(s.Points.Count <= 200, $"at most 200 points, got {s.Points.Count}");
        Assert.True(s.StepSeconds > 60, "wider steps");
        Assert.True(s.Points.Max(p => p.CpuMax) >= 1998, "the peak survives the merge");
        var a = MetricsHistory.Merge(new HistoryPoint(T0, 60, 10, 20, 0, 0, 0, 0, 0, 0, []), new HistoryPoint(T0, 180, 30, 40, 0, 0, 0, 0, 0, 0, []), T0);
        Assert.Equal(25, a.Cpu, "averages are weighted by seconds (60 s at 10 and 180 s at 30)"); Assert.Equal(40, a.CpuMax, "maximum is the larger");
        Assert.Equal(T0.AddMinutes(15), MetricsHistory.Floor(T0.AddMinutes(22), TimeSpan.FromMinutes(15)), "floor to the quarter hour");
    }

    // ------------------------------------------------------------------ reports

    [Test]
    public static void A_report_summarises_uptime_crashes_alerts_dropped_devices_and_program_changes()
    {
        var from = At(0); var to = At(86400);
        var uptime = UptimeCalculator.Compute([Ev(0, "up"), Ev(0, "s", 1, 0, "Running"), Ev(0, "s", 2, 0, "Running"), Ev(0, "d", 1, 3, "on"),
            Ev(3600, "d", 1, 3, "off"), Ev(3900, "d", 1, 3, "on"), Ev(40000, "s", 2, 0, "Error"), Ev(40600, "s", 2, 0, "Running"), Ev(86300, "hb")], from, to, 24);
        var alerts = new List<AlertView>
        {
            new("1", "slot.fault:2", AlertKinds.SlotFault, 2, AlertSeverity.Critical, "Slot 2 is in Error", "m", At(40000), At(40000), 1, "resolved", At(40600), null, null, null, []),
            new("2", "watch:1:w", AlertKinds.Watch, 1, AlertSeverity.Warning, "Slot 1: watch fired", "m", At(5000), At(9000), 4, "event", null, null, null, null, []),
        };
        var activity = new List<ActivityEntry>
        {
            new(At(40010), "crash", "Slot 2 crashed (stuck in OnPoll): restarting (1 of 3 in 10 min)", 2, "auto"),
            new(At(41000), "crash", "Slot 2 crashed (x): policy is to stay stopped", 2, "auto"),
            new(At(500), "load", "Loaded Lobby.cpz into slot 1", 1, "this computer"),
            new(At(600), "settings", "Changed a setting", 1, "this computer"),
            new(At(-90000), "load", "Too old", 1, "this computer"),
        };
        var d = HealthReport.Build("daily", from, to, uptime, alerts, activity, (87.5, 612.2, 41), n => (n == 1 ? "Lobby" : null, n == 1 ? "Lobby.cpz" : "Meeting.lpz"), "box");
        Assert.Equal(2, d.Slots.Count, "two slots");
        var s2 = d.Slots.Single(s => s.Slot == 2);
        Assert.Equal(2, s2.Crashes, "two crashes for slot 2"); Assert.Equal(1, s2.Restarts, "one restart"); Assert.Equal(1, s2.Alerts, "one alert");
        Assert.Equal(600, (int)s2.DownSeconds, "ten minutes down");
        Assert.True(s2.Availability is > 99 and < 100, "uptime near 99.3: " + s2.Availability);
        Assert.Equal(1, d.Devices.Count, "one device dropped"); Assert.Equal(1, d.Devices[0].Drops, "one drop"); Assert.Equal(300, (int)d.Devices[0].OfflineSeconds, "for five minutes");
        Assert.Equal(1, d.Critical, "critical"); Assert.Equal(1, d.Warning, "warning"); Assert.Equal(2, d.AlertsTotal, "total");
        Assert.Equal("Slot 1: watch fired", d.TopEvents[0].Title, "most frequent first (4 times)"); Assert.Equal(4, d.TopEvents[0].Count, "count sums repeats");
        Assert.Equal(1, d.Changes.Count, "only the load inside the period");
        Assert.Equal(87.5, d.PeakCpu, "peak cpu");

        var html = HealthReport.Html(d);
        Assert.True(html.Contains("Lobby</td>") && html.Contains("Lobby.cpz") && html.Contains("99.3"), "html has the slot, program and uptime");
        Assert.True(html.Contains("Slot 2 is in Error") && html.Contains("Loaded Lobby.cpz into slot 1") && html.Contains("87.5%"), "html has top events, changes and the peak");
        Assert.True(!html.Contains("<script") && html.StartsWith("<!doctype html>"), "a plain page");
        var text = HealthReport.Summary(d);
        Assert.True(text.Contains("Slot 2 (Meeting.lpz): 99.31% up") && text.Contains("2 crashes (1 restart)") && text.Contains("IP ID 03 (1x, 5 min)"), "text summary: " + text);
        Assert.True(HealthReport.Facts(d).Any(f => f.Key == "Peak CPU" && f.Value == "87.5%"), "facts for the chat cards");
    }

    [Test]
    public static void The_report_is_sent_once_for_each_scheduled_time_and_not_for_a_time_already_past()
    {
        var sent = 0;
        var cfg = AlertConfig.Default() with { Report = new ReportSchedule { Enabled = true, Frequency = "daily", Time = "08:00", Channels = ["desktop"] } };
        var now = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);
        var sch = new ReportScheduler(() => cfg, _ => { sent++; return Task.CompletedTask; }, null, () => now, TimeZoneInfo.Utc, autoStart: false);
        Assert.True(!sch.Tick().GetAwaiter().GetResult(), "just turned on at 10:00: today's 08:00 is already past, so nothing goes out");
        now = now.AddHours(20);   // 06:00 next day
        Assert.True(!sch.Tick().GetAwaiter().GetResult(), "before 08:00");
        now = now.AddHours(2.5);  // 08:30
        Assert.True(sch.Tick().GetAwaiter().GetResult(), "at 08:30 it goes out"); Assert.Equal(1, sent, "once");
        now = now.AddHours(1);
        Assert.True(!sch.Tick().GetAwaiter().GetResult(), "not twice"); Assert.Equal(1, sent, "still once");
        now = now.AddDays(1);
        Assert.True(sch.Tick().GetAwaiter().GetResult(), "next day"); Assert.Equal(2, sent, "twice");

        var weekly = new ReportSchedule { Enabled = true, Frequency = "weekly", Day = 1, Time = "08:00" };   // Mondays
        var wed = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);                                    // a Wednesday
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero), ReportScheduler.LastDue(weekly, wed, TimeZoneInfo.Utc)!.Value, "the Monday before");
        Assert.Equal(new DateTimeOffset(2026, 3, 3, 8, 0, 0, TimeSpan.Zero), ReportScheduler.LastDue(new ReportSchedule { Time = "08:00" }, new DateTimeOffset(2026, 3, 4, 7, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc)!.Value, "yesterday 08:00 before 08:00 today");
    }
}
