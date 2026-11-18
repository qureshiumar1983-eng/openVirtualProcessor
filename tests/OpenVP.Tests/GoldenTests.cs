using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>
/// Golden traces: drive a fixed input script into a program and compare the complete, ordered list of
/// signal changes (slot, signal, value, source) with a checked-in file in tests/OpenVP.Tests/golden/.
/// Performance work must not change a single line: same changes, same order.
///
/// To regenerate after an intended behaviour change (never to make a failing test pass):
///   OPENVP_UPDATE_GOLDEN=1 dotnet run -c Release --project tests/OpenVP.Tests Golden
/// then review the diff of the golden files like any other code change.
///
/// Scripts avoid timers and cross-thread races: every step ends with a barrier on the slot thread(s),
/// and EISC runs are recorded per slot (slots run on their own threads, so only per-slot order is fixed).
/// </summary>
public static class GoldenTests
{
    // ------------------------------------------------------------ harness

    private sealed class Recorder
    {
        private readonly object _gate = new();
        private readonly List<(int slot, string line)> _lines = new();

        /// <summary>The one place that subscribes to signal traces (all slots).</summary>
        public Recorder(Processor p) => p.Traced += OnTrace;

        private void OnTrace(TraceEvent e)
        {
            var value = e.Value switch { bool b => b ? "1" : "0", string s => $"\"{s}\"", _ => e.Value.ToString() };
            lock (_gate) _lines.Add((e.Slot, $"slot{e.Slot} {e.Signal} {e.Kind.ToString()[0]}={value} <- {e.Source}"));
        }

        public void Mark(int slot, string text) { lock (_gate) _lines.Add((slot, $"-- {text}")); }

        public string Render()
        {
            // Stable sort by slot: within one slot the order is the real order.
            lock (_gate) return string.Join("\n", _lines.Select((l, i) => (l, i)).OrderBy(x => x.l.slot).ThenBy(x => x.i).Select(x => x.l.line)) + "\n";
        }
    }

    private static string GoldenDir => Path.Combine(Paths.RepoRoot, "tests", "OpenVP.Tests", "golden");

    private static void Check(string name, string actual)
    {
        var path = Path.Combine(GoldenDir, name + ".txt");
        if (Environment.GetEnvironmentVariable("OPENVP_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(GoldenDir);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }
        if (!File.Exists(path)) throw new AssertException($"golden file missing: {path} (generate with OPENVP_UPDATE_GOLDEN=1)");
        var want = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        var got = actual.Split('\n');
        for (int i = 0; i < Math.Max(want.Length, got.Length); i++)
        {
            var w = i < want.Length ? want[i] : "<end>";
            var g = i < got.Length ? got[i] : "<end>";
            if (w != g) throw new AssertException($"golden '{name}' differs at line {i + 1}:\n          expected: {w}\n          actual:   {g}");
        }
    }

    private static void Settle(params ProgramSlot[] slots)
    {
        // Several rounds: a change can hop between slots (EISC) and come back.
        for (int round = 0; round < 4; round++)
            foreach (var s in slots) s.Dispatcher.Invoke(() => { });
    }

    private static JoinKey D(uint j) => new(SigKind.Digital, j);
    private static JoinKey A(uint j) => new(SigKind.Analog, j);
    private static JoinKey S(uint j) => new(SigKind.Serial, j);

    /// <summary>One script step: send the inputs back to back, then wait until everything settles.</summary>
    private static void Step(Recorder rec, ProgramSlot[] slots, JoinDevice dev, string label, params (JoinKey key, object value)[] inputs)
    {
        foreach (var s in slots) rec.Mark(s.Number, label); // in every slot, so per-slot groups line up
        foreach (var (k, v) in inputs) dev.ReceiveInput(k, v);
        Settle(slots);
    }

    private static string Pack(string name, List<byte> bin, string dip, string? dsc = null)
    {
        var path = Path.Combine(Paths.TempData(), name + ".lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string n, byte[] c) { using var s = zip.CreateEntry(n).Open(); s.Write(c); }
        Put("boot.bt", Encoding.ASCII.GetBytes($"[LG_BOOT_DATA]\r\nBinFileName={name}.bin\r\nSMWFileName={name}.smw\r\nRackType=CP4\r\n"));
        Put(name + ".bin", bin.ToArray());
        Put(name + ".dip", Encoding.ASCII.GetBytes(dip));
        if (dsc != null) Put(name + ".dsc", Encoding.ASCII.GetBytes(dsc));
        return path;
    }

    private static List<byte> Header(string name) => new(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, $"Program File: {name}.smw")));

    private static byte[] Panel(int ipid, int[] ins, int[] outs, int inD, int inAEnd, int outD, int outAEnd) =>
        Record(-12, $"IP-ID-{ipid:X2}", ins, outs, Int(6, ipid), Int(10, inD), Int(11, outD), Int(12, inAEnd), Int(13, outAEnd));

    private static byte[] DigitalPanel(int ipid, int[] ins, int[] outs) => Panel(ipid, ins, outs, ins.Length, ins.Length, outs.Length, outs.Length);

    private const string Dip03 = "[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n";

    private static (Processor p, ProgramSlot slot, JoinDevice panel, Recorder rec) Run(string path)
    {
        var p = new Processor(Paths.TempData(), 2);
        p.AddLoader(new LpzLoader());
        var rec = new Recorder(p); // subscribe before loading so start-up changes are recorded
        p.LoadProgram(1, path);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        var panel = p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03");
        Settle(p.Slot(1));
        return (p, p.Slot(1), panel, rec);
    }

    // ------------------------------------------------------------ the bench programs, small

    [Test]
    public static void Golden_chain()
    {
        const int N = 8;
        var bin = Header("chain");
        bin.AddRange(DigitalPanel(3, [10 + N], [10]));
        for (int i = 0; i < N; i++) bin.AddRange(Record(1, $"S-{i + 1}", [10 + i], [11 + i]));
        var (p, slot, panel, rec) = Run(Pack("chain", bin, Dip03));
        using var _ = p;
        var slots = new[] { slot };
        foreach (var v in new[] { true, false, true, false, false, true })
            Step(rec, slots, panel, $"press d1={v}", (D(1), v));
        Check("chain", rec.Render());
    }

    [Test]
    public static void Golden_fanout()
    {
        const int F = 6;
        var outs = Enumerable.Range(11, F).ToArray();
        var bin = Header("fanout");
        bin.AddRange(DigitalPanel(3, outs, [10]));
        for (int i = 0; i < F; i++) bin.AddRange(Record(0, $"S-{i + 1}", [10, 1], [outs[i]]));
        var (p, slot, panel, rec) = Run(Pack("fanout", bin, Dip03));
        using var _ = p;
        var slots = new[] { slot };
        foreach (var v in new[] { true, false, true, true, false })
            Step(rec, slots, panel, $"press d1={v}", (D(1), v));
        Check("fanout", rec.Render());
    }

    [Test]
    public static void Golden_interlock()
    {
        const int W = 5;
        var presses = Enumerable.Range(10, W).ToArray();
        var fb = Enumerable.Range(10 + W, W).ToArray();
        var bin = Header("interlock");
        bin.AddRange(DigitalPanel(3, fb, presses));
        bin.AddRange(Record(11, "S-1", [0, .. presses], fb));
        var (p, slot, panel, rec) = Run(Pack("interlock", bin, Dip03));
        using var _ = p;
        var slots = new[] { slot };
        for (uint j = 1; j <= W; j++)
        {
            Step(rec, slots, panel, $"press d{j}", (D(j), true));
            Step(rec, slots, panel, $"release d{j}", (D(j), false));
        }
        Step(rec, slots, panel, "press d2 and d4 together", (D(2), true), (D(4), true));
        Step(rec, slots, panel, "release both", (D(2), false), (D(4), false));
        Step(rec, slots, panel, "press d2 again", (D(2), true));
        Check("interlock", rec.Render());
    }

    // ------------------------------------------------------------ the synthetic program from LpzRuntimeTests

    [Test]
    public static void Golden_synthetic_program()
    {
        var (p, slot, panel, rec) = Run(LpzRuntimeTests.BuildProgram(timers: false));
        using var _ = p;
        var slots = new[] { slot };
        Step(rec, slots, panel, "d1 press (toggle on)", (D(1), true));
        Step(rec, slots, panel, "d1 release", (D(1), false));
        Step(rec, slots, panel, "d1 press (toggle off)", (D(1), true));
        Step(rec, slots, panel, "d1 release", (D(1), false));
        Step(rec, slots, panel, "d2 press (interlock 1)", (D(2), true));
        Step(rec, slots, panel, "d3 press (interlock 2)", (D(3), true));
        Step(rec, slots, panel, "d2 release, d3 release", (D(2), false), (D(3), false));
        Step(rec, slots, panel, "d6 clear", (D(6), true));
        Step(rec, slots, panel, "d4 press (AND low)", (D(4), true));
        Step(rec, slots, panel, "d5 press (AND high)", (D(5), true));
        Step(rec, slots, panel, "d4 release (AND low)", (D(4), false));
        Step(rec, slots, panel, "a1 = 777", (A(1), (ushort)777));
        Step(rec, slots, panel, "a1 = 0", (A(1), (ushort)0));
        Step(rec, slots, panel, "s1 = abc", (S(1), "abc"));
        Step(rec, slots, panel, "s1 = abc again (serials always fire)", (S(1), "abc"));
        Step(rec, slots, panel, "s1 empty", (S(1), ""));
        Check("synthetic", rec.Render());
    }

    // ------------------------------------------------------------ more primitives

    [Test]
    public static void Golden_primitives()
    {
        // Presses d1..d13 = signals 10..22, a1 = 23, a2 = 24, s1 = 25. Feedback signals start at 40.
        int[] outs = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25];
        // digital feedback, then analog, then serial
        int[] ins = [40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 55, 56, 57, 58, /*analog*/ 50, 52, 53, 59, /*serial*/ 51, 54, 60];
        var bin = Header("prims");
        bin.AddRange(Panel(3, ins, outs, inD: 14, inAEnd: 18, outD: 13, outAEnd: 15));
        bin.AddRange(Record(1, "S-1 NOT", [10], [40]));
        bin.AddRange(Record(2, "S-2 OR", [10, 11, 12], [41]));
        bin.AddRange(Record(3, "S-3 NOR", [10, 11], [42]));
        bin.AddRange(Record(4, "S-4 XOR", [10, 11, 12], [43]));
        bin.AddRange(Record(10, "S-5 Toggle", [13, 14, 15], [44, 45]));
        bin.AddRange(Record(9, "S-6 SR latch", [16, 17], [46, 47]));
        bin.AddRange(Record(18, "S-7 Buffer", [18, 10, 11], [48, 49]));
        bin.AddRange(Record(67, "S-8 Analog buffer", [18, 23], [50]));
        bin.AddRange(Record(88, "S-9 Serial buffer", [18, 25], [51]));
        bin.AddRange(Record(38, "S-10 Analog init", [19], [52, 53], Int(15, 2), Int(2, 111), Int(2, 222)));
        bin.AddRange(Record(38, "S-11 Serial init", [19], [54], Int(15, 1), Str(3, "init-text")));
        bin.AddRange(Record(51, "S-12 Equate", [23], [55, 56], Int(15, 2), Int(2, 5), Int(2, 10)));
        bin.AddRange(Record(11, "S-13 Interlock", [20, 21, 22], [57, 58]));
        bin.AddRange(Record(38, "S-14 Analog init at start", [1], [59], Int(15, 1), Int(2, 7)));
        bin.AddRange(Record(38, "S-15 Serial init at start", [1], [60], Int(15, 1), Str(3, "boot")));

        var (p, slot, panel, rec) = Run(Pack("prims", bin, Dip03));
        using var _ = p;
        var slots = new[] { slot };
        // gates
        Step(rec, slots, panel, "A on", (D(1), true));
        Step(rec, slots, panel, "B on", (D(2), true));
        Step(rec, slots, panel, "C on", (D(3), true));
        Step(rec, slots, panel, "A off", (D(1), false));
        Step(rec, slots, panel, "B off", (D(2), false));
        Step(rec, slots, panel, "C off", (D(3), false));
        Step(rec, slots, panel, "A and B together", (D(1), true), (D(2), true));
        Step(rec, slots, panel, "all off", (D(1), false), (D(2), false));
        // toggle: clock, reset, set
        Step(rec, slots, panel, "toggle clk up", (D(4), true));
        Step(rec, slots, panel, "toggle clk down", (D(4), false));
        Step(rec, slots, panel, "toggle clk up", (D(4), true));
        Step(rec, slots, panel, "toggle clk down", (D(4), false));
        Step(rec, slots, panel, "toggle set", (D(6), true));
        Step(rec, slots, panel, "toggle set release", (D(6), false));
        Step(rec, slots, panel, "toggle reset", (D(5), true));
        Step(rec, slots, panel, "toggle reset release", (D(5), false));
        // SR latch: set, reset, reset wins over set
        Step(rec, slots, panel, "SR set", (D(7), true));
        Step(rec, slots, panel, "SR set release", (D(7), false));
        Step(rec, slots, panel, "SR reset", (D(8), true));
        Step(rec, slots, panel, "SR set while reset held", (D(7), true));
        Step(rec, slots, panel, "SR release both", (D(7), false), (D(8), false));
        // buffers: enable gates the followers; analog and serial hold their last value when disabled
        Step(rec, slots, panel, "buffer: A on while disabled", (D(1), true));
        Step(rec, slots, panel, "buffer: a1 = 5 while disabled", (A(1), (ushort)5));
        Step(rec, slots, panel, "buffer: s1 = x while disabled", (S(1), "x"));
        Step(rec, slots, panel, "buffer: enable on", (D(9), true));
        Step(rec, slots, panel, "buffer: B on", (D(2), true));
        Step(rec, slots, panel, "buffer: a1 = 10", (A(1), (ushort)10));
        Step(rec, slots, panel, "buffer: s1 = y", (S(1), "y"));
        Step(rec, slots, panel, "buffer: enable off", (D(9), false));
        Step(rec, slots, panel, "buffer: a1 = 100 while disabled", (A(1), (ushort)100));
        Step(rec, slots, panel, "buffer: s1 = z while disabled", (S(1), "z"));
        Step(rec, slots, panel, "buffer: A off while disabled", (D(1), false));
        Step(rec, slots, panel, "buffer: enable on again", (D(9), true));
        Step(rec, slots, panel, "buffer: enable off", (D(9), false));
        // equate
        Step(rec, slots, panel, "equate: a1 = 5", (A(1), (ushort)5));
        Step(rec, slots, panel, "equate: a1 = 10", (A(1), (ushort)10));
        Step(rec, slots, panel, "equate: a1 = 11", (A(1), (ushort)11));
        // init on a rising trigger
        Step(rec, slots, panel, "init trigger", (D(10), true));
        Step(rec, slots, panel, "init trigger release", (D(10), false));
        Step(rec, slots, panel, "init trigger again", (D(10), true));
        // interlock with clear
        Step(rec, slots, panel, "interlock 1", (D(12), true));
        Step(rec, slots, panel, "interlock 1 release", (D(12), false));
        Step(rec, slots, panel, "interlock 2", (D(13), true));
        Step(rec, slots, panel, "interlock 2 release", (D(13), false));
        Step(rec, slots, panel, "interlock clear", (D(11), true));
        Step(rec, slots, panel, "interlock clear release", (D(11), false));
        Step(rec, slots, panel, "interlock 1 again", (D(12), true));
        Check("primitives", rec.Render());
    }

    // ------------------------------------------------------------ EISC between two slots

    [Test]
    public static void Golden_eisc_between_slots()
    {
        // Slot 1: panel 03 press d1 (10) -> EISC 1F in d1; EISC out d1 (12) -> panel fb d1; EISC out d2 (24) -> panel fb d2.
        //         a1 (20) and s1 (21) go the same way (outs 22, 23).
        // Slot 2: EISC 1F out d1 (10) -> NOT -> EISC in d1 (13); out a1 (11) -> analog buffer -> in a1 (14);
        //         out s1 (12) -> serial buffer -> in s1 (15); panel 04 press d1 (16) -> EISC in d2.
        const string dip1 = "[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\nid1=1F\r\naddr1=127.0.0.2\r\n";
        const string dip2 = "[IPTable]\r\nid0=04\r\naddr0=127.0.0.1\r\nid1=1F\r\naddr1=127.0.0.2\r\n";
        var b1 = Header("eisc1");
        b1.AddRange(Panel(3, ins: [12, 24, 22, 23], outs: [10, 20, 21], inD: 2, inAEnd: 3, outD: 1, outAEnd: 2));
        b1.AddRange(Panel(0x1F, ins: [10, 20, 21], outs: [12, 24, 22, 23], inD: 1, inAEnd: 2, outD: 2, outAEnd: 3));
        var b2 = Header("eisc2");
        b2.AddRange(DigitalPanel(4, [], [16]));
        b2.AddRange(Panel(0x1F, ins: [13, 16, 14, 15], outs: [10, 11, 12], inD: 2, inAEnd: 3, outD: 1, outAEnd: 2));
        b2.AddRange(Record(1, "S-1", [10], [13]));
        b2.AddRange(Record(67, "S-2", [1, 11], [14]));
        b2.AddRange(Record(88, "S-3", [1, 12], [15]));

        using var p = new Processor(Paths.TempData(), 2);
        p.AddLoader(new LpzLoader());
        var rec = new Recorder(p);
        p.LoadProgram(1, Pack("eisc1", b1, dip1));
        p.LoadProgram(2, Pack("eisc2", b2, dip2));
        Assert.Equal(SlotState.Running, p.Slot(1).State, "slot 1: " + p.Slot(1).LastError);
        Assert.Equal(SlotState.Running, p.Slot(2).State, "slot 2: " + p.Slot(2).LastError);
        var slots = new[] { p.Slot(1), p.Slot(2) };
        var panel1 = p.Slot(1).FindDevice(3)!;
        var panel2 = p.Slot(2).FindDevice(4)!;
        Settle(slots);

        Step(rec, slots, panel1, "slot1 d1 press", (D(1), true));
        Step(rec, slots, panel1, "slot1 d1 release", (D(1), false));
        Step(rec, slots, panel1, "slot1 a1 = 1234", (A(1), (ushort)1234));
        Step(rec, slots, panel1, "slot1 s1 = hello", (S(1), "hello"));
        Step(rec, slots, panel2, "slot2 d1 press", (D(1), true));
        Step(rec, slots, panel2, "slot2 d1 release", (D(1), false));
        Step(rec, slots, panel1, "slot1 d1 press", (D(1), true));
        Check("eisc", rec.Render());
    }

    // ------------------------------------------------------------ SIMPL Windows source through SimplRuntime

    [Test]
    public static void Golden_smw_sample_program()
    {
        using var p = new Processor(Paths.TempData(), 2);
        p.AddLoader(new SimplLoader());
        var rec = new Recorder(p);
        p.LoadProgram(1, Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw"));
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel IPID 03");
        var slots = new[] { slot };
        Settle(slots);

        Step(rec, slots, panel, "d1 press (toggle on)", (D(1), true));
        Step(rec, slots, panel, "d1 release", (D(1), false));
        Step(rec, slots, panel, "d1 press (toggle off)", (D(1), true));
        Step(rec, slots, panel, "d1 release", (D(1), false));
        Step(rec, slots, panel, "d3 press (interlock)", (D(3), true));
        Step(rec, slots, panel, "d4 press (interlock)", (D(4), true));
        Step(rec, slots, panel, "d3 and d4 release", (D(3), false), (D(4), false));
        Step(rec, slots, panel, "d6 press (AND low)", (D(6), true));
        Step(rec, slots, panel, "d7 press (AND high)", (D(7), true));
        Step(rec, slots, panel, "d6 release", (D(6), false));
        Step(rec, slots, panel, "a1 = 40000", (A(1), (ushort)40000));
        Step(rec, slots, panel, "a1 = 0", (A(1), (ushort)0));
        Step(rec, slots, panel, "d5 press (serial send)", (D(5), true));
        Step(rec, slots, panel, "d5 release", (D(5), false));
        Step(rec, slots, panel, "d10 press (EISC out)", (D(10), true));
        Step(rec, slots, panel, "d10 release", (D(10), false));
        Check("smw_sample", rec.Render());
    }
}
