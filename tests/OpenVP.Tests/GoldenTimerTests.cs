using System.Text;
using OpenVP.Core;
using OpenVP.Simpl;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>
/// Golden traces for the primitives that run on timers (LPZ types 14, 15, 16, 17, 20, 27, 29, 56, 61,
/// 114, 139 and 241, and the SIMPL Windows Delay and One Shot). They pin the order of changes, so the timer
/// service can be replaced without changing what a program does.
///
/// Timing is deliberately slack so the order is the same on a slow, noisy machine: timers are 50-200 ms,
/// every step waits for the output it expects (or, when nothing should happen, for longer than any timer),
/// and a quiet gap follows every step. No scenario lets two timers come due in the same 10 ms: the order
/// of timers armed for the same moment was never defined (each ran on its own thread-pool thread), so a
/// golden file can't pin it. Where a primitive arms such a pair (241 arms its progress and its step
/// outputs for the same moment) the two are traced in separate scenarios, with the other half unconnected.
///
/// Left out: type 62 Clock Driver. It writes the wall-clock time, which can't be reproduced.
/// </summary>
public static partial class GoldenTests
{
    private const int Gap = 200;   // real time after every step
    private const int Quiet = 400; // real time to prove that nothing more happens (longer than any timer here)

    private static bool Hi(JoinDevice p, uint join) => (bool)p.GetFeedback(D(join));
    private static ushort Lvl(JoinDevice p, uint join) => (ushort)p.GetFeedback(A(join));
    private static string Txt(JoinDevice p, uint join) => (string)p.GetFeedback(S(join));

    /// <summary>
    /// Counts every feedback change as it happens, so a wait can't miss a short pulse between two polls.
    /// Rise, Fall and Saw take their baseline when called: call them before the step that causes the change.
    /// </summary>
    private sealed class FbWatch
    {
        private readonly object _gate = new();
        private readonly List<(JoinKey key, object value)> _log = new();

        public FbWatch(JoinDevice dev) { dev.FeedbackChanged += (_, k, v) => { lock (_gate) _log.Add((k, v)); }; }

        private int Count(JoinKey key, object value) { lock (_gate) return _log.Count(e => e.key == key && Equals(e.value, value)); }
        public int Rises(uint join) => Count(D(join), true);
        public int Falls(uint join) => Count(D(join), false);
        public Func<bool> Rise(uint join, int n = 1) { var b = Rises(join); return () => Rises(join) >= b + n; }
        public Func<bool> Fall(uint join, int n = 1) { var b = Falls(join); return () => Falls(join) >= b + n; }
        public Func<bool> Saw(JoinKey key, object value) { var b = Count(key, value); return () => Count(key, value) > b; }
    }

    /// <summary>One step, then wait for what the step should cause (or, with null, for nothing to happen),
    /// let the logic settle, and leave a quiet gap.</summary>
    private static void TStep(Recorder rec, ProgramSlot[] slots, JoinDevice dev, string label, Func<bool>? until, params (JoinKey key, object value)[] inputs)
    {
        Step(rec, slots, dev, label, inputs);
        if (until != null) Assert.Eventually(until, label, 4000);
        Settle(slots);
        Thread.Sleep(until != null ? Gap : Quiet);
    }

    // ------------------------------------------------------------ Delay, One Shots, Stretch, Debounce, Change Pulse

    [Test]
    public static void Golden_timers_delay_oneshot_stretch_debounce()
    {
        var r = new Rig();
        int dlIn = r.PressD(), osIn = r.PressD(), osCancel = r.PressD(), rtIn = r.PressD(), stIn = r.PressD(),
            dbIn = r.PressD(), m1 = r.PressD(), m2 = r.PressD();                    // d1..d8
        int anaIn = r.PressA();                                                     // a1
        int txtIn = r.PressS();                                                     // s1
        uint fb = 0;
        (int sig, uint join) Out() => (r.OutD(), ++fb);

        var dl = Out(); r.Add(Record(27, "S-1 Delay", [dlIn], [dl.sig], Int(15, 1), Int(2, 15)));
        var os = Out(); var osInv = Out();
        r.Add(Record(15, "S-2 One shot", [osIn, 0, osCancel], [os.sig, osInv.sig], Int(15, 1), Int(2, 10)));
        var rt = Out(); var rtInv = Out();
        r.Add(Record(17, "S-3 Retriggerable one shot", [rtIn, 0, 0], [rt.sig, rtInv.sig], Int(15, 1), Int(2, 20)));
        var st = Out(); r.Add(Record(16, "S-4 Pulse stretcher", [stIn], [st.sig], Int(15, 1), Int(2, 10)));
        var db = Out(); r.Add(Record(56, "S-5 Debounce", [dbIn], [db.sig], Int(15, 1), Int(2, 10)));
        var mo1 = Out(); var mo2 = Out();
        r.Add(Record(29, "S-6 Multiple one shots", [m1, m2], [mo1.sig, mo2.sig], Int(15, 1), Int(2, 10)));
        var cpA = Out(); r.Add(Record(114, "S-7 Change pulse (analog)", [anaIn], [cpA.sig], Int(15, 1), Int(2, 10)));
        var cpS = Out(); r.Add(Record(114, "S-8 Change pulse (serial)", [txtIn], [cpS.sig], Int(15, 1), Int(2, 10)));

        var (p, slots, panel, rec) = Start(r.Build("timers_basic"));
        using var _ = p;
        var w = new FbWatch(panel);
        Func<uint, Func<bool>> high = j => () => Hi(panel, j);
        Func<uint, Func<bool>> low = j => () => !Hi(panel, j);

        // Delay (150 ms): high after the delay, low at once, and a short press never gets through.
        TStep(rec, slots, panel, "delay in on", high(dl.join), (D(1), true));
        TStep(rec, slots, panel, "delay in off (low at once)", null, (D(1), false));
        Step(rec, slots, panel, "delay in on, then off after 20 ms (timer cancelled)", (D(1), true));
        Thread.Sleep(20);
        TStep(rec, slots, panel, "delay in off", null, (D(1), false));
        TStep(rec, slots, panel, "delay in on again", high(dl.join), (D(1), true));
        TStep(rec, slots, panel, "delay in off", null, (D(1), false));

        // One Shot (100 ms): a pulse; a second edge while it runs is ignored; input 3 cancels.
        TStep(rec, slots, panel, "one shot trigger", w.Fall(os.join), (D(2), true));
        TStep(rec, slots, panel, "one shot trigger release", null, (D(2), false));
        TStep(rec, slots, panel, "one shot trigger, release and trigger again while running", w.Fall(os.join),
            (D(2), true), (D(2), false), (D(2), true));
        TStep(rec, slots, panel, "one shot trigger release", null, (D(2), false));
        TStep(rec, slots, panel, "one shot trigger, then cancel", null, (D(2), true), (D(3), true));
        TStep(rec, slots, panel, "one shot release both", null, (D(2), false), (D(3), false));
        TStep(rec, slots, panel, "one shot trigger after cancel", w.Fall(os.join), (D(2), true));
        TStep(rec, slots, panel, "one shot release", null, (D(2), false));

        // Retriggerable One Shot (200 ms): a second edge 80 ms in restarts the pulse.
        var rtEnd = w.Fall(rt.join);
        Step(rec, slots, panel, "retriggerable trigger", (D(4), true));
        Thread.Sleep(80);
        Step(rec, slots, panel, "retriggerable release and trigger again", (D(4), false), (D(4), true));
        Assert.Eventually(rtEnd, "retriggerable pulse ends", 4000);
        Settle(slots);
        Thread.Sleep(Quiet);
        TStep(rec, slots, panel, "retriggerable release", null, (D(4), false));

        // Pulse Stretcher (100 ms): follows the input, holds 100 ms after it drops; high again cancels the hold.
        TStep(rec, slots, panel, "stretch in on", high(st.join), (D(5), true));
        TStep(rec, slots, panel, "stretch in off (held, then low)", low(st.join), (D(5), false));
        Step(rec, slots, panel, "stretch in on", (D(5), true));
        Step(rec, slots, panel, "stretch in off", (D(5), false));
        Thread.Sleep(40);
        TStep(rec, slots, panel, "stretch in on again while held", null, (D(5), true));
        TStep(rec, slots, panel, "stretch in off", low(st.join), (D(5), false));

        // Debounce (100 ms): the output follows once the input has been steady.
        TStep(rec, slots, panel, "debounce in on", high(db.join), (D(6), true));
        TStep(rec, slots, panel, "debounce in off", low(db.join), (D(6), false));
        TStep(rec, slots, panel, "debounce glitch: on and off at once", null, (D(6), true), (D(6), false));
        TStep(rec, slots, panel, "debounce in on", high(db.join), (D(6), true));
        TStep(rec, slots, panel, "debounce glitch: off and on at once", null, (D(6), false), (D(6), true));
        TStep(rec, slots, panel, "debounce in off", low(db.join), (D(6), false));

        // Multiple One Shots (100 ms each): independent per input, retrigger restarts only its own.
        TStep(rec, slots, panel, "multi one shot 1", w.Fall(mo1.join), (D(7), true));
        TStep(rec, slots, panel, "multi one shot 1 release", null, (D(7), false));
        TStep(rec, slots, panel, "multi one shot 2", w.Fall(mo2.join), (D(8), true));
        TStep(rec, slots, panel, "multi one shot 2 release", null, (D(8), false));
        var end1 = w.Fall(mo1.join);
        var end2 = w.Fall(mo2.join);
        Step(rec, slots, panel, "multi one shot 1", (D(7), true));
        Thread.Sleep(80);
        Step(rec, slots, panel, "multi one shot 2, 80 ms later", (D(8), true));
        Assert.Eventually(end1, "multi 1 ends", 4000);
        Assert.Eventually(end2, "multi 2 ends", 4000);
        Settle(slots);
        Thread.Sleep(Gap);
        TStep(rec, slots, panel, "multi one shot releases", null, (D(7), false), (D(8), false));
        var end3 = w.Fall(mo1.join);
        Step(rec, slots, panel, "multi one shot 1 on", (D(7), true));
        TStep(rec, slots, panel, "multi one shot 1 off and on at once (restarts)", end3, (D(7), false), (D(7), true));
        TStep(rec, slots, panel, "multi one shot 1 release", null, (D(7), false));

        // Change Pulse (100 ms): any change on an analog input, and every write of a serial one.
        TStep(rec, slots, panel, "analog a1 = 5", w.Fall(cpA.join), (A(1), (ushort)5));
        TStep(rec, slots, panel, "analog a1 = 5 again (no change)", null, (A(1), (ushort)5));
        Step(rec, slots, panel, "analog a1 = 6", (A(1), (ushort)6));
        Thread.Sleep(50);
        TStep(rec, slots, panel, "analog a1 = 7, 50 ms later (restarts the pulse)", w.Fall(cpA.join), (A(1), (ushort)7));
        TStep(rec, slots, panel, "serial s1 = x", w.Fall(cpS.join), (S(1), "x"));
        TStep(rec, slots, panel, "serial s1 = x again (serials always fire)", w.Fall(cpS.join), (S(1), "x"));
        Check("timers_basic", rec.Render());
    }

    // ------------------------------------------------------------ Oscillator, Analog Ramps, When

    [Test]
    public static void Golden_timers_oscillator_ramps_when()
    {
        var r = new Rig();
        int en = r.PressD(), up = r.PressD(), down = r.PressD();                                          // d1..d3
        int clock = r.PressS();                                                                         // s1
        uint nd = 0, na = 0;
        (int sig, uint join) OutD() => (r.OutD(), ++nd);
        (int sig, uint join) OutA() => (r.OutA(), ++na);

        var osc = OutD(); r.Add(Record(14, "S-1 Oscillator", [en], [osc.sig], Int(15, 2), Int(2, 15), Int(2, 15)));
        // Full range in 200 ms: 16383 per 50 ms step.
        var ramp = OutA(); r.Add(Record(20, "S-2 Analog ramp", [up, down], [ramp.sig], Int(15, 1), Int(2, 20)));
        // (Type 223, the bounded ramp, works from elapsed time, so its in-between values aren't reproducible;
        // RampLimiterTests covers it.)
        var when = OutD(); r.Add(Record(61, "S-4 When", [clock], [when.sig], Int(15, 2), Int(2, 10), Int(2, 360000)));

        var (p, slots, panel, rec) = Start(r.Build("timers_osc"));
        using var _ = p;
        var w = new FbWatch(panel);

        // Oscillator (150 ms high, 150 ms low) runs while its input is high.
        var low1 = w.Fall(osc.join);
        var high2 = w.Rise(osc.join, 2); // high at once, and again after the low phase
        Step(rec, slots, panel, "oscillator on", (D(1), true));
        Assert.Eventually(low1, "oscillator goes low", 4000);
        Assert.Eventually(high2, "oscillator goes high again", 4000);
        TStep(rec, slots, panel, "oscillator off (while high)", null, (D(1), false));
        var low3 = w.Fall(osc.join);
        Step(rec, slots, panel, "oscillator on again", (D(1), true));
        Assert.Eventually(low3, "oscillator goes low", 4000);
        TStep(rec, slots, panel, "oscillator off (while low)", null, (D(1), false));
        var low4 = w.Fall(osc.join, 2); // the restart's own fall and the low phase after it
        Step(rec, slots, panel, "oscillator on", (D(1), true));
        Step(rec, slots, panel, "oscillator off and on at once (restarts high)", (D(1), false), (D(1), true));
        Assert.Eventually(low4, "oscillator goes low", 4000);
        TStep(rec, slots, panel, "oscillator off", null, (D(1), false));

        // Analog Ramp: steps every 50 ms while held, clamped at the ends, stops on release.
        TStep(rec, slots, panel, "ramp up held", () => Lvl(panel, ramp.join) == 65535, (D(2), true));
        TStep(rec, slots, panel, "ramp up released", null, (D(2), false));
        TStep(rec, slots, panel, "ramp down held", () => Lvl(panel, ramp.join) == 0, (D(3), true));
        TStep(rec, slots, panel, "ramp down released", null, (D(3), false));
        TStep(rec, slots, panel, "ramp up tapped (first step only)", null, (D(2), true), (D(2), false));
        TStep(rec, slots, panel, "ramp down tapped", null, (D(3), true), (D(3), false));

        // When: pulses 100 ms when the clock text reaches 01:00:00.
        TStep(rec, slots, panel, "clock 00:59:59", null, (S(1), "00:59:59"));
        TStep(rec, slots, panel, "clock 01:00:00", w.Fall(when.join), (S(1), "01:00:00"));
        TStep(rec, slots, panel, "clock 01:00:00 again (serials always fire)", w.Fall(when.join), (S(1), "01:00:00"));
        TStep(rec, slots, panel, "clock text that isn't a time", null, (S(1), "noon"));
        TStep(rec, slots, panel, "clock 01:00:01", null, (S(1), "01:00:01"));
        Check("timers_oscillator_ramps_when", rec.Render());
    }

    // ------------------------------------------------------------ Steppers

    [Test]
    public static void Golden_timers_steppers()
    {
        var r = new Rig();
        int start = r.PressD(), pStart = r.PressD(), pReset = r.PressD(), qStart = r.PressD(), qReset = r.PressD(); // d1..d5
        uint nd = 0, na = 0, ns = 0;
        (int sig, uint join) OutD() => (r.OutD(), ++nd);
        (int sig, uint join) OutA() => (r.OutA(), ++na);
        (int sig, uint join) OutS() => (r.OutS(), ++ns);

        // Stepper: after 100 ms three outputs pulse for 100 ms each with 100 ms between (output 1 is reserved).
        var s2 = OutD(); var s3 = OutD(); var s4 = OutD();
        r.Add(Record(139, "S-1 Stepper", [start], [0, s2.sig, s3.sig, s4.sig], Int(15, 7),
            Int(2, 10), Int(2, 10), Int(2, 10), Int(2, 10), Int(2, 10), Int(2, 10), Int(2, 0)));
        // Stepper with Progress, steps traced (progress and text outputs left unconnected): 100 ms, then three
        // 100 ms steps with 100 ms between, and 100 ms before the end.
        var run = OutD(); var t1 = OutD(); var t2 = OutD(); var t3 = OutD();
        var prog = new[] { 10, 10, 10, 10, 10, 10, 10 };
        byte[][] pp = prog.Select(v => Int(2, v)).ToArray();
        r.Add(Record(241, "S-2 Stepper with progress (steps)", [pStart, pReset], [run.sig, 0, 0, 0, 0, t1.sig, t2.sig, t3.sig], [Int(15, 7), .. pp]));
        // The same, progress and text traced, steps unconnected.
        var run2 = OutD(); var pct = OutA(); var txt = OutS();
        r.Add(Record(241, "S-3 Stepper with progress (progress)", [qStart, qReset], [run2.sig, pct.sig, 0, 0, txt.sig, 0, 0, 0], [Int(15, 7), .. pp]));

        var (p, slots, panel, rec) = Start(r.Build("timers_steppers"));
        using var _ = p;
        var w = new FbWatch(panel);

        // Stepper
        TStep(rec, slots, panel, "stepper start", w.Fall(s4.join), (D(1), true));
        TStep(rec, slots, panel, "stepper start release", null, (D(1), false));
        var out3 = w.Rise(s3.join);
        Step(rec, slots, panel, "stepper start again", (D(1), true));
        Assert.Eventually(out3, "stepper output 3", 4000);
        // Output 3 stays high until the new sequence turns it off.
        TStep(rec, slots, panel, "stepper release and start again while output 3 is high (restarts the sequence)",
            w.Fall(s4.join), (D(1), false), (D(1), true));
        TStep(rec, slots, panel, "stepper start release", null, (D(1), false));

        // Stepper with Progress, steps
        TStep(rec, slots, panel, "progress(steps) start", w.Fall(run.join), (D(2), true));
        TStep(rec, slots, panel, "progress(steps) start release", null, (D(2), false));
        var step2 = w.Rise(t2.join);
        Step(rec, slots, panel, "progress(steps) start again", (D(2), true));
        Assert.Eventually(step2, "progress(steps) output 2", 4000);
        TStep(rec, slots, panel, "progress(steps) reset while a step is high", null, (D(3), true));
        TStep(rec, slots, panel, "progress(steps) reset release", null, (D(3), false), (D(2), false));

        // Stepper with Progress, progress
        TStep(rec, slots, panel, "progress start", w.Fall(run2.join), (D(4), true));
        TStep(rec, slots, panel, "progress start release", null, (D(4), false));
        var at42 = w.Saw(S(txt.join), "42%");
        Step(rec, slots, panel, "progress start again", (D(4), true));
        Assert.Eventually(at42, "progress 42%", 4000);
        TStep(rec, slots, panel, "progress reset part way", null, (D(5), true));
        TStep(rec, slots, panel, "progress reset release", null, (D(5), false), (D(4), false));
        Check("timers_steppers", rec.Render());
    }

    [Test]
    public static void Debugger_pulse_is_two_edges_with_no_gap()
    {
        // A Stepper whose first step drives its own trigger (a panel press), and an OR of every step that
        // shows on the panel. A debugger Pulse is high then low at once, so when the first step (100 ms in)
        // drives the trigger high it is a new rising edge: the OR rises for the pulse, then once per step
        // (the stepper restarts on its own edge, which doesn't add a rise). A press held past 100 ms hides
        // that edge, which is what the old 200 ms pulse did.
        var r = new Rig();
        int trig = r.PressD(), s2 = r.Sig(), s3 = r.Sig();
        int any = r.OutD(); // feedback d1
        r.Add(Record(139, "S-1 Stepper", [trig], [0, trig, s2, s3], Int(15, 6),
            Int(2, 10), Int(2, 10), Int(2, 10), Int(2, 10), Int(2, 10), Int(2, 10)));
        r.Add(Record(2, "S-2 OR", [trig, s2, s3], [any]));
        var (p, slots, panel, _) = Start(r.Build("debugger_pulse"));
        using var _p = p;
        var w = new FbWatch(panel);
        Assert.True(slots[0].Runtime!.TryPulseSignal($"#{trig}"), "pulse accepted");
        Assert.Eventually(() => w.Rises(1) >= 4, "OR rises for the pulse and for each of the three steps", 4000);
        Thread.Sleep(Quiet);
        Settle(slots);
        Assert.Equal(4, w.Rises(1), "OR rises");
        Assert.True(!slots[0].Runtime!.TryPulseSignal("no such signal"), "unknown signal is refused");
    }

    // ------------------------------------------------------------ SIMPL Windows Delay and One Shot

    [Test]
    public static void Golden_timers_smw_delay_oneshot()
    {
        // Press d1 -> Delay (150 ms) -> feedback d1; press d2 -> One Shot (100 ms) -> feedback d2.
        const string smw = """
            [
            ObjTp=Hd
            PgmNm=Timers
            ]
            [
            ObjTp=Sm
            H=10
            SmC=6840
            Nm=XPanel
            DvH=589
            n1I=2
            n1O=2
            mI=2
            I1=30
            I2=31
            mO=2
            O1=20
            O2=21
            ]
            [
            ObjTp=Sm
            H=40
            SmC=0
            Nm=Delay
            mI=1
            I1=20
            mO=1
            O1=30
            mP=1
            P1=15t
            ]
            [
            ObjTp=Sm
            H=41
            SmC=0
            Nm=One Shot
            mI=1
            I1=21
            mO=1
            O1=31
            mP=1
            P1=10t
            ]
            [
            ObjTp=Sg
            H=20
            Nm=delay_press
            ]
            [
            ObjTp=Sg
            H=21
            Nm=oneshot_press
            ]
            [
            ObjTp=Sg
            H=30
            Nm=delay_fb
            ]
            [
            ObjTp=Sg
            H=31
            Nm=oneshot_fb
            ]
            """;
        var path = Path.Combine(Paths.TempData(), "timers_smw.smw");
        File.WriteAllText(path, smw.Replace("\r\n", "\n"), new UTF8Encoding(false));
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"10\": \"03\" } }");

        using var p = new Processor(Paths.TempData(), 2);
        p.AddLoader(new SimplLoader());
        var rec = new Recorder(p);
        p.LoadProgram(1, path);
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel IPID 03");
        var slots = new[] { slot };
        Settle(slots);
        var w = new FbWatch(panel);

        // Delay
        TStep(rec, slots, panel, "delay in on", () => Hi(panel, 1), (D(1), true));
        TStep(rec, slots, panel, "delay in off (low at once)", null, (D(1), false));
        Step(rec, slots, panel, "delay in on, then off after 20 ms (timer cancelled)", (D(1), true));
        Thread.Sleep(20);
        TStep(rec, slots, panel, "delay in off", null, (D(1), false));
        TStep(rec, slots, panel, "delay in on again", () => Hi(panel, 1), (D(1), true));
        TStep(rec, slots, panel, "delay in off", null, (D(1), false));
        // One Shot (retriggerable: a new edge restarts the pulse)
        TStep(rec, slots, panel, "one shot trigger", w.Fall(2), (D(2), true));
        TStep(rec, slots, panel, "one shot release", null, (D(2), false));
        var end = w.Fall(2);
        Step(rec, slots, panel, "one shot trigger", (D(2), true));
        Thread.Sleep(60);
        Step(rec, slots, panel, "one shot release and trigger again, 60 ms in", (D(2), false), (D(2), true));
        Assert.Eventually(end, "one shot pulse ends", 4000);
        Settle(slots);
        Thread.Sleep(Quiet);
        TStep(rec, slots, panel, "one shot release", null, (D(2), false));
        Check("timers_smw_delay_oneshot", rec.Render());
    }
}
