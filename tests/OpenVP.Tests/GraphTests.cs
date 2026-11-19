using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>The program graph and trace causality (docs/debugger-paths.md sections 1 and 2).</summary>
public static class GraphTests
{
    private static string SampleSmw => Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw");

    // Press -> AND -> AND -> panel feedback, wired so one press causes changes at depth 0, 1 and 2.
    private const string ChainSmw =
        "[\nObjTp=Hd\nPgmNm=Chain\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
        "[\nObjTp=Sg\nH=10\nNm=press\n]\n[\nObjTp=Sg\nH=11\nNm=mid\n]\n[\nObjTp=Sg\nH=12\nNm=fb\n]\n" +
        "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=12\nO1=10\n]\n" +
        "[\nObjTp=Sm\nH=20\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=10\nI2=2\nO1=11\n]\n" +
        "[\nObjTp=Sm\nH=21\nSmC=0\nNm=AND\nmI=2\nmO=1\nI1=11\nI2=2\nO1=12\n]\n";

    private static string WriteChainSmw()
    {
        var path = Path.Combine(Paths.TempData(), "chain.smw");
        File.WriteAllText(path, ChainSmw);
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        return path;
    }

    private static string WriteChainLpz()
    {
        // Panel IPID 03 (same layout as LpzRuntimeTests): press d1 = signal 10, feedback d1 = signal 11.
        const int Press = 10, Fb = 11, Mid = 30;
        int[] panelIns = [Fb, 14, 15, 21, 16, 17, 25];
        int[] panelOuts = [Press, 12, 13, 19, 20, 22, 16, 26];
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: chain.smw")));
        bin.AddRange(Record(-12, "IP-ID-03", panelIns, panelOuts, Int(6, 3), Int(10, 4), Int(11, 6), Int(12, 5), Int(13, 7)));
        bin.AddRange(Record(0, "S-2.1:S-1", [Press, 1], [Mid]));
        bin.AddRange(Record(0, "S-2.1:S-2", [Mid, 1], [Fb]));
        var path = Path.Combine(Paths.TempData(), "chain.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=chain.bin\r\nSMWFileName=chain.smw\r\nRackType=CP4\r\n"));
        Put("chain.bin", bin.ToArray());
        Put("chain.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        Put("chain.dsc", Encoding.ASCII.GetBytes("[DSC0003]\r\nSlot : 2.IP-ID : 03;3;Touchpanels(Enet);TS-1080;;TS-1080;-12\r\n"));
        return path;
    }

    private static Processor NewProcessor()
    {
        var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        p.AddLoader(new SimplLoader());
        return p;
    }

    private static void CheckConsistent(ProgramGraph g)
    {
        Assert.True(g.Symbols.Count > 0, "has symbols");
        Assert.Equal(g.Symbols.Count, g.Symbols.Select(s => s.Id).Distinct().Count(), "symbol ids are unique");
        var names = g.Signals.Select(s => s.Name).ToHashSet();
        foreach (var s in g.Symbols)
            foreach (var n in s.Inputs.Concat(s.Outputs))
                if (n.Length > 0) Assert.True(names.Contains(n), $"signal '{n}' of symbol {s.Id} is in the signal list");
        foreach (var sg in g.Signals) Assert.True(sg.Kind is "Digital" or "Analog" or "Serial", "kind " + sg.Kind);
    }

    [Test]
    public static void Graph_of_sample_smw_lists_symbols_wiring_and_devices()
    {
        using var p = NewProcessor();
        p.LoadProgram(2, SampleSmw);
        var g = ((IProgramGraphSource)p.Slot(2).Runtime!).GetGraph();
        CheckConsistent(g);
        Assert.Equal("SIMPL Windows", g.Kind, "kind");
        var and = g.Symbols.Single(s => s.Name == "AND");
        Assert.Equal("S43", and.Id, "id from the symbol handle");
        Assert.True(!and.Device, "AND is logic");
        Assert.Equal(2, and.Inputs.Count, "AND inputs");
        Assert.Equal(1, and.Outputs.Count, "AND outputs");
        Assert.True(g.Symbols.Single(s => s.Id == "S21").Device, "the XPanel is a device");
        // Toggle's output feeds the panel's feedback pin: the same signal name is on both.
        var toggle = g.Symbols.Single(s => s.Name == "Toggle");
        var panel = g.Symbols.Single(s => s.Id == "S21");
        Assert.True(panel.Inputs.Contains(toggle.Outputs[0]), "toggle output is a panel input");
        // Names match GetSignals, which is what the trace uses.
        var live = p.Slot(2).Runtime!.GetSignals().Select(s => s.Name).ToHashSet();
        Assert.True(live.Contains(toggle.Outputs[0]), "graph signal names match GetSignals");
    }

    [Test]
    public static void Graph_of_compiled_program_has_modules_devices_and_constants()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteChainLpz());
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var g = ((IProgramGraphSource)p.Slot(1).Runtime!).GetGraph();
        CheckConsistent(g);
        var a = g.Symbols.Single(s => s.Id == "S-2.1:S-1");
        Assert.Equal("S-2.1", a.Group, "group is the module path");
        Assert.True(!a.Device, "logic");
        Assert.Equal("1", a.Inputs[1], "constant 1 is named '1'");
        Assert.Equal("#10", a.Inputs[0], "unnamed signals show #index");
        Assert.True(g.Symbols.Single(s => s.Id == "IP-ID-03").Device, "IP device is a device");
        Assert.True(g.Signals.Any(s => s.Name == "1"), "constants are in the signal list");
    }

    private static List<TraceEvent> Capture(ProgramSlot slot)
    {
        var seen = new List<TraceEvent>();
        slot.Traced += e => { lock (seen) seen.Add(e); };
        return seen;
    }

    private static void CheckChain(List<TraceEvent> seen, string press, string mid, string fb, string sym1, string sym2)
    {
        TraceEvent Find(string sig) { lock (seen) return seen.FirstOrDefault(e => e.Signal == sig)!; }
        Assert.Eventually(() => Find(fb) != null, "feedback signal changed");
        var a = Find(press); var b = Find(mid); var c = Find(fb);
        Assert.True(a.Symbol == null && a.Cause == null && a.Depth == 0 && a.Chain > 0, "press is external: no symbol, depth 0, new chain");
        Assert.Equal(sym1, b.Symbol, "mid driven by first gate");
        Assert.Equal(press, b.Cause, "mid caused by the press");
        Assert.Equal(1, b.Depth, "mid depth");
        Assert.Equal(sym2, c.Symbol, "fb driven by second gate");
        Assert.Equal(mid, c.Cause, "fb caused by mid");
        Assert.Equal(2, c.Depth, "fb depth");
        Assert.True(a.Chain == b.Chain && b.Chain == c.Chain, "one chain");
        // A second press starts a new chain.
        lock (seen) seen.Clear();
    }

    [Test]
    public static void Smw_trace_carries_symbol_cause_depth_and_chain()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteChainSmw());
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel IPID 03");
        var seen = Capture(slot);
        var d1 = new JoinKey(SigKind.Digital, 1);
        panel.ReceiveInput(d1, true);
        CheckChain(seen, "press", "mid", "fb", "S20", "S21");
        long first;
        panel.ReceiveInput(d1, false);
        Assert.Eventually(() => { lock (seen) return seen.Count >= 3; }, "release traced");
        lock (seen) first = seen[0].Chain;
        panel.ReceiveInput(d1, true);
        Assert.Eventually(() => { lock (seen) return seen.Count >= 6; }, "second press traced");
        lock (seen) Assert.True(seen[3].Chain != first, "a new press is a new chain");
    }

    [Test]
    public static void Lpz_trace_carries_symbol_cause_depth_and_chain()
    {
        using var p = NewProcessor();
        p.LoadProgram(1, WriteChainLpz());
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel IPID 03");
        var seen = Capture(slot);
        var d1 = new JoinKey(SigKind.Digital, 1);
        panel.ReceiveInput(d1, true);
        CheckChain(seen, "#10", "#30", "#11", "S-2.1:S-1", "S-2.1:S-2");
        panel.ReceiveInput(d1, false);
        Assert.Eventually(() => { lock (seen) return seen.Count >= 3; }, "release traced");
        long chain;
        lock (seen) chain = seen[0].Chain;
        panel.ReceiveInput(d1, true);
        Assert.Eventually(() => { lock (seen) return seen.Count >= 6; }, "second press traced");
        lock (seen) Assert.True(seen[3].Chain != chain, "a new press is a new chain");
    }
}
