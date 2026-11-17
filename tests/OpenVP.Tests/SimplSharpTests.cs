using OpenVP.Core;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

public static class SimplSharpTests
{
    private static Processor NewProcessor()
    {
        var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        return p;
    }

    [Test]
    public static void Compat_scan_finds_entry_class_and_reports_supported()
    {
        var shim = System.Reflection.Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "OpenVP.SimplSharp.dll"));
        var report = CompatScanner.Scan(Paths.SampleDll, shim);
        Assert.Equal("SampleProgram.ControlSystem", report.ControlSystemType, "entry class");
        Assert.True(report.CrestronAssemblies.Contains("SimplSharpPro"), "references SimplSharpPro by name");
        Assert.True(report.FullySupported, "fully supported:\n" + report);
    }

    [Test]
    public static void Sample_program_runs_and_answers_panel_presses()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, Paths.SampleDll);
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "slot state (" + slot.LastError + ")");

        var panel = slot.FindDevice(0x03);
        Assert.True(panel != null, "panel IPID 03 registered");
        Assert.Equal("SampleProgram running", (string)panel!.GetFeedback(new JoinKey(SigKind.Serial, 10)), "startup text");

        panel.AttachConnection();
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 1), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 1)), "d1 toggled on");
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 1), false);
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 1), true);
        Assert.Eventually(() => !(bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 1)), "d1 toggled off");

        panel.ReceiveInput(new JoinKey(SigKind.Analog, 1), 1234);
        Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 1)) == "Level 1234", "analog echoed as text");

        panel.ReceiveInput(new JoinKey(SigKind.Serial, 1), "hello");
        Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 2)) == "HELLO", "serial upper-cased");

        Assert.Eventually(() => (ushort)panel.GetFeedback(new JoinKey(SigKind.Analog, 10)) >= 1, "heartbeat timer ticking", 2500);
    }

    [Test]
    public static void Console_command_registered_by_program_works()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, Paths.SampleDll);
        var resp = p.Slot(1).RunConsoleCommand("sample abc");
        Assert.True(resp.StartsWith("sample ok") && resp.EndsWith("args=abc"), "console response: " + resp);
    }

    [Test]
    public static void Two_slots_run_separately_and_link_over_eisc()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, Paths.SampleDll);
        p.LoadProgram(2, Paths.SampleDll);
        var a = p.Slot(1).FindDevice(0x03)!;
        var b = p.Slot(2).FindDevice(0x03)!;
        Assert.True(a != b, "each slot has its own panel");

        // d10 on slot 1's panel -> EISC 10 -> slot 2's EISC output d1 -> slot 2 panel feedback d10
        a.ReceiveInput(new JoinKey(SigKind.Digital, 10), true);
        Assert.Eventually(() => (bool)b.GetFeedback(new JoinKey(SigKind.Digital, 10)), "slot 2 panel sees slot 1 press");
        Assert.True(!(bool)a.GetFeedback(new JoinKey(SigKind.Digital, 1)), "slot 1 state untouched by slot 2");
    }

    [Test]
    public static void Stop_and_reload_unloads_cleanly()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, Paths.SampleDll);
        p.Slot(1).Stop();
        Assert.Equal(SlotState.Stopped, p.Slot(1).State, "stopped");
        Assert.True(p.RecentLog(1).Any(l => l.Message.Contains("SampleProgram stopping")), "program saw Stopping event");
        p.LoadProgram(1, Paths.SampleDll);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running again");
    }
}
