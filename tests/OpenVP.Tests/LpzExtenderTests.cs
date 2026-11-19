using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>Type −12 child records with join bases (fields 27-32): panel reserved joins, toolbars, app control.</summary>
public static class LpzExtenderTests
{
    const int FbA = 10, FbB = 11, Press = 12, Level = 13;

    // IP-ID-03 is the panel; its digital input 1 shows the extender's press so the test can see it.
    // IP-ID-03.2 is a hard-buttons style extender: 2 digital feedback pins, 1 digital press, digital base 18899
    // (joins from 18900). IP-ID-03.3 has one analog feedback at base 17199 (join 17200) and no digital joins.
    private static string Build()
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: ext.smw")));
        bin.AddRange(Record(-12, "IP-ID-03", [Press], [], Int(6, 3), Int(10, 1), Int(11, 0), Int(12, 1), Int(13, 0)));
        bin.AddRange(Record(-12, "IP-ID-03.2", [FbA, FbB], [Press], Int(10, 2), Int(11, 1), Int(12, 2), Int(13, 1),
            Int(27, 18899), Int(28, 10000), Int(29, 10000), Int(30, 18899), Int(31, 10000), Int(32, 10000)));
        bin.AddRange(Record(-12, "IP-ID-03.3", [Level], [], Int(10, 0), Int(11, 0), Int(12, 1), Int(13, 0),
            Int(27, 10000), Int(28, 17199), Int(29, 10000), Int(30, 10000), Int(31, 17199), Int(32, 10000)));

        var path = Path.Combine(Paths.TempData(), "ext.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=ext.bin\r\nSMWFileName=ext.smw\r\nRackType=CP4\r\n"));
        Put("ext.bin", bin.ToArray());
        Put("ext.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        return path;
    }

    [Test]
    public static void Extender_joins_sit_on_the_panel_at_their_bases()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, Build());
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var rt = (LpzRuntime)p.Slot(1).Runtime!;
        Assert.Equal(0, rt.Unsupported.Count, "no unsupported records");
        var panel = p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03");

        rt.TrySetSignal("#11", true);
        Assert.Eventually(() => (bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 18901)), "feedback pin 2 → d18901");
        Assert.True(!(bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 18900)), "pin 1 untouched");

        rt.TrySetSignal("#13", (ushort)1234);
        Assert.Eventually(() => (ushort)panel.GetFeedback(new JoinKey(SigKind.Analog, 17200)) == 1234, "analog feedback → a17200");

        panel.ReceiveInput(new JoinKey(SigKind.Digital, 18900), true);
        Assert.Eventually(() => (bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 1)), "press on d18900 drives the extender's output");
        panel.ReceiveInput(new JoinKey(SigKind.Digital, 18900), false);
        Assert.Eventually(() => !(bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 1)), "release");
    }
}
