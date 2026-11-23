using System.Globalization;
using OpenVP.Core.Ops;

namespace OpenVP.Tests;

/// <summary>The cron engine and the scheduler, including daylight saving changes and runs missed while OpenVP was off.</summary>
public static class SchedulerTests
{
    static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
    static DateTimeOffset U(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
    static string Z(DateTimeOffset? t) => t == null ? "none" : t.Value.UtcDateTime.ToString(t.Value.UtcDateTime.Second == 0 ? "yyyy-MM-dd'T'HH:mm'Z'" : "yyyy-MM-dd'T'HH:mm:ss'Z'");
    static DateTimeOffset? Next(string cron, string after, TimeZoneInfo? zone = null) => CronExpression.Parse(cron).Next(U(after), zone ?? TimeZoneInfo.Utc);

    // ------------------------------------------------------------------ parsing

    [Test]
    public static void Cron_basic_fields()
    {
        Assert.Equal("2025-01-01T03:30Z", Z(Next("30 3 * * *", "2025-01-01T00:00Z")), "daily");
        Assert.Equal("2025-01-02T03:30Z", Z(Next("30 3 * * *", "2025-01-01T03:30Z")), "strictly after");
        Assert.Equal("2025-01-01T00:20Z", Z(Next("*/20 * * * *", "2025-01-01T00:05Z")), "step");
        Assert.Equal("2025-01-01T08:00Z", Z(Next("0 8,17 * * *", "2025-01-01T00:00Z")), "list");
        Assert.Equal("2025-01-01T17:00Z", Z(Next("0 8,17 * * *", "2025-01-01T08:00Z")), "list second");
        Assert.Equal("2025-01-06T09:00Z", Z(Next("0 9 * * mon-fri", "2025-01-03T09:00Z")), "weekdays skip the weekend");
        Assert.Equal("2025-06-06T00:00Z", Z(Next("0 0 13 * fri", "2025-06-01T00:00Z")), "day-of-month and day-of-week are either/or");
        Assert.Equal("2028-02-29T00:00Z", Z(Next("0 0 29 2 *", "2025-01-01T00:00Z")), "leap day");
        Assert.True(Next("0 0 31 2 *", "2025-01-01T00:00Z") == null, "February 31st never comes");
        Assert.Equal("2025-01-05T00:00Z", Z(Next("@weekly", "2025-01-01T00:00Z")), "@weekly is Sunday midnight");
        Assert.Equal("2025-01-05T10:00Z", Z(Next("0 10 * * 7", "2025-01-01T00:00Z")), "7 is Sunday");
    }

    [Test]
    public static void Cron_errors_say_which_field()
    {
        Assert.True(Assert.Throws<FormatException>(() => CronExpression.Parse("61 * * * *")).Message.Contains("minute"), "minute range");
        Assert.True(Assert.Throws<FormatException>(() => CronExpression.Parse("* 25 * * *")).Message.Contains("hour"), "hour range");
        Assert.True(Assert.Throws<FormatException>(() => CronExpression.Parse("* * *")).Message.Contains("5 fields"), "field count");
        Assert.True(Assert.Throws<FormatException>(() => CronExpression.Parse("* * * foo *")).Message.Contains("month"), "month name");
        Assert.True(Assert.Throws<FormatException>(() => CronExpression.Parse("5-1 * * * *")).Message.Contains("backwards"), "backwards range");
        Assert.True(Assert.Throws<FormatException>(() => CronExpression.Parse("*/0 * * * *")).Message.Contains("step"), "zero step");
        Assert.True(Assert.Throws<FormatException>(() => CronExpression.Parse("")).Message.Contains("Enter"), "empty");
    }

    // ------------------------------------------------------------------ daylight saving

    [Test]
    public static void Dst_spring_forward_runs_a_skipped_time_after_the_gap()
    {
        var ny = Zone("America/New_York"); // 2025-03-09 02:00 EST becomes 03:00 EDT
        var t1 = Next("30 2 * * *", "2025-03-08T12:00Z", ny);
        Assert.Equal("2025-03-09T07:00Z", Z(t1), "02:30 doesn't exist on the 9th: it runs at 03:00 EDT, once");
        Assert.Equal("2025-03-10T06:30Z", Z(CronExpression.Parse("30 2 * * *").Next(t1!.Value, ny)), "the next day is normal (02:30 EDT)");
        Assert.Equal("2025-03-09T07:00Z", Z(Next("0 3 * * *", "2025-03-08T12:00Z", ny)), "03:00 on the 9th is the same instant");
        // A job at both 02:30 and 03:00 runs once at 03:00, not twice.
        var both = CronExpression.Parse("30 2 * * *").Upcoming(U("2025-03-08T12:00Z"), ny, 3);
        Assert.Equal("2025-03-09T07:00Z|2025-03-10T06:30Z|2025-03-11T06:30Z", string.Join("|", both.Select(x => Z(x))), "three days in a row");
        var two = CronExpression.Parse("0,30 2,3 * * *").Upcoming(U("2025-03-09T00:00Z"), ny, 4);
        Assert.Equal("2025-03-09T07:00Z|2025-03-09T07:30Z|2025-03-10T06:00Z|2025-03-10T06:30Z", string.Join("|", two.Select(x => Z(x))), "gap times and real times that land on the same moment are one run");
    }

    [Test]
    public static void Dst_fall_back_runs_a_repeated_time_once_unless_the_hour_is_a_wildcard()
    {
        var ny = Zone("America/New_York"); // 2025-11-02 02:00 EDT becomes 01:00 EST: 01:00-02:00 happens twice
        Assert.Equal("2025-11-02T05:30Z", Z(Next("30 1 * * *", "2025-11-02T04:00Z", ny)), "01:30 runs the first time (EDT)");
        Assert.Equal("2025-11-03T06:30Z", Z(Next("30 1 * * *", "2025-11-02T05:30Z", ny)), "and not again at 01:30 EST");
        var hourly = CronExpression.Parse("0 * * * *").Upcoming(U("2025-11-02T04:30Z"), ny, 4);
        Assert.Equal("2025-11-02T05:00Z|2025-11-02T06:00Z|2025-11-02T07:00Z|2025-11-02T08:00Z", string.Join("|", hourly.Select(x => Z(x))), "an hourly job still runs every hour, including both 01:00s");
        var q = CronExpression.Parse("*/30 * * * *").Upcoming(U("2025-11-02T05:00Z"), ny, 4);
        Assert.Equal("2025-11-02T05:30Z|2025-11-02T06:00Z|2025-11-02T06:30Z|2025-11-02T07:00Z", string.Join("|", q.Select(x => Z(x))), "half-hourly too");
        Assert.Equal("2025-11-03T07:30Z", Z(Next("30 2 * * *", "2025-11-02T10:00Z", ny)), "02:30 isn't repeated: normal next day");
    }

    [Test]
    public static void Dst_other_zones()
    {
        Assert.Equal("2025-03-30T01:00Z", Z(Next("30 2 * * *", "2025-03-29T12:00Z", Zone("Europe/Berlin"))), "Berlin: 02:30 skipped, runs at 03:00 CEST = 01:00Z");
        Assert.Equal("2025-10-04T16:00Z", Z(Next("30 2 * * *", "2025-10-03T20:00Z", Zone("Australia/Sydney"))), "Sydney (southern hemisphere): runs at 03:00 AEDT");
        Assert.Equal("2025-10-26T00:30Z", Z(Next("30 2 * * *", "2025-10-25T12:00Z", Zone("Europe/Berlin"))), "Berlin fall back: 02:30 is the first one (CEST)");
        Assert.Equal("2025-06-01T00:00Z", Z(Next("0 12 * * *", "2025-05-31T20:00Z", Zone("Pacific/Auckland"))), "a zone ahead of UTC");
    }

    [Test]
    public static void One_off_times_in_dst_changes()
    {
        var ny = Zone("America/New_York");
        Assert.Equal("2025-03-09T07:00Z", Z(CronExpression.InstantOf(new DateTime(2025, 3, 9, 2, 30, 0), ny)), "skipped time runs after the gap");
        Assert.Equal("2025-11-02T05:30Z", Z(CronExpression.InstantOf(new DateTime(2025, 11, 2, 1, 30, 0), ny)), "repeated time is the first one");
        Assert.Equal("2025-07-01T16:00Z", Z(CronExpression.InstantOf(new DateTime(2025, 7, 1, 12, 0, 0), ny)), "ordinary time");
    }

    // ------------------------------------------------------------------ the scheduler

    sealed class Clock { public DateTimeOffset Now; }

    static Scheduler Make(string dir, Clock c, TimeZoneInfo zone, List<string> ran, Func<ScheduleDef, string, Task<string>>? exec = null) =>
        new(dir, exec ?? ((d, t) => { lock (ran) ran.Add($"{d.Name}:{t}"); return Task.FromResult("done"); }), zone, () => c.Now, autoStart: false);

    static ScheduleDef Daily(string name, string time, bool runMissed = false, string type = "restart") =>
        new() { Name = name, Mode = "daily", Time = time, RunMissed = runMissed, Action = new ScheduleAction { Type = type, Slot = 1 } };

    [Test]
    public static void Scheduler_runs_a_daily_schedule_once_at_its_time()
    {
        var dir = Paths.TempData();
        var clock = new Clock { Now = U("2025-03-08T20:00Z") };
        var ran = new List<string>();
        using var s = Make(dir, clock, Zone("America/New_York"), ran);
        var def = s.Add(Daily("Nightly", "03:30"));
        Assert.Equal("2025-03-09T07:30Z", Z(def.NextDue), "03:30 EDT on the morning the clocks went forward");
        clock.Now = U("2025-03-09T07:29:59Z"); s.Tick();
        Assert.Equal(0, ran.Count, "not yet");
        clock.Now = U("2025-03-09T07:30:00Z"); s.Tick();
        Assert.Equal(1, ran.Count, "ran at its time");
        Assert.Equal("Nightly:schedule", ran[0], "trigger");
        s.Tick(); clock.Now = U("2025-03-09T07:30:05Z"); s.Tick();
        Assert.Equal(1, ran.Count, "only once");
        Assert.Equal("2025-03-10T07:30Z", Z(s.Get(def.Id)!.NextDue), "next day, still 03:30 EDT");
        var v = s.List()[0];
        Assert.True(v.Def.LastOk == true && v.Def.Runs == 1, "state recorded");
        Assert.Equal(1, s.History().Count, "history entry");
    }

    [Test]
    public static void Scheduler_runs_a_skipped_dst_time_after_the_gap()
    {
        var clock = new Clock { Now = U("2025-03-08T20:00Z") };
        var ran = new List<string>();
        using var s = Make(Paths.TempData(), clock, Zone("America/New_York"), ran);
        var def = s.Add(Daily("Two thirty", "02:30"));
        Assert.Equal("2025-03-09T07:00Z", Z(def.NextDue), "02:30 doesn't exist that day");
        clock.Now = U("2025-03-09T07:00:00Z"); s.Tick();
        Assert.Equal(1, ran.Count, "ran once");
        Assert.Equal("2025-03-10T06:30Z", Z(s.Get(def.Id)!.NextDue), "02:30 EDT the next day");
    }

    [Test]
    public static void Scheduler_missed_runs_are_skipped_unless_the_schedule_asks()
    {
        var dir = Paths.TempData();
        var clock = new Clock { Now = U("2025-01-10T10:00Z") };
        var ran = new List<string>();
        string a, b;
        using (var s = Make(dir, clock, TimeZoneInfo.Utc, ran))
        {
            a = s.Add(Daily("Skips", "03:00", runMissed: false)).Id;
            b = s.Add(Daily("Catches up", "03:00", runMissed: true)).Id;
        }
        // OpenVP was off; it starts again at 08:00 the next day, five hours after the 03:00 run it missed.
        clock.Now = U("2025-01-11T08:00Z");
        using (var s = Make(dir, clock, TimeZoneInfo.Utc, ran))
        {
            Assert.Equal(2, s.Tick(), "both were due");
            Assert.Equal(1, ran.Count, "only the one that asked to run missed runs ran");
            Assert.Equal("Catches up:missed", ran[0], "and says it was a missed run");
            var hist = s.History();
            Assert.True(hist.Any(h => h.Name == "Skips" && h.Status == "skipped" && h.Message.Contains("wasn't running")), "the skipped one is in the history");
            Assert.Equal("2025-01-12T03:00Z", Z(s.Get(a)!.NextDue), "next regular time");
            Assert.Equal("2025-01-12T03:00Z", Z(s.Get(b)!.NextDue), "next regular time");
        }
        // Several days off: a schedule that asks still only catches up within 24 hours.
        clock.Now = U("2025-01-20T09:00Z");
        ran.Clear();
        using (var s = Make(dir, clock, TimeZoneInfo.Utc, ran))
        {
            s.Tick();
            Assert.Equal(0, ran.Count, "a run missed more than 24 hours ago is skipped even when asked");
            Assert.True(s.History().Count(h => h.Status == "skipped") >= 2, "both noted as skipped");
        }
    }

    [Test]
    public static void Scheduler_missed_several_times_runs_once()
    {
        var dir = Paths.TempData();
        var clock = new Clock { Now = U("2025-01-10T10:00Z") };
        var ran = new List<string>();
        using (var s = Make(dir, clock, TimeZoneInfo.Utc, ran)) s.Add(new ScheduleDef { Name = "Often", Mode = "cron", Cron = "0 * * * *", RunMissed = true, Action = new ScheduleAction { Type = "start", Slot = 1 } });
        clock.Now = U("2025-01-10T15:30Z");
        using (var s = Make(dir, clock, TimeZoneInfo.Utc, ran)) { s.Tick(); s.Tick(); }
        Assert.Equal(1, ran.Count, "five missed hours are one run");
    }

    [Test]
    public static void Scheduler_one_off_runs_once_then_is_done()
    {
        var clock = new Clock { Now = U("2025-06-01T10:00Z") };
        var ran = new List<string>();
        using var s = Make(Paths.TempData(), clock, Zone("Europe/Berlin"), ran);
        var d = s.Add(new ScheduleDef { Name = "Once", Mode = "once", At = "2025-06-01T14:00", Action = new ScheduleAction { Type = "stop", Slot = 2 } });
        Assert.Equal("2025-06-01T12:00Z", Z(d.NextDue), "14:00 CEST is 12:00 UTC");
        clock.Now = U("2025-06-01T12:00:01Z"); s.Tick();
        Assert.Equal(1, ran.Count, "ran");
        var after = s.Get(d.Id)!;
        Assert.True(!after.Enabled && after.NextDue == null, "switched off when done");
        clock.Now = U("2025-06-02T12:00Z"); s.Tick();
        Assert.Equal(1, ran.Count, "never again");
        // A one-off in the past when it is made is accepted but never runs.
        var old = s.Add(new ScheduleDef { Name = "Past", Mode = "once", At = "2025-01-01T00:00", Action = new ScheduleAction { Type = "stop", Slot = 2 } });
        clock.Now = U("2025-06-03T00:00Z"); s.Tick();
        Assert.Equal(1, ran.Count, "a one-off in the past doesn't run");
        Assert.True(old.NextDue == null, "no next run");
    }

    [Test]
    public static void Scheduler_failed_runs_are_recorded_and_disabled_schedules_dont_run()
    {
        var clock = new Clock { Now = U("2025-01-01T00:00Z") };
        var ran = new List<string>();
        using var s = Make(Paths.TempData(), clock, TimeZoneInfo.Utc, ran, (d, t) => throw new InvalidOperationException("Slot 1 has no program to restart."));
        var seen = new List<ScheduleRun>();
        s.Ran += r => seen.Add(r);
        var d = s.Add(Daily("Broken", "01:00"));
        clock.Now = U("2025-01-01T01:00Z"); s.Tick();
        Assert.Equal(1, seen.Count, "ran event");
        Assert.Equal("failed", seen[0].Status, "status");
        Assert.True(seen[0].Message.Contains("no program"), "message");
        var def = s.Get(d.Id)!;
        Assert.True(def.LastOk == false && def.LastResult!.Contains("no program"), "last result kept");
        s.Update(d.Id, x => x with { Enabled = false });
        clock.Now = U("2025-01-02T01:00Z"); s.Tick();
        Assert.Equal(1, seen.Count, "disabled: not run");
        s.Update(d.Id, x => x with { Enabled = true });
        Assert.Equal("2025-01-03T01:00Z", Z(s.Get(d.Id)!.NextDue), "enabling doesn't catch up on what was missed while it was off");
    }

    [Test]
    public static void Scheduler_run_now_and_no_overlap()
    {
        var clock = new Clock { Now = U("2025-01-01T00:00Z") };
        var gate = new TaskCompletionSource<string>();
        var starts = 0;
        using var s = Make(Paths.TempData(), clock, TimeZoneInfo.Utc, new List<string>(), (d, t) => { Interlocked.Increment(ref starts); return gate.Task; });
        var d = s.Add(new ScheduleDef { Name = "Slow", Mode = "cron", Cron = "* * * * *", Action = new ScheduleAction { Type = "backup" } });
        clock.Now = U("2025-01-01T00:01Z"); s.Tick();
        Assert.Equal(1, starts, "started");
        clock.Now = U("2025-01-01T00:02Z"); s.Tick();
        Assert.Equal(1, starts, "still running, so the next one is skipped");
        Assert.True(s.History().Any(h => h.Status == "skipped" && h.Message.Contains("still going")), "noted");
        gate.SetResult("ok");
        Assert.Eventually(() => !s.List()[0].Running, "finished");
        var run = s.RunNowAsync(d.Id).GetAwaiter().GetResult();
        Assert.True(run != null && run.Trigger == "manual", "run now works whatever the time");
    }

    [Test]
    public static void Scheduler_validates_what_a_person_typed()
    {
        var clock = new Clock { Now = U("2025-01-01T00:00Z") };
        using var s = Make(Paths.TempData(), clock, TimeZoneInfo.Utc, new List<string>());
        string Msg(ScheduleDef d) => Assert.Throws<ArgumentException>(() => s.Add(d)).Message;
        Assert.True(Msg(new ScheduleDef { Name = "x", Mode = "weekly", Time = "03:00", Days = [], Action = new ScheduleAction { Type = "restart", Slot = 1 } }).Contains("day"), "weekly needs a day");
        Assert.True(Msg(new ScheduleDef { Name = "x", Mode = "daily", Time = "25:00", Action = new ScheduleAction { Type = "restart", Slot = 1 } }).Contains("HH:mm"), "bad time");
        Assert.True(Msg(new ScheduleDef { Name = "", Mode = "daily", Time = "03:00", Action = new ScheduleAction { Type = "restart", Slot = 1 } }).Contains("name"), "needs a name");
        Assert.True(Msg(new ScheduleDef { Name = "x", Mode = "cron", Cron = "99 * * * *", Action = new ScheduleAction { Type = "restart", Slot = 1 } }).Contains("minute"), "bad cron");
        Assert.True(Msg(new ScheduleDef { Name = "x", Mode = "daily", Time = "03:00", Action = new ScheduleAction { Type = "restart" } }).Contains("slot"), "slot needed");
        Assert.True(Msg(new ScheduleDef { Name = "x", Mode = "daily", Time = "03:00", Action = new ScheduleAction { Type = "setSignal", Slot = 1, Signal = "A" } }).Contains("value"), "value needed");
        Assert.True(Msg(new ScheduleDef { Name = "x", Mode = "daily", Time = "03:00", Action = new ScheduleAction { Type = "loadVersion", Slot = 1 } }).Contains("version"), "version needed");
        Assert.True(Msg(new ScheduleDef { Name = "x", Mode = "once", At = "tomorrow", Action = new ScheduleAction { Type = "stop", Slot = 1 } }).Contains("yyyy-MM-dd"), "bad date");
        var ok = s.Add(new ScheduleDef { Name = "Backup", Mode = "weekly", Time = "01:15", Days = [1, 3], Action = new ScheduleAction { Type = "backup" } });
        Assert.Equal("Mon, Wed at 01:15", Scheduler.Describe(ok), "description");
        Assert.Equal("2025-01-01T01:15Z", Z(ok.NextDue), "Wednesday 1 January 2025");
    }
}
