using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>The wiring check: which compiled pin sits behind which .smw pin, read off signal names, against OpenVP's model.
/// The programs here are small original ones built in memory; nothing in them comes from a real program.</summary>
public static class WiringCheckTests
{
    /// <summary>A synthetic program: signals by name (digital unless said), symbols under a Logic folder. A signal's compiled
    /// index is its .smw handle + 100.</summary>
    private sealed class Builder
    {
        private readonly List<(int H, string Name, int Kind)> _sigs = new();
        private readonly List<string> _smw = new();
        private readonly List<byte[]> _bin = new();
        private int _next = 10, _symbols;
        private readonly Dictionary<string, int> _byName = new();

        public int Sig(string name, int kind = 0)
        {
            if (_byName.TryGetValue(name, out var h)) return h;
            _byName[name] = h = _next++;
            _sigs.Add((h, name, kind));
            return h;
        }
        private int Idx(string? name) => name == null ? 0 : name == "1" ? 1 : Sig(name) + 100;

        /// <summary>One logic symbol: its .smw pins (a name, "0"/"1" for constants, null for unwired) and its compiled pins.
        /// <paramref name="compiled"/> null = compiled type is the same pins as the .smw.</summary>
        public Builder Symbol(int smc, string name, int type, string?[] smwIn, string?[] smwOut, string?[]? compIn = null, string?[]? compOut = null)
        {
            _symbols++;
            var h = 100 + _symbols;
            var b = new StringBuilder($"[\nObjTp=Sm\nH={h}\nSmC={smc}\nNm={name}\nPrH=4\nmI={smwIn.Length}\nmO={smwOut.Length}\n");
            for (int i = 0; i < smwIn.Length; i++) if (smwIn[i] != null) b.Append($"I{i + 1}={Handle(smwIn[i]!)}\n");
            for (int i = 0; i < smwOut.Length; i++) if (smwOut[i] != null) b.Append($"O{i + 1}={Handle(smwOut[i]!)}\n");
            _smw.Add(b.Append("]\n").ToString());
            _bin.Add(Record(type, $"S-{_symbols}", (compIn ?? smwIn).Select(Idx).ToArray(), (compOut ?? smwOut).Select(Idx).ToArray()));
            return this;
        }
        private int Handle(string name) => name == "0" ? 1 : name == "1" ? 2 : Sig(name);

        public string Write(string name)
        {
            var dir = Paths.TempData();
            var smw = new StringBuilder("[\nObjTp=Hd\nPgmNm=Pins\n]\n");
            foreach (var (h, n, _) in _sigs) smw.Append($"[\nObjTp=Sg\nH={h}\nNm={n}\n]\n");
            smw.Append($"[\nObjTp=Sm\nH=4\nSmC=0\nNm=Logic\nmC={_symbols}\n");
            for (int i = 1; i <= _symbols; i++) smw.Append($"C{i}={100 + i}\n");
            smw.Append("]\n");
            foreach (var s in _smw) smw.Append(s);
            var smwPath = Path.Combine(dir, name + ".smw");
            File.WriteAllText(smwPath, smw.ToString());

            var bin = new List<byte>(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, $"Program File: {name}.smw")));
            foreach (var r in _bin) bin.AddRange(r);
            using (var zip = ZipFile.Open(Path.Combine(dir, name + ".lpz"), ZipArchiveMode.Create))
            {
                void Put(string n, byte[] content) { using var s = zip.CreateEntry(n).Open(); s.Write(content); }
                Put("boot.bt", Encoding.ASCII.GetBytes($"[LG_BOOT_DATA]\r\nBinFileName={name}.bin\r\nSMWFileName={name}.smw\r\nRackType=CP4\r\n"));
                Put(name + ".bin", bin.ToArray());
            }
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes("[LOGOSSIG001.000]"));
            foreach (var (h, n, kind) in _sigs)
            {
                var nb = Encoding.Unicode.GetBytes(n);
                ms.Write(BitConverter.GetBytes((ushort)(nb.Length + 8)));
                ms.Write(nb);
                ms.Write(BitConverter.GetBytes(h + 100));
                ms.WriteByte((byte)kind);
                ms.WriteByte(0);
            }
            File.WriteAllBytes(Path.Combine(dir, name + ".sig"), ms.ToArray());
            return smwPath;
        }
    }

    private static WiringReport Run(string smwPath, Func<int, LogicSymbol?>? model = null)
    {
        var lpz = Path.ChangeExtension(smwPath, ".lpz");
        var pkg = LpzPackage.Open(lpz);
        pkg.UseSignals(SigTable.Parse(File.ReadAllBytes(Path.ChangeExtension(smwPath, ".sig"))));
        return WiringCheck.Analyze(SmwFile.Load(smwPath), pkg, model);
    }

    // The four symbols whose compiled pins sit differently from the .smw's, wired as a real program would (names are made up).
    private static Builder FourSymbols() => new Builder()
        .Symbol(12, "Set/Reset Latch", 9, ["SET_A", "RESET_A"], ["Q_A"], ["RESET_A", "SET_A"], ["Q_A"])
        .Symbol(13, "Toggle", 10, ["SET_B", "RESET_B", "CLK_B"], ["Q_B"], ["CLK_B", "RESET_B", "SET_B"], ["Q_B"])
        // Two outputs: the .smw has clear, a spare pin, then the two inputs; compiled has the inputs, then clear.
        .Symbol(19, "Interlock", 11, ["CLEAR_C", null, "IN_C1", "IN_C2"], ["OUT_C1", "OUT_C2"], ["IN_C1", "IN_C2", "CLEAR_C"], ["OUT_C1", "OUT_C2"])
        // The .smw lists a spare pin first; compiled has it last.
        .Symbol(21, "Multiple One Shots", 29, [null, "T_D1", "T_D2"], ["OUT_D1", "OUT_D2"], ["T_D1", "T_D2", null], ["OUT_D1", "OUT_D2"])
        .Symbol(1, "AND", 0, ["X1", "X2"], ["XOUT"]);

    private static WiringTypeReport Type(WiringReport r, string name) => r.Types.First(t => t.Name == name);

    [Test]
    public static void A_correct_model_agrees_on_every_type()
    {
        var r = Run(FourSymbols().Write("wiring_ok"));
        Assert.Equal(5, r.TypesChecked, "five symbol types");
        Assert.Equal(5, r.TypesAgree, "all agree: " + r.Summary);
        Assert.Equal(0, r.TypesDisagree, "none disagree");
        Assert.Equal("5 symbol types checked: 5 agree", r.Summary, "summary");
        foreach (var t in r.Types) Assert.True(t.Status == "agree" && t.Instances == 1 && t.Agree == 1, $"{t.Name}: {t.Status}");
        var latch = Type(r, "Set/Reset Latch");
        Assert.True(latch.Pairs.Any(p => p.Direction == "in" && p.SmwPin == 1 && p.CompiledPin == 2 && p.ExpectedCompiledPin == 2), "the observed latch mapping is smw I1 to compiled 2");
    }

    [Test]
    public static void A_wrong_pin_order_in_the_model_disagrees_with_the_observed_mapping()
    {
        // The model as it was before the pin orders were read off a real program: the .smw order taken as the compiled one.
        var smw = FourSymbols().Write("wiring_bad");
        var r = Run(smw, t => t is 9 or 10 or 11 or 29 ? LogicSymbolCatalog.ByLpzType(t)! with { SmwPinOrder = null } : LogicSymbolCatalog.ByLpzType(t));
        Assert.Equal(4, r.TypesDisagree, "the four symbols disagree: " + r.Summary);
        Assert.Equal(1, r.TypesAgree, "AND still agrees");
        Assert.Equal("5 symbol types checked: 1 agree, 4 disagree", r.Summary, "summary");

        var latch = Type(r, "Set/Reset Latch");
        Assert.Equal("disagree", latch.Status, "latch");
        Assert.Equal(1, latch.Problems["order"], "an order problem");
        Assert.True(latch.Pairs.Any(p => p.Direction == "in" && p.SmwPin == 1 && p.CompiledPin == 2 && p.ExpectedCompiledPin == 1), "smw I1 is compiled 2, the model said 1");
        Assert.True(latch.Pairs.Any(p => p.Direction == "in" && p.SmwPin == 2 && p.CompiledPin == 1 && p.ExpectedCompiledPin == 2), "smw I2 is compiled 1, the model said 2");
        Assert.Equal("S-1", latch.Examples[0].Path, "the example is a symbol path");
        Assert.True(latch.Examples[0].Problem.Contains("smw I1→compiled 2 (OpenVP expects 1)"), "the mapping is spelled out: " + latch.Examples[0].Problem);
        Assert.True(!latch.Examples[0].Problem.Contains("SET_A") && !latch.Examples[0].Problem.Contains("Q_A"), "no signal names in the report");
        Assert.Equal("disagree", Type(r, "Toggle").Status, "toggle");
        Assert.Equal("disagree", Type(r, "Interlock").Status, "interlock");
        Assert.Equal("disagree", Type(r, "Multiple One Shots").Status, "multiple one shots");
    }

    [Test]
    public static void A_swapped_pin_order_is_caught_whichever_way_it_is_swapped()
    {
        var smw = new Builder().Symbol(12, "Set/Reset Latch", 9, ["SET_A", "RESET_A"], ["Q_A"], ["RESET_A", "SET_A"], ["Q_A"]).Write("wiring_swap");
        var r = Run(smw, t => LogicSymbolCatalog.ByLpzType(t)! with { SmwPinOrder = static (_, _) => [1, 2] });
        Assert.Equal("disagree", Type(r, "Set/Reset Latch").Status, "an injected swapped order");
    }

    [Test]
    public static void The_same_signal_on_several_pins_is_ambiguous_not_guessed()
    {
        // Set and reset wired to one signal: the compiled pins can't be told apart, so the pairing isn't guessed, and a
        // wrong model can't be caught on this instance either.
        var smw = new Builder().Symbol(12, "Set/Reset Latch", 9, ["SAME", "SAME"], ["Q_A"], ["SAME", "SAME"], ["Q_A"]).Write("wiring_amb");
        var r = Run(smw);
        Assert.Equal("agree", Type(r, "Set/Reset Latch").Status, "the output is unambiguous, so there is evidence");
        var onlyAmbiguous = new Builder().Symbol(12, "Set/Reset Latch", 9, ["SAME", "SAME"], [null], ["SAME", "SAME"], [null]).Write("wiring_amb2");
        var r2 = Run(onlyAmbiguous);
        var t = Type(r2, "Set/Reset Latch");
        Assert.Equal("ambiguous", t.Status, "no pin can be told");
        Assert.Equal(1, t.Ambiguous, "one ambiguous symbol");
        Assert.Equal(0, t.Pairs.Count, "no pairing is claimed");
        Assert.True(r2.Summary.Contains("1 ambiguous"), r2.Summary);
        var wrong = Run(onlyAmbiguous, ty => LogicSymbolCatalog.ByLpzType(ty)! with { SmwPinOrder = null });
        Assert.Equal("ambiguous", Type(wrong, "Set/Reset Latch").Status, "the same with the other model");
    }

    [Test]
    public static void A_signal_on_two_pins_must_still_land_on_pins_that_carry_it()
    {
        // The .smw wires X on pins 1 and 2 and Y on pin 3; the compiled record has Y first. The model (identity here) can't be right.
        var smw = new Builder().Symbol(1, "AND", 0, ["X", "X", "Y"], ["O"], ["Y", "X", "X"], ["O"]).Write("wiring_amb3");
        var t = Type(Run(smw), "AND");
        Assert.Equal("disagree", t.Status, "the unique signal Y is on the wrong pin");
    }

    [Test]
    public static void A_compiled_type_without_a_model_is_reported()
    {
        var smw = new Builder().Symbol(1, "AND", 0, ["A", "B"], ["O"]).Symbol(9999, "Mystery Symbol", 9999, ["P", "Q"], ["R"]).Write("wiring_unknown");
        var r = Run(smw);
        var t = Type(r, "type 9999");
        Assert.Equal("unmodelled", t.Status, "no model");
        Assert.Equal(9999, t.LpzType, "its compiled type");
        Assert.True(t.SmwNames.Contains("Mystery Symbol"), "the .smw's name for it");
        Assert.Equal("S-2", t.Examples[0].Path, "where it is");
        Assert.Equal(1, r.TypesUnmodelled, "counted");
        Assert.True(r.Summary.Contains("1 not modelled"), r.Summary);
    }

    [Test]
    public static void Extra_pins_and_wrong_kinds_are_flagged_and_dropped_trailing_pins_are_not()
    {
        // The compiled latch has a third input the model doesn't know: a count problem.
        var extra = new Builder().Symbol(12, "Set/Reset Latch", 9, ["SET_A", "RESET_A"], ["Q_A"], ["RESET_A", "SET_A", "SPARE_A"], ["Q_A"]).Write("wiring_extra");
        var t = Type(Run(extra), "Set/Reset Latch");
        Assert.Equal("disagree", t.Status, "extra compiled pin");
        Assert.Equal(1, t.Problems["count"], "a count problem");

        // The compiler drops trailing pins nothing is wired to: a toggle with only its clock wired has one compiled input.
        var dropped = new Builder().Symbol(13, "Toggle", 10, [null, null, "CLK_B"], ["Q_B"], ["CLK_B"], ["Q_B"]).Write("wiring_dropped");
        Assert.Equal("agree", Type(Run(dropped), "Toggle").Status, "fewer compiled pins than the .smw is fine");

        // Unless the dropped pin is wired in the .smw.
        var lost = new Builder().Symbol(13, "Toggle", 10, ["SET_B", null, "CLK_B"], ["Q_B"], ["CLK_B"], ["Q_B"]).Write("wiring_lost");
        Assert.Equal(1, Type(Run(lost), "Toggle").Problems["count"], "a wired .smw pin the compiled record doesn't have");

        // A digital signal on a pin the model says is analog (Analog Comparison takes analog inputs).
        var kind = new Builder().Symbol(2200, "Analog Comparison (Full Set)", 220, ["LEVEL_A", "LEVEL_B"], ["EQ"]).Write("wiring_kind");
        var ok = Type(Run(kind), "Analog Comparison (Full Set)");
        Assert.Equal("disagree", ok.Status, "both pins are digital in the .sig, the model says analog");
        var analog = new Builder();
        analog.Sig("LEVEL_A", 1); analog.Sig("LEVEL_B", 1);
        var good = Type(Run(analog.Symbol(2200, "Analog Comparison (Full Set)", 220, ["LEVEL_A", "LEVEL_B"], ["EQ"]).Write("wiring_kind_ok")), "Analog Comparison (Full Set)");
        Assert.Equal("agree", good.Status, "analog signals on analog pins");
        var k = ok;
        Assert.Equal(1, k.Problems["kind"], "a kind problem");
    }

    [Test]
    public static void Macros_modules_and_constants_are_not_compared()
    {
        // Constants and unwired pins give no evidence; the symbol still counts as checked, with nothing to compare.
        var smw = new Builder().Symbol(1, "AND", 0, ["1", "0"], ["O"], ["1", "0"], ["O"]).Write("wiring_const");
        var t = Type(Run(smw), "AND");
        Assert.Equal("agree", t.Status, "the output is named, so it still has evidence");
        var none = new Builder().Symbol(1, "AND", 0, ["1", "0"], [null], ["1", "0"], [null]).Write("wiring_const2");
        Assert.Equal("no-evidence", Type(Run(none), "AND").Status, "nothing named is wired");
    }

    [Test]
    public static void A_program_that_loads_logs_a_line_when_a_type_disagrees()
    {
        // A compiled latch whose pins are (set, reset), against the model's (reset, set).
        var smw = new Builder().Symbol(12, "Set/Reset Latch", 9, ["SET_A", "RESET_A"], ["Q_A"], ["SET_A", "RESET_A"], ["Q_A"]).Write("wiring_log");
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, smw);
        Assert.True(p.RecentLog(1).Any(e => e.Message.StartsWith("Wiring check: 1 symbol type's pin order differs from OpenVP's model") && e.Message.Contains("Compatibility")), "the slot log says so");

        var ok = new Builder().Symbol(12, "Set/Reset Latch", 9, ["SET_A", "RESET_A"], ["Q_A"], ["RESET_A", "SET_A"], ["Q_A"]).Write("wiring_log_ok");
        using var p2 = new Processor(Paths.TempData());
        p2.AddLoader(new SimplLoader());
        p2.LoadProgram(1, ok);
        Assert.True(!p2.RecentLog(1).Any(e => e.Message.StartsWith("Wiring check")), "nothing is logged when everything agrees");
    }

    [Test]
    public static void The_wiring_endpoint_reports_the_loaded_program_or_says_what_is_missing()
    {
        using var s = new OpsTestServer();
        Assert.Equal(false, s.Get("/api/slots/1/wiring").Json.GetProperty("available").GetBoolean(), "no program yet");
        Assert.True(s.Get("/api/slots/1/wiring").Json.GetProperty("message").GetString()!.Contains("No program"), "says so");

        var smw = FourSymbols().Write("wiring_api");
        s.P.LoadProgram(1, smw);
        var ok = s.Get("/api/slots/1/wiring");
        Assert.Equal(200, ok.Status, "ok");
        Assert.True(ok.Json.GetProperty("available").GetBoolean(), "available: " + ok.Text);
        Assert.Equal("5 symbol types checked: 5 agree", ok.Json.GetProperty("report").GetProperty("summary").GetString(), "the summary");
        Assert.True(!ok.Text.Contains("SET_A") && !ok.Text.Contains("OUT_C1"), "the report names no signals");

        // Without the .sig the check can't run and says what to give it.
        var dir = Paths.TempData();
        File.Copy(smw, Path.Combine(dir, "only.smw"));
        File.Copy(Path.ChangeExtension(smw, ".lpz"), Path.Combine(dir, "only.lpz"));
        s.P.LoadProgram(2, Path.Combine(dir, "only.smw"));
        var missing = s.Get("/api/slots/2/wiring");
        Assert.Equal(false, missing.Json.GetProperty("available").GetBoolean(), "not available");
        Assert.True(missing.Json.GetProperty("message").GetString()!.Contains(".smw, .lpz and .sig together"), "clear message: " + missing.Text);
    }

    [Test]
    public static void The_bundled_samples_that_ship_all_three_files_agree()
    {
        // Programs in the repo with a .smw, .lpz and .sig together are checked; none ship a compile yet, so this holds vacuously.
        int found = 0;
        foreach (var root in new[] { "samples", "examples" })
        {
            var dir = Path.Combine(Paths.RepoRoot, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var smw in Directory.EnumerateFiles(dir, "*.smw", SearchOption.AllDirectories))
            {
                if (!File.Exists(Path.ChangeExtension(smw, ".lpz")) || !File.Exists(Path.ChangeExtension(smw, ".sig"))) continue;
                found++;
                var r = WiringCheck.AnalyzeFiles(smw);
                Assert.Equal(0, r.TypesDisagree, $"{Path.GetFileName(smw)}: {r.Summary}");
            }
        }
        Console.WriteLine($"        (wiring check: {found} bundled program(s) with .smw + .lpz + .sig)");
    }

    [Test]
    public static void Wiring_corpus()
    {
        var root = Environment.GetEnvironmentVariable("OPENVP_WIRING_CORPUS");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Console.WriteLine("        SKIPPED: set OPENVP_WIRING_CORPUS to a folder of program folders (each with a .smw, .lpz and .sig) to run the wiring check over them.");
            return;
        }
        int programs = 0, skipped = 0;
        var failures = new List<string>();
        var totals = new SortedDictionary<string, int[]>();   // type -> agree, disagree, ambiguous, no evidence
        foreach (var folder in Directory.EnumerateDirectories(root).Prepend(root).Distinct())
        {
            var smw = Directory.GetFiles(folder, "*.smw").FirstOrDefault();
            if (smw == null) continue;
            WiringReport r;
            try { r = WiringCheck.AnalyzeFiles(smw); }
            catch (InvalidOperationException) { skipped++; continue; }
            programs++;
            foreach (var t in r.Types)
            {
                var row = totals.TryGetValue(t.Name, out var x) ? x : totals[t.Name] = new int[5];
                row[0] += t.Agree; row[1] += t.Disagree; row[2] += t.Ambiguous; row[3] += t.NoEvidence; row[4] += t.Status == "unmodelled" ? t.Instances : 0;
                if (t.Status is "disagree") failures.Add($"program {programs}: {t.Name} ({string.Join(", ", t.Problems.Select(p => $"{p.Key} {p.Value}"))}); first at {t.Examples.FirstOrDefault()?.Path}");
            }
        }
        Console.WriteLine($"        wiring corpus: {programs} program(s) checked, {skipped} skipped (a file missing or the .sig not from that compile)");
        foreach (var (name, v) in totals) Console.WriteLine($"          {name}: {v[0]} agree, {v[1]} disagree, {v[2]} ambiguous, {v[3]} no evidence{(v[4] > 0 ? $", {v[4]} not modelled" : "")}");
        Assert.True(failures.Count == 0, "pin order differs from the model:\n          " + string.Join("\n          ", failures));
    }
}
