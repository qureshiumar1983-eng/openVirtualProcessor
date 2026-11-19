using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>Runs small synthetic compiled programs through the engine.</summary>
public static class LpzRuntimeTests
{
    // Signals
    const int Press1 = 10, Fb1 = 11, Press2 = 12, Press3 = 13, Fb2 = 14, Fb3 = 15, Level = 16, Text = 17, Hello = 18,
              BothA = 19, BothB = 20, BothFb = 21, Clear = 22, Busy = 23, DelayOut = 24, Echo = 25, Rx = 26;

    /// <param name="timers">False leaves out the Delay, so the golden trace never depends on a timer.</param>
    internal static string BuildProgram(bool timers = true)
    {
        // Panel IPID 03: inputs (feedback) d1..d4, a1, s1..s2 ; outputs (presses) d1..d6, a1, s1
        int[] panelIns = [Fb1, Fb2, Fb3, BothFb, /*a1*/ Level, /*s1*/ Text, /*s2*/ Echo];
        int[] panelOuts = [Press1, Press2, Press3, BothA, BothB, Clear, /*a1*/ Level, /*s1*/ Rx];
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: synth.smw")));
        bin.AddRange(Record(-12, "IP-ID-03", panelIns, panelOuts,
            Int(6, 3), Int(10, 4), Int(11, 6), Int(12, 5), Int(13, 7)));
        bin.AddRange(Record(10, "S-1", [Press1], [Fb1, 0]));                       // Toggle
        bin.AddRange(Record(11, "S-2", [Clear, Press2, Press3], [Fb2, Fb3]));      // Interlock
        bin.AddRange(Record(0, "S-3", [BothA, BothB], [BothFb]));                  // AND
        bin.AddRange(Record(38, "S-4", [1], [Hello], Int(15, 1), Str(3, "ready"))); // init string at start
        bin.AddRange(Record(88, "S-5", [1, Rx], [Echo]));                          // Serial Buffer, always enabled
        bin.AddRange(Record(88, "S-6", [1, Hello], [Text]));
        if (timers) bin.AddRange(Record(27, "S-7", [BothFb], [DelayOut], Int(15, 1), Int(2, 5)));  // Delay 50 ms
        bin.AddRange(Record(-48, "S-8", [Press1], [Busy], Str(3, "Some_Module.dll")));  // SIMPL+ (unsupported)

        var path = Path.Combine(Paths.TempData(), "synth.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=synth.bin\r\nSMWFileName=synth.smw\r\nRackType=CP4\r\n"));
        Put("synth.bin", bin.ToArray());
        Put("synth.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        Put("synth.dsc", Encoding.ASCII.GetBytes("[DSC0003]\r\nSlot : 2.IP-ID : 03;3;Touchpanels(Enet);TS-1080;;TS-1080;-12\r\n"));
        return path;
    }

    private static (Processor p, JoinDevice panel) Start()
    {
        var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, BuildProgram());
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var panel = p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03 registered from the compiled device record");
        return (p, panel);
    }

    static JoinKey D(uint j) => new(SigKind.Digital, j);
    static JoinKey A(uint j) => new(SigKind.Analog, j);
    static JoinKey S(uint j) => new(SigKind.Serial, j);

    [Test]
    public static void Compiled_program_runs_panel_logic()
    {
        var (p, panel) = Start();
        using var _ = p;
        Assert.Equal("TS-1080", panel.Type, "model from .dsc");
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "ready", "string init at start reached s1 via serial buffer");

        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(1)), "toggle on");
        panel.ReceiveInput(D(1), false); panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => !(bool)panel.GetFeedback(D(1)), "toggle off");

        panel.ReceiveInput(D(2), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(2)), "interlock 1");
        panel.ReceiveInput(D(3), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(3)) && !(bool)panel.GetFeedback(D(2)), "interlock 2 replaces 1");
        panel.ReceiveInput(D(6), true);
        Assert.Eventually(() => !(bool)panel.GetFeedback(D(3)), "interlock clear");

        panel.ReceiveInput(D(4), true);
        Thread.Sleep(30);
        Assert.True(!(bool)panel.GetFeedback(D(4)), "AND low with one input");
        panel.ReceiveInput(D(5), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(4)), "AND high");

        panel.ReceiveInput(A(1), 777);
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(1)) == 777, "analog loop-back through one signal");

        panel.ReceiveInput(S(1), "abc");
        Assert.Eventually(() => (string)panel.GetFeedback(S(2)) == "abc", "serial buffer echo");
    }

    [Test]
    public static void Step_debugger_pauses_each_change_until_next()
    {
        var (p, panel) = Start();
        using var _ = p;
        var st = p.Slot(1).Stepper;
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "ready", "started");

        // Break only on the press signal: the toggle must not react until Next.
        st.Configure(enabled: true, onlyBreakSignals: true, breakSignals: ["#10"]);
        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => st.Paused?.Signal == "#10", "paused on the marked press signal");
        Assert.Equal(true, st.Paused!.Value, "paused value");
        Thread.Sleep(50);
        Assert.True(!(bool)panel.GetFeedback(D(1)), "toggle has not run while paused");
        Assert.True(st.Next(), "next");
        Assert.Eventually(() => (bool)panel.GetFeedback(D(1)), "toggle ran after Next");
        Assert.True(st.Paused == null, "unmarked feedback change did not pause");

        // Every change: press pauses, then its feedback pauses, one step at a time.
        panel.ReceiveInput(D(1), false);
        Assert.Eventually(() => st.Paused?.Signal == "#10", "release paused");
        st.Configure(onlyBreakSignals: false);
        st.Next();
        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => st.Paused?.Signal == "#10" && Equals(st.Paused.Value, true), "press paused");
        st.Next();
        Assert.Eventually(() => st.Paused?.Signal == "#11", "toggle output paused as the next step");
        Assert.True((bool)panel.GetFeedback(D(1)), "panel feedback not updated yet");
        st.Next();
        Assert.Eventually(() => !(bool)panel.GetFeedback(D(1)), "feedback reached panel");

        // Stopping a paused slot releases it instead of hanging.
        panel.ReceiveInput(D(1), false);
        Assert.Eventually(() => st.IsPaused, "paused again");
        var stop = Task.Run(() => p.Slot(1).Stop());
        Assert.True(stop.Wait(3000), "stop did not hang on a paused slot");
        Assert.True(!st.Enabled && !st.IsPaused, "stepping turned off by stop");
    }

    [Test]
    public static void Trace_subscriptions_are_per_slot()
    {
        var (p, panel) = Start();
        using var _ = p;
        var slot = p.Slot(1);
        var seen1 = new List<TraceEvent>();
        var seen2 = new List<TraceEvent>();
        Assert.True(!slot.TraceWanted, "nobody listening yet");
        Action<TraceEvent> h = e => { lock (seen1) seen1.Add(e); };
        slot.Traced += h;
        p.Slot(2).Traced += e => { lock (seen2) seen2.Add(e); };
        Assert.True(slot.TraceWanted && p.Slot(2).TraceWanted, "subscribers turn tracing on for their own slot");
        Assert.True(!p.Slot(3).TraceWanted, "other slots stay off");
        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => { lock (seen1) return seen1.Any(e => e.Signal == "#10"); }, "slot 1 subscriber saw the press");
        lock (seen2) Assert.Equal(0, seen2.Count, "slot 2 subscriber saw nothing");
        slot.Traced -= h;
        Assert.True(!slot.TraceWanted, "unsubscribing turns tracing off");
        slot.Stepper.Configure(enabled: true, onlyBreakSignals: true);
        Assert.True(slot.TraceWanted, "stepping turns tracing on");
        slot.Stepper.Run();
    }

    [Test]
    public static void Nvram_saves_atomically_and_reloads()
    {
        var path = Path.Combine(Paths.TempData(), "slot", "nvram.json");
        var store = new NvramStore(path);
        store.Set("a", "hello");
        store.Set("b", (ushort)42);
        store.Flush();
        Assert.True(!File.Exists(path + ".tmp"), "temp file swapped in, not left behind");
        store.Set("b", (ushort)43);
        // Concurrent flushes (timer thread plus Stop) must not corrupt the file or leave a stale snapshot.
        Parallel.For(0, 8, _ => store.Flush());
        var again = new NvramStore(path);
        Assert.Equal("hello", (string)again.Get("a")!, "string reloaded");
        Assert.Equal((ushort)43, (ushort)again.Get("b")!, "latest value reloaded");
        Assert.True(!File.Exists(path + ".tmp"), "no temp file after concurrent flushes");
    }

    [Test]
    public static void Unsupported_types_are_reported_not_fatal()
    {
        var (p, _) = Start();
        using var __ = p;
        var rt = (LpzRuntime)p.Slot(1).Runtime!;
        Assert.Equal(1, rt.Unsupported.GetValueOrDefault(-48), "SIMPL+ counted as unsupported");
        Assert.True(p.RecentLog(1).Any(l => l.Message.Contains("SIMPL+")), "warning logged");
    }

    [Test]
    public static void Delay_fires_after_its_time()
    {
        var (p, panel) = Start();
        using var _ = p;
        var rt = (LpzRuntime)p.Slot(1).Runtime!;
        panel.ReceiveInput(D(4), true); panel.ReceiveInput(D(5), true);
        Assert.Eventually(() => rt.GetSignals().Any(s => s.Name == "#24" && s.Value is true), "delay output high after 50 ms");
    }

    [Test]
    public static void Analog_equate_honors_its_enable()
    {
        // Panel IPID 03: outputs d1 = enable, a1 = level; inputs (feedback) d1..d3.
        const int En = 10, Lvl = 11, Eq1 = 12, Eq2 = 13, NoEn = 14;
        var bin = new List<byte>();
        bin.AddRange(Record(-12, "IP-ID-03", [Eq1, Eq2, NoEn], [En, Lvl], Int(6, 3), Int(10, 3), Int(11, 1), Int(12, 3), Int(13, 2)));
        bin.AddRange(Record(51, "S-1", [Lvl, En], [Eq1, Eq2], Int(15, 2), Int(2, 1), Int(2, 2)));  // input 2 is the enable
        bin.AddRange(Record(51, "S-2", [Lvl], [NoEn], Int(15, 1), Int(2, 1)));                     // no enable: always on

        var path = Path.Combine(Paths.TempData(), "equate.lpz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=equate.bin\r\nSMWFileName=equate.smw\r\nRackType=CP4\r\n"));
            Put("equate.bin", bin.ToArray());
            Put("equate.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        }
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, path);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var panel = p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03 registered");
        bool Fb(uint j) => (bool)panel.GetFeedback(D(j));

        panel.ReceiveInput(A(1), (ushort)1);
        Assert.Eventually(() => Fb(3), "equate without an enable follows the analog");
        Thread.Sleep(30);
        Assert.True(!Fb(1) && !Fb(2), "enable low: all outputs low");

        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => Fb(1) && !Fb(2), "enable high: output for value 1");
        panel.ReceiveInput(A(1), (ushort)2);
        Assert.Eventually(() => !Fb(1) && Fb(2) && !Fb(3), "value 2");

        panel.ReceiveInput(D(1), false);
        Assert.Eventually(() => !Fb(1) && !Fb(2), "enable low again: all outputs drop");
        panel.ReceiveInput(A(1), (ushort)1);
        Thread.Sleep(30);
        Assert.True(!Fb(1) && !Fb(2) && Fb(3), "analog changes while disabled are ignored");
    }

    /// <summary>A one-panel (IPID 03) program from a list of records, written to its own .lpz.</summary>
    private static string BuildPanelProgram(string name, IEnumerable<byte[]> records)
    {
        var path = Path.Combine(Paths.TempData(), name + ".lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string entry, byte[] content) { using var s = zip.CreateEntry(entry).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes($"[LG_BOOT_DATA]\r\nBinFileName={name}.bin\r\nSMWFileName={name}.smw\r\nRackType=CP4\r\n"));
        Put(name + ".bin", records.SelectMany(r => r).ToArray());
        Put(name + ".dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        return path;
    }

    private static JoinDevice LoadPanel(Processor p, string path)
    {
        p.LoadProgram(1, path);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        return p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03 registered");
    }

    [Test]
    public static void Analog_initialize_with_one_trigger_per_value_sets_one_output()
    {
        // Panel IPID 03: outputs d1..d3 = triggers; inputs (feedback) a1, a2, s1.
        const int T1 = 10, T2 = 11, T3 = 12, Out = 13, AtStart = 14, Ser = 15;
        var path = BuildPanelProgram("initmulti",
        [
            Record(-12, "IP-ID-03", [Out, AtStart, Ser], [T1, T2, T3], Int(6, 3), Int(10, 0), Int(11, 3), Int(12, 2), Int(13, 3)),
            Record(38, "S-1", [T1, T2, T3], [Out], Int(15, 3), Int(2, 5), Int(2, 6), Int(2, 7)),
            // input 1 is the constant 1, so parameter 1 loads at start; input 2 then replaces it on a rising edge
            Record(38, "S-2", [1, T2], [AtStart], Int(15, 2), Int(2, 9), Int(2, 8)),
            // string parameters make it a serial initialize
            Record(38, "S-3", [T1, T3], [Ser], Int(15, 2), Str(3, "one"), Str(3, "three")),
        ]);
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        var panel = LoadPanel(p, path);
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(2)) == 9, "constant-1 trigger loads parameter 1 at start");
        Assert.Equal((ushort)0, (ushort)panel.GetFeedback(A(1)), "no trigger yet, output untouched");

        panel.ReceiveInput(D(2), true);
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(1)) == 6 && (ushort)panel.GetFeedback(A(2)) == 8, "trigger 2 sets 6 (and the other symbol's 8)");
        panel.ReceiveInput(D(2), false);
        panel.ReceiveInput(D(3), true);
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(1)) == 7, "trigger 3 sets 7");
        Assert.Equal("three", (string)panel.GetFeedback(S(1)), "serial form");
        panel.ReceiveInput(D(3), false);
        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(1)) == 5 && (string)panel.GetFeedback(S(1)) == "one", "trigger 1 sets 5 / one");
        panel.ReceiveInput(D(1), false);
        Thread.Sleep(30);
        Assert.Equal((ushort)5, (ushort)panel.GetFeedback(A(1)), "a falling edge does nothing");
    }

    [Test]
    public static void Analog_initialize_single_trigger_still_loads_every_output()
    {
        const int T = 10, O1 = 11, O2 = 12;
        var path = BuildPanelProgram("initsingle",
        [
            Record(-12, "IP-ID-03", [O1, O2], [T], Int(6, 3), Int(10, 0), Int(11, 1), Int(12, 2), Int(13, 1)),
            Record(38, "S-1", [T], [O1, O2], Int(15, 2), Int(2, 3), Int(2, 4)),
        ]);
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        var panel = LoadPanel(p, path);
        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(1)) == 3 && (ushort)panel.GetFeedback(A(2)) == 4, "outputs 1 and 2 = parameters 1 and 2");
    }

    [Test]
    public static void Make_string_permanent_restores_its_inputs_after_a_restart()
    {
        // Panel IPID 03: outputs s1, s2 (the strings the program changes); inputs (feedback) s1, s2.
        const int S1 = 10, S2 = 11;
        var path = BuildPanelProgram("permanent",
        [
            Record(-12, "IP-ID-03", [S1, S2], [S1, S2], Int(6, 3), Int(10, 0), Int(11, 0), Int(12, 0), Int(13, 0)),
            Record(205, "S-1", [S1, S2], [], Int(15, 1), Int(2, 5)),   // keeps at most 5 characters
        ]);
        var data = Paths.TempData();
        using (var p = new Processor(data))
        {
            p.AddLoader(new LpzLoader());
            var panel = LoadPanel(p, path);
            Thread.Sleep(30);
            Assert.Equal("", (string)panel.GetFeedback(S(1)), "nothing saved yet");
            panel.ReceiveInput(S(1), "hello world");
            panel.ReceiveInput(S(2), "ab");
            Assert.Eventually(() => (string)panel.GetFeedback(S(2)) == "ab", "value reached the panel");
            p.Slot(1).Unload();   // flushes the store
        }
        using (var p = new Processor(data))
        {
            p.AddLoader(new LpzLoader());
            var panel = LoadPanel(p, path);
            Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "hello" && (string)panel.GetFeedback(S(2)) == "ab",
                "both strings restored, the first cut to the parameter's length");
            panel.ReceiveInput(S(2), "changed");
            p.Slot(1).Unload();
        }
        using (var p = new Processor(data))
        {
            p.AddLoader(new LpzLoader());
            var panel = LoadPanel(p, path);
            Assert.Eventually(() => (string)panel.GetFeedback(S(2)) == "chang", "a later change is the one restored");
        }
        Assert.True(PrimitiveCatalog.Known[205].Implemented, "listed as running in the compatibility report");
    }

    /// <summary>The Alarm Clock v2.0 macro's shape: panel presses reach the logic through an input Buffer, while an
    /// Equipment Crosspoint (type 213) drives the same signals and nobody has to connect to it. Both routes must
    /// work, and the time-of-day relay (type 101) must carry the clock string to a crosspoint serial join.</summary>
    [Test]
    public static void Alarm_clock_macro_shape_buffer_route_and_crosspoint_route()
    {
        const int Press = 10, Buf = 11, Fb = 12, CtlPress = 13, CtlFb = 14, TodIn = 15, TodOut = 16, CtlTxt = 17,
                  CidA = 18, EidA = 19, Conn = 20;
        var bin = new List<byte>();
        // Panel feedback: d1 Fb, d2 CtlFb, s1 CtlTxt. Presses: d1 Press, d2 CtlPress, d3 Conn, a1 control ID, a2 equipment ID, s1 clock text.
        bin.AddRange(Record(-12, "IP-ID-03", [Fb, CtlFb, CtlTxt], [Press, CtlPress, Conn, CidA, EidA, TodIn],
            Int(6, 3), Int(10, 2), Int(11, 3), Int(12, 2), Int(13, 5)));
        bin.AddRange(Record(18, "S-1.1:S-10.1", [1, Press], [Buf]));                      // the macro's input buffer
        bin.AddRange(Record(10, "S-1.1:S-5", [Buf], [Fb, 0]));                            // stands in for the alarm logic
        bin.AddRange(Record(101, "S-1.1:S-1", [TodIn], [TodOut], Int(15, 1), Int(2, 1))); // time-of-day relay
        bin.AddRange(Record(213, "S-1.1:S-14", [Fb, TodOut], [Buf],                       // drives Buf too, like the macro's
            Int(15, 1), Int(2, 60001), Int(10, 1), Int(11, 1), Int(12, 1), Int(13, 1)));
        bin.AddRange(Record(214, "S-2", [CtlPress], [CtlFb, CtlTxt],
            Int(15, 1), Int(2, 1), Int(10, 1), Int(11, 1), Int(12, 1), Int(13, 1)));
        bin.AddRange(Record(215, "S-3", [CidA, EidA, Conn, 0, 0, 0, 0], [], Int(15, 0)));

        var path = Path.Combine(Paths.TempData(), "alarmshape.lpz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=alarmshape.bin\r\nSMWFileName=alarmshape.smw\r\nRackType=CP4\r\n"));
            Put("alarmshape.bin", bin.ToArray());
            Put("alarmshape.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        }
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, path);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var rt = (LpzRuntime)p.Slot(1).Runtime!;
        Assert.True(!rt.Unsupported.ContainsKey(101), "type 101 runs");
        Assert.True(!rt.Unsupported.ContainsKey(213) && !rt.Unsupported.ContainsKey(214) && !rt.Unsupported.ContainsKey(215), "crosspoints run");
        var panel = p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03 registered");
        bool Fbk(uint j) => (bool)panel.GetFeedback(D(j));

        // Route 1: the buffer. The crosspoint has no control connected and must neither block nor echo.
        panel.ReceiveInput(D(1), true); panel.ReceiveInput(D(1), false);
        Assert.Eventually(() => Fbk(1), "press through the buffer toggles the logic on");
        panel.ReceiveInput(D(1), true); panel.ReceiveInput(D(1), false);
        Assert.Eventually(() => !Fbk(1), "second press toggles it off");
        Assert.True(!Fbk(2) && (string)panel.GetFeedback(S(1)) == "", "unconnected control sees nothing");

        // The clock string passes through the relay; a control connected later is given it at once.
        panel.ReceiveInput(S(1), "12:34:56");
        Thread.Sleep(30);
        Assert.Equal("", (string)panel.GetFeedback(S(1)), "still nothing while disconnected");
        panel.ReceiveInput(A(1), (ushort)1); panel.ReceiveInput(A(2), (ushort)60001);
        panel.ReceiveInput(D(3), true); panel.ReceiveInput(D(3), false);
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "12:34:56", "connect delivers the relayed time string");
        panel.ReceiveInput(S(1), "12:34:57");
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "12:34:57", "relay follows the clock");

        // Route 2: a control crosspoint connected to equipment 60001 presses the same buffered signal.
        panel.ReceiveInput(D(2), true); panel.ReceiveInput(D(2), false);
        Assert.Eventually(() => Fbk(1) && Fbk(2), "press through the crosspoint toggles the logic and mirrors the feedback");
        panel.ReceiveInput(D(1), true); panel.ReceiveInput(D(1), false);
        Assert.Eventually(() => !Fbk(1) && !Fbk(2), "the buffer route still works while connected");
    }

    [Test]
    public static void Joins_tied_to_the_constant_1_reach_the_panel()
    {
        // SIMPL Windows lets you type 1 on a pin to hold a join high, e.g. smart object items that are always
        // visible. Panel IPID 03: digital feedback d1 = constant 1, d2 = constant 0, d3 = a real signal; smart
        // object 3 with item visibility d4011..d4012 tied to 1 and d4013 to a real signal.
        const int Real = 10, SoReal = 11;
        var so = new int[4013];
        so[4010] = 1; so[4011] = 1; so[4012] = SoReal;
        var path = BuildPanelProgram("constone",
        [
            Record(-12, "IP-ID-03", [1, 0, Real], [], Int(6, 3), Int(10, 3), Int(11, 0), Int(12, 3), Int(13, 0)),
            Record(268, "IP-ID-03.2", so, [], Int(10, 4013), Int(11, 0), Int(12, 4013), Int(13, 0), Int(44, 3)),
        ]);
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        var panel = LoadPanel(p, path);
        var snap = panel.FeedbackSnapshot();
        bool? At(JoinKey k) => snap.TryGetValue(k, out var v) ? v is true : null;
        Assert.Equal(true, At(D(1)), "plain join tied to 1 is sent high");
        Assert.Equal(false, At(D(3)), "plain join on a real signal is sent with its value");
        Assert.Equal(true, At(new JoinKey(SigKind.Digital, 4011, 3)), "smart object item visibility tied to 1 is sent high");
        Assert.Equal(true, At(new JoinKey(SigKind.Digital, 4012, 3)), "second item too");
        Assert.Equal(false, At(new JoinKey(SigKind.Digital, 4013, 3)), "item on a real signal is sent with its value");
    }

    [Test]
    public static void Logic_triggered_by_the_constant_1_fires_once_at_start()
    {
        // The 1 signal goes high at start-up, and symbols triggered by it see that rising edge once, as on a
        // processor: a Serial Send sends, a Toggle flips (once, not twice), an Analog Initialize loads.
        const int Sent = 10, Toggled = 11, Loaded = 12;
        var path = BuildPanelProgram("startedge",
        [
            Record(-12, "IP-ID-03", [Toggled, Loaded, Sent], [], Int(6, 3), Int(10, 1), Int(11, 0), Int(12, 2), Int(13, 0)),
            Record(58, "S-1", [1], [Sent], Int(15, 1), Str(3, "hello")),
            Record(10, "S-2", [1], [Toggled, 0]),
            Record(38, "S-3", [1], [Loaded], Int(15, 1), Int(2, 42)),
        ]);
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        var panel = LoadPanel(p, path);
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "hello", "Serial Send on 1 sent at start");
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(1)) == 42, "Analog Initialize on 1 loaded at start");
        Thread.Sleep(50);
        Assert.True((bool)panel.GetFeedback(D(1)), "Toggle on 1 flipped once at start");
    }

    [Test]
    public static void Multiple_serial_send_with_one_trigger_sets_every_output()
    {
        // Programs set every room, display and source name at start-up with one Multiple Serial Send on the
        // constant 1: one trigger, output k = parameter k.
        const int N1 = 10, N2 = 11, N3 = 12;
        var path = BuildPanelProgram("multisend",
        [
            Record(-12, "IP-ID-03", [N1, N2, N3], [], Int(6, 3), Int(10, 0), Int(11, 0), Int(12, 0), Int(13, 0)),
            Record(58, "S-1", [1], [N1, N2, N3], Int(15, 3), Str(3, "Room"), Str(3, "Local HDMI"), Str(3, "Cable 1")),
        ]);
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        var panel = LoadPanel(p, path);
        Assert.Eventually(() => (string)panel.GetFeedback(S(3)) == "Cable 1", "last output set");
        Assert.Equal("Room", (string)panel.GetFeedback(S(1)), "first output");
        Assert.Equal("Local HDMI", (string)panel.GetFeedback(S(2)), "second output");
    }
}
