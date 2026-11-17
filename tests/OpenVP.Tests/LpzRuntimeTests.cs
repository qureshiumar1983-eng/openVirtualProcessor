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

    private static string BuildProgram()
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
        bin.AddRange(Record(27, "S-7", [BothFb], [DelayOut], Int(15, 1), Int(2, 5)));  // Delay 50 ms
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
        var panel = p.Slot(1).FindDevice(3)!;
        Assert.True(panel != null, "panel 03 registered from the compiled device record");
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
}
