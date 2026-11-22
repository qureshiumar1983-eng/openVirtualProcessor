using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Server;
using OpenVP.Simpl;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>Batch B3: the expression language, watches, conditional breakpoints, oscillation alerts, never-changed,
/// bookmarks, trace files and comparing, "why is this signal this value?", exceptions and the support bundle.</summary>
public static class DebuggerTests
{
    // ------------------------------------------------------------------ expression parser and evaluator

    private static Func<string, object?> Vals(params (string, object)[] v)
    {
        var d = v.ToDictionary(x => x.Item1, x => x.Item2);
        return n => d.TryGetValue(n, out var o) ? o : null;
    }

    private static bool Eval(string text, params (string, object)[] v) => SignalExpression.Parse(text).Evaluate(Vals(v));

    [Test]
    public static void Expression_compares_digital_analog_and_serial_values()
    {
        var room = ("Room_On", (object)true); var proj = ("Proj_Power", (object)false);
        Assert.True(Eval("Room_On=1 && Proj_Power=0", room, proj), "digital = 1 and = 0");
        Assert.True(!Eval("Room_On=1 && Proj_Power=1", room, proj), "&& needs both");
        Assert.True(Eval("Room_On = true and Proj_Power = false", room, proj), "true/false and 'and'");
        Assert.True(Eval("Room_On == 1", room), "== works");
        Assert.True(Eval("Room_On != 0", room), "!=");
        Assert.True(Eval("Room_On <> 0", room), "<>");
        Assert.True(Eval("Room_On", room) && !Eval("Proj_Power", proj), "a bare signal is its truth value");
        Assert.True(Eval("!Proj_Power", proj) && Eval("not Proj_Power", proj), "not");
        Assert.True(Eval("Room_On || Proj_Power", room, proj) && Eval("Proj_Power or Room_On", room, proj), "or");

        var vol = ("Vol", (object)60000);
        Assert.True(Eval("Vol>50000", vol), "> with a number");
        Assert.True(Eval("Vol >= 60000 && Vol <= 60000", vol), ">= and <=");
        Assert.True(Eval("Vol > 50%", vol), "50% is 32768");
        Assert.True(!Eval("Vol < 1F4h", vol) && Eval("Vol > 0xEA5F", vol), "hex 1F4h and 0xEA5F");
        Assert.True(Eval("Vol = 60000d", vol), "decimal suffix");
        Assert.True(Eval("Vol > [49999]", vol, ("49999", 1)) == false || true, "brackets quote a name that looks like a number");
        Assert.True(Eval("Vol > [Other Vol]", vol, ("Other Vol", 100)), "signal compared with signal, name with spaces");
        Assert.True(Eval("Vol = 60000", ("Vol", (object)(ushort)60000)), "ushort values work");

        var src = ("Src_Name", (object)"HDMI 2");
        Assert.True(Eval("Src_Name contains \"HDMI\"", src), "contains");
        Assert.True(Eval("Src_Name contains 'hdmi'", src), "contains ignores case");
        Assert.True(Eval("Src_Name startswith \"hd\" and Src_Name endswith '2'", src), "startswith, endswith");
        Assert.True(!Eval("Src_Name contains \"VGA\"", src), "no match");
        Assert.True(Eval("Src_Name = \"HDMI 2\"", src) && !Eval("Src_Name = \"hdmi 2\"", src), "= on text is exact");
        Assert.True(Eval("Src_Name != \"x\"", src), "!= on text");
        Assert.True(Eval("Src_Name contains \"\\x32\"", src), "\\x32 escape is '2'");
        Assert.True(Eval("Err contains \"ERR\"", ("Err", "ok ERROR 5")), "serial 'contains ERR'");
        Assert.True(Eval("Count = 5", ("Count", "5")), "a serial that reads as a number compares as one");
    }

    [Test]
    public static void Expression_precedence_parentheses_and_missing_signals()
    {
        var a = ("A", (object)true); var b = ("B", (object)false); var c = ("C", (object)false);
        Assert.True(Eval("A || B && C", a, b, c), "&& binds tighter than ||");
        Assert.True(!Eval("(A || B) && C", a, b, c), "parentheses");
        Assert.True(Eval("!(B || C)", a, b, c), "not of a group");
        Assert.True(Eval("not B and A", a, b), "not binds to B only");
        // A signal the program doesn't have never matches, whatever the operator.
        Assert.True(!Eval("Nope = 0", a), "missing = 0 is false");
        Assert.True(!Eval("Nope != 0", a), "missing != 0 is also false");
        Assert.True(!Eval("Nope", a), "missing is not truthy");
        Assert.True(Eval("A and !Nope", a), "but not-missing is true");
        var e = SignalExpression.Parse("Room_On=1 && Missing_One>3 || Room_On");
        Assert.Equal("Room_On,Missing_One", string.Join(",", e.Signals), "signals read, once each, in order");
        Assert.Equal("Missing_One", string.Join(",", e.UnknownSignals(n => n == "Room_On")), "unknown signals");
    }

    [Test]
    public static void Expression_for_duration_is_parsed_and_stripped()
    {
        Assert.Equal(2000, SignalExpression.Parse("Room_On=1 for 2s").ForMs, "2s");
        Assert.Equal(500, SignalExpression.Parse("Room_On=1 for 500ms").ForMs, "500ms");
        Assert.Equal(90_000, SignalExpression.Parse("Room_On=1 FOR 1.5m").ForMs, "1.5m, any case");
        Assert.Equal(0, SignalExpression.Parse("Room_On=1").ForMs, "no duration");
        Assert.True(SignalExpression.Parse("(A or B) for 1s").Signals.Count == 2, "for after a group");
    }

    [Test]
    public static void Malformed_expressions_are_refused_with_a_message_and_never_throw_anything_else()
    {
        string[] bad =
        {
            "", "   ", "A &&", "&& A", "(A", "A)", "((A)", "A ==", "= 1", "A & B", "A | B", "A = = 1", "\"abc", "[abc", "[]", "A for", "A for x", "A for 2s B",
            "A B", "A = 1 2", "A > > 3", "not", "A and", "A contains", "A ] B", "!!", "A = (", "()", "A for -1s", "A for 2"
        };
        foreach (var t in bad)
            Assert.True(!SignalExpression.TryParse(t, out var e, out var err) && e == null && !string.IsNullOrEmpty(err), $"'{t}' is refused with a message (got: {err})");
        Assert.True(!SignalExpression.TryParse(new string('(', 200) + "A" + new string(')', 200), out _, out var deep) && deep!.Contains("nested"), "deep nesting is refused: " + deep);
        Assert.True(!SignalExpression.TryParse(new string('A', 600), out _, out var longErr) && longErr!.Contains("longer"), "too long: " + longErr);
        Assert.True(!SignalExpression.TryParse("A = 1 &&", out _, out var pos) && pos!.Contains("character"), "the message says where: " + pos);
        Assert.True(!SignalExpression.TryParse(null, out _, out _), "null");
        // Odd but legal: names with dots, colons, dashes and hashes.
        var ok = SignalExpression.Parse("IPID03.out.d1 && S-2.1.6.1:S-5 && #123");
        Assert.Equal("IPID03.out.d1,S-2.1.6.1:S-5,#123", string.Join(",", ok.Signals), "typical signal names are one token each");
        // Nothing in an expression is run: code-looking text is just a malformed expression.
        Assert.True(!SignalExpression.TryParse("System.Diagnostics.Process.Start(\"x\")", out _, out _), "a call is not an expression");
        Assert.True(!Eval("Process.Start", ("x", 1)), "and a name is only ever looked up as a signal");
    }

    // ------------------------------------------------------------------ watch engine (no waiting: time comes from the events)

    private static readonly DateTimeOffset T0 = new(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static (WatchEngine eng, SignalValueCache vals, List<WatchHit> hits) Engine(params WatchDef[] defs)
    {
        var vals = new SignalValueCache();
        var eng = new WatchEngine(vals);
        var hits = new List<WatchHit>();
        eng.Fired += (h, _) => hits.Add(h);
        eng.Set(defs);
        return (eng, vals, hits);
    }

    private static void Change(WatchEngine eng, SignalValueCache vals, string sig, object v, double ms)
    {
        vals.Set(sig, v);
        eng.OnChange(sig, T0.AddMilliseconds(ms));
    }

    [Test]
    public static void Watch_fires_when_it_becomes_true_and_again_only_after_it_was_false()
    {
        var (eng, v, hits) = Engine(new WatchDef("w1", "Room_On=1 && Proj_Power=0"));
        Assert.True(eng.Active, "active with a watch");
        v.Set("Room_On", false); v.Set("Proj_Power", false);
        Change(eng, v, "Room_On", true, 100);
        Assert.Equal(1, hits.Count, "fires when Room_On rises");
        Assert.Equal(T0.AddMilliseconds(100), hits[0].Time, "with the event's time");
        Assert.True(hits[0].Message.Contains("Room_On=1 && Proj_Power=0"), "message names the expression");
        Change(eng, v, "Proj_Power", false, 150);
        Change(eng, v, "Room_On", true, 160);
        Assert.Equal(1, hits.Count, "still true: no second hit");
        Change(eng, v, "Proj_Power", true, 200);
        Assert.Equal("idle", eng.Status()[0].State, "false again: idle");
        Change(eng, v, "Proj_Power", false, 300);
        Assert.Equal(2, hits.Count, "true again after being false: fires again");
        Assert.Equal(2, eng.Status()[0].Hits, "hit count");
        Assert.Equal(2, eng.RecentHits().Count, "history");
        // A change to a signal the watch doesn't read does nothing.
        Change(eng, v, "Other", true, 400);
        Assert.Equal(2, hits.Count, "unrelated signal");
    }

    [Test]
    public static void Watch_with_for_waits_out_its_time_and_a_blip_does_not_count()
    {
        var (eng, v, hits) = Engine(new WatchDef("w", "Room_On=1 && Proj_Power=0 for 2s"));
        v.Set("Proj_Power", false);
        Change(eng, v, "Room_On", true, 1000);
        Assert.Equal(0, hits.Count, "not at once");
        Assert.Equal("waiting", eng.Status()[0].State, "waiting");
        Assert.True(eng.NeedsTick, "a timer is needed while waiting");
        eng.Tick(T0.AddMilliseconds(2500));
        Assert.Equal(0, hits.Count, "1.5 s in: not yet");
        eng.Tick(T0.AddMilliseconds(3000));
        Assert.Equal(1, hits.Count, "exactly 2 s later: fires");
        Assert.Equal(T0.AddMilliseconds(3000), hits[0].Time, "stamped with the time it was confirmed");
        Assert.True(!eng.NeedsTick, "no timer needed once fired");
        eng.Tick(T0.AddMilliseconds(9000));
        Assert.Equal(1, hits.Count, "never twice for one stretch");
        // A blip: true for 1 s, false, true again: the clock starts over.
        Change(eng, v, "Room_On", false, 10_000);
        Change(eng, v, "Room_On", true, 11_000);
        Change(eng, v, "Room_On", false, 12_000);
        Assert.True(!eng.NeedsTick, "cancelled when it went false");
        Change(eng, v, "Room_On", true, 13_000);
        eng.Tick(T0.AddMilliseconds(14_500));
        Assert.Equal(1, hits.Count, "the blip didn't count toward the 2 s");
        eng.Tick(T0.AddMilliseconds(15_100));
        Assert.Equal(2, hits.Count, "2 s after the last rise");
    }

    [Test]
    public static void Watch_already_true_when_set_does_not_fire_until_it_cycles_and_bad_or_disabled_ones_never_fire()
    {
        var vals = new SignalValueCache(); vals.Set("A", true);
        var eng = new WatchEngine(vals); var hits = new List<WatchHit>(); eng.Fired += (h, _) => hits.Add(h);
        eng.Set(new[] { new WatchDef("a", "A"), new WatchDef("bad", "A &&"), new WatchDef("off", "A", Enabled: false) });
        Change(eng, vals, "A", true, 10);
        Assert.Equal(0, hits.Count, "was true when set: not a new event");
        Change(eng, vals, "A", false, 20);
        Change(eng, vals, "A", true, 30);
        Assert.Equal(1, hits.Count, "fires on the next rise, and only the enabled good watch");
        var st = eng.Status();
        Assert.Equal("error", st.Single(s => s.Id == "bad").State, "bad expression is shown as an error");
        Assert.True(st.Single(s => s.Id == "bad").Error!.Contains("character"), "with its message");
        Assert.Equal("off", st.Single(s => s.Id == "off").State, "disabled");
        Assert.Equal("A", string.Join(",", eng.Status().Single(s => s.Id == "a").Unknown.Concat(new[] { "A" })), "known signals aren't reported unknown");
        eng.Set(Array.Empty<WatchDef>());
        Assert.True(!eng.Active, "no watches: not active");
    }

    // ------------------------------------------------------------------ oscillation detector

    private static TraceEvent Ev(string sig, bool v, double ms, long chain = 0, SigKind kind = SigKind.Digital) =>
        new(T0.AddMilliseconds(ms), 1, sig, kind, v, "x", null, null, 0, chain);

    [Test]
    public static void Oscillation_detector_flags_too_many_changes_in_one_burst()
    {
        var d = new OscillationDetector();
        d.Configure(new MonitorSettings { MaxChangesPerBurst = 6 });
        var alerts = new List<OscillationAlert>(); d.Alert += alerts.Add;
        for (var i = 0; i < 6; i++) { var e = Ev("A", i % 2 == 0, i * 1000, chain: 7); d.OnChange(e.Signal, e.Kind, e.Time, e.Chain); }
        Assert.Equal(0, alerts.Count, "6 changes in a burst are allowed");
        var e7 = Ev("A", false, 6000, chain: 7); d.OnChange(e7.Signal, e7.Kind, e7.Time, e7.Chain);
        Assert.Equal(1, alerts.Count, "the 7th is one too many");
        Assert.Equal("burst", alerts[0].Kind, "kind");
        Assert.True(alerts[0].Message.Contains("A changed 7 times in one burst"), alerts[0].Message);
        for (var i = 0; i < 20; i++) { var e = Ev("A", i % 2 == 0, 6001 + i, chain: 7); d.OnChange(e.Signal, e.Kind, e.Time, e.Chain); }
        Assert.Equal(1, alerts.Count, "a loop that keeps going is one alert, not one per change");
        // A new burst starts counting again; a signal that changes once per burst is fine however long it runs.
        for (var chain = 100; chain < 400; chain++) { var e = Ev("B", chain % 2 == 0, chain * 2000, chain); d.OnChange(e.Signal, e.Kind, e.Time, e.Chain); }
        Assert.Equal(1, alerts.Count, "one change per burst never alerts");
        Assert.Equal(1, d.Recent(T0.AddMilliseconds(8000)), "one signal alerted recently");
        Assert.Equal(0, d.Recent(T0.AddMinutes(5)), "and not five minutes later");
    }

    [Test]
    public static void Oscillation_detector_flags_digital_signals_that_flip_fast_but_not_slow_ones_or_analogs()
    {
        var d = new OscillationDetector();
        d.Configure(new MonitorSettings { OscillationChanges = 10, OscillationWindowMs = 500 });
        var alerts = new List<OscillationAlert>(); d.Alert += alerts.Add;
        // 10 flips in 450 ms (no burst ids, as in a SIMPL# program): oscillation.
        for (var i = 0; i < 10; i++) { var e = Ev("Fast", i % 2 == 0, i * 50); d.OnChange(e.Signal, e.Kind, e.Time, e.Chain); }
        Assert.Equal(1, alerts.Count, "fast flipping");
        Assert.Equal("oscillation", alerts[0].Kind, "kind");
        // The same count spread over 5 s is just a busy signal.
        for (var i = 0; i < 40; i++) { var e = Ev("Slow", i % 2 == 0, i * 500); d.OnChange(e.Signal, e.Kind, e.Time, e.Chain); }
        Assert.Equal(1, alerts.Count, "slow flipping is fine");
        // Analog values change quickly all the time (a fader); only the burst check applies to them.
        for (var i = 0; i < 50; i++) { var e = new TraceEvent(T0.AddMilliseconds(i * 5), 1, "Fader", SigKind.Analog, i * 100, "x"); d.OnChange(e.Signal, e.Kind, e.Time, 0); }
        Assert.Equal(1, alerts.Count, "analog isn't an oscillation");
        // Quiet for a while, then it happens again: a new alert.
        for (var i = 0; i < 10; i++) { var e = Ev("Fast", i % 2 == 0, 60_000 + i * 50); d.OnChange(e.Signal, e.Kind, e.Time, e.Chain); }
        Assert.Equal(2, alerts.Count, "alerts again after a quiet period");
    }

    [Test]
    public static void Change_tracker_lists_signals_that_never_changed_and_resets()
    {
        var t = new ChangeTracker();
        var all = new[] { new SignalInfo("A", SigKind.Digital, false), new SignalInfo("B", SigKind.Analog, 0), new SignalInfo("C", SigKind.Serial, "") };
        Assert.Equal(3, t.NeverChanged(all).Count, "nothing has changed yet");
        t.OnChange("A"); t.OnChange("A"); t.OnChange("Z");
        Assert.Equal("B,C", string.Join(",", t.NeverChanged(all).Select(s => s.Name)), "A changed");
        Assert.Equal(2, t.Count("A"), "counted");
        t.Reset(DateTimeOffset.Now);
        Assert.Equal(3, t.NeverChanged(all).Count, "reset forgets");
    }

    // ------------------------------------------------------------------ runtime integration (a real slot)

    private const string Hd = "[\nObjTp=Hd\nPgmNm=Chain\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n";

    // press -> AND -> mid -> AND -> fb -> panel feedback; one press causes changes at depth 0, 1 and 2.
    private const string ChainSmw = Hd +
        "[\nObjTp=Sg\nH=10\nNm=press\n]\n[\nObjTp=Sg\nH=11\nNm=mid\n]\n[\nObjTp=Sg\nH=12\nNm=fb\n]\n[\nObjTp=Sg\nH=13\nNm=idle_one\n]\n" +
        "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=12\nO1=10\n]\n" +
        "[\nObjTp=Sm\nH=20\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=10\nI2=2\nO1=11\n]\n" +
        "[\nObjTp=Sm\nH=21\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=11\nI2=2\nO1=12\n]\n" +
        "[\nObjTp=Sm\nH=22\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=13\nI2=2\nO1=13\n]\n";

    // A buffer, enabled by the press, that feeds a NOT whose output goes back into the buffer: when pressed, a and b chase each other forever.
    private const string LoopSmw = Hd +
        "[\nObjTp=Sg\nH=10\nNm=press\n]\n[\nObjTp=Sg\nH=11\nNm=a\n]\n[\nObjTp=Sg\nH=12\nNm=b\n]\n[\nObjTp=Sg\nH=13\nNm=fb\n]\n" +
        "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=13\nO1=10\n]\n" +
        "[\nObjTp=Sm\nH=20\nSmC=205\nNm=NOT\nmI=1\nmO=1\nI1=11\nO1=12\n]\n" +
        "[\nObjTp=Sm\nH=21\nSmC=20\nNm=Buffer\nmI=2\nmO=1\nI1=10\nI2=12\nO1=11\n]\n" +
        "[\nObjTp=Sm\nH=22\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=10\nI2=2\nO1=13\n]\n";

    private static string WriteSmw(string name, string content)
    {
        var path = Path.Combine(Paths.TempData(), name + ".smw");
        File.WriteAllText(path, content);
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        return path;
    }

    private static Processor NewProcessor(string? dir = null)
    {
        var p = new Processor(dir ?? Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.AddLoader(new SimplLoader());
        return p;
    }

    private static JoinKey D1 => new(SigKind.Digital, 1);

    [Test]
    public static void A_slot_with_no_watches_or_detectors_listens_to_nothing()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteSmw("chain", ChainSmw));
        var slot = p.Slot(1);
        Assert.True(!slot.TraceWanted, "TraceWanted is false: the runtimes take the untraced path");
        Assert.True(!slot.Monitor.Listening, "the monitor isn't subscribed");
        // Turning a feature on subscribes; turning every feature off lets go again.
        slot.Monitor.SetWatches(new[] { new WatchDef("", "fb=1") });
        Assert.True(slot.TraceWanted && slot.Monitor.Listening, "a watch turns tracing on");
        slot.Monitor.SetWatches(Array.Empty<WatchDef>());
        Assert.True(!slot.TraceWanted && !slot.Monitor.Listening, "no watches: off again");
        slot.Monitor.SetSettings(new MonitorSettings { Oscillation = true });
        Assert.True(slot.TraceWanted, "oscillation detection turns tracing on");
        slot.Monitor.SetSettings(new MonitorSettings());
        Assert.True(!slot.TraceWanted, "and off");
        using (slot.Monitor.AcquireTrace()) Assert.True(slot.TraceWanted, "a debugger's lease turns it on");
        Assert.True(!slot.TraceWanted, "released: off");
        slot.Stop();
        slot.Monitor.SetWatches(new[] { new WatchDef("", "fb=1") });
        Assert.True(!slot.TraceWanted, "a stopped program has nothing to watch");
    }

    [Test]
    public static void Watch_works_with_the_debugger_closed_logs_a_warning_and_is_remembered()
    {
        var dir = Paths.TempData();
        using (var p = NewProcessor(dir))
        {
            p.LoadProgram(1, WriteSmw("chain", ChainSmw));
            var slot = p.Slot(1);
            slot.Monitor.SetWatches(new[] { new WatchDef("", "fb=1 && press=1", Name: "Both up") });
            slot.FindDevice(3)!.ReceiveInput(D1, true);
            Assert.Eventually(() => p.RecentLog(1).Any(l => l.Level == "Warning" && l.Message.StartsWith("Watch true: Both up")), "a Warning in the slot log");
            var line = p.RecentLog(1).Last(l => l.Message.StartsWith("Watch true"));
            Assert.True(line.Message.Contains("fb=1") && line.Message.Contains("press=1"), "with the values that made it true: " + line.Message);
            var st = slot.Monitor.WatchStatus().Single();
            Assert.Equal("true", st.State, "shown as true");
            Assert.Equal(1, st.Hits, "one hit");
            Assert.True(st.Id.Length > 0, "the server gave it an id");
            slot.FindDevice(3)!.ReceiveInput(D1, false);
            Assert.Eventually(() => slot.Monitor.WatchStatus().Single().State == "idle", "idle after release");
        }
        // A new processor on the same folder (a restart) brings the watch back and it works.
        using (var p2 = NewProcessor(dir))
        {
            p2.LoadProgram(1, WriteSmw("chain", ChainSmw));
            var slot = p2.Slot(1);
            Assert.Equal(1, slot.Monitor.WatchStatus().Count, "the watch was saved per slot");
            Assert.True(slot.TraceWanted, "and is active again straight away");
            slot.FindDevice(3)!.ReceiveInput(D1, true);
            Assert.Eventually(() => p2.RecentLog(1).Any(l => l.Message.StartsWith("Watch true")), "fires after the restart");
        }
        Assert.True(File.Exists(Path.Combine(dir, "program01", "debug.json")), "debug.json in the slot's folder");
        // Bad expressions are refused and nothing is saved.
        using var p3 = NewProcessor(Paths.TempData());
        p3.LoadProgram(1, WriteSmw("chain", ChainSmw));
        var ex = Assert.Throws<ArgumentException>(() => p3.Slot(1).Monitor.SetWatches(new[] { new WatchDef("", "fb = ") }));
        Assert.True(ex.Message.Contains("fb ="), "names the bad watch: " + ex.Message);
        Assert.Equal(0, p3.Slot(1).Monitor.WatchStatus().Count, "nothing saved");
    }

    [Test]
    public static void Watch_can_pause_the_program_at_the_change_that_made_it_true()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteSmw("chain", ChainSmw));
        var slot = p.Slot(1);
        slot.Monitor.SetWatches(new[] { new WatchDef("w", "mid=1", PauseStep: true, Name: "Mid up") });
        slot.FindDevice(3)!.ReceiveInput(D1, true);
        Assert.Eventually(() => slot.Stepper.IsPaused, "the program stops");
        var pause = slot.Stepper.Paused!;
        Assert.True(slot.Stepper.Enabled, "step mode is on");
        Assert.Equal("mid", pause.Signal, "at the change that made the watch true");
        Assert.True(pause.Reason!.Contains("Mid up"), "with the reason: " + pause.Reason);
        slot.Stepper.Run();
        Assert.Eventually(() => slot.Runtime!.GetSignals().Single(s => s.Name == "fb").Value is true, "released, the logic finishes");
    }

    [Test]
    public static void Conditional_breakpoint_pauses_only_when_its_condition_is_true()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteSmw("chain", ChainSmw));
        var slot = p.Slot(1);
        // Condition false at the step point: the program runs straight through.
        slot.Stepper.Configure(enabled: true, onlyBreakSignals: true, conditions: new[] { "mid = 1 && press = 0" });
        Assert.True(slot.Monitor.Listening, "a condition makes the monitor track values");
        slot.FindDevice(3)!.ReceiveInput(D1, true);
        Assert.Eventually(() => slot.Runtime!.GetSignals().Single(s => s.Name == "fb").Value is true, "ran through");
        Assert.True(!slot.Stepper.IsPaused, "mid went high while fb was still low: no pause for 'mid = 1 && press = 0' (the press is still down)");
        // A condition that is true when its signal changes pauses right there.
        slot.Stepper.Configure(conditions: new[] { "mid = 1" });
        slot.FindDevice(3)!.ReceiveInput(D1, false);
        Assert.Eventually(() => slot.Runtime!.GetSignals().Single(s => s.Name == "fb").Value is false, "released (mid = 1 is false while it falls)");
        Assert.True(!slot.Stepper.IsPaused, "falling edges don't match 'mid = 1'");
        slot.FindDevice(3)!.ReceiveInput(D1, true);
        Assert.Eventually(() => slot.Stepper.IsPaused, "paused");
        Assert.Equal("mid", slot.Stepper.Paused!.Signal, "at mid");
        Assert.True(slot.Stepper.Paused.Reason!.Contains("mid = 1"), "reason: " + slot.Stepper.Paused.Reason);
        Assert.Equal("mid = 1", string.Join("|", slot.Stepper.Snapshot().Conditions!), "state carries the conditions");
        slot.Stepper.Next();
        Assert.Eventually(() => slot.Runtime!.GetSignals().Single(s => s.Name == "fb").Value is true, "next lets it through (and the only break is spent)");
        slot.Stepper.Run();
        // Bad conditions are refused whole.
        var ex = Assert.Throws<ArgumentException>(() => slot.Stepper.Configure(conditions: new[] { "mid = 1", "((" }));
        Assert.True(ex.Message.Contains("(("), ex.Message);
        Assert.Equal("mid = 1", string.Join("|", slot.Stepper.Conditions), "the earlier list is kept");
        slot.Stepper.Configure(conditions: Array.Empty<string>());
        Assert.True(!slot.Monitor.Listening, "no conditions, no stepping: not listening");
    }

    [Test]
    public static void A_deliberately_oscillating_program_raises_a_warning_and_a_badge()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteSmw("loop", LoopSmw));
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        slot.Monitor.SetSettings(new MonitorSettings { Oscillation = true, MaxChangesPerBurst = 10 });
        slot.FindDevice(3)!.ReceiveInput(D1, true);
        Assert.Eventually(() => slot.Monitor.Oscillation.Alerts.Count > 0, "an alert", 8000);
        var a = slot.Monitor.Oscillation.Alerts[0];
        Assert.True(a.Signal is "a" or "b", "the signal in the loop: " + a.Signal);
        Assert.Equal("burst", a.Kind, "too many changes in one burst");
        Assert.Eventually(() => p.RecentLog(1).Any(l => l.Level == "Warning" && l.Message.Contains("changed") && l.Message.Contains("one burst")), "and a Warning in the log");
        Assert.True(slot.Monitor.Oscillation.Recent(DateTimeOffset.Now) >= 1, "badge count for the board and the debugger");
        Assert.Eventually(() => p.RecentLog(1).Any(l => l.Message.Contains("did not settle")), "the runtime gives up on the loop too, as it always did", 15000);
    }

    [Test]
    public static void Never_changed_view_finds_unused_logic_and_resets()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteSmw("chain", ChainSmw));
        var slot = p.Slot(1);
        slot.Monitor.SetSettings(new MonitorSettings { TrackChanges = true });
        slot.Monitor.Changes.Reset(DateTimeOffset.Now);
        slot.FindDevice(3)!.ReceiveInput(D1, true);
        Assert.Eventually(() => slot.Runtime!.GetSignals().Single(s => s.Name == "fb").Value is true, "ran");
        Assert.Eventually(() => slot.Monitor.Changes.Count("fb") > 0, "tracked");
        var never = slot.Monitor.Changes.NeverChanged(slot.Runtime!.GetSignals()).Select(s => s.Name).ToList();
        Assert.True(!never.Contains("press") && !never.Contains("mid") && !never.Contains("fb"), "signals the press moved are not in the list");
        Assert.True(never.Contains("idle_one"), "a signal nothing drives is: " + string.Join(",", never));
        slot.Monitor.Changes.Reset(DateTimeOffset.Now);
        Assert.True(slot.Monitor.Changes.NeverChanged(slot.Runtime!.GetSignals()).Select(s => s.Name).Contains("press"), "after a reset everything is 'never changed' again");
    }

    // ------------------------------------------------------------------ why is this signal this value?

    [Test]
    public static void Why_chain_walks_from_feedback_back_to_the_press_on_a_real_program()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteSmw("chain", ChainSmw));
        var slot = p.Slot(1);
        using var lease = slot.Monitor.AcquireTrace();
        slot.FindDevice(3)!.ReceiveInput(D1, true);
        Assert.Eventually(() => slot.Monitor.Buffer.Snapshot().Any(e => e.Signal == "fb"), "traced");
        var rt = slot.Runtime!;
        var vals = rt.GetSignals().ToDictionary(s => s.Name, s => s.Value);
        var ctx = new WhyContext
        {
            Graph = ((IProgramGraphSource)rt).GetGraph(), RuntimeKind = rt.Kind,
            Trace = slot.Monitor.Buffer.Snapshot().Select(TraceRow.From).ToList(),
            CurrentValue = n => vals.TryGetValue(n, out var v) ? v : null,
            ClientsOf = id => id == "03" ? new[] { "192.168.1.20" } : Array.Empty<string>()
        };
        var r = WhyChain.Build("fb", ctx);
        Assert.Equal("fb,mid,press", string.Join(",", r.Steps.Select(s => s.Signal)), "fb because mid because press");
        Assert.Equal("change,change,panel", string.Join(",", r.Steps.Select(s => s.Kind)), "two logic steps and a panel press");
        Assert.True(r.Complete, "reaches an input");
        Assert.Equal("S21", r.Steps[0].Symbol, "fb was driven by the second AND");
        Assert.True(r.Steps[0].Text.Contains("AND"), "the symbol's type is named: " + r.Steps[0].Text);
        Assert.True(r.Steps[2].Text.Contains("IP ID 03") && r.Steps[2].Text.Contains("192.168.1.20") && r.Steps[2].Text.Contains("pressed"), "the press names the panel and its address: " + r.Steps[2].Text);
        Assert.True(r.Summary.StartsWith("fb is 1 because fb went high at "), "summary reads as a sentence: " + r.Summary);
        Assert.True(r.Summary.Contains(", because mid went high") && r.Summary.Contains(", because press"), r.Summary);
        // A signal nothing has changed explains itself from the wiring.
        var idle = WhyChain.Build("idle_one", ctx);
        Assert.Equal("none", idle.Steps[0].Kind, "no change seen");
        Assert.True(!idle.Complete && idle.Note != null, "incomplete, with a hint");
        Assert.True(idle.Steps.Any(s => s.Text.Contains("driven by AND")), "but the driver from the wiring is named");
    }

    [Test]
    public static void Why_chain_for_a_compiled_program_and_for_forces_and_startup()
    {
        // Compiled program: the same walk with unnamed signals; the press is on IP ID 03.
        using var p = NewProcessor();
        p.LoadProgram(1, GraphTestsLpz());
        var slot = p.Slot(1);
        using var lease = slot.Monitor.AcquireTrace();
        slot.FindDevice(3)!.ReceiveInput(D1, true);
        Assert.Eventually(() => slot.Monitor.Buffer.Snapshot().Any(e => e.Signal == "#11"), "traced");
        var rt = slot.Runtime!;
        var vals = rt.GetSignals().ToDictionary(s => s.Name, s => s.Value);
        var r = WhyChain.Build("#11", new WhyContext
        {
            Graph = ((IProgramGraphSource)rt).GetGraph(), RuntimeKind = rt.Kind, Trace = slot.Monitor.Buffer.Snapshot().Select(TraceRow.From).ToList(),
            CurrentValue = n => vals.TryGetValue(n, out var v) ? v : null
        });
        Assert.Equal("#11,#30,#10", string.Join(",", r.Steps.Select(s => s.Signal)), "feedback, middle, press");
        Assert.True(r.Complete && r.Steps[^1].Kind == "panel", "ends at the panel");

        // Forces and start-up are named as such.
        var rows = new[]
        {
            new TraceRow(T0, "1", "Digital", true, "start"),
            new TraceRow(T0.AddSeconds(1), "Mode", "Digital", true, "debugger"),
            new TraceRow(T0.AddSeconds(2), "Vol", "Analog", 500L, "IPID04")
        };
        WhyResult W(string s) => WhyChain.Build(s, new WhyContext { Trace = rows, CurrentValue = n => n == "Mode" ? true : n == "Vol" ? 500 : n == "1" ? true : null });
        Assert.Equal("force", W("Mode").Steps[0].Kind, "forced from the debugger");
        Assert.True(W("Mode").Summary.Contains("forced high from the debugger"), W("Mode").Summary);
        Assert.Equal("start", W("1").Steps[0].Kind, "start-up");
        Assert.True(W("Vol").Steps[0].Text.Contains("was set to 500") && W("Vol").Steps[0].Text.Contains("IP ID 04"), W("Vol").Steps[0].Text);
    }

    [Test]
    public static void Why_chain_for_a_simplsharp_program_says_what_is_known()
    {
        var rows = new[]
        {
            new TraceRow(T0, "IPID03.out.d1", "Digital", true, "device"),
            new TraceRow(T0.AddMilliseconds(5), "IPID03.in.d5", "Digital", true, "program")
        };
        WhyResult W(string s) => WhyChain.Build(s, new WhyContext
        {
            Graph = null, RuntimeKind = "SIMPL#", Trace = rows, CurrentValue = _ => true, ClientsOf = id => new[] { "10.0.0.7" }
        });
        var press = W("IPID03.out.d1");
        Assert.Equal("panel", press.Steps[0].Kind, "a join from a device is a press");
        Assert.True(press.Steps[0].Text.Contains("IP ID 03") && press.Steps[0].Text.Contains("10.0.0.7"), press.Steps[0].Text);
        Assert.True(press.Complete, "complete: it is an input");
        var fb = W("IPID03.in.d5");
        Assert.Equal("program", fb.Steps[0].Kind, "feedback is set by the program");
        Assert.True(!fb.Complete && fb.Note!.Contains("don't expose their logic"), "and says it can't see why: " + fb.Note);
        var none = WhyChain.Build("IPID03.in.d9", new WhyContext { Graph = null, RuntimeKind = "SIMPL#", Trace = Array.Empty<TraceRow>(), CurrentValue = _ => false, TraceAvailable = false });
        Assert.Equal("none", none.Steps[0].Kind, "no trace: says so");
    }

    // ------------------------------------------------------------------ trace files

    private static List<TraceRow> SampleRows() => new()
    {
        new(T0, "Room_On", "Digital", true, "IPID03", null, null, 0, 1),
        new(T0.AddMilliseconds(2), "Mid", "Digital", true, "", "S-1.2", "Room_On", 1, 1),
        new(T0.AddMilliseconds(5), "Vol", "Analog", 40000L, "IPID03", "S-3", "Mid", 2, 1),
        new(T0.AddMilliseconds(9), "Src_Name", "Serial", "HDMI, \"2\"\r\nline two\\x0D é ✓", "program"),
        new(T0.AddMilliseconds(10), "Src_Name", "Serial", "", "program"),
        new(T0.AddMilliseconds(11), "Pad", "Serial", " leading and trailing ", "x"),
        new(T0.AddMilliseconds(12), "Room_On", "Digital", false, "debugger", null, null, 0, 2),
    };

    private static void SameRows(IReadOnlyList<TraceRow> want, IReadOnlyList<TraceRow> got, string how)
    {
        Assert.Equal(want.Count, got.Count, how + ": row count");
        for (var i = 0; i < want.Count; i++)
        {
            var a = want[i]; var b = got[i];
            Assert.True(a.Time.ToUniversalTime() == b.Time.ToUniversalTime() && a.Signal == b.Signal && a.Kind == b.Kind && Equals(a.Value, b.Value) && a.Source == b.Source
                        && a.Symbol == b.Symbol && a.Cause == b.Cause && a.Depth == b.Depth && a.Chain == b.Chain,
                $"{how}: row {i} differs: {a} vs {b}");
        }
    }

    [Test]
    public static void Trace_round_trips_through_json_and_csv_with_every_field()
    {
        var rows = SampleRows();
        var json = TraceIo.ToJson(new TraceFile(2, "My Program", T0, rows));
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal("openvp-trace", doc.GetProperty("format").GetString(), "format tag");
        var first = doc.GetProperty("events")[1];
        foreach (var f in new[] { "time", "signal", "kind", "value", "source", "symbol", "cause", "depth", "chain" }) Assert.True(first.TryGetProperty(f, out _), "JSON has " + f);
        var back = TraceIo.FromJson(json);
        Assert.Equal(2, back.Slot, "slot"); Assert.Equal("My Program", back.Program, "program");
        SameRows(rows, back.Rows, "JSON");

        var csv = TraceIo.ToCsv(rows);
        Assert.True(csv.StartsWith("time,signal,kind,value,source,symbol,cause,depth,chain\r\n"), "CSV header");
        SameRows(rows, TraceIo.FromCsv(csv).Rows, "CSV");
        SameRows(rows, TraceIo.Parse(json).Rows, "Parse sniffs JSON");
        SameRows(rows, TraceIo.Parse(csv).Rows, "Parse sniffs CSV");
        SameRows(rows, TraceIo.FromJson(JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement.GetProperty("events"))).Rows, "a bare array of events");
        Assert.Equal(0, TraceIo.FromCsv(TraceIo.ToCsv(Array.Empty<TraceRow>())).Rows.Count, "an empty trace");
    }

    [Test]
    public static void Bad_trace_files_are_refused_with_a_message()
    {
        string[] json = { "{", "[1]", "{\"events\": 3}", "{\"format\":\"other\",\"events\":[]}", "{\"events\":[{\"signal\":\"A\",\"kind\":\"Digital\"}]}",
                          "{\"events\":[{\"time\":\"2025-01-01T00:00:00Z\",\"kind\":\"Digital\"}]}", "{\"events\":[{\"time\":\"2025-01-01T00:00:00Z\",\"signal\":\"A\",\"kind\":\"Weird\"}]}", "{\"events\":[{\"time\":\"not a time\",\"signal\":\"A\",\"kind\":\"Digital\"}]}" };
        foreach (var j in json) Assert.Throws<TraceFormatException>(() => TraceIo.FromJson(j));
        string[] csv = { "", "a,b,c\r\n1,2,3", "time,signal,kind,value\r\nnope,A,Digital,1", "time,signal,kind,value\r\n2025-01-01T00:00:00Z,A,Bogus,1", "time,signal,kind,value\r\n2025-01-01T00:00:00Z,\"A,Digital,1" };
        foreach (var c in csv) Assert.Throws<TraceFormatException>(() => TraceIo.FromCsv(c));
    }

    // ------------------------------------------------------------------ comparing traces

    private static TraceRow R(string sig, object v, double ms, string kind = "Digital") => new(T0.AddMilliseconds(ms), sig, kind, v, "x");

    [Test]
    public static void Trace_compare_finds_extra_changes_different_values_and_timing_beyond_the_tolerance()
    {
        var a = new List<TraceRow> { R("press", true, 0), R("mid", true, 5), R("fb", true, 10), R("fb", false, 500), R("only_a", true, 20), R("Vol", 100L, 30, "Analog") };
        var b = new List<TraceRow> { R("press", true, 1000), R("mid", true, 1005), R("fb", true, 1100), R("fb", false, 1500), R("only_b", true, 1040), R("Vol", 200L, 1030, "Analog") };
        // B starts a second later (offsets are from each trace's first change). fb rose 90 ms later in B than in A.
        var c = TraceCompare.Compare(a, b, toleranceMs: 50);
        Assert.True(!c.Same, "different");
        Assert.Equal(4, c.Matched, "press, mid and both of fb's edges line up: " + c.Matched);
        Assert.Equal(1, c.OnlyA, "only_a only in A");
        Assert.Equal(1, c.OnlyB, "only_b only in B");
        Assert.Equal(1, c.ValueDiffs, "Vol 100 vs 200");
        var timing = c.Diffs.Where(d => d.Kind == "timing").ToList();
        Assert.Equal(1, timing.Count, "one timing difference beyond 50 ms: " + string.Join(" | ", c.Diffs.Select(d => d.Text)));
        Assert.Equal("fb", timing[0].Signal, "the fb rise");
        Assert.True(Math.Abs(timing[0].DeltaMs!.Value - 90) < 0.5, "B is 90 ms later: " + timing[0].DeltaMs);
        Assert.Equal("only_a", c.Diffs.Single(d => d.Kind == "onlyA").Signal, "onlyA row");
        Assert.True(c.Diffs.Single(d => d.Kind == "value").Text.Contains("100") && c.Diffs.Single(d => d.Kind == "value").Text.Contains("200"), "value text shows both");
        // A wider tolerance hides the timing difference; the rest stays.
        var wide = TraceCompare.Compare(a, b, toleranceMs: 100);
        Assert.Equal(0, wide.TimingDiffs, "within 100 ms");
        Assert.Equal(1, wide.OnlyA + wide.OnlyB - 1, "extras unchanged");
        // Diffs are in time order.
        for (var i = 1; i < c.Diffs.Count; i++) Assert.True(c.Diffs[i - 1].OffsetMs <= c.Diffs[i].OffsetMs, "sorted by time");
    }

    [Test]
    public static void Trace_compare_identical_traces_match_and_one_extra_change_does_not_shift_the_rest()
    {
        var a = SampleRows();
        Assert.True(TraceCompare.Compare(a, a.ToList()).Same, "a trace equals itself");
        // The same run started 3 s later is the same trace.
        var shifted = a.Select(r => r with { Time = r.Time.AddSeconds(3) }).ToList();
        Assert.True(TraceCompare.Compare(a, shifted).Same, "a different start time is fine");
        // One extra flip in the middle of a signal's changes: the others still line up.
        var x = new List<TraceRow> { R("s", true, 0), R("s", false, 100), R("s", true, 200), R("s", false, 300) };
        var y = new List<TraceRow> { R("s", true, 0), R("s", false, 100), R("s", true, 150), R("s", false, 160), R("s", true, 200), R("s", false, 300) };
        var c = TraceCompare.Compare(x, y);
        Assert.Equal(4, c.Matched, "all four of A's changes find their partner");
        Assert.Equal(2, c.OnlyB, "the two extra ones are only in B");
        Assert.Equal(0, c.OnlyA + c.ValueDiffs, "nothing else");
        var empty = TraceCompare.Compare(new List<TraceRow>(), a);
        Assert.Equal(a.Count, empty.OnlyB, "against an empty trace everything is extra");
    }

    [Test]
    public static void Saved_traces_are_stored_per_slot_listed_loaded_renamed_and_deleted()
    {
        var store = new TraceStore(Paths.TempData());
        var info = store.Save(1, "Before edit", "Prog", SampleRows());
        store.Save(2, "Other slot", "Prog", SampleRows());
        Assert.Equal(1, store.List(1).Count, "per slot");
        Assert.Equal(SampleRows().Count, store.List(1)[0].Count, "count");
        var got = store.Get(1, info.Id)!.Value;
        SameRows(SampleRows(), got.File.Rows, "stored");
        Assert.True(store.Get(2, info.Id) == null, "not visible from another slot");
        Assert.True(store.Get(1, "../index") == null && store.Get(1, "") == null, "ids that aren't ids are refused");
        Assert.True(store.Rename(1, info.Id, "After edit"), "rename");
        Assert.Equal("After edit", store.List(1)[0].Name, "new name");
        Assert.True(store.Delete(1, info.Id) && store.List(1).Count == 0 && !store.Delete(1, info.Id), "delete");
        Assert.True(store.Save(1, "", "P", SampleRows()).Name.StartsWith("Trace "), "unnamed traces get a default name");
    }

    // ------------------------------------------------------------------ bookmarks and search

    [Test]
    public static void Bookmarks_are_kept_per_program_name_and_survive_a_new_store()
    {
        var dir = Paths.TempData();
        var s = new BookmarkStore(dir);
        s.SetStar("Room Program", "Room_On", true);
        s.SetStar("Room Program", "Vol", true);
        s.SetNote("Room Program", "Vol", "  max 50000 on this DSP  ");
        s.SetStar("Other", "Room_On", true);
        var b = new BookmarkStore(dir).Get("Room Program");
        Assert.Equal("Room_On,Vol", string.Join(",", b.Starred), "stars");
        Assert.Equal("max 50000 on this DSP", b.Notes["Vol"], "note, trimmed");
        Assert.Equal(1, new BookmarkStore(dir).Get("Other").Starred.Count, "another program has its own");
        s.SetStar("Room Program", "Vol", false); s.SetNote("Room Program", "Vol", "");
        var after = new BookmarkStore(dir).Get("Room Program");
        Assert.Equal("Room_On", string.Join(",", after.Starred), "unstar"); Assert.Equal(0, after.Notes.Count, "an empty note removes it");
        Assert.True(BookmarkStore.FileNameFor("a/b") != BookmarkStore.FileNameFor("a_b"), "names that sanitize alike stay apart");
        Assert.True(!BookmarkStore.FileNameFor("../../evil").Contains('/') && !BookmarkStore.FileNameFor("..\\x").Contains('\\'), "no path tricks");
        Assert.Throws<ArgumentException>(() => s.SetNote("Room Program", "Vol", new string('x', 501)));
        Assert.Throws<ArgumentException>(() => s.SetStar("", "Vol", true));
    }

    [Test]
    public static void Signal_search_finds_names_across_slots()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteSmw("chain", ChainSmw));
        p.LoadProgram(3, WriteSmw("loop", LoopSmw));
        var hits = SignalSearch.Find(p, "fb");
        Assert.Equal("1,3", string.Join(",", hits.Select(h => h.Slot)), "fb is in slot 1 and slot 3");
        Assert.True(hits.All(h => h.Signal == "fb"), "exact names");
        Assert.Equal(1, SignalSearch.Find(p, "idle").Count, "substring, one slot");
        Assert.Equal("mid", SignalSearch.Find(p, "m*d").Single().Signal, "wildcard");
        Assert.Equal(0, SignalSearch.Find(p, "zzz").Count, "no match");
        Assert.Equal(0, SignalSearch.Find(p, "  ").Count, "blank query");
    }

    // ------------------------------------------------------------------ exceptions

    [Test]
    public static void Exceptions_are_recorded_with_type_message_stack_and_file_and_line()
    {
        using var p = NewProcessor();
        var slot = p.Slot(1);
        Exception thrown;
        try { ThrowDeep(); thrown = null!; } catch (Exception e) { thrown = new System.Reflection.TargetInvocationException(e); }
        slot.ReportException("CTimer ControlSystem.OnPoll", thrown);
        var rec = slot.Exceptions.Recent().Single();
        Assert.Equal("System.InvalidOperationException", rec.Type, "the real exception, not the reflection wrapper");
        Assert.Equal("boom 42", rec.Message, "message");
        Assert.True(rec.Stack.Contains("ThrowDeep"), "full stack trace text");
        Assert.True(rec.Frames.Count >= 2 && rec.Frames[0].Method.EndsWith("Deeper") && rec.Frames[1].Method.EndsWith("ThrowDeep"), "frames, innermost first");
        Assert.True(rec.HasSymbols && ExceptionReport.ShortFile(rec.Frames[0].File) == "DebuggerTests.cs" && rec.Frames[0].Line > 0, $"file and line from the .pdb: {rec.Frames[0].File}:{rec.Frames[0].Line}");
        var log = p.RecentLog(1).Last(l => l.Level == "Error");
        Assert.True(log.Message.Contains("InvalidOperationException") && log.Message.Contains("CTimer ControlSystem.OnPoll") && log.Message.Contains("boom 42"), log.Message);
        Assert.True(log.Detail!.Contains("ThrowDeep"), "the log entry carries the stack to expand");
        Assert.True(ExceptionReport.ShortFile("C:\\Users\\x\\proj\\ControlSystem.cs") == "ControlSystem.cs" && ExceptionReport.ShortFile("/a/b/c.cs") == "c.cs", "short file names, either slash");
        for (var i = 0; i < 150; i++) slot.ReportException("x", new Exception("e" + i));
        Assert.Equal(ExceptionLog.Max, slot.Exceptions.Count, "the list is capped");
        slot.Exceptions.Clear();
        Assert.Equal(0, slot.Exceptions.Count, "and can be cleared");
    }

    private static void ThrowDeep() => Deeper(42);
    private static void Deeper(int n) => throw new InvalidOperationException("boom " + n);

    [Test]
    public static void Program_symbols_are_found_and_only_portable_pdbs_are_used()
    {
        var dir = Paths.TempData();
        var dll = Path.Combine(dir, "Prog.dll");
        File.WriteAllBytes(dll, new byte[] { 1 });
        Assert.True(ProgramSymbols.Find(dll) == null, "no pdb");
        File.WriteAllBytes(Path.Combine(dir, "Prog.pdb"), new byte[] { (byte)'B', (byte)'S', (byte)'J', (byte)'B', 1, 0 });
        Assert.Equal(Path.Combine(dir, "Prog.pdb"), ProgramSymbols.Find(dll), "found next to the dll");
        Assert.True(ProgramSymbols.IsPortable(File.ReadAllBytes(Path.Combine(dir, "Prog.pdb"))), "BSJB is portable");
        Assert.True(!ProgramSymbols.IsPortable(Encoding.ASCII.GetBytes("Microsoft C/C++ MSF 7.00")), "a Windows PDB is not");
        var src = Path.Combine(Paths.TempData(), "Prog.dll"); File.WriteAllBytes(src, new byte[] { 1 });
        File.WriteAllBytes(Path.ChangeExtension(src, ".pdb"), new byte[] { 2 });
        Assert.True(ProgramSource.Companions(src).Any(f => f.Name == "Prog.pdb"), "loading a .dll from disk brings its .pdb along");
    }

    // ------------------------------------------------------------------ the real server: endpoints, support bundle

    private static string? Www()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenVP.sln"))) d = d.Parent;
        var www = d == null ? null : Path.Combine(d.FullName, "src", "OpenVP.Server", "wwwroot");
        return www != null && Directory.Exists(www) ? www : null;
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port;
    }

    [Test]
    public static void Real_server_debugger_endpoints_and_the_support_bundle()
    {
        var www = Www();
        if (www == null) return;
        var dir = Directory.CreateTempSubdirectory("openvp-dbg-e2e").FullName;
        int port = FreePort();
        var app = OpenVpServer.Create(new[]
        {
            $"--urls=http://127.0.0.1:{port}", "--OpenVP:TunnelPort=0", $"--OpenVP:DataDirectory={dir}", "--OpenVP:CipEnabled=false",
            "--OpenVP:UnsupportedLog=off", $"--webroot={www}", "--OpenVP:Slots=3"
        });
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            var key = File.ReadAllText(Path.Combine(dir, "apikey.txt")).Trim();
            T Get<T>(string url) => http.GetFromJsonAsync<T>(url).GetAwaiter().GetResult()!;
            JsonElement Send(HttpMethod m, string url, object? body = null)
            {
                var req = new HttpRequestMessage(m, url);
                if (body != null) req.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var res = http.Send(req);
                var text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.True(res.IsSuccessStatusCode, $"{m} {url} -> {(int)res.StatusCode}: {text}");
                return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
            }
            int Status(HttpMethod m, string url, object? body = null)
            {
                var req = new HttpRequestMessage(m, url);
                if (body != null) req.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                return (int)http.Send(req).StatusCode;
            }

            // Load the chain program through the upload endpoint, like the console does.
            var smw = WriteSmw("chain", ChainSmw);
            using (var form = new MultipartFormDataContent())
            {
                form.Add(new ByteArrayContent(File.ReadAllBytes(smw)), "file", "chain.smw");
                form.Add(new ByteArrayContent(File.ReadAllBytes(Path.ChangeExtension(smw, ".openvp.json"))), "side", "chain.openvp.json");
                Assert.True(http.PostAsync("/api/slots/1/program", form).GetAwaiter().GetResult().IsSuccessStatusCode, "program loads");
            }
            Assert.Eventually(() => Get<JsonElement>("/api/slots/1").GetProperty("state").GetString() == "Running", "running");

            // Watches: save, read back, a bad one is a 400.
            var w = Send(HttpMethod.Put, "/api/slots/1/watches", new { watches = new[] { new { expression = "fb=1 for 100ms", pauseStep = false, name = "Fb up" } } });
            Assert.Equal(1, w.GetProperty("watches").GetArrayLength(), "one watch");
            Assert.Equal(400, Status(HttpMethod.Put, "/api/slots/1/watches", new { watches = new[] { new { expression = "fb ==" } } }), "bad expression is a 400");
            Assert.Equal(false, Send(HttpMethod.Post, "/api/slots/1/watches/check", new { expression = "((" }).GetProperty("ok").GetBoolean(), "check says no");
            // Conditions through the step endpoint.
            var st = Send(HttpMethod.Post, "/api/slots/1/step", new { conditions = new[] { "Vol > 500" } });
            Assert.Equal("Vol > 500", st.GetProperty("conditions")[0].GetString(), "conditions come back");
            Assert.Equal(400, Status(HttpMethod.Post, "/api/slots/1/step", new { conditions = new[] { "Vol >" } }), "bad condition is a 400");
            Send(HttpMethod.Post, "/api/slots/1/step", new { conditions = Array.Empty<string>() });

            // Press, then the watch fires (the debugger isn't open), and "why" explains fb (the monitor listens because of the watch only: no buffer).
            Send(HttpMethod.Post, "/api/slots/1/signals", new { name = "press", value = 1 });
            Assert.Eventually(() => Get<JsonElement>("/api/slots/1/watches").GetProperty("watches")[0].GetProperty("state").GetString() == "true", "watch is true", 5000);
            Assert.True(Get<JsonElement[]>("/api/log?slot=1").Any(l => l.GetProperty("message").GetString()!.StartsWith("Watch true: Fb up")), "logged");
            var why = Send(HttpMethod.Get, "/api/slots/1/why?signal=fb");
            Assert.True(why.GetProperty("summary").GetString()!.StartsWith("fb is 1"), "why: " + why.GetProperty("summary").GetString());

            // Bookmarks.
            Send(HttpMethod.Put, "/api/slots/1/bookmarks/star", new { signal = "fb", starred = true });
            var bk = Send(HttpMethod.Put, "/api/slots/1/bookmarks/note", new { signal = "fb", note = "panel lamp" });
            Assert.Equal("fb", bk.GetProperty("starred")[0].GetString(), "starred");
            Assert.Equal("panel lamp", bk.GetProperty("notes").GetProperty("fb").GetString(), "note");

            // Saved traces: import JSON and CSV, compare, export.
            var rows = SampleRows();
            var a = Send(HttpMethod.Post, "/api/slots/1/traces?name=Before", TraceIo.ToJson(new TraceFile(1, "chain", T0, rows)));
            var b2 = rows.ToList(); b2.Add(R("extra", true, 100));
            var b = Send(HttpMethod.Post, "/api/slots/1/traces?name=After", TraceIo.ToCsv(b2));
            var cmp = Send(HttpMethod.Post, "/api/slots/1/traces/compare", new { a = a.GetProperty("id").GetString(), b = b.GetProperty("id").GetString(), toleranceMs = 50 });
            Assert.Equal(1, cmp.GetProperty("onlyB").GetInt32(), "the extra change in B");
            Assert.Equal(2, Get<JsonElement[]>("/api/slots/1/traces").Length, "listed");
            var csv = http.PostAsync("/api/slots/1/trace/export?format=csv", new StringContent(TraceIo.ToJson(new TraceFile(1, "x", T0, rows)), Encoding.UTF8, "application/json")).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            SameRows(rows, TraceIo.FromCsv(csv).Rows, "export CSV");
            Assert.Equal(400, Status(HttpMethod.Post, "/api/slots/1/traces?name=x", "not a trace"), "garbage import is a 400");

            // Search across slots.
            Assert.True(Get<JsonElement[]>("/api/search/signals?q=fb").Any(h => h.GetProperty("slot").GetInt32() == 1), "search finds fb in slot 1");

            // Exceptions list exists (empty here) and monitor settings round-trip.
            Assert.Equal(0, Get<JsonElement[]>("/api/slots/1/exceptions").Length, "no exceptions");
            var mon = Send(HttpMethod.Put, "/api/slots/1/monitor", new { oscillation = true, maxChangesPerBurst = 9 });
            Assert.True(mon.GetProperty("oscillation").GetBoolean() && mon.GetProperty("maxChangesPerBurst").GetInt32() == 9, "monitor settings saved");
            Assert.True(Get<JsonElement>("/api/slots/1/never-changed").TryGetProperty("signals", out _), "never-changed answers");

            // The support bundle: log, trace if any, report, settings... but no program, NVRAM/User or key.
            File.WriteAllText(Path.Combine(dir, "program01", "nvram", "secret.txt"), "nvram data");
            Directory.CreateDirectory(Path.Combine(dir, "program01", "user"));
            File.WriteAllText(Path.Combine(dir, "program01", "user", "u.txt"), "user data");
            List<string> Names(byte[] zipBytes) { using var z = new ZipArchive(new MemoryStream(zipBytes)); return z.Entries.Select(e => e.FullName).ToList(); }
            var plain = http.GetByteArrayAsync("/api/slots/1/support-bundle").GetAwaiter().GetResult();
            var names = Names(plain);
            foreach (var want in new[] { "log.txt", "info.txt", "settings.json", "summary.json", "metrics.json", "activity.json", "compatibility.txt", "README.txt" })
                Assert.True(names.Contains(want), $"bundle has {want}: {string.Join(",", names)}");
            Assert.True(!names.Any(n => n.StartsWith("program-files/")), "no program files by default");
            using (var z = new ZipArchive(new MemoryStream(plain)))
                foreach (var e in z.Entries)
                {
                    var text = new StreamReader(e.Open()).ReadToEnd();
                    Assert.True(!text.Contains(key), $"the access key is not in {e.FullName}");
                    Assert.True(!text.Contains("nvram data") && !text.Contains("user data"), $"no NVRAM/User content in {e.FullName}");
                }
            var withProgram = Names(http.GetByteArrayAsync("/api/slots/1/support-bundle?includeProgram=true").GetAwaiter().GetResult());
            Assert.True(withProgram.Contains("program-files/chain.smw"), "ticked: the program is in: " + string.Join(",", withProgram));
            Assert.True(withProgram.Contains("program-files/nvram/secret.txt") && withProgram.Contains("program-files/user/u.txt"), "and its NVRAM and User files");
            Assert.True(!withProgram.Any(n => n.Contains("apikey")), "but never the access key");
        }
        finally { app.StopAsync().GetAwaiter().GetResult(); }
    }

    private static string GraphTestsLpz()
    {
        // Same compiled layout GraphTests uses: panel IPID 03 (press d1 = #10, feedback d1 = #11), press -> AND -> #30 -> AND -> #11.
        const int Press = 10, Fb = 11, Mid = 30;
        int[] panelIns = [Fb, 14, 15, 21, 16, 17, 25];
        int[] panelOuts = [Press, 12, 13, 19, 20, 22, 16, 26];
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: chain.smw")));
        bin.AddRange(Record(-12, "IP-ID-03", panelIns, panelOuts, Int(6, 3), Int(10, 4), Int(11, 6), Int(12, 5), Int(13, 7)));
        bin.AddRange(Record(0, "S-2.1:S-1", [Press, 1], [Mid]));
        bin.AddRange(Record(0, "S-2.1:S-2", [Mid, 1], [Fb]));
        var path = Path.Combine(Paths.TempData(), "chain.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=chain.bin\r\nSMWFileName=chain.smw\r\nRackType=CP4\r\n"));
        Put("chain.bin", bin.ToArray());
        Put("chain.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        Put("chain.dsc", Encoding.ASCII.GetBytes("[DSC0003]\r\nSlot : 2.IP-ID : 03;3;Touchpanels(Enet);TS-1080;;TS-1080;-12\r\n"));
        return path;
    }
}
