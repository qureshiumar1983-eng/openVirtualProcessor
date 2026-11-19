using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>A .smw running what it can't on its own (user macros) from its compiled .lpz.</summary>
public static class SimplBorrowTests
{
    // .smw: panel 03 press d1 → "press", feedback d1 ← "fb". Under Logic: macro "Invert.umc" (press → fb) and macro
    // "Other.umc" (wired to "press" only). Handles in the .smw (10, 11) differ from the compiled signal numbers.
    private const string Smw =
        "[\nObjTp=Hd\nPgmNm=Borrow\n]\n[\nObjTp=Sg\nH=10\nNm=press\n]\n[\nObjTp=Sg\nH=11\nNm=fb\n]\n[\nObjTp=Sg\nH=12\nNm=other_out\n]\n" +
        "[\nObjTp=Sm\nH=1\nSmC=1\nNm=Central Control Modules\nmC=1\nC1=5\n]\n" +
        "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nPrH=1\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=11\nO1=10\n]\n" +
        "[\nObjTp=Sm\nH=4\nSmC=0\nNm=Logic\nmC=2\nC1=20\nC2=21\n]\n" +
        "[\nObjTp=Sm\nH=20\nSmC=155\nNm=Invert.umc\nPrH=4\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=10\nO1=11\n]\n" +
        "[\nObjTp=Sm\nH=21\nSmC=155\nNm=Other.umc\nPrH=4\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=10\nO1=12\n]\n";

    // Compiled: press = 40, fb = 41, other_out = 42; inside Invert (S-1) three NOTs chained through local signals
    // 50 and 51, so fb = NOT press. Inside Other (S-2) one NOT reads "fb", which isn't wired to that macro.
    private static byte[] Bin()
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: borrow.smw")));
        bin.AddRange(Record(-12, "IP-ID-03", [41], [40], Int(6, 3), Int(10, 1), Int(11, 1), Int(12, 1), Int(13, 1)));
        bin.AddRange(Record(1, "S-1:S-1", [40], [50]));
        bin.AddRange(Record(1, "S-1:S-2", [50], [51]));
        bin.AddRange(Record(1, "S-1:S-3", [51], [41]));
        bin.AddRange(Record(1, "S-2:S-1", [41], [42]));
        return bin.ToArray();
    }

    private static byte[] Sig()
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("[LOGOSSIG001.000]"));
        foreach (var (index, name) in new[] { (40, "press"), (41, "fb"), (42, "other_out"), (50, "::A:S-1.00000032"), (51, "::B:S-1.00000033") })
        {
            var n = Encoding.Unicode.GetBytes(name);
            ms.Write(BitConverter.GetBytes((ushort)(n.Length + 8)));
            ms.Write(n);
            ms.Write(BitConverter.GetBytes(index));
            ms.WriteByte(0); // digital
            ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    [Test]
    public static void Macros_run_from_the_compiled_program_and_mismatches_stay_unsupported()
    {
        var dir = Paths.TempData();
        var smw = Path.Combine(dir, "borrow.smw");
        File.WriteAllText(smw, Smw);
        File.WriteAllText(Path.ChangeExtension(smw, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        File.WriteAllBytes(Path.Combine(dir, "borrow.sig"), Sig());
        using (var zip = ZipFile.Open(Path.Combine(dir, "borrow.lpz"), ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=borrow.bin\r\nSMWFileName=borrow.smw\r\nRackType=CP4\r\n"));
            Put("borrow.bin", Bin());
            Put("borrow.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        }

        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, smw);
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var rt = (SimplRuntime)slot.Runtime!;
        Assert.Equal(1, rt.BorrowedCount.Symbols, "only Invert.umc runs from the compiled program");
        Assert.Equal(3, rt.BorrowedCount.Records, "its three compiled records");
        Assert.True(rt.Unsupported.Any(s => s.Name == "Other.umc"), "Other.umc touches a signal not wired to it: stays unsupported");

        var panel = slot.FindDevice(3) ?? throw new AssertException("panel 03");
        var d1 = new JoinKey(SigKind.Digital, 1);
        Assert.Eventually(() => (bool)panel.GetFeedback(d1), "fb = NOT press: high at start through the macro's local signals");
        panel.ReceiveInput(d1, true);
        Assert.Eventually(() => !(bool)panel.GetFeedback(d1), "press → fb low");
        panel.ReceiveInput(d1, false);
        Assert.Eventually(() => (bool)panel.GetFeedback(d1), "release → fb high");
    }

    [Test]
    public static void Built_in_constants_exist_without_signal_blocks()
    {
        // Real .smw files write no blocks for handles 1 and 2 (the constants 0 and 1): an Analog Initialize
        // triggered "on 1" is wired to handle 2 and must load at start.
        const string text = "[\nObjTp=Hd\nPgmNm=Constants\n]\n" +
            "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn2I=1\nmI=1\nI1=30\n]\n" +
            "[\nObjTp=Sm\nH=20\nSmC=32\nNm=Analog Initialize\nn1I=1\nn1O=1\nmI=1\nI1=2\nmO=1\nO1=30\nmP=1\nP1=42d\n]\n";
        var path = Path.Combine(Paths.TempData(), "constants.smw");
        File.WriteAllText(path, text);
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, path);
        var panel = p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03");
        Assert.Eventually(() => (ushort)panel.GetFeedback(new JoinKey(SigKind.Analog, 1)) == 42, "handle 2 is the constant 1: the initialize fired at start");
    }

    [Test]
    public static void Disabled_symbols_are_left_out()
    {
        // OpF=0: switched off in SIMPL Windows. The macro isn't reported, and the disabled NOT doesn't drive "fb".
        const string text = "[\nObjTp=Hd\nPgmNm=Disabled\n]\n[\nObjTp=Sg\nH=10\nNm=press\n]\n[\nObjTp=Sg\nH=11\nNm=fb\n]\n" +
            "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=11\nO1=10\n]\n" +
            "[\nObjTp=Sm\nH=20\nSmC=155\nNm=Off.umc\nOpF=0\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=10\nO1=11\n]\n" +
            "[\nObjTp=Sm\nH=21\nSmC=205\nNm=NOT\nOpF=0\nmI=1\nmO=1\nI1=10\nO1=11\n]\n";
        var path = Path.Combine(Paths.TempData(), "disabled.smw");
        File.WriteAllText(path, text);
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, path);
        var rt = (SimplRuntime)p.Slot(1).Runtime!;
        Assert.Equal(0, rt.Unsupported.Count, "a disabled macro isn't unsupported");
        var panel = p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03");
        Thread.Sleep(100);
        Assert.True(!(bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 1)), "the disabled NOT doesn't run (it would drive fb high)");
    }
}
