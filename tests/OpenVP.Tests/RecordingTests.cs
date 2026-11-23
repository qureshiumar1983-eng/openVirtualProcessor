using System.Diagnostics;
using OpenVP.Core;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

/// <summary>Record and replay: a slot's inputs and the feedback they caused, replayed as a regression test.</summary>
public static class RecordingTests
{
    private static string SampleSmw => Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw");
    private static JoinKey D(uint j) => new(SigKind.Digital, j);

    // Press (panel IPID 03 d1) -> AND -> AND -> panel feedback d1. With tieToZero the second AND is held low, so nothing comes back.
    private static string WriteChain(string dir, string name, bool tieToZero = false)
    {
        var text =
            "[\nObjTp=Hd\nPgmNm=Chain\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
            "[\nObjTp=Sg\nH=10\nNm=press\n]\n[\nObjTp=Sg\nH=11\nNm=mid\n]\n[\nObjTp=Sg\nH=12\nNm=fb\n]\n" +
            "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=12\nO1=10\n]\n" +
            "[\nObjTp=Sm\nH=20\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=10\nI2=2\nO1=11\n]\n" +
            $"[\nObjTp=Sm\nH=21\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=11\nI2={(tieToZero ? 1 : 2)}\nO1=12\n]\n";
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        return path;
    }

    private static Processor NewProcessor(int slots = 2)
    {
        var p = new Processor(Paths.TempData(), slots);
        p.AddLoader(new SimplLoader());
        p.AddLoader(new SimplSharpLoader());
        return p;
    }

    private static void Wait(int ms) => Thread.Sleep(ms);

    /// <summary>Records a short session on the chain program: press and release, twice, with a debugger pulse in between.</summary>
    private static (Processor p, SlotRecordings rec, string dir, Recording recording) RecordChain()
    {
        var p = NewProcessor();
        var dir = Paths.TempData();
        p.LoadProgram(1, WriteChain(dir, "chain.smw"));
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel IPID 03");
        var svc = new SlotRecordings(p, new RecordingStore(dir));

        svc.StartRecording(1);
        Wait(50);
        panel.ReceiveInput(D(1), true);
        Wait(120);
        panel.ReceiveInput(D(1), false);
        Wait(150);
        Assert.True(slot.ForceSignal("press", true), "force press high");
        Wait(120);
        Assert.True(slot.ForceSignal("press", false), "force press low");
        Wait(100);
        Assert.True(slot.ForcePulse("press"), "pulse press");
        Wait(150);
        var info = svc.StopRecording(1, "chain session");
        return (p, svc, dir, svc.Store.Load(1, info.Id)!);
    }

    [Test]
    public static void Recording_captures_panel_inputs_forces_and_feedback()
    {
        var (p, svc, _, r) = RecordChain();
        using var _p = p; using var _s = svc;
        Assert.Equal("chain session", r.Name, "name");
        Assert.Equal("chain", r.Program, "program name at record time");
        Assert.Equal("chain.smw", r.ProgramFile, "program file at record time");
        Assert.Equal(2, r.Inputs.Count, "panel inputs");
        Assert.True(r.Inputs.All(i => i.Source == "panel" && i.Ip == 3 && i.Kind == "d" && i.Join == 1), "inputs are panel IPID 03 d1");
        Assert.Equal(true, r.Inputs[0].Value, "press"); Assert.Equal(false, r.Inputs[1].Value, "release");
        Assert.True(r.Inputs[0].T >= 40 && r.Inputs[1].T - r.Inputs[0].T >= 100, "relative times kept");
        Assert.Equal(3, r.Forces.Count, "two sets and a pulse");
        Assert.Equal("press", r.Forces[0].Signal, "force name");
        Assert.True(r.Forces[2].Pulse && r.Forces[2].Value == null, "pulse");
        Assert.True(r.Feedback.Count >= 4, "feedback captured: " + r.Feedback.Count);
        Assert.True(r.Feedback.All(f => f.Ip == 3 && f.Kind == "d" && f.Join == 1), "feedback is d1 on IPID 03");
        Assert.Equal(true, r.Feedback[0].Value, "first feedback is on");
        Assert.True(r.Feedback[0].T >= r.Inputs[0].T, "feedback comes after the input that caused it");
        Assert.True(r.DurationMs >= 600, "duration");
        Assert.Equal(2, r.Counts.Inputs, "counts: inputs");
        Assert.Equal(3, r.Counts.Forces, "counts: forces");
    }

    [Test]
    public static void Replay_against_the_same_program_passes()
    {
        var (p, svc, _, r) = RecordChain();
        using var _p = p; using var _s = svc;
        var report = new RecordingReplayer(p.Slot(1), r).Run(CancellationToken.None);
        var detail = string.Join("; ", report.Differences.Select(d => d.Message));
        Assert.True(report.Passed, "passes: " + detail);
        Assert.Equal(r.Feedback.Count, report.Counts.FeedbackExpected, "expected count");
        Assert.Equal(r.Feedback.Count, report.Counts.Matched, "every feedback change matched");
        Assert.Equal(0, report.Counts.Missing + report.Counts.Different + report.Counts.FinalDifferences, "no differences");
        Assert.Equal(2, report.Counts.InputsSent, "inputs sent");
        Assert.Equal(3, report.Counts.ForcesSent, "forces sent");
        Assert.True(report.ReplayMs >= r.DurationMs, "original timing: replay takes at least as long as the recording");
    }

    [Test]
    public static void Replay_through_the_service_saves_a_report_and_reports_progress()
    {
        var (p, svc, _, r) = RecordChain();
        using var _p = p; using var _s = svc;
        svc.StartReplay(1, r.Id);
        Assert.Equal("replaying", svc.Status(1).Mode, "busy while replaying");
        try { svc.StartRecording(1); throw new AssertException("recording during a replay should be refused"); } catch (InvalidOperationException) { }
        try { svc.StartReplay(1, r.Id); throw new AssertException("a second replay should be refused"); } catch (InvalidOperationException) { }
        Assert.Eventually(() => svc.Status(1).Replay?.State == "done", "replay done", 8000);
        var st = svc.Status(1);
        Assert.Equal("idle", st.Mode, "idle afterwards");
        Assert.Equal(1.0, st.Replay!.Progress, "progress is complete");
        Assert.True(st.Replay.Report!.Passed, "passed");
        var saved = svc.Store.LatestReport(1, r.Id);
        Assert.True(saved != null && saved.Passed && saved.Id == st.Replay.Report.Id, "report saved with the recording");
        Assert.Equal(true, svc.Store.Get(1, r.Id)!.LastPassed, "list shows the last result");
    }

    [Test]
    public static void Replay_against_changed_behaviour_fails_with_the_difference()
    {
        var (p, svc, dir, r) = RecordChain();
        using var _p = p; using var _s = svc;
        // The "new version" of the program never answers: the second AND is tied low.
        p.LoadProgram(1, WriteChain(dir, "chain-v2.smw", tieToZero: true));
        Assert.Equal(SlotState.Running, p.Slot(1).State, "v2 running");
        var report = new RecordingReplayer(p.Slot(1), r).Run(CancellationToken.None);
        Assert.True(!report.Passed, "fails");
        var missing = report.Differences.Where(d => d.Kind == "missing").ToList();
        Assert.True(missing.Count >= 1, "missing feedback is listed");
        var first = missing[0];
        Assert.Equal(3u, first.Ip, "device");
        Assert.Equal("d1", first.Join, "join");
        Assert.Equal(true, first.Expected, "expected value");
        Assert.Equal(r.Feedback[0].T, first.TimeMs, "time in the recording where it was expected");
        Assert.Equal(report.Counts.Missing, missing.Count, "count agrees with the list");
        Assert.True(report.Counts.Matched < report.Counts.FeedbackExpected, "not everything matched");
        Assert.True(report.Differences.All(d => d.Severity == "fail" || d.Kind is "baseline" or "extra"), "severities");
    }

    [Test]
    public static void Replay_notices_a_different_value()
    {
        // The sample's Toggle symbol is swapped for a NOT: the same press now gives the opposite feedback.
        using var p = NewProcessor();
        var dir = Paths.TempData();
        var v1 = Path.Combine(dir, "sample.smw");
        File.Copy(SampleSmw, v1);
        File.Copy(Path.ChangeExtension(SampleSmw, ".openvp.json"), Path.ChangeExtension(v1, ".openvp.json"));
        p.LoadProgram(2, v1);
        var panel = p.Slot(2).FindDevice(3) ?? throw new AssertException("panel");
        using var svc = new SlotRecordings(p, new RecordingStore(dir));
        svc.StartRecording(2);
        Wait(30);
        panel.ReceiveInput(D(1), true);
        Wait(100);
        panel.ReceiveInput(D(1), false);
        Wait(100);
        panel.ReceiveInput(D(1), true);
        Wait(100);
        panel.ReceiveInput(D(1), false);
        Wait(100);
        var rec = svc.Store.Load(2, svc.StopRecording(2).Id)!;
        Assert.True(rec.Feedback.Any(f => f.Join == 1 && Equals(f.Value, true)), "toggle turned d1 on");

        var text = File.ReadAllText(v1);
        var changed = text.Replace("Nm=Toggle", "Nm=NOT");
        Assert.True(changed != text, "the sample has a Toggle symbol to change");
        var v2 = Path.Combine(dir, "sample-v2.smw");
        File.WriteAllText(v2, changed);
        File.Copy(Path.ChangeExtension(v1, ".openvp.json"), Path.ChangeExtension(v2, ".openvp.json"));
        p.LoadProgram(2, v2);
        Assert.Equal(SlotState.Running, p.Slot(2).State, "changed program runs: " + p.Slot(2).LastError);

        var report = new RecordingReplayer(p.Slot(2), rec).Run(CancellationToken.None);
        Assert.True(!report.Passed, "fails: " + string.Join("; ", report.Differences.Select(d => d.Message)));
        Assert.True(report.Differences.Any(d => d.Severity == "fail" && d.Ip == 3 && d.Join == "d1" && d.TimeMs != null), "d1 is reported with its time");
        Assert.True(report.Differences.Any(d => d.Severity == "fail" && d.Kind is "different" or "missing" or "final"), "a real difference is listed");
    }

    [Test]
    public static void Replay_puts_inputs_back_to_their_starting_values_first()
    {
        // The recording leaves a1 at 40000. Replayed straight away, the recorded 40000 would be "no change", so the replay sets a1 back first.
        using var p = NewProcessor();
        p.LoadProgram(2, SampleSmw);
        var panel = p.Slot(2).FindDevice(3)!;
        using var svc = new SlotRecordings(p, new RecordingStore(Paths.TempData()));
        svc.StartRecording(2); Wait(30);
        panel.ReceiveInput(new JoinKey(SigKind.Analog, 1), (ushort)40000); Wait(150);
        panel.ReceiveInput(D(6), true); panel.ReceiveInput(D(7), true); Wait(150);   // AND: both_pressed lights d6 and goes out again
        panel.ReceiveInput(D(7), false); panel.ReceiveInput(D(6), false); Wait(100);
        var rec = svc.Store.Load(2, svc.StopRecording(2).Id)!;
        Assert.True(rec.Feedback.Any(f => f.Kind == "a" && Equals(f.Value, (ushort)40000)), "analog feedback recorded");
        Assert.True(rec.StartInputs.All(d => d.Joins.All(j => !Equals(j.Value, (ushort)40000))), "a1 wasn't set when recording started");
        Assert.Equal((ushort)40000, panel.GetInput(new JoinKey(SigKind.Analog, 1)), "the room is left dirty");

        var report = new RecordingReplayer(p.Slot(2), rec).Run(CancellationToken.None);
        Assert.True(report.Passed, "passes: " + string.Join("; ", report.Differences.Select(d => d.Message)));
        Assert.True(report.Notes.Any(n => n.Contains("set back") && n.Contains("a1@03")), "the reset is noted: " + string.Join("|", report.Notes));
    }

    [Test]
    public static void Comparison_rules_on_synthetic_feedback()
    {
        var rec = new Recording { DurationMs = 5000 };
        rec.Inputs.Add(new RecordedInput { T = 100, Ip = 3, Join = 9, Value = true });
        void Fb(long t, uint join, object v) => rec.Feedback.Add(new RecordedFeedback { T = t, Ip = 3, Join = join, Value = v });
        Fb(1000, 1, true);     // arrives 400 ms late: inside the window, matches
        Fb(2000, 2, true);     // arrives with the wrong value: different
        Fb(3000, 3, true);     // arrives 600 ms late: outside the window, so missing
        Fb(4000, 4, true);     // never arrives: missing
        rec.End = new() { new RecordedDevice { Ip = 3, Joins = new() { RecordedJoin.From(D(1), true), RecordedJoin.From(D(2), true), RecordedJoin.From(D(3), true), RecordedJoin.From(D(4), true) } } };
        var actual = new List<(long T, uint Ip, JoinKey Key, object Value)>
        {
            (1400, 3, D(1), true), (2100, 3, D(2), false), (3600, 3, D(3), true), (4500, 3, D(5), true)
        };
        var final = new Dictionary<(uint Ip, JoinKey Key), object>
        {
            [(3, D(1))] = true, [(3, D(2))] = false, [(3, D(3))] = true, [(3, D(5))] = true
        };
        var report = new ReplayReport();
        var diffs = new List<ReplayDifference>();
        RecordingReplayer.Compare(rec, new ReplayTimeline(rec, false), actual, final, 500, report, diffs);
        Assert.Equal(1, report.Counts.Matched, "one match");
        Assert.Equal(1, report.Counts.Different, "one different value");
        Assert.Equal(2, report.Counts.Missing, "two missing (late, never)");
        Assert.Equal(2, report.Counts.Extra, "the late one and the unexpected join are extra");
        var different = diffs.Single(d => d.Kind == "different");
        Assert.True(Equals(different.Expected, true) && Equals(different.Actual, false) && different.TimeMs == 2000 && different.Join == "d2", "different value details");
        Assert.True(diffs.Where(d => d.Kind == "extra").All(d => d.Severity == "warn"), "extras are warnings");
        Assert.True(diffs.Where(d => d.Kind is "missing" or "different").All(d => d.Severity == "fail"), "the rest fail");
        Assert.Equal("d5", string.Join(",", diffs.Where(d => d.Kind == "final").Select(d => d.Join)), "joins already reported as missing or different aren't listed again as final-state differences");
        Assert.Equal(3, report.Counts.FinalDifferences, "d2, d4 and d5 end differently");
    }

    [Test]
    public static void Extra_feedback_is_only_a_warning_unless_the_final_state_differs()
    {
        var p = NewProcessor(); using var _p = p;
        var dir = Paths.TempData();
        p.LoadProgram(1, WriteChain(dir, "quiet.smw", tieToZero: true));
        var panel = p.Slot(1).FindDevice(3)!;
        using var svc = new SlotRecordings(p, new RecordingStore(dir));
        svc.StartRecording(1); Wait(30);
        panel.ReceiveInput(D(1), true); Wait(100);
        panel.ReceiveInput(D(1), false); Wait(100);
        var quiet = svc.Store.Load(1, svc.StopRecording(1).Id)!;
        Assert.Equal(0, quiet.Feedback.Count, "the quiet program sent nothing back");

        p.LoadProgram(1, WriteChain(dir, "chatty.smw"));
        var report = new RecordingReplayer(p.Slot(1), quiet).Run(CancellationToken.None);
        Assert.True(report.Passed, "still passes: " + string.Join("; ", report.Differences.Select(d => d.Message)));
        Assert.True(report.Counts.Extra >= 2 && report.Differences.Count(d => d.Kind == "extra" && d.Severity == "warn") == report.Counts.Extra, "extras listed as warnings");

        // Same extras, but the replay ends in a different state: d1 is left lit.
        quiet.End = new() { new RecordedDevice { Ip = 3, Joins = new() { RecordedJoin.From(D(1), true) } } };
        var report2 = new RecordingReplayer(p.Slot(1), quiet).Run(CancellationToken.None);
        Assert.True(!report2.Passed && report2.Differences.Any(d => d.Kind == "final" && d.Expected is true && d.Actual is false), "final state difference fails");
    }

    [Test]
    public static void Fast_mode_shortens_long_gaps_and_keeps_short_ones()
    {
        var rec = new Recording
        {
            DurationMs = 8000,
            Inputs =
            {
                new RecordedInput { T = 100, Ip = 3, Join = 1, Value = true }, new RecordedInput { T = 150, Ip = 3, Join = 1, Value = false },
                new RecordedInput { T = 5000, Ip = 3, Join = 1, Value = true }, new RecordedInput { T = 5100, Ip = 3, Join = 1, Value = false }
            }
        };
        var normal = new ReplayTimeline(rec, fast: false);
        Assert.Equal(5000L, normal.InputTime(5000), "original timing keeps the long gap");
        Assert.Equal(8000L, normal.EndTime, "original end");
        var fast = new ReplayTimeline(rec, fast: true, capMs: 1000);
        Assert.Equal(100L, fast.InputTime(100), "leading gap under the cap");
        Assert.Equal(50L, fast.InputTime(150) - fast.InputTime(100), "short gap kept exactly");
        Assert.Equal(1000L, fast.InputTime(5000) - fast.InputTime(150), "long gap capped at 1 s");
        Assert.Equal(100L, fast.InputTime(5100) - fast.InputTime(5000), "press/release gap kept");
        Assert.Equal(fast.InputTime(5100) + 1000, fast.EndTime, "long idle tail capped");
        Assert.Equal(fast.InputTime(5000) + 200, fast.FeedbackTime(5200), "feedback keeps its distance from the input before it");
    }

    [Test]
    public static void Fast_replay_runs_sooner_and_keeps_press_release_timing()
    {
        using var p = NewProcessor();
        var dir = Paths.TempData();
        p.LoadProgram(1, WriteChain(dir, "chain.smw"));
        var panel = p.Slot(1).FindDevice(3)!;
        var rec = new Recording
        {
            DurationMs = 5300,
            Inputs =
            {
                new RecordedInput { T = 50, Ip = 3, Join = 1, Value = true }, new RecordedInput { T = 150, Ip = 3, Join = 1, Value = false },
                new RecordedInput { T = 5000, Ip = 3, Join = 1, Value = true }, new RecordedInput { T = 5100, Ip = 3, Join = 1, Value = false }
            },
            Feedback =
            {
                new RecordedFeedback { T = 55, Ip = 3, Join = 1, Value = true }, new RecordedFeedback { T = 155, Ip = 3, Join = 1, Value = false },
                new RecordedFeedback { T = 5005, Ip = 3, Join = 1, Value = true }, new RecordedFeedback { T = 5105, Ip = 3, Join = 1, Value = false }
            }
        };
        var at = new List<(long ms, bool v)>();
        var sw = Stopwatch.StartNew();
        panel.InputObserved += (_, _, v, _) => { lock (at) at.Add((sw.ElapsedMilliseconds, (bool)v)); };
        sw.Restart();
        var report = new RecordingReplayer(p.Slot(1), rec, new ReplayOptions { Fast = true }).Run(CancellationToken.None);
        Assert.True(report.Passed, "passes: " + string.Join("; ", report.Differences.Select(d => d.Message)));
        Assert.True(report.ReplayMs < 3500, $"fast replay is much shorter than 5.3 s: {report.ReplayMs} ms");
        Assert.Equal(4, at.Count, "four inputs");
        Assert.True(Math.Abs((at[1].ms - at[0].ms) - 100) < 60, $"short press kept: {at[1].ms - at[0].ms} ms");
        Assert.True(Math.Abs((at[2].ms - at[1].ms) - 1000) < 120, $"long gap capped at 1 s: {at[2].ms - at[1].ms} ms");
        Assert.True(Math.Abs((at[3].ms - at[2].ms) - 100) < 60, $"second press kept: {at[3].ms - at[2].ms} ms");
        Assert.True(report.Notes.Any(n => n.Contains("Fast mode")), "report says fast mode was used");
    }

    [Test]
    public static void Cancelled_replay_ends_without_a_report()
    {
        using var p = NewProcessor();
        var dir = Paths.TempData();
        p.LoadProgram(1, WriteChain(dir, "chain.smw"));
        using var svc = new SlotRecordings(p, new RecordingStore(dir));
        var rec = new Recording { Slot = 1, Program = "Chain", DurationMs = 60_000, RecordedAt = DateTimeOffset.Now };
        rec.Inputs.Add(new RecordedInput { T = 100, Ip = 3, Join = 1, Value = true });
        rec.Inputs.Add(new RecordedInput { T = 30_000, Ip = 3, Join = 1, Value = false });
        var id = svc.Store.Add(1, rec).Id;
        svc.StartReplay(1, id);
        Wait(200);
        Assert.True(svc.CancelReplay(1), "cancel accepted");
        Assert.Eventually(() => svc.Status(1).Replay?.State == "cancelled", "cancelled");
        Assert.Equal("idle", svc.Status(1).Mode, "idle again");
        Assert.True(svc.Store.LatestReport(1, id) == null, "no report for a cancelled replay");
        Assert.True(!svc.CancelReplay(1), "nothing left to cancel");
    }

    [Test]
    public static void Unloading_the_slot_stops_a_recording_and_a_replay_cleanly()
    {
        using var p = NewProcessor();
        var dir = Paths.TempData();
        p.LoadProgram(1, WriteChain(dir, "chain.smw"));
        using var svc = new SlotRecordings(p, new RecordingStore(dir));
        var panel = p.Slot(1).FindDevice(3)!;

        svc.StartRecording(1);
        try { svc.StartRecording(1); throw new AssertException("second recording should be refused"); } catch (InvalidOperationException) { }
        Wait(30); panel.ReceiveInput(D(1), true); Wait(100);
        p.Slot(1).Unload();
        Assert.Equal("idle", svc.Status(1).Mode, "recording ended");
        var saved = svc.Store.List(1);
        Assert.Equal(1, saved.Count, "what was captured is saved");
        Assert.Equal("program unloaded", saved[0].EndedBy, "says why it ended");
        Assert.True(svc.Status(1).Notice != null, "user is told");
        var r = svc.Store.Load(1, saved[0].Id)!;
        Assert.True(r.End.Any(d => d.Ip == 3 && d.Joins.Any(j => j.Join == 1 && Equals(j.Value, true))), "final state worked out from the feedback");

        try { svc.StartRecording(1); throw new AssertException("no program, no recording"); } catch (InvalidOperationException) { }
        try { svc.StartReplay(1, saved[0].Id); throw new AssertException("no running program, no replay"); } catch (InvalidOperationException) { }

        p.LoadProgram(1, WriteChain(dir, "chain.smw"));
        var slow = new Recording { Slot = 1, DurationMs = 60_000, RecordedAt = DateTimeOffset.Now };
        slow.Inputs.Add(new RecordedInput { T = 20_000, Ip = 3, Join = 1, Value = true });
        var id = svc.Store.Add(1, slow).Id;
        svc.StartReplay(1, id);
        Wait(150);
        p.Slot(1).Unload();
        Assert.Eventually(() => svc.Status(1).Replay?.State == "aborted", "replay aborted");
        Assert.True(svc.Status(1).Replay!.Message!.Contains("unloaded"), "says why");
        Assert.Equal("idle", svc.Status(1).Mode, "idle");
    }

    [Test]
    public static void Eisc_inputs_are_recorded_and_can_be_left_out_of_a_replay()
    {
        using var p = NewProcessor();
        var dir = Paths.TempData();
        p.LoadProgram(1, Paths.SampleDll);
        p.LoadProgram(2, SampleSmw);
        var panel1 = p.Slot(1).FindDevice(3) ?? throw new AssertException("slot 1 panel");
        var panel2 = p.Slot(2).FindDevice(3) ?? throw new AssertException("slot 2 panel");
        using var svc = new SlotRecordings(p, new RecordingStore(dir));
        Assert.Eventually(() => p.Slot(2).FindDevice(0x10)?.Online == true, "EISC linked");

        svc.StartRecording(2);
        Wait(30);
        panel1.ReceiveInput(D(10), true);   // slot 1's panel; reaches slot 2 through the EISC
        Assert.Eventually(() => Equals(panel2.GetFeedback(D(10)), true), "slot 2's panel d10 lit");
        panel1.ReceiveInput(D(10), false);
        Assert.Eventually(() => Equals(panel2.GetFeedback(D(10)), false), "slot 2's panel d10 off");
        Wait(50);
        var rec = svc.Store.Load(2, svc.StopRecording(2).Id)!;
        Assert.True(rec.Inputs.Count >= 2 && rec.Inputs.All(i => i.Source == "eisc" && i.Ip == 0x10), "EISC inputs are recorded as such");
        Assert.True(rec.Counts.EiscInputs >= 2 && rec.Counts.Inputs == 0, "counted separately");
        Assert.True(rec.Feedback.Any(f => f.Ip == 3 && f.Join == 10), "the feedback they caused is recorded");

        // With EISC inputs included, slot 2 is driven the same way and passes.
        var withEisc = new RecordingReplayer(p.Slot(2), rec).Run(CancellationToken.None);
        Assert.True(withEisc.Passed, "passes with EISC inputs: " + string.Join("; ", withEisc.Differences.Select(d => d.Message)));
        Assert.True(withEisc.Counts.EiscInputsSent >= 2 && withEisc.Notes.Any(n => n.Contains("EISC")), "EISC inputs sent and noted");

        // Without them nothing drives the EISC (slot 1's panel is idle), so slot 2's feedback is missing.
        var without = new RecordingReplayer(p.Slot(2), rec, new ReplayOptions { IncludeEisc = false }).Run(CancellationToken.None);
        Assert.True(!without.Passed, "fails without EISC inputs");
        Assert.True(without.Counts.EiscInputsSkipped >= 2 && without.Notes.Any(n => n.Contains("not sent")), "skipped inputs are noted");
    }

    [Test]
    public static void Recording_survives_a_JSON_round_trip()
    {
        var (p, svc, _, r) = RecordChain();
        using var _p = p; using var _s = svc;
        r.Inputs.Add(new RecordedInput { T = 10, Seq = 99, Ip = 3, Join = 2, Kind = "a", Value = (ushort)40000, Source = "eisc" });
        r.Inputs.Add(new RecordedInput { T = 11, Seq = 100, Ip = 3, Join = 3, Kind = "s", Value = "hi \"there\"\r\n" });
        r.Forces.Add(new RecordedForce { T = 12, Seq = 101, Signal = "level", Value = 500 });
        r.Forces.Add(new RecordedForce { T = 13, Seq = 102, Signal = "text", Value = "x" });
        var json = RecordingJson.Serialize(r);
        var back = RecordingJson.Parse(json);
        Assert.Equal(r.Name, back.Name, "name");
        Assert.Equal(r.DurationMs, back.DurationMs, "duration");
        Assert.Equal(r.Inputs.Count, back.Inputs.Count, "inputs");
        Assert.Equal(r.Feedback.Count, back.Feedback.Count, "feedback");
        Assert.Equal(r.Forces.Count, back.Forces.Count, "forces");
        Assert.Equal(r.Start.Count, back.Start.Count, "start state");
        Assert.True(back.Inputs.Any(i => i.Kind == "a" && i.Value is ushort u && u == 40000 && i.Source == "eisc"), "analog comes back as ushort");
        Assert.True(back.Inputs.Any(i => i.Kind == "s" && (string?)i.Value == "hi \"there\"\r\n"), "serial text kept exactly");
        Assert.True(back.Inputs.Any(i => i.Kind == "d" && i.Value is bool), "digital comes back as bool");
        Assert.True(back.Forces.Any(f => f.Signal == "level" && f.Value is int n && n == 500), "numeric force");
        Assert.True(back.Forces.Any(f => f.Pulse && f.Value == null), "pulse");
        Assert.True(back.Forces.Any(f => f.Value is bool), "boolean force");
        Assert.Equal(RecordingJson.Serialize(back), RecordingJson.Serialize(RecordingJson.Parse(RecordingJson.Serialize(back))), "writes the same text again");
        Assert.Equal(r.Inputs.Count + r.Forces.Count, back.Counts.Inputs + back.Counts.EiscInputs + back.Counts.Forces, "counts follow the events");

        // A file someone edited badly is refused with a message, not a crash.
        foreach (var bad in new[]
        {
            "not json", "[]", "{\"format\":2}",
            "{\"format\":1,\"durationMs\":100,\"inputs\":[{\"t\":5,\"ip\":3,\"kind\":\"x\",\"join\":1,\"value\":true}]}",
            "{\"format\":1,\"durationMs\":100,\"inputs\":[{\"t\":5,\"ip\":3,\"kind\":\"d\",\"join\":0,\"value\":true}]}",
            "{\"format\":1,\"durationMs\":100,\"inputs\":[{\"t\":5,\"ip\":3,\"kind\":\"a\",\"join\":1,\"value\":70000}]}",
            "{\"format\":1,\"durationMs\":100,\"inputs\":[{\"t\":5,\"ip\":3,\"kind\":\"s\",\"join\":1,\"value\":5}]}",
            "{\"format\":1,\"durationMs\":100,\"inputs\":[{\"t\":5000,\"ip\":3,\"kind\":\"d\",\"join\":1,\"value\":true}]}",
            "{\"format\":1,\"durationMs\":100,\"inputs\":[{\"t\":5,\"ip\":3,\"kind\":\"d\",\"join\":1,\"value\":true,\"source\":\"alien\"}]}",
            "{\"format\":1,\"durationMs\":100,\"forces\":[{\"t\":5,\"signal\":\"\",\"value\":1}]}",
        })
        {
            try { RecordingJson.Parse(bad); throw new AssertException("should be refused: " + bad); }
            catch (RecordingFormatException) { }
        }
    }

    [Test]
    public static void Store_saves_lists_renames_exports_and_deletes()
    {
        var (p, svc, dir, r) = RecordChain();
        using var _p = p; using var _s = svc;
        var store = svc.Store;
        var path = Path.Combine(dir, "recordings", "slot01", r.Id + ".json");
        Assert.True(File.Exists(path), "saved under data/recordings/slotNN/<id>.json");
        var list = store.List(1);
        Assert.Equal(1, list.Count, "listed");
        Assert.Equal("chain session", list[0].Name, "name");
        Assert.True(list[0].Counts.Forces == 3 && list[0].DurationMs == r.DurationMs, "counts and duration in the list");

        Assert.Equal("renamed", store.Rename(1, r.Id, "  renamed ")!.Name, "rename trims");
        foreach (var bad in new string?[] { null, "", "   ", new string('x', 101), "a\nb" })
        {
            try { store.Rename(1, r.Id, bad); throw new AssertException("bad name accepted: " + bad); } catch (ArgumentException) { }
        }

        // Download and upload: the bytes go back in under a new id (an id inside the file is never trusted).
        var bytes = store.ReadFile(1, r.Id)!;
        var uploaded = store.Import(1, bytes);
        Assert.True(uploaded.Id != r.Id && RecordingIds.IsValidId(uploaded.Id), "uploaded under a new id");
        Assert.Equal("renamed", uploaded.Name, "uploaded keeps its name");
        Assert.Equal("kept", store.Import(1, bytes, "kept").Name, "or takes the name given");
        Assert.Equal(3, store.List(1).Count, "three now");
        try { store.Import(1, "{\"format\":1,\"inputs\":[{\"kind\":\"q\"}]}"u8); throw new AssertException("bad upload accepted"); } catch (RecordingFormatException) { }

        // Replay reports: the newest few are kept with the recording.
        for (var i = 0; i < 7; i++)
            store.AddReport(1, r.Id, new ReplayReport { Id = $"rep{i}", RecordingId = r.Id, Passed = i % 2 == 0, StartedAt = DateTimeOffset.Now.AddMinutes(i) });
        Assert.Equal(RecordingStore.KeepReports, store.Reports(1, r.Id).Count, "only the last few reports are kept");
        Assert.Equal("rep6", store.LatestReport(1, r.Id)!.Id, "newest first");
        Assert.Equal(true, store.Get(1, r.Id)!.LastPassed, "list shows the latest result");

        Assert.True(store.Delete(1, r.Id), "deleted");
        Assert.True(!File.Exists(path) && !File.Exists(Path.Combine(dir, "recordings", "slot01", r.Id + ".reports.json")), "file and reports gone");
        Assert.True(!store.Delete(1, r.Id) && store.Load(1, r.Id) == null, "gone for good");
        Assert.Equal(2, store.List(1).Count, "the uploads stay");
    }

    [Test]
    public static void Invalid_ids_are_rejected()
    {
        var dir = Paths.TempData();
        var store = new RecordingStore(dir);
        var decoy = Path.Combine(dir, "decoy.json");
        File.WriteAllText(decoy, "{}");
        Directory.CreateDirectory(Path.Combine(dir, "recordings", "slot01"));
        File.WriteAllText(Path.Combine(dir, "recordings", "slot01", "secret.json"), "{}");
        var good = store.Add(1, new Recording { Slot = 1, DurationMs = 10, RecordedAt = DateTimeOffset.Now }, "ok");
        Assert.True(RecordingIds.IsValidId(good.Id), "generated ids are valid");

        foreach (var bad in new string?[]
        {
            null, "", "..", "../decoy", "..\\decoy", "../../decoy", "secret", "a/b", "/etc/passwd", good.Id + "/..", good.Id + ".json",
            good.Id + ".reports", "20240101T000000Z-zzzz", "20240101T000000Z-123", "20240101T000000Z-12345", " " + good.Id, good.Id + "\n", "%2e%2e"
        })
        {
            Assert.True(!RecordingIds.IsValidId(bad), $"invalid: {bad}");
            Assert.True(store.Get(1, bad) == null && store.Load(1, bad) == null && store.ReadFile(1, bad) == null, $"read {bad}");
            Assert.True(!store.Delete(1, bad), $"delete {bad}");
            Assert.True(store.Rename(1, bad, "x") == null, $"rename {bad}");
            Assert.True(store.Reports(1, bad).Count == 0 && store.LatestReport(1, bad) == null, $"reports {bad}");
            store.AddReport(1, bad, new ReplayReport());
        }
        Assert.True(File.Exists(decoy) && File.Exists(Path.Combine(dir, "recordings", "slot01", "secret.json")), "nothing outside was touched");
        Assert.Equal(1, store.List(1).Count, "only the real recording is listed");
        using var p = NewProcessor();
        using var svc = new SlotRecordings(p, store);
        try { svc.StartReplay(1, "../decoy"); throw new AssertException("replay with a bad id"); } catch (KeyNotFoundException) { }
    }

    [Test]
    public static void Forces_are_recorded_once_even_when_they_reach_a_device()
    {
        // In a SIMPL# slot, forcing IPID03.out.d1 calls ReceiveInput on the device. That must be recorded as the force, not also as a panel input.
        using var p = NewProcessor();
        var dir = Paths.TempData();
        p.LoadProgram(1, Paths.SampleDll);
        using var svc = new SlotRecordings(p, new RecordingStore(dir));
        svc.StartRecording(1);
        Assert.True(p.Slot(1).ForceSignal("IPID03.out.d1", true), "force accepted");
        Assert.True(!p.Slot(1).ForceSignal("no.such.signal", true), "unknown signal refused and not recorded");
        Wait(80);
        var rec = svc.Store.Load(1, svc.StopRecording(1).Id)!;
        Assert.Equal(0, rec.Inputs.Count, "no panel input");
        Assert.Equal(1, rec.Forces.Count, "one force");
        Assert.Equal("IPID03.out.d1", rec.Forces[0].Signal, "its name");
    }
}
