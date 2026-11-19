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
}
