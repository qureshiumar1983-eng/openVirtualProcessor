using OpenVP.Core;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

public static class SimplTests
{
    private static string SampleSmw => Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw");

    private static Processor NewProcessor()
    {
        var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        p.AddLoader(new SimplLoader());
        return p;
    }

    private static readonly JoinKey D1 = new(SigKind.Digital, 1);

    [Test]
    public static void Parser_reads_symbols_signals_and_groups()
    {
        var f = SmwFile.Load(SampleSmw);
        Assert.Equal("OpenVP SIMPL sample", f.ProgramName, "program name");
        var panel = f.Symbols[21];
        Assert.Equal(13, panel.Inputs.Count, "panel input pins");
        Assert.Equal(10, panel.InputGroups[0], "digital inputs");
        Assert.Equal(2, panel.InputGroups[2], "serial inputs");
        Assert.Equal(16, panel.Outputs[10], "analog out pin 11 -> volume_level");
        Assert.Equal("volume_level", f.SignalName(16), "signal name");
    }

    [Test]
    public static void Parser_fills_serial_group_from_totals()
    {
        // The real-file case: only n1I/n2I listed, mI=96 -> the remaining 32 are serial.
        var f = SmwFile.Parse("[\nObjTp=Sm\nH=5\nSmC=6840\nNm=TSW-1070\nDvH=1\nn1I=32\nn2I=32\nn1O=32\nmI=96\nmO=64\n]\n");
        Assert.Equal(32, f.Symbols[5].InputGroups[2], "serial inputs inferred");
        Assert.Equal(32, f.Symbols[5].OutputGroups[1], "analog outputs inferred");
    }

    [Test]
    public static void Parameters_parse_crestron_units()
    {
        Assert.True(SmwFile.TryParseNumber("255d", out var a) && a == 255, "255d");
        Assert.True(SmwFile.TryParseNumber("1Fh", out var b) && b == 31, "1Fh");
        Assert.True(SmwFile.TryParseNumber("1.5s", out var c) && c == 1500, "1.5s");
        Assert.True(SmwFile.TryParseNumber("25t", out var d) && d == 250, "25t");
        Assert.True(SmwFile.TryParseNumber("100%", out var e) && e == 65535, "100%");
    }

    [Test]
    public static void Smw_program_runs_logic_from_panel_presses()
    {
        using var p = NewProcessor();
        p.LoadProgram(2, SampleSmw);
        var slot = p.Slot(2);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(0x03) ?? throw new AssertException("panel IPID 03 from sidecar");

        // startup pulse from the "1" signal
        Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 2)) == "SIMPL program running", "start pulse ran Serial Send");

        // Toggle
        panel.ReceiveInput(D1, true);
        Assert.Eventually(() => (bool)panel.GetFeedback(D1), "toggle on");
        panel.ReceiveInput(D1, false);
        panel.ReceiveInput(D1, true);
        Assert.Eventually(() => !(bool)panel.GetFeedback(D1), "toggle off");

        // Interlock
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 3), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 3)), "source 2 selected");
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 4), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 4)) &&
                                !(bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 3)), "source 3 replaces source 2");

        // AND
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 6), true);
        Thread.Sleep(50);
        Assert.True(!(bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 6)), "AND low with one input");
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 7), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 6)), "AND high with both");

        // Analog loop-back through one signal, serial send on press
        panel.ReceiveInput(new JoinKey(SigKind.Analog, 1), 40000);
        Assert.Eventually(() => (ushort)panel.GetFeedback(new JoinKey(SigKind.Analog, 1)) == 40000, "analog echoed");
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 5), true);
        Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 1)) == "Hello from SIMPL", "serial send");
    }

    [Test]
    public static void Analog_initialize_with_one_trigger_per_value_sets_one_output()
    {
        // Panel 03: presses d1, d2 (handles 10, 11), feedback a1 (12) and a2 (13). Symbol 20 has a trigger per
        // value and one output; symbol 21 has its first trigger on the constant "1" and one on d2.
        const string text = "[\nObjTp=Hd\nPgmNm=InitMulti\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
            "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I=0\nn2I=2\nn1O=2\nmI=2\nmO=2\nI1=12\nI2=13\nO1=10\nO2=11\n]\n" +
            "[\nObjTp=Sm\nH=20\nSmC=32\nNm=Analog Initialize\nmI=2\nmO=1\nI1=10\nI2=11\nO1=12\nmP=2\nP1=5\nP2=6\n]\n" +
            "[\nObjTp=Sm\nH=21\nSmC=32\nNm=Analog Initialize\nmI=2\nmO=1\nI1=2\nI2=11\nO1=13\nmP=2\nP1=9\nP2=8\n]\n";
        var path = Path.Combine(Paths.TempData(), "initmulti.smw");
        File.WriteAllText(path, text);
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        using var p = NewProcessor();
        p.LoadProgram(1, path);
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel IPID 03");
        var a1 = new JoinKey(SigKind.Analog, 1);
        var a2 = new JoinKey(SigKind.Analog, 2);
        Assert.Eventually(() => (ushort)panel.GetFeedback(a2) == 9, "trigger on the constant 1 loads parameter 1 at start");
        Assert.Equal((ushort)0, (ushort)panel.GetFeedback(a1), "no trigger yet");
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 2), true);
        Assert.Eventually(() => (ushort)panel.GetFeedback(a1) == 6 && (ushort)panel.GetFeedback(a2) == 8, "trigger 2 loads parameter 2");
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 2), false);
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 1), true);
        Assert.Eventually(() => (ushort)panel.GetFeedback(a1) == 5, "trigger 1 loads parameter 1");
    }

    [Test]
    public static void Debugger_can_watch_and_drive_named_signals()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, SampleSmw);
        var slot = p.Slot(1);
        var traced = new List<TraceEvent>();
        slot.Traced += e => { lock (traced) traced.Add(e); };
        Assert.True(slot.Runtime!.TrySetSignal("source1_press", true), "drive by name");
        Assert.Eventually(() => slot.Runtime.GetSignals().Any(s => s.Name == "source1_fb" && s.Value is true), "source1_fb high");
        lock (traced) Assert.True(traced.Any(t => t.Signal == "source1_fb" && t.Slot == 1), "trace event for slot 1");
    }

    [Test]
    public static void Simpl_windows_and_simpl_sharp_slots_talk_over_eisc()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, Paths.SampleDll);   // SIMPL#: panel d10 -> EISC 10 d1; EISC d1 -> panel fb d10
        p.LoadProgram(2, SampleSmw);         // SIMPL:  panel d10 -> EISC 10 d1; EISC d1 -> panel fb d10
        var sharpPanel = p.Slot(1).FindDevice(0x03)!;
        var simplPanel = p.Slot(2).FindDevice(0x03)!;

        sharpPanel.ReceiveInput(new JoinKey(SigKind.Digital, 10), true);
        Assert.Eventually(() => (bool)simplPanel.GetFeedback(new JoinKey(SigKind.Digital, 10)), "SIMPL# press shows on SIMPL panel");

        simplPanel.ReceiveInput(new JoinKey(SigKind.Digital, 10), true);
        sharpPanel.ReceiveInput(new JoinKey(SigKind.Digital, 10), false);
        Assert.Eventually(() => (bool)sharpPanel.GetFeedback(new JoinKey(SigKind.Digital, 10)), "SIMPL press shows on SIMPL# panel");
    }

    /// <summary>
    /// Real files describe the hardware as a tree of Dv blocks: processor -> EthernetCard -> Ethernet Devices ->
    /// slot (Ad = IP ID) -> device -> its own slots (Ad = slot number in the device). Only the device on the
    /// Ethernet slot has an IP ID; its slots and ports, and the Cresnet card, must not show up in the IP table.
    /// </summary>
    [Test]
    public static void Smw_hardware_tree_gives_only_ethernet_devices_an_ipid()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("[\nObjTp=Hd\nPgmNm=Tree\n]\n");
        void Dv(int h, int parent, string name, string ad, int sm = 0) =>
            sb.Append($"[\nObjTp=Dv\nH={h}\nPrH={parent}\nNm={name}\nAd={ad}\n" + (sm > 0 ? $"SmH={sm}\n" : "") + "]\n");
        void Sym(int h, string name, int dv, int pin) =>
            sb.Append($"[\nObjTp=Sm\nH={h}\nSmC=1\nNm={name}\nDvH={dv}\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1={pin}\nO1={pin + 1}\n]\n");
        Dv(2, 0, "CP4", "00");
        Dv(3, 2, "CresnetCard", "01");
        Dv(4, 3, "Cresnet Devices", "01", 6);
        Dv(5, 4, "CresnetSlot", "03");
        Dv(6, 5, "Cresnet Keypad", "03", 30);                // Cresnet ID 03: not an IP ID
        Dv(10, 2, "EthernetCard", "02");
        Dv(11, 10, "Ethernet Devices", "02", 7);
        Dv(12, 11, "P4Ethernet", "04");
        Dv(13, 11, "P4Ethernet", "1F");
        Dv(20, 12, "UI.1", "04", 31);                         // panel at IP ID 04
        Dv(21, 20, "FixedSlot", "01");
        Dv(22, 21, "TSW-770 Buttons", "01", 32);              // sub-device: slot 01 of the panel
        Dv(23, 13, "RT1", "1F", 33);                          // encoder at IP ID 1F
        Dv(24, 23, "FixedSlot", "04");
        Dv(25, 24, "HDMI In", "04", 34);                      // sub-device: slot 04 of the encoder
        Dv(26, 25, "Port", "05", 35);                         // sub-device of a sub-device
        Sym(30, "Keypad", 6, 100);
        Sym(31, "TSW-770", 20, 110);
        Sym(32, "TSW-770 Buttons", 22, 120);
        Sym(33, "DM-NVX-36x", 23, 130);
        Sym(34, "DM-NVX-36x HDMI In", 25, 140);
        Sym(35, "Port", 26, 150);

        var path = Path.Combine(Paths.TempData(), "hardware_tree.smw");
        File.WriteAllText(path, sb.ToString());
        using var p = NewProcessor();
        p.LoadProgram(1, path);
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        Assert.Equal("04,1F", string.Join(",", slot.Devices.Select(d => d.IpId.ToString("X2")).OrderBy(x => x)), "IP table");
        Assert.Equal("TSW-770", slot.FindDevice(0x04)!.Type, "panel model");
        Assert.Equal("DM-NVX-36x", slot.FindDevice(0x1F)!.Type, "encoder model");

        // The sidecar still decides when it says so: sub-device symbol 32 forced to IP ID 20.
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"32\": \"20\" } }");
        p.LoadProgram(2, path);
        Assert.True(p.Slot(2).FindDevice(0x20) != null, "ipids override registers the sub-device");
    }
}
