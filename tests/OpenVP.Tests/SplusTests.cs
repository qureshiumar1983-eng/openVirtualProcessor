using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>Runs a SIMPL+ module (samples/SplusModule, written like SIMPL+ compiler output) inside a compiled program.</summary>
public static class SplusTests
{
    const int Press = 10, Start = 11, Level = 12, Name = 13, Busy = 20, Doubled = 21, Greeting = 22, Ticks = 23;

    private static string ModuleDll =>
        Directory.GetFiles(Path.Combine(Paths.RepoRoot, "samples", "SplusModule", "bin"), "Sample_Splus.dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).First();

    /// <param name="startTiedTo1">Tie the module's START input to the constant 1 (a 1 typed on the pin in SIMPL Windows).</param>
    private static string BuildProgram(bool startTiedTo1 = false)
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: splus.smw")));
        // Panel IPID 04: feedback d1, a1, s1..s2 ; presses d1..d2, a1, s1
        bin.AddRange(Record(-12, "IP-ID-04", [Busy, Doubled, Greeting, Ticks], [Press, Start, Level, Name],
            Int(6, 4), Int(10, 1), Int(11, 2), Int(12, 2), Int(13, 3)));
        // SIMPL+ module: 2 digital + 2 analog/serial inputs, 1 digital + 3 analog/serial outputs, STEP = 3
        bin.AddRange(Record(-48, "S-1", [Press, startTiedTo1 ? 1 : Start, Level, Name], [Busy, Doubled, Greeting, Ticks],
            Int(10, 2), Int(12, 4), Int(11, 1), Int(13, 4),
            Str(3, "Sample Module"), Str(3, "Sample_Splus.dll"), Int(2, 3)));

        var path = Path.Combine(Paths.TempData(), "splus.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=splus.bin\r\nSMWFileName=splus.smw\r\nRackType=CP4\r\n"));
        Put("splus.bin", bin.ToArray());
        Put("splus.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=04\r\naddr0=127.0.0.1\r\n"));
        Put("Sample_Splus.dll", File.ReadAllBytes(ModuleDll));
        return path;
    }

    static JoinKey D(uint j) => new(SigKind.Digital, j);
    static JoinKey A(uint j) => new(SigKind.Analog, j);
    static JoinKey S(uint j) => new(SigKind.Serial, j);

    [Test]
    public static void Splus_module_runs_inside_compiled_program()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, BuildProgram());
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var rt = (LpzRuntime)p.Slot(1).Runtime!;
        Assert.Equal(0, rt.Unsupported.GetValueOrDefault(-48), "module loaded: " + string.Join(" | ", p.RecentLog(1).Select(l => l.Message)));
        var panel = p.Slot(1).FindDevice(4)!;

        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "ready", "Function Main ran after initialization");

        panel.ReceiveInput(A(1), 21);
        Assert.Eventually(() => (ushort)panel.GetFeedback(A(1)) == 42, "CHANGE on analog input drives analog output");

        panel.ReceiveInput(S(1), "World");
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "Hello, World!", "MakeString into string output");

        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(1)), "PUSH sets output");
        Assert.Eventually(() => !(bool)panel.GetFeedback(D(1)), "Delay(5) then output clears");

        panel.ReceiveInput(D(2), true);
        Assert.Eventually(() => (string)panel.GetFeedback(S(2)) == "9", "WAIT loop ticks using the STEP parameter (3 per tick)");
        panel.ReceiveInput(D(2), false);
    }

    [Test]
    public static void Splus_module_keeps_working_while_stepping()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, BuildProgram());
        var panel = p.Slot(1).FindDevice(4)!;
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "ready", "started");
        var st = p.Slot(1).Stepper;
        st.Configure(enabled: true, onlyBreakSignals: false);

        var seen = new List<string>();
        void StepUntil(Func<bool> done, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done())
            {
                if (DateTime.UtcNow > deadline) throw new Exception($"{what}; steps seen: {string.Join(", ", seen)}");
                if (st.Paused is { } ps) { seen.Add($"{ps.Signal}={ps.Value}"); st.Next(); }
                Thread.Sleep(20);
            }
        }

        panel.ReceiveInput(A(1), 21);
        StepUntil(() => panel.GetFeedback(A(1)) is ushort u && u == 42, "analog CHANGE reaches output while stepping");

        panel.ReceiveInput(S(1), "World");
        StepUntil(() => (string)panel.GetFeedback(S(1)) == "Hello, World!", "string output while stepping");

        panel.ReceiveInput(D(1), true);
        StepUntil(() => (bool)panel.GetFeedback(D(1)), "PUSH sets output while stepping");
        StepUntil(() => !(bool)panel.GetFeedback(D(1)), "Delay then output clears while stepping");
        panel.ReceiveInput(D(1), false);
        panel.ReceiveInput(D(2), true);
        StepUntil(() => (string)panel.GetFeedback(S(2)) == "9", "WAIT loop while stepping");
        st.Run();
    }

    private static int _timerHits;
    private static void TimerHit(object _) => Interlocked.Increment(ref _timerHits);

    [Test]
    public static void Simplsharp_callbacks_run_while_step_debugger_holds_the_logic()
    {
        // SIMPL+ modules lean on SIMPL# libraries (CTimer, sockets). Their callbacks must not queue behind
        // the logic thread, which the step debugger holds at every pause.
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, BuildProgram());
        var panel = p.Slot(1).FindDevice(4)!;
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "ready", "started");

        var st = p.Slot(1).Stepper;
        st.Configure(enabled: true, onlyBreakSignals: false);
        panel.ReceiveInput(A(1), 21);
        Assert.Eventually(() => st.IsPaused, "logic paused by the step debugger");

        // The program's own copy of the SIMPL# API, as a module's library would use it.
        var alc = System.Runtime.Loader.AssemblyLoadContext.All.First(c => c.Name == "openvp-lpz-slot01");
        var shim = alc.Assemblies.First(a => a.GetName().Name == "OpenVP.SimplSharp");
        var timerType = shim.GetType("Crestron.SimplSharp.CTimer", throwOnError: true)!;
        var cbType = shim.GetType("Crestron.SimplSharp.CTimerCallbackFunction", throwOnError: true)!;
        var cb = Delegate.CreateDelegate(cbType, typeof(SplusTests).GetMethod(nameof(TimerHit),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!);
        _timerHits = 0;
        using var timer = (IDisposable)Activator.CreateInstance(timerType, cb, 10L)!;

        Assert.Eventually(() => Volatile.Read(ref _timerHits) == 1, "CTimer callback ran while the logic was paused");
        Assert.True(st.IsPaused, "logic still paused");
        st.Run();
        Assert.Eventually(() => panel.GetFeedback(A(1)) is ushort u && u == 42, "logic carried on after Run");
    }

    // ---------------- start-up ordering, with a module driven directly (no program, no host)

    /// <summary>A module shaped like compiler output: LEVEL (analog 0), PRESS and PULSE (digital 0, 1); Main runs <see cref="MainBody"/>.</summary>
    private sealed class OrderModule : Crestron.Logos.SplusObjects.SplusObject
    {
        public readonly List<string> Events = new();
        public Action<OrderModule>? MainBody;
        public readonly Crestron.Logos.SplusObjects.AnalogInput Level;
        public readonly Crestron.Logos.SplusObjects.DigitalInput PressIn, PulseIn;

        public OrderModule() : base("order", "order", Crestron.Logos.SplusObjects.CrestronStringEncoding.eEncodingASCII)
        {
            Level = new(0, this); m_AnalogInputList.Add(0, Level);
            PressIn = new(0, this); m_DigitalInputList.Add(0, PressIn);
            PulseIn = new(1, this); m_DigitalInputList.Add(1, PulseIn);
            Level.OnAnalogChange.Add(new(e => Handler(() => $"level {Level.UshortValue}", e), false));
            PressIn.OnDigitalPush.Add(new(e => Handler(() => "press push", e), false));
            PulseIn.OnDigitalPush.Add(new(e => Handler(() => "pulse push", e), false));
            PulseIn.OnDigitalRelease.Add(new(e => Handler(() => "pulse release", e), false));
        }

        public void Log(string text) { lock (Events) Events.Add(text); }
        public string[] Seen() { lock (Events) return Events.ToArray(); }

        private object Handler(Func<string> text, object eventInfo)
        {
            var args = (Crestron.Logos.SplusObjects.SignalEventArgs)eventInfo;
            try { SplusThreadStartCode(args); Log(text()); }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(args); }
            return this;
        }

        public override object FunctionMain(object obj)
        {
            try { SplusFunctionMainStartCode(); MainBody?.Invoke(this); }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(); }
            return obj;
        }
    }

    [Test]
    public static void Splus_input_changes_before_Main_yields_are_coalesced_and_delivered_after()
    {
        var m = new OrderModule();
        var started = new ManualResetEventSlim();
        var go = new ManualResetEventSlim();
        m.MainBody = o =>
        {
            started.Set();
            go.Wait();   // Main busy, holding the module: no yield
            o.Log($"main level={o.Level.UshortValue} press={o.PressIn.Value} pulse={o.PulseIn.Value}");
            Crestron.Logos.SplusLibrary.Functions.Delay(1);
            o.Log("main done");
        };
        var host = (ISplusModule)m;
        try
        {
            host.Initialize();
            host.Preset(SigKind.Analog, 0, (ushort)3);
            host.StartMain();
            Assert.True(started.Wait(3000), "Main started");

            host.Input(SigKind.Analog, 0, (ushort)5);
            host.Input(SigKind.Analog, 0, (ushort)7);
            host.Input(SigKind.Digital, 0, true);
            host.Input(SigKind.Digital, 1, true);
            host.Input(SigKind.Digital, 1, false);  // pulsed and back to what Main started with
            Thread.Sleep(300);
            Assert.Equal(0, m.Seen().Length, "no handler runs while Main is busy");

            go.Set();
            Assert.Eventually(() => m.Seen().Length == 4, "queued events delivered once Main is through");
            var seen = m.Seen();
            Assert.Equal("main level=7 press=1 pulse=0", seen[0], "Main reads the current input values");
            Assert.Equal("main done", seen[1], "Main finishes before any handler");
            Assert.Equal("level 7|press push", string.Join("|", seen.Skip(2).OrderBy(x => x)), "one event per changed input, latest value; the pulse that ended where it began is not an event");

            host.Input(SigKind.Analog, 0, (ushort)9);
            Assert.Eventually(() => m.Seen().Contains("level 9"), "later changes are delivered straight away");
            Assert.Equal(5, m.Seen().Length, "and nothing else fired");
        }
        finally { host.Stop(); }
    }

    [Test]
    public static void Splus_handlers_wait_for_Main_to_resume_after_WaitForInitializationComplete()
    {
        // Main parks in WaitForInitializationComplete until the other module has started. An input that changes
        // meanwhile must not run before the code after that call, which the module relies on (globals set up there).
        for (int round = 0; round < 5; round++)
        {
            var a = new OrderModule();
            var b = new OrderModule();
            var inWait = new ManualResetEventSlim();
            var bGo = new ManualResetEventSlim();
            a.MainBody = o =>
            {
                inWait.Set();
                o.WaitForInitializationComplete();
                o.Log("after wfic");
                Thread.Sleep(100);   // still in Main, no yield
                o.Log("main done");
            };
            b.MainBody = _ => bGo.Wait();
            var ha = (ISplusModule)a;
            var hb = (ISplusModule)b;
            try
            {
                ha.Initialize(); hb.Initialize();
                ha.StartMain(); hb.StartMain();
                Assert.True(inWait.Wait(3000), "Main reached WaitForInitializationComplete");
                Thread.Sleep(50);
                ha.Input(SigKind.Analog, 0, (ushort)5);
                Thread.Sleep(100);
                Assert.Equal(0, a.Seen().Length, "nothing ran while Main waits for the other modules");

                bGo.Set();   // the other module's Main returns: everyone has started
                Assert.Eventually(() => a.Seen().Length == 3, "Main and the queued event finished, no deadlock");
                Assert.Equal("after wfic|main done|level 5", string.Join("|", a.Seen()), "Main's code after the wait runs before the event");
            }
            finally { ha.Stop(); hb.Stop(); }
        }
    }

    [Test]
    public static void Splus_input_tied_to_1_gets_its_push_at_start()
    {
        // On a processor a SIMPL+ input held high by a typed 1 gets its PUSH at start-up. START's PUSH runs a
        // WAIT loop that ticks onto s2, so ticks appearing with nobody pressing anything shows it ran.
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, BuildProgram(startTiedTo1: true));
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var panel = p.Slot(1).FindDevice(4)!;
        Assert.Eventually(() => (string)panel.GetFeedback(S(1)) == "ready", "Function Main ran");
        Assert.Eventually(() => (string)panel.GetFeedback(S(2)) is { Length: > 0 }, "PUSH on the input tied to 1 ran at start");
    }
}
