using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>Analog Ramp (Bounds Limited, type 223) and Analog Rate Limiter (type 80) in a synthetic .lpz.
/// Timing is slack: only direction of travel, the end values and "holds still" are asserted.</summary>
public static class RampLimiterTests
{
    static JoinKey D(uint j) => new(SigKind.Digital, j);
    static JoinKey A(uint j) => new(SigKind.Analog, j);

    private static JoinDevice Start(out Processor p)
    {
        // Panel IPID 03: outputs d1 up, d2 down, a1 limiter input; inputs (feedback) a1 ramp, a2 limiter.
        const int Up = 10, Down = 11, LimIn = 12, RampOut = 13, LimOut = 14;
        var bin = new List<byte>();
        bin.AddRange(Record(-12, "IP-ID-03", [RampOut, LimOut], [Up, Down, LimIn],
            Int(6, 3), Int(10, 0), Int(11, 2), Int(12, 2), Int(13, 3)));
        // 0..100 in 1 s, starting at 0.
        bin.AddRange(Record(223, "S-1", [Up, Down], [RampOut], Int(15, 4), Int(2, 100), Int(2, 0), Int(2, 100), Int(2, 0)));
        // Full 0-65535 range in 1 s.
        bin.AddRange(Record(80, "S-2", [LimIn], [LimOut], Int(15, 1), Int(2, 100)));

        var path = Path.Combine(Paths.TempData(), "ramplimit.lpz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=ramplimit.bin\r\nSMWFileName=ramplimit.smw\r\nRackType=CP4\r\n"));
            Put("ramplimit.bin", bin.ToArray());
            Put("ramplimit.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        }
        p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, path);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        return p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03 registered");
    }

    private static ushort Fb(JoinDevice panel, uint join) => (ushort)panel.GetFeedback(A(join));

    [Test]
    public static void Bounded_ramp_runs_between_its_bounds()
    {
        var panel = Start(out var p);
        using var _ = p;
        ushort V() => Fb(panel, 1);
        Assert.Equal(0, V(), "starts at the initial value");

        panel.ReceiveInput(D(1), true);
        Assert.Eventually(() => V() > 5, "ramps up");
        Assert.True(V() < 100, "not there yet after a fraction of the time");
        var a = V();
        Assert.Eventually(() => V() > a, "keeps rising");
        Assert.Eventually(() => V() == 100, "stops at the upper bound", 5000);
        Thread.Sleep(100);
        Assert.Equal(100, V(), "holds at the bound");

        panel.ReceiveInput(D(1), false);
        panel.ReceiveInput(D(2), true);
        Assert.Eventually(() => V() < 95, "ramps down");
        var b = V();
        Assert.Eventually(() => V() < b, "keeps falling");
        panel.ReceiveInput(D(2), false);
        Thread.Sleep(60);
        var held = V();
        Assert.True(held > 0 && held < 100, "released mid-way");
        Thread.Sleep(250);
        Assert.Equal(held, V(), "stays where it was released");

        // Both buttons: hold still.
        panel.ReceiveInput(D(1), true);
        panel.ReceiveInput(D(2), true);
        Thread.Sleep(60);
        held = V();
        Thread.Sleep(250);
        Assert.Equal(held, V(), "both held: no movement");
        panel.ReceiveInput(D(2), false);
        Assert.Eventually(() => V() > held, "releasing one resumes the other's direction");
        panel.ReceiveInput(D(1), false);
        panel.ReceiveInput(D(2), true);
        Assert.Eventually(() => V() == 0, "ramps to the lower bound", 5000);
        panel.ReceiveInput(D(2), false);
    }

    [Test]
    public static void Rate_limiter_slews_toward_its_input()
    {
        var panel = Start(out var p);
        using var _ = p;
        ushort V() => Fb(panel, 2);

        panel.ReceiveInput(A(1), (ushort)65535);
        Assert.Eventually(() => V() > 3000, "starts moving up");
        Assert.True(V() < 65535, "not there yet");
        var a = V();
        Assert.Eventually(() => V() > a, "keeps rising");
        Assert.Eventually(() => V() == 65535, "arrives at the input", 5000);

        // Retarget mid-slew: reverses toward the new input without jumping.
        panel.ReceiveInput(A(1), (ushort)0);
        Assert.Eventually(() => V() < 60000, "starts moving down");
        var b = V();
        panel.ReceiveInput(A(1), (ushort)30000);
        Assert.Eventually(() => V() < b - 3000, "keeps falling toward the new target");
        Assert.Eventually(() => V() == 30000, "stops at the new target", 5000);
        Thread.Sleep(100);
        Assert.Equal(30000, V(), "holds there");

        // A small step takes proportionally little time (about 100 ms for 10%), and lands exactly.
        panel.ReceiveInput(A(1), (ushort)(30000 + 6554));
        Assert.Eventually(() => V() == 36554, "small step arrives");
    }

    [Test]
    public static void Customer_program_reports_ramp_and_rate_limiter_as_running()
    {
        // Optional: a real program that uses both (never committed; set OPENVP_FITNESS_LPZ to check one).
        var path = Environment.GetEnvironmentVariable("OPENVP_FITNESS_LPZ");
        if (path == null || !File.Exists(path)) return;
        var pkg = LpzPackage.Open(path);
        var report = LpzReport.Build(pkg);
        foreach (var name in new[] { "Analog Ramp (Bounds Limited)", "Analog Rate Limiter" })
            Assert.True(report.Split('\n').Any(l => l.Contains(" runs ") && l.Contains(name)), $"{name} runs:\n{report}");
    }

    [Test]
    public static void Catalog_lists_both_as_running()
    {
        Assert.True(PrimitiveCatalog.Known[223].Implemented, "223");
        Assert.True(PrimitiveCatalog.Known[80].Implemented, "80");
    }
}
