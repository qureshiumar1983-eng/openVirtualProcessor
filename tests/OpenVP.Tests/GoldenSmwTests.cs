using System.Text;
using OpenVP.Core;
using OpenVP.Simpl;

namespace OpenVP.Tests;

public static partial class GoldenTests
{
    /// <summary>
    /// The .smw interpreter with every symbol type it knows (AND, OR, XOR, NOT, Buffer in three kinds,
    /// Interlock, Toggle, SR Latch, Analog Initialize, Equate, Serial Send), the constant signals "0" and "1",
    /// a NOT wired to an analog signal (its error is logged, nothing changes) and an input nobody uses.
    /// Timers have their own scenario in GoldenTimerTests.
    /// </summary>
    [Test]
    public static void Golden_smw_symbols()
    {
        var sb = new StringBuilder();
        sb.Append("[\nObjTp=Hd\nPgmNm=SmwSymbols\n]\n");
        sb.Append("[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n");
        void Sig(int h, string name) => sb.Append($"[\nObjTp=Sg\nH={h}\nNm={name}\n]\n");
        // Panel 03: digital presses d1..d8 = 10..17, analog a1, a2 = 20, 21, serial s1, s2 = 30, 31.
        // Feedback: digital 40..51, 55, 56, 58; analog 52, 54; serial 53, 57.
        int[] fbD = [40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 55, 56, 58], fbA = [52, 54], fbS = [53, 57];
        int[] outD = [10, 11, 12, 13, 14, 15, 16, 17], outA = [20, 21], outS = [30, 31];
        var ins = fbD.Concat(fbA).Concat(fbS).ToArray();
        var outs = outD.Concat(outA).Concat(outS).ToArray();
        sb.Append($"[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I={fbD.Length}\nn2I={fbA.Length}\nn3I={fbS.Length}\nn1O={outD.Length}\nn2O={outA.Length}\nn3O={outS.Length}\nmI={ins.Length}\nmO={outs.Length}\n");
        for (int i = 0; i < ins.Length; i++) sb.Append($"I{i + 1}={ins[i]}\n");
        for (int i = 0; i < outs.Length; i++) sb.Append($"O{i + 1}={outs[i]}\n");
        sb.Append("]\n");
        void Sym(int h, string name, int[] i, int[] o, params string[] p)
        {
            sb.Append($"[\nObjTp=Sm\nH={h}\nSmC=0\nNm={name}\nmI={i.Length}\nmO={o.Length}\n");
            for (int k = 0; k < i.Length; k++) sb.Append($"I{k + 1}={i[k]}\n");
            for (int k = 0; k < o.Length; k++) sb.Append($"O{k + 1}={o[k]}\n");
            if (p.Length > 0) { sb.Append($"mP={p.Length}\n"); for (int k = 0; k < p.Length; k++) sb.Append($"P{k + 1}={p[k]}\n"); }
            sb.Append("]\n");
        }
        Sym(20, "AND", [10, 11, 2], [40]);
        Sym(21, "OR", [10, 11, 1], [41]);
        Sym(22, "XOR", [10, 11, 12], [42]);
        Sym(23, "NOT", [10, 11], [43, 44]);
        Sym(24, "Interlock", [12, 13, 14], [45, 46, 47]);
        Sym(25, "Toggle", [15, 16, 17], [48]);
        Sym(26, "SR Latch", [15, 16], [49]);
        Sym(27, "Buffer", [12, 10, 11], [50, 51]);
        Sym(28, "Analog Buffer", [12, 20], [52]);
        Sym(29, "Serial Buffer", [12, 30], [53]);
        Sym(30, "Analog Initialize", [13], [54], "777");
        Sym(31, "Equate", [20], [55, 56], "5", "10");
        Sym(32, "Serial Send", [14, 15], [57], "\"x\"", "\"y\"");
        Sym(33, "NOT", [20], [58]);          // wired to an analog signal: the cast fails and is logged
        Sym(34, "NOT", [21], [60]);          // output nobody reads
        for (int h = 10; h <= 17; h++) Sig(h, $"d{h - 9}_press");
        sb.Append("[\nObjTp=Sg\nH=20\nNm=a1_press\n]\n[\nObjTp=Sg\nH=21\nNm=a2_press\n]\n[\nObjTp=Sg\nH=30\nNm=s1_press\n]\n[\nObjTp=Sg\nH=31\nNm=s2_press\n]\n");
        Sig(40, "and_fb"); Sig(52, "abuf_fb"); Sig(53, "sbuf_fb"); Sig(54, "init_fb");

        var path = Path.Combine(Paths.TempData(), "smw_symbols.smw");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");

        using var p = new Processor(Paths.TempData(), 2);
        p.AddLoader(new SimplLoader());
        var rec = new Recorder(p);
        p.LoadProgram(1, path);
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel IPID 03");
        var slots = new[] { slot };
        Settle(slots);

        Step(rec, slots, panel, "d1 on", (D(1), true));
        Step(rec, slots, panel, "d2 on", (D(2), true));
        Step(rec, slots, panel, "d3 on", (D(3), true));
        Step(rec, slots, panel, "d1 off", (D(1), false));
        Step(rec, slots, panel, "d2 off", (D(2), false));
        Step(rec, slots, panel, "d3 off", (D(3), false));
        Step(rec, slots, panel, "d1 and d2 together", (D(1), true), (D(2), true));
        Step(rec, slots, panel, "both off", (D(1), false), (D(2), false));
        // buffers: enable is d3
        Step(rec, slots, panel, "a1 = 5 while buffers are off", (A(1), (ushort)5));
        Step(rec, slots, panel, "s1 = x while buffers are off", (S(1), "x"));
        Step(rec, slots, panel, "buffers on (d3)", (D(3), true));
        Step(rec, slots, panel, "a1 = 10", (A(1), (ushort)10));
        Step(rec, slots, panel, "a1 = 11", (A(1), (ushort)11));
        Step(rec, slots, panel, "s1 = y", (S(1), "y"));
        Step(rec, slots, panel, "s1 = y again", (S(1), "y"));
        Step(rec, slots, panel, "d1 on while buffers are on", (D(1), true));
        Step(rec, slots, panel, "d1 off", (D(1), false));
        Step(rec, slots, panel, "buffers off (d3)", (D(3), false));
        Step(rec, slots, panel, "a1 = 20 while off", (A(1), (ushort)20));
        Step(rec, slots, panel, "a1 = 5 (equate)", (A(1), (ushort)5));
        Step(rec, slots, panel, "a1 = 10 (equate)", (A(1), (ushort)10));
        Step(rec, slots, panel, "a2 = 3", (A(2), (ushort)3));
        // interlock d3, d4, d5
        Step(rec, slots, panel, "interlock d4", (D(4), true));
        Step(rec, slots, panel, "interlock d4 off", (D(4), false));
        Step(rec, slots, panel, "interlock d5", (D(5), true));
        Step(rec, slots, panel, "interlock d3", (D(3), true));
        Step(rec, slots, panel, "interlock off", (D(3), false), (D(5), false));
        // toggle d6 (clock), d7 (set), d8 (reset); SR latch d6 set, d7 reset; serial send d5, d6
        Step(rec, slots, panel, "d6 on (toggle, SR set, send y)", (D(6), true));
        Step(rec, slots, panel, "d6 off", (D(6), false));
        Step(rec, slots, panel, "d6 on (toggle off)", (D(6), true));
        Step(rec, slots, panel, "d6 off", (D(6), false));
        Step(rec, slots, panel, "d7 on (toggle set, SR reset)", (D(7), true));
        Step(rec, slots, panel, "d6 on while d7 held", (D(6), true));
        Step(rec, slots, panel, "d7 off, d6 off", (D(7), false), (D(6), false));
        Step(rec, slots, panel, "d8 on (toggle reset)", (D(8), true));
        Step(rec, slots, panel, "d8 off", (D(8), false));
        Step(rec, slots, panel, "d5 on (send x)", (D(5), true));
        Step(rec, slots, panel, "d5 off", (D(5), false));
        // analog initialize on d4's rising edge (input 1 of the Initialize is d4)
        Step(rec, slots, panel, "d4 on (analog init)", (D(4), true));
        Step(rec, slots, panel, "d4 off", (D(4), false));
        Step(rec, slots, panel, "s2 = z (nobody reads it)", (S(2), "z"));
        Check("smw_symbols", rec.Render());
    }
}
