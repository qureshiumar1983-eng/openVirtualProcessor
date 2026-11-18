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

    private static string BuildProgram()
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: splus.smw")));
        // Panel IPID 04: feedback d1, a1, s1..s2 ; presses d1..d2, a1, s1
        bin.AddRange(Record(-12, "IP-ID-04", [Busy, Doubled, Greeting, Ticks], [Press, Start, Level, Name],
            Int(6, 4), Int(10, 1), Int(11, 2), Int(12, 2), Int(13, 3)));
        // SIMPL+ module: 2 digital + 2 analog/serial inputs, 1 digital + 3 analog/serial outputs, STEP = 3
        bin.AddRange(Record(-48, "S-1", [Press, Start, Level, Name], [Busy, Doubled, Greeting, Ticks],
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
}
