using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>The processor's System Control symbol: its program-start feedback output goes high when the program starts,
/// which is how a program kicks off its own start-up chain (one shot, delay, then whatever the delay releases).</summary>
public static class SystemControlTests
{
    const int StartFb = 20, Pulse = 21, Delayed = 22, Late = 23;

    private static string BuildCompiled(bool withSystemControl = true, int cardSlot = 8)
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: sysctl.smw")));
        // The System Monitor card (top-level hardware, record index 1) and its fixed slot 1, System Control (index 2).
        bin.AddRange(Record(-12, $"Slot-{cardSlot:00}", [], [], Int(6, cardSlot), Int(10, 0), Int(11, 0), Int(12, 0), Int(13, 0), Int(7, 0)));
        bin.AddRange(Record(-12, "Slot-01", [], withSystemControl ? [StartFb] : [], Int(6, 1), Int(10, 0), Int(11, withSystemControl ? 1 : 0), Int(12, 0), Int(13, withSystemControl ? 1 : 0), Int(7, 1)));
        bin.AddRange(Record(15, "S-1", [StartFb, 0, 0], [Pulse, 0], Int(15, 1), Int(2, 10)));   // One Shot, 100 ms
        bin.AddRange(Record(27, "S-2", [Pulse], [Delayed], Int(15, 1), Int(2, 5)));              // Delay 50 ms
        bin.AddRange(Record(15, "S-3", [Delayed, 0, 0], [Late, 0], Int(15, 1), Int(2, 100)));    // One Shot after the delay
        var path = Path.Combine(Paths.TempData(), $"sysctl{(withSystemControl ? "" : "_none")}{cardSlot}.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=sysctl.bin\r\nSMWFileName=sysctl.smw\r\nRackType=RMC4\r\n"));
        Put("sysctl.bin", bin.ToArray());
        return path;
    }

    private static bool High(IProgramRuntime rt, int sig) => rt.GetSignals().Any(s => s.Name == $"#{sig}" && s.Value is true);

    [Test]
    public static void Compiled_system_control_goes_high_at_start_and_starts_the_chain()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, BuildCompiled());
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var rt = slot.Runtime!;
        Assert.Eventually(() => High(rt, StartFb), "program-start feedback high");
        Assert.Eventually(() => High(rt, Pulse), "one shot fired from the feedback");
        Assert.Eventually(() => High(rt, Delayed), "delay released after its time");
        Assert.Eventually(() => High(rt, Late), "what the delay releases ran");
        Thread.Sleep(200);
        Assert.True(High(rt, StartFb), "held high while the program runs");
        Assert.True(!p.Unsupported.Items.Any(i => i.Name.Contains("System Control")), "not reported as unsupported");
    }

    [Test]
    public static void Compiled_hardware_that_is_not_system_control_is_left_alone()
    {
        // Same shape under another card slot: not the System Monitor, so nothing drives the signal.
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, BuildCompiled(cardSlot: 11));
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        Thread.Sleep(300);
        Assert.True(!High(slot.Runtime!, StartFb), "no feedback from another card's slot");
    }

    [Test]
    public static void Smw_system_control_goes_high_at_start_and_starts_the_chain()
    {
        const string text = "[\nObjTp=Hd\nPgmNm=SysCtl\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
            "[\nObjTp=Sg\nH=10\nNm=START.FB\n]\n[\nObjTp=Sg\nH=11\nNm=PULSE\n]\n[\nObjTp=Sg\nH=12\nNm=RELEASED\n]\n" +
            "[\nObjTp=Dv\nH=539\nPrH=538\nNm=System Control\nDvC=6097\nAd=01\nSmH=14\n]\n" +
            "[\nObjTp=Sm\nH=14\nSmC=6543\nNm=RMC4 System Control\nDvH=539\nPrH=13\nn1I=50\nn2I=2\nn1O=50\nmI=55\nmO=52\nO1=10\n]\n" +
            "[\nObjTp=Sm\nH=20\nSmC=9\nNm=One Shot\nmI=3\nmO=2\nI1=10\nO1=11\nmP=1\nP1=10t\n]\n" +
            "[\nObjTp=Sm\nH=21\nSmC=16\nNm=Delay\nmI=1\nmO=1\nI1=11\nO1=12\nmP=1\nP1=5t\n]\n";
        var path = Path.Combine(Paths.TempData(), "sysctl_src.smw");
        File.WriteAllText(path, text);
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, path);
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var rt = slot.Runtime!;
        Assert.Eventually(() => rt.GetSignals().Any(s => s.Value is true && s.Name == "START.FB"), "program-start feedback high");
        Assert.Eventually(() => rt.GetSignals().Any(s => s.Value is true && s.Name == "RELEASED"), "one shot then delay released");
        Assert.True(!p.Unsupported.Items.Any(i => i.Name.Contains("System Control")), "not reported as unsupported");
    }

    [Test]
    public static void System_control_rows_from_older_builds_count_as_supported()
    {
        Assert.True(SupportedSymbols.IsSupported("SIMPL hardware", "RMC4 System Control (SmC 6543)"), "hardware row");
        Assert.True(SupportedSymbols.IsSupported("SIMPL symbol", "CP3 System Control (SmC 1)"), "symbol row");
        Assert.True(!SupportedSymbols.IsSupported("SIMPL hardware", "RMC4 System Information (SmC 6544)"), "System Information is not covered");
    }
}

/// <summary>Pin order of the Set/Reset Latch: the .smw symbol has set first, the compiled record (type 9) reset first.
/// Both orders were read off a real program's latches, source against compiled.</summary>
public static class LatchPinOrderTests
{
    const int A = 30, B = 31, Out = 32;

    private static bool High(IProgramRuntime rt, string name) => rt.GetSignals().Any(s => s.Name == name && s.Value is true);

    [Test]
    public static void Compiled_latch_takes_reset_first_then_set()
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: latch.smw")));
        bin.AddRange(Record(9, "S-1", [A, B], [Out, 0])); // pin 1 = reset (A), pin 2 = set (B)
        var path = Path.Combine(Paths.TempData(), "latch.lpz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=latch.bin\r\nSMWFileName=latch.smw\r\nRackType=CP4\r\n"));
            Put("latch.bin", bin.ToArray());
        }
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, path);
        var rt = p.Slot(1).Runtime!;
        Assert.True(rt.TryPulseSignal("#" + A), "pulse reset pin");
        Thread.Sleep(100);
        Assert.True(!High(rt, "#" + Out), "the first pin does not set");
        Assert.True(rt.TryPulseSignal("#" + B), "pulse set pin");
        Assert.Eventually(() => High(rt, "#" + Out), "the second pin sets");
        Assert.True(rt.TrySetSignal("#" + A, true), "reset held");
        Assert.Eventually(() => !High(rt, "#" + Out), "the first pin resets");
        Assert.True(rt.TryPulseSignal("#" + B), "set while reset is high");
        Thread.Sleep(100);
        Assert.True(!High(rt, "#" + Out), "reset wins when both are high");
    }

    [Test]
    public static void Smw_latch_takes_set_first_then_reset()
    {
        const string text = "[\nObjTp=Hd\nPgmNm=Latch\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
            "[\nObjTp=Sg\nH=10\nNm=SET\n]\n[\nObjTp=Sg\nH=11\nNm=RESET\n]\n[\nObjTp=Sg\nH=12\nNm=OUT\n]\n" +
            "[\nObjTp=Sm\nH=20\nSmC=12\nNm=Set/Reset Latch\nmI=2\nmO=2\nI1=10\nI2=11\nO1=12\n]\n";
        var path = Path.Combine(Paths.TempData(), "latch_src.smw");
        File.WriteAllText(path, text);
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, path);
        var rt = p.Slot(1).Runtime!;
        Assert.True(rt.TryPulseSignal("SET"), "pulse set");
        Assert.Eventually(() => High(rt, "OUT"), "pin 1 sets");
        Assert.True(rt.TryPulseSignal("RESET"), "pulse reset");
        Assert.Eventually(() => !High(rt, "OUT"), "pin 2 resets");
    }
}

/// <summary>The .smw lists Toggle, Interlock and Multiple One Shots pins differently from the compiled records
/// (read off a real program's source and its compiled records): the .smw engine maps them.</summary>
public static class SmwPinOrderTests
{
    private static string Sg(int h, string name) => $"[\nObjTp=Sg\nH={h}\nNm={name}\n]\n";

    private static IProgramRuntime Run(string name, string symbols, params (int, string)[] signals)
    {
        var text = "[\nObjTp=Hd\nPgmNm=Pins\n]\n" + Sg(1, "0") + Sg(2, "1") + string.Concat(signals.Select(s => Sg(s.Item1, s.Item2))) + symbols;
        var path = Path.Combine(Paths.TempData(), name + ".smw");
        File.WriteAllText(path, text);
        var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, path);
        _keep.Add(p);
        return p.Slot(1).Runtime!;
    }
    private static readonly List<Processor> _keep = new();

    private static bool High(IProgramRuntime rt, string name) => rt.GetSignals().Any(s => s.Name == name && s.Value is true);

    [Test]
    public static void Smw_toggle_lists_set_reset_clock()
    {
        var rt = Run("pins_toggle", "[\nObjTp=Sm\nH=20\nSmC=13\nNm=Toggle\nmI=3\nmO=2\nI1=10\nI2=11\nI3=12\nO1=13\n]\n",
            (10, "SET"), (11, "RESET"), (12, "CLOCK"), (13, "OUT"));
        Assert.True(rt.TryPulseSignal("CLOCK"), "clock");
        Assert.Eventually(() => High(rt, "OUT"), "pin 3 is the clock");
        Assert.True(rt.TryPulseSignal("RESET"), "reset");
        Assert.Eventually(() => !High(rt, "OUT"), "pin 2 resets");
        Assert.True(rt.TryPulseSignal("SET"), "set");
        Assert.Eventually(() => High(rt, "OUT"), "pin 1 sets");
    }

    [Test]
    public static void Smw_interlock_lists_clear_spare_then_inputs()
    {
        // Two outputs, so four pins: clear, a spare, then the two inputs (as in a real .smw).
        var rt = Run("pins_interlock", "[\nObjTp=Sm\nH=20\nSmC=19\nNm=Interlock\nmI=4\nmO=2\nI1=10\nI3=11\nI4=12\nO1=13\nO2=14\n]\n",
            (10, "CLEAR"), (11, "IN1"), (12, "IN2"), (13, "OUT1"), (14, "OUT2"));
        Assert.True(rt.TryPulseSignal("IN2"), "second input");
        Assert.Eventually(() => High(rt, "OUT2") && !High(rt, "OUT1"), "pin 4 selects output 2");
        Assert.True(rt.TryPulseSignal("IN1"), "first input");
        Assert.Eventually(() => High(rt, "OUT1") && !High(rt, "OUT2"), "pin 3 selects output 1");
        Assert.True(rt.TryPulseSignal("CLEAR"), "clear");
        Assert.Eventually(() => !High(rt, "OUT1") && !High(rt, "OUT2"), "pin 1 clears");
    }

    [Test]
    public static void Smw_multiple_one_shots_lists_a_spare_pin_first()
    {
        var rt = Run("pins_mos", "[\nObjTp=Sm\nH=20\nSmC=21\nNm=Multiple One Shots\nmI=3\nmO=2\nI2=10\nI3=11\nO1=12\nO2=13\nmP=1\nP1=50t\n]\n",
            (10, "T1"), (11, "T2"), (12, "O1"), (13, "O2"));
        Assert.True(rt.TrySetSignal("T2", true), "second trigger");
        Assert.Eventually(() => High(rt, "O2") && !High(rt, "O1"), "pin 3 drives output 2");
        Assert.True(rt.TrySetSignal("T1", true), "first trigger");
        Assert.Eventually(() => High(rt, "O1"), "pin 2 drives output 1");
    }
}

/// <summary>A Delay symbol can have several outputs, each with its own time.</summary>
public static class MultiDelayTests
{
    [Test]
    public static void Delay_outputs_each_use_their_own_time()
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: delay.smw")));
        bin.AddRange(Record(27, "S-1", [30], [31, 32, 33], Int(15, 3), Int(2, 5), Int(2, 40), Int(2, 20)));   // 50 ms, 400 ms, 200 ms
        bin.AddRange(Record(15, "S-2", [34, 0, 0], [30, 0], Int(15, 1), Int(2, 20)));                          // trigger 34: 200 ms pulse into the delay
        var path = Path.Combine(Paths.TempData(), "multidelay.lpz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=delay.bin\r\nSMWFileName=delay.smw\r\nRackType=CP4\r\n"));
            Put("delay.bin", bin.ToArray());
        }
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, path);
        var rt = p.Slot(1).Runtime!;
        bool High(int sig) => rt.GetSignals().Any(s => s.Name == $"#{sig}" && s.Value is true);
        Assert.True(rt.TryPulseSignal("#34"), "trigger");
        Assert.Eventually(() => High(31), "first output after its own 50 ms");
        Assert.True(!High(32), "second output waits for its 400 ms");
        Thread.Sleep(450);
        // The 200 ms pulse ended at the instant the third output's 200 ms ran out: that output still fires (one logic tick of slack);
        // the second one (400 ms) never does.
        Assert.True(!High(32), "the pulse ended before the second output's time");
        Assert.True(!High(31), "outputs go low when the input drops");
    }
}
