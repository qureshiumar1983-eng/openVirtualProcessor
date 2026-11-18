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
}
