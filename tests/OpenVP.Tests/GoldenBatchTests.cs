using System.Text;
using OpenVP.Core;
using OpenVP.Simpl;

namespace OpenVP.Tests;

public static partial class GoldenTests
{
    /// <summary>
    /// The .smw interpreter running the documented symbols added in the second batch, wired together and driven
    /// from a panel: an up/down counter, math, a multiplexer feeding Analog to Hex, hex and string conversions,
    /// a string equate, and Time Split feeding Time Join. No timers, so the trace is exact.
    /// </summary>
    [Test]
    public static void Golden_smw_second_batch_symbols()
    {
        var sb = new StringBuilder();
        sb.Append("[\nObjTp=Hd\nPgmNm=SmwBatch\n]\n");
        sb.Append("[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n");
        void Sig(int h, string name) => sb.Append($"[\nObjTp=Sg\nH={h}\nNm={name}\n]\n");
        // Panel 03 presses: d1 up, d2 down, d3 reset; a1 select, a2 x, a3 y; s1 text, s2 hex text.
        // Feedback: digital 45; analog 41-44, 46-48; serial 50-53.
        int[] fbD = [45], fbA = [41, 42, 43, 44, 46, 47, 48], fbS = [50, 51, 52, 53];
        int[] outD = [10, 11, 12], outA = [20, 21, 22], outS = [30, 31];
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
        Sym(20, "Up/Down Counter", [10, 11, 12], [41], "0d", "3d", "1d");
        Sym(21, "Analog Multiplier", [21, 22], [42]);
        Sym(22, "Analog Subtract", [21, 22], [43]);
        Sym(23, "Analog Mux", [20, 21, 22], [44]);
        Sym(24, "Analog to Hex", [44], [50]);
        Sym(25, "Serial to Hex", [30], [51]);
        Sym(26, "Hex to Serial", [31], [52]);
        Sym(27, "Serial Equate", [30, 0], [45], "\"ON\"");
        Sym(28, "Time Split", [30], [46, 47, 48]);
        Sym(29, "Time Join", [46, 47, 48], [53]);
        for (int h = 10; h <= 12; h++) Sig(h, $"d{h - 9}_press");
        Sig(20, "a1_press"); Sig(21, "a2_press"); Sig(22, "a3_press"); Sig(30, "s1_press"); Sig(31, "s2_press");
        Sig(41, "count"); Sig(44, "chosen"); Sig(50, "chosen_hex");

        var path = Path.Combine(Paths.TempData(), "smw_batch.smw");
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

        Step(rec, slots, panel, "up", (D(1), true));
        Step(rec, slots, panel, "up released", (D(1), false));
        Step(rec, slots, panel, "up held twice (one edge)", (D(1), true), (D(1), true));
        Step(rec, slots, panel, "up released", (D(1), false));
        Step(rec, slots, panel, "up at the limit", (D(1), true));
        Step(rec, slots, panel, "up released", (D(1), false));
        Step(rec, slots, panel, "down", (D(2), true));
        Step(rec, slots, panel, "down released", (D(2), false));
        Step(rec, slots, panel, "reset held, then up ignored", (D(3), true), (D(1), true));
        Step(rec, slots, panel, "all released", (D(3), false), (D(1), false));
        Step(rec, slots, panel, "x = 300", (A(2), (ushort)300));
        Step(rec, slots, panel, "y = 5", (A(3), (ushort)5));
        Step(rec, slots, panel, "x = 2 (subtract wraps)", (A(2), (ushort)2));
        Step(rec, slots, panel, "select 1 (x)", (A(1), (ushort)1));
        Step(rec, slots, panel, "select 2 (y)", (A(1), (ushort)2));
        Step(rec, slots, panel, "select 3 (past the last input)", (A(1), (ushort)3));
        Step(rec, slots, panel, "select 7 (still past)", (A(1), (ushort)7));
        Step(rec, slots, panel, "x = 65535, select 1", (A(2), (ushort)65535), (A(1), (ushort)1));
        Step(rec, slots, panel, "text ON", (S(1), "ON"));
        Step(rec, slots, panel, "text on", (S(1), "on"));
        Step(rec, slots, panel, "text 13:45:07", (S(1), "13:45:07"));
        Step(rec, slots, panel, "text 1:05 PM", (S(1), "1:05 PM"));
        Step(rec, slots, panel, "hex 414a0d", (S(2), "414a0d"));
        Step(rec, slots, panel, "hex 41G2", (S(2), "41G2"));
        Check("smw_batch_symbols", rec.Render());
    }
}
