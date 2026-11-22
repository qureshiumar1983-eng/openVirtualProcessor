using OpenVP.Core;

namespace OpenVP.Tests;

public static class ActivityLogTests
{
    static string Dir() => Directory.CreateTempSubdirectory("openvp-activity").FullName;

    [Test]
    public static void Entries_come_back_newest_first_and_can_be_filtered_by_slot()
    {
        var log = new ActivityLog(null);
        log.Add("load", "Loaded A into slot 1", 1, "this computer");
        log.Add("start", "Started A in slot 1", 1, "this computer");
        log.Add("load", "Loaded B into slot 2", 2, "192.168.1.9");
        log.Add("internet.on", "Turned Internet access on", null, "this computer");
        var all = log.Query();
        Assert.Equal(4, all.Count, "all four");
        Assert.Equal("internet.on", all[0].Action, "newest first");
        Assert.Equal("Loaded A into slot 1", all[^1].Text, "oldest last");
        var s1 = log.Query(slot: 1);
        Assert.Equal(2, s1.Count, "slot 1 has two");
        Assert.True(s1.All(e => e.Slot == 1), "only slot 1");
        Assert.Equal("192.168.1.9", log.Query(2)[0].Who, "who is kept");
        Assert.Equal(2, log.Query(limit: 2).Count, "limit");
        Assert.Equal(0, log.Query(slot: 9).Count, "nothing for a slot with no entries");
    }

    [Test]
    public static void Who_defaults_to_this_computer_and_a_user_name_can_ride_along()
    {
        var log = new ActivityLog(null);
        var a = log.Add("stop", "Stopped", 3);
        Assert.Equal(Actors.ThisComputer, a.Who, "default who");
        Assert.True(a.User == null, "no user until accounts exist");
        var b = log.Add("stop", "Stopped", 3, "10.0.0.5", user: "alice");
        Assert.Equal("alice", log.Query()[0].User, "a user name is kept");
        Assert.Equal("10.0.0.5", b.Who, "and the address stays");
        Assert.Equal("this computer", Actors.FromAddress("127.0.0.1", true), "local request");
        Assert.Equal("10.1.1.1", Actors.FromAddress("10.1.1.1", false), "remote request");
        Assert.Equal("unknown", Actors.FromAddress(null, false), "no address");
        Assert.Equal("share link Boardroom", Actors.ShareLink(" Boardroom "), "share link actor");
        Assert.Equal("share link", Actors.ShareLink(""), "unlabelled share link");
    }

    [Test]
    public static void The_feed_survives_a_restart_and_a_damaged_line_is_skipped()
    {
        var dir = Dir();
        var log = new ActivityLog(dir);
        log.Add("load", "one", 1, "auto", detail: "SIMPL#");
        log.Add("restore", "two", 2, "auto", user: "bob");
        File.AppendAllText(Path.Combine(dir, "activity.jsonl"), "{this is not json\n\n");
        log.Add("stop", "three", 1);
        var again = new ActivityLog(dir);
        var all = again.Query();
        Assert.Equal(3, all.Count, "three good entries after a restart");
        Assert.Equal("three", all[0].Text, "order kept");
        Assert.Equal("bob", all[1].User, "user round-trips");
        Assert.Equal("SIMPL#", all[2].Detail, "detail round-trips");
        Assert.True(File.ReadAllText(Path.Combine(dir, "activity.jsonl")).Contains("\"who\":\"auto\""), "stored as JSON lines");
    }

    [Test]
    public static void Only_the_last_entries_are_kept_in_memory_and_in_the_file()
    {
        var dir = Dir();
        var log = new ActivityLog(dir, max: 20);
        for (var i = 0; i < 100; i++) log.Add("x", "entry " + i, i % 3);
        Assert.Equal(20, log.Count, "capped at max");
        Assert.Equal("entry 99", log.Query()[0].Text, "newest kept");
        Assert.Equal("entry 80", log.Query(limit: 100)[^1].Text, "oldest kept is 80");
        var lines = File.ReadAllLines(Path.Combine(dir, "activity.jsonl")).Length;
        Assert.True(lines <= 20 + 2 + 1, $"the file is trimmed too ({lines} lines)");
        Assert.Equal(20, new ActivityLog(dir, max: 20).Count, "and reloads at the cap");
        Assert.Equal("entry 99", new ActivityLog(dir, max: 20).Query()[0].Text, "with the newest still there");
    }

    [Test]
    public static void Adding_from_many_threads_loses_nothing()
    {
        var dir = Dir();
        var log = new ActivityLog(dir);
        var seen = 0;
        log.Added += _ => Interlocked.Increment(ref seen);
        Parallel.For(0, 8, t => { for (var i = 0; i < 50; i++) log.Add("x", $"t{t} {i}", t + 1); });
        Assert.Equal(400, log.Count, "all entries");
        Assert.Equal(400, seen, "event raised for each");
        Assert.Equal(400, new ActivityLog(dir).Count, "all saved");
    }

    [Test]
    public static void A_feed_that_cant_be_saved_still_works()
    {
        var dir = Dir();
        File.WriteAllText(Path.Combine(dir, "blocker"), "x");
        var log = new ActivityLog(Path.Combine(dir, "blocker"));   // a file where the folder should be
        log.Add("x", "still here", 1);
        Assert.Equal(1, log.Count, "kept in memory");
    }

    [Test]
    public static void Crash_policy_and_start_up_restores_are_reported_as_events()
    {
        var dir = Dir();
        using var p = new Processor(dir, 2);
        using var crash = new CrashSupervisor(p, restartDelay: TimeSpan.Zero, stuckCheck: TimeSpan.FromHours(1));
        var got = new List<(int, string, string)>();
        crash.Acted += (n, what, why) => { lock (got) got.Add((n, what, why)); };
        p.AddLoader(new OpenVP.SimplSharpHost.SimplSharpLoader());
        p.LoadProgram(2, Paths.SampleDll);
        p.Slot(2).Fault("unhandled exception");
        Assert.Eventually(() => { lock (got) return got.Count == 1; }, "the policy's decision is reported");
        Assert.Equal(2, got[0].Item1, "for slot 2");
        Assert.Equal("unhandled exception", got[0].Item3, "with the reason");

        var restorer = new StartupRestorer(p, _ => { });
        var results = new List<(RestoreEntry, string?)>();
        restorer.Restored += (e, err) => results.Add((e, err));
        restorer.Run(new[] { new RestoreEntry(1, 1, 0, "config", Path.Combine(dir, "missing.fake"), null) });
        Assert.Equal(1, results.Count, "the restore attempt is reported");
        Assert.True(results[0].Item2 != null, "with the error, since there is no such program");
    }
}
