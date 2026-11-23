using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>The shared logic symbol catalog, and the symbols added from the SIMPL descriptions, run through the
/// .smw interpreter (which also exercises the adapter that lets it run the compiled-program primitives).</summary>
public static class SymbolCatalogTests
{
    /// <summary>A small .smw program with no devices: signals are driven and read by name, as the debugger does.</summary>
    internal sealed class Smw : IDisposable
    {
        private readonly StringBuilder _sb = new("[\nObjTp=Hd\nPgmNm=CatalogTest\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n");
        private readonly Dictionary<string, int> _sigs = new() { ["0"] = 1, ["1"] = 2 };
        private int _nextSig = 10, _nextSym = 1000;
        private Processor? _p;
        private ProgramSlot? _slot;
        public string Dir { get; } = Paths.TempData();

        private int Sig(string name)
        {
            if (name.Length == 0) return 0; // unconnected
            if (_sigs.TryGetValue(name, out var h)) return h;
            h = _nextSig++;
            _sigs[name] = h;
            _sb.Append($"[\nObjTp=Sg\nH={h}\nNm={name}\n]\n");
            return h;
        }

        public Smw Sym(string name, string[] ins, string[] outs, params string[] ps) => Sym(name, 0, ins, outs, ps);

        public Smw Sym(string name, int code, string[] ins, string[] outs, params string[] ps)
        {
            var i = ins.Select(Sig).ToArray();
            var o = outs.Select(Sig).ToArray();
            _sb.Append($"[\nObjTp=Sm\nH={_nextSym++}\nSmC={code}\nNm={name}\nmI={i.Length}\nmO={o.Length}\n");
            for (int k = 0; k < i.Length; k++) if (i[k] > 0) _sb.Append($"I{k + 1}={i[k]}\n");
            for (int k = 0; k < o.Length; k++) _sb.Append($"O{k + 1}={o[k]}\n");
            if (ps.Length > 0) { _sb.Append($"mP={ps.Length}\n"); for (int k = 0; k < ps.Length; k++) _sb.Append($"P{k + 1}={ps[k]}\n"); }
            _sb.Append("]\n");
            return this;
        }

        public Smw Run(Action<string>? sidecars = null)
        {
            var path = Path.Combine(Dir, "catalog.smw");
            File.WriteAllText(path, _sb.ToString(), new UTF8Encoding(false));
            sidecars?.Invoke(path);
            _p = new Processor(Dir, 1);
            _p.AddLoader(new SimplLoader());
            _p.LoadProgram(1, path);
            _slot = _p.Slot(1);
            Assert.Equal(SlotState.Running, _slot.State, "running: " + _slot.LastError);
            Settle();
            return this;
        }

        public ProgramSlot Slot => _slot!;

        private void Settle()
        {
            for (int i = 0; i < 3; i++) _slot!.Dispatcher.Invoke(() => { });
        }

        public void Set(string name, object value)
        {
            Assert.True(_slot!.Runtime!.TrySetSignal(name, value), $"signal {name}");
            Settle();
        }

        public object Get(string name) => _slot!.Runtime!.GetSignals().Single(s => s.Name == name).Value;
        public bool D(string name) => (bool)Get(name);
        public ushort A(string name) => (ushort)Get(name);
        public string S(string name) => (string)Get(name);

        public void Dispose() => _p?.Dispose();
    }

    [Test]
    public static void Catalog_names_codes_and_types_are_unique_and_every_symbol_builds()
    {
        // The lookups are built with ToDictionary/Add, so a duplicate name, alias, SmC or type throws here.
        Assert.True(LogicSymbolCatalog.All.Count > 0, "catalog has symbols");
        foreach (var s in LogicSymbolCatalog.All)
        {
            Assert.True(s.Create() is not null, $"{s.Name} builds");
            Assert.True(!ReferenceEquals(s.Create(), s.Create()), $"{s.Name} gives each symbol its own state");
            Assert.Equal(s, LogicSymbolCatalog.ByName(s.Name.ToLowerInvariant()), $"{s.Name} by name");
            foreach (var c in s.SmCodes) Assert.Equal(s, LogicSymbolCatalog.BySmc(c), $"{s.Name} by SmC {c}");
            foreach (var t in s.LpzTypes) Assert.Equal(s, LogicSymbolCatalog.ByLpzType(t), $"{s.Name} by type {t}");
            if (s.Evidence == SymbolEvidence.Documented) Assert.True(s.LpzTypes.Length == 0, $"{s.Name}: a documented symbol has no compiled type yet");
            if (s.Evidence == SymbolEvidence.Inferred) Assert.True(s.LpzTypes.Length > 0, $"{s.Name}: an inferred symbol was seen compiled");
            if (s.Evidence == SymbolEvidence.Compiled) Assert.True(s.LpzTypes.Length + s.SmCodes.Length > 0, $"{s.Name}: a compiled symbol was seen in real programs");
        }
    }

    [Test]
    public static void Every_compiled_type_that_ran_before_the_catalog_still_runs()
    {
        int[] before = [0, 1, 2, 3, 4, 6, 7, 9, 10, 11, 14, 15, 16, 17, 18, 19, 20, 23, 27, 29, 30, 36, 37, 38, 51, 52, 56, 58, 61, 62,
            67, 80, 82, 85, 87, 88, 93, 97, 99, 100, 101, 103, 106, 114, 121, 122, 136, 138, 139, 156, 161, 202, 203, 204, 205, 206, 207,
            220, 223, 224, 230, 236, 237, 241, 254, 266];
        foreach (var t in before) Assert.True(PrimitiveLibrary.Supports(t), $"type {t}");
        foreach (var info in PrimitiveCatalog.Known.Values.Where(k => k.Type >= 0 && PrimitiveLibrary.Supports(k.Type)))
            Assert.True(LogicSymbolCatalog.ByLpzType(info.Type) != null, $"type {info.Type} {info.Name}");
    }

    [Test]
    public static void Docs_symbols_md_matches_the_catalog()
    {
        var path = Path.Combine(Paths.RepoRoot, "docs", "symbols.md");
        var text = LogicSymbolCatalog.RenderMarkdown();
        if (Environment.GetEnvironmentVariable("OPENVP_UPDATE_GOLDEN") == "1") File.WriteAllText(path, text, new UTF8Encoding(false));
        Assert.True(File.Exists(path), "docs/symbols.md exists (generate with OPENVP_UPDATE_GOLDEN=1)");
        Assert.True(File.ReadAllText(path).Replace("\r\n", "\n") == text,
            "docs/symbols.md is out of date: OPENVP_UPDATE_GOLDEN=1 dotnet run --project tests/OpenVP.Tests Docs_symbols");
    }

    [Test]
    public static void Smw_parameters_become_compiled_units()
    {
        Assert.Equal((50L, (string?)null), SimplRuntime.ParseParam("0.5s"), "0.5s = 50 ticks");
        Assert.Equal((12000L, (string?)null), SimplRuntime.ParseParam("2m"), "2m");
        Assert.Equal((25L, (string?)null), SimplRuntime.ParseParam("250ms"), "250ms");
        Assert.Equal((7L, (string?)null), SimplRuntime.ParseParam("7t"), "7t stays ticks");
        Assert.Equal((255L, (string?)null), SimplRuntime.ParseParam("255d"), "255d");
        Assert.Equal((31L, (string?)null), SimplRuntime.ParseParam("1Fh"), "1Fh");
        Assert.Equal((32768L, (string?)null), SimplRuntime.ParseParam("50%"), "50%");
        Assert.Equal(((long?)null, "PWR\r"), SimplRuntime.ParseParam("\"PWR\\x0D\""), "quoted text with escape");
        Assert.Equal(((long?)null, "a\"b\\c\n"), SimplRuntime.ParseParam("\"a\\\"b\\\\c\\n\""), "other escapes");
        Assert.Equal(((long?)null, "\\xZZ"), SimplRuntime.ParseParam("\"\\xZZ\""), "a bad escape stays as typed");
    }

    [Test]
    public static void Nand_and_xnor()
    {
        using var t = new Smw()
            .Sym("NAND", ["a", "b", ""], ["nand"])   // the unconnected third input is ignored
            .Sym("XNOR", ["a", "b"], ["xnor"])
            .Run();
        Assert.True(t.D("nand") && t.D("xnor"), "both low: nand 1, xnor 1");
        t.Set("a", true);
        Assert.True(t.D("nand") && !t.D("xnor"), "one high: nand 1, xnor 0");
        t.Set("b", true);
        Assert.True(!t.D("nand") && t.D("xnor"), "both high: nand 0, xnor 1");
    }

    [Test]
    public static void Digital_to_analog_and_back()
    {
        using var t = new Smw()
            .Sym("Digital to Analog", ["b3", "b2", "b1", "b0"], ["value"])
            .Sym("Analog to Digital", ["value"], ["o3", "o2", "o1", "o0"])
            .Run();
        t.Set("b3", true);
        t.Set("b0", true);
        Assert.Equal((ushort)9, t.A("value"), "1001b");
        Assert.True(t.D("o3") && !t.D("o2") && !t.D("o1") && t.D("o0"), "decoded back MSB first");
    }

    [Test]
    public static void Twos_offset_and_negation()
    {
        using var t = new Smw()
            .Sym("Analog 2's Offset Converter", ["x", "y"], ["xo", "yo"])
            .Sym("Analog Negation", ["x"], ["neg"])
            .Sym("Analog Initialize", ["1"], ["y"], "1")
            .Run();
        Assert.Equal((ushort)0x8000, t.A("xo"), "0 -> 8000h at start");
        Assert.Equal((ushort)0x8001, t.A("yo"), "a value initialized at start converts too");
        t.Set("x", (ushort)0x8005);
        Assert.Equal((ushort)5, t.A("xo"), "8005h -> 5");
        t.Set("x", (ushort)3);
        Assert.Equal((ushort)65533, t.A("neg"), "-3");
    }

    [Test]
    public static void Serial_substring_and_escapes_in_serial_send()
    {
        using var t = new Smw()
            .Sym("Serial Substring", ["text"], ["mid"], "3d", "4d")
            .Sym("Serial Substring", ["text"], ["tail"], "5d")
            .Sym("Serial Send", ["go"], ["sent"], "\"PWR\\x0D\"")
            .Run();
        t.Set("text", "ABCDEFGH");
        Assert.Equal("CDEF", t.S("mid"), "start 3, length 4");
        Assert.Equal("EFGH", t.S("tail"), "start 5 to the end");
        t.Set("text", "AB");
        Assert.Equal("", t.S("mid"), "start past the end");
        t.Set("go", true);
        Assert.Equal("PWR\r", t.S("sent"), "\\x0D decoded");
    }

    [Test]
    public static void Analog_debounce_and_long_delay()
    {
        using var t = new Smw()
            .Sym("Analog Debounce", ["in"], ["out"], "0.1s")
            .Sym("Long Delay", ["start"], ["done"], "0.1s")
            .Run();
        t.Set("in", (ushort)1);
        t.Set("in", (ushort)2);
        Assert.Equal((ushort)0, t.A("out"), "not yet stable");
        Assert.Eventually(() => t.A("out") == 2, "settles on the last value");
        t.Set("start", true);
        Assert.True(!t.D("done"), "delay not done yet");
        Assert.Eventually(() => t.D("done"), "delay done after 0.1 s");
    }

    [Test]
    public static void Digital_sum_counts_high_inputs()
    {
        using var t = new Smw().Sym("Digital Sum", 61, ["a", "b", "c"], ["count"]).Run();
        Assert.Equal((ushort)0, t.A("count"), "none high");
        t.Set("a", true);
        t.Set("c", true);
        Assert.Equal((ushort)2, t.A("count"), "two high");
        t.Set("a", false);
        Assert.Equal((ushort)1, t.A("count"), "one high");
    }

    [Test]
    public static void Analog_compare_gives_greater_and_equal()
    {
        using var t = new Smw().Sym("Analog Compare", 35, ["a", "b"], ["gt", "eq", "lt"]).Run();
        Assert.True(!t.D("gt") && t.D("eq") && !t.D("lt"), "0 = 0");
        t.Set("a", (ushort)5);
        Assert.True(t.D("gt") && !t.D("eq") && !t.D("lt"), "5 > 0");
        t.Set("b", (ushort)9);
        Assert.True(!t.D("gt") && !t.D("eq") && t.D("lt"), "5 < 9");
    }

    [Test]
    public static void Ascii_keypad_types_deletes_and_clears()
    {
        var ins = new string[4 + 128];
        for (int i = 0; i < ins.Length; i++) ins[i] = "";
        ins[0] = "clear"; ins[1] = "1"; ins[2] = "back";
        ins[3 + 'A'] = "kA"; ins[3 + '7'] = "k7"; // pin n + 4 is index n + 3
        using var t = new Smw().Sym("ASCII Keypad", 123, ins, ["text"], "3d").Run();
        void Press(string k) { t.Set(k, true); t.Set(k, false); }
        Press("kA"); Press("k7"); Press("kA");
        Assert.Equal("A7A", t.S("text"), "typed");
        Press("kA");
        Assert.Equal("A7A", t.S("text"), "max length 3");
        Press("back");
        Assert.Equal("A7", t.S("text"), "backspace");
        Press("clear");
        Assert.Equal("", t.S("text"), "clear");
    }

    [Test]
    public static void Ascii_serial_decoder_plays_text_as_presses()
    {
        var outs = new string[2 + '9' + 1];
        for (int i = 0; i < outs.Length; i++) outs[i] = "";
        outs[0] = "busy"; outs['1' + 1] = "t1"; outs['2' + 1] = "t2";
        using var t = new Smw()
            .Sym("ASCII Serial Decoder", 54, ["number", "dial"], outs, "0.02s")
            .Sym("Toggle", ["t1"], ["saw1"])
            .Sym("Toggle", ["t2"], ["saw2"])
            .Run();
        t.Set("number", "12");
        t.Set("dial", true);
        Assert.True(t.D("busy"), "busy while playing");
        Assert.Eventually(() => !t.D("busy"), "done");
        Assert.True(t.D("saw1") && t.D("saw2"), "each digit pressed once");
    }

    [Test]
    public static void Password_keypad_memory_and_match()
    {
        string[] keys = ["clear", "", "k0", "k1", "k2", "k3", "", "", "", "", "", "", "star", ""];
        using var t = new Smw()
            .Sym("Digit Keypad", keys, ["code"], "4d")
            .Sym("Digit Keypad", ["clear", "", "", "", "", "", "", "", "", "", "", "", "any", ""], ["stars"], "4d")
            .Sym("Password Memory", ["newcode", "store", "recall", ""], ["stored"], "\"1234\"")
            .Sym("Password Match", ["code", "enter", "", "stored"], ["match"])
            .Run();
        void Press(params string[] ks) { foreach (var k in ks) { t.Set(k, true); t.Set(k, false); } }
        Assert.Equal("1234", t.S("stored"), "default password");
        Press("k1", "k2", "k3");
        Press("any", "any", "any");
        Assert.Equal("123", t.S("code"), "digits");
        Assert.Equal("***", t.S("stars"), "stars");
        Press("enter");
        Assert.True(!t.D("match"), "123 is wrong");
        Press("clear", "k1", "k2", "k3", "k1");
        Press("enter");
        Assert.True(!t.D("match"), "1231 is wrong");
        t.Set("newcode", "1231");
        Press("store");
        Assert.Equal("1231", t.S("stored"), "new password stored");
        Press("enter");
        Assert.True(t.D("match"), "1231 now matches");
    }

    [Test]
    public static void Numeric_keypad_and_delay_with_analog_time()
    {
        string[] keys = ["d0", "d1", "", "", "", "", "", "", "", "", "clear", "up", "down", "enter"];
        using var t = new Smw()
            .Sym("Numeric Keypad", keys, ["value", "entered"], "15d")
            .Sym("Toggle", ["entered"], ["saw_enter"])
            .Sym("Delay (Analog Time)", ["start", "cancel", "time"], ["late"])
            .Run();
        void Press(string k) { t.Set(k, true); t.Set(k, false); }
        Press("d1"); Press("d0");
        Assert.Equal((ushort)10, t.A("value"), "10");
        Press("d1");
        Assert.Equal((ushort)10, t.A("value"), "101 is over the maximum of 15");
        Press("up");
        Assert.Equal((ushort)11, t.A("value"), "up");
        Press("enter");
        Assert.True(t.D("saw_enter"), "enter pulsed");
        Press("clear");
        Assert.Equal((ushort)0, t.A("value"), "clear");

        t.Set("time", (ushort)5); // 0.05 s
        t.Set("start", true);
        Press("cancel");
        Thread.Sleep(150);
        Assert.True(!t.D("late"), "cancelled");
        t.Set("start", false);
        t.Set("start", true);
        Assert.Eventually(() => t.D("late"), "high after the analog time");
        t.Set("start", false);
        Assert.True(!t.D("late"), "drops with the input");
    }

    [Test]
    public static void Equate_enable_defaults_on_when_unconnected()
    {
        using var t = new Smw()
            .Sym("Analog Equate", ["v", ""], ["is5", "is7"], "5d", "7d")
            .Sym("Analog Equate", ["v", "en"], ["gated5"], "5d")
            .Run();
        t.Set("v", (ushort)5);
        Assert.True(t.D("is5") && !t.D("is7"), "enable unconnected: enabled");
        Assert.True(!t.D("gated5"), "enable wired and low: disabled");
        t.Set("en", true);
        Assert.True(t.D("gated5"), "enable high");
    }

    [Test]
    public static void Equate_outputs_on_one_signal_act_as_an_or()
    {
        using var t = new Smw()
            .Sym("Analog Equate", ["v", ""], ["inc", "inc", "other", "inc"], "1d", "6d", "7d", "15d")
            .Sym("Toggle", ["inc", "", ""], ["edges"])
            .Run();
        foreach (var v in new ushort[] { 1, 6, 15 })
        {
            t.Set("v", v);
            Assert.True(t.D("inc") && !t.D("other"), $"{v}: one of the shared outputs matches");
        }
        Assert.True(t.D("edges"), "no pulse while moving between matching values: one rising edge");
        t.Set("v", (ushort)7);
        Assert.True(!t.D("inc") && t.D("other"), "7: only the output on its own signal");
        t.Set("v", (ushort)6);
        Assert.True(t.D("inc") && !t.D("other"), "back to a matching value");
        t.Set("v", (ushort)2);
        Assert.True(!t.D("inc") && !t.D("other"), "no match");
    }

    [Test]
    public static void Symbols_json_teaches_codes_and_reports_unknown_names()
    {
        using var t = new Smw().Sym("Some Renamed Thing", 4321, ["a", "b"], ["o"]);
        t.Run(path => File.WriteAllText(Path.ChangeExtension(path, ".symbols.json"), "{ \"4321\": \"NAND\", \"99\": \"No Such Symbol\", \"x\": \"AND\" }"));
        Assert.True(t.D("o"), "4321 runs as NAND");
        t.Set("a", true);
        t.Set("b", true);
        Assert.True(!t.D("o"), "NAND of two highs");
        var log = string.Join("\n", t.Slot.Processor.RecentLog(1).Select(e => e.Message));
        Assert.True(log.Contains("no symbol named \"No Such Symbol\""), "unknown name reported");
        Assert.True(log.Contains("\"x\" is not a number"), "bad key reported");
    }

    [Test]
    public static void Primitives_json_names_compiled_types()
    {
        var dir = Paths.TempData();
        var path = Path.Combine(dir, "primitives.json");
        File.WriteAllText(path, "{ \"9001\": \"NAND\", \"0\": \"OR\" }");
        var map = LogicSymbolCatalog.LoadMap(path);
        Assert.Equal("NAND", map[9001].Name, "9001 -> NAND");
        Assert.True(PrimitiveLibrary.Create(9001) == null, "unknown without the map");
        Assert.True(PrimitiveLibrary.Create(9001, map) is NandPrim, "known with the map");
        Assert.True(PrimitiveLibrary.Create(0, map) is AndPrim, "the catalog wins over the map for identified types");
    }
}

public static class SymbolSurveyTests
{
    private static byte[] Sig(params (int Index, string Name)[] entries)
    {
        var data = new List<byte>(Encoding.ASCII.GetBytes("[LOGOSSIG001.000]"));
        foreach (var (index, name) in entries)
        {
            var n = Encoding.Unicode.GetBytes(name);
            data.AddRange(BitConverter.GetBytes((ushort)(n.Length + 8)));
            data.AddRange(n);
            data.AddRange(BitConverter.GetBytes(index));
            data.Add(0);
            data.Add(0);
        }
        return data.ToArray();
    }

    [Test]
    public static void Survey_pairs_source_symbols_with_compiled_types()
    {
        var dir = Paths.TempData();
        // Source: AND (known), XOR with a code the catalog lacks, NAND (documented) and a symbol OpenVP has no behavior for.
        File.WriteAllText(Path.Combine(dir, "test.smw"),
            "[\nObjTp=Hd\nPgmNm=Survey\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
            "[\nObjTp=Sg\nH=10\nNm=a\n]\n[\nObjTp=Sg\nH=11\nNm=and_out\n]\n[\nObjTp=Sg\nH=12\nNm=xor_out\n]\n[\nObjTp=Sg\nH=13\nNm=nand_out\n]\n[\nObjTp=Sg\nH=14\nNm=mystery_out\n]\n" +
            "[\nObjTp=Sm\nH=20\nSmC=1\nNm=AND\nmI=2\nI1=10\nI2=2\nmO=1\nO1=11\n]\n" +
            "[\nObjTp=Sm\nH=21\nSmC=555\nNm=XOR\nmI=1\nI1=10\nmO=1\nO1=12\n]\n" +
            "[\nObjTp=Sm\nH=22\nSmC=7777\nNm=NAND\nmI=1\nI1=10\nmO=1\nO1=13\n]\n" +
            "[\nObjTp=Sm\nH=23\nSmC=888\nNm=Mystery Symbol\nmI=1\nI1=10\nmO=1\nO1=14\n]\n");
        var bin = new List<byte>();
        bin.AddRange(LpzTests.Record(31, "LeadingREM", [], [], LpzTests.Int(15, 1), LpzTests.Str(3, "Program File: test.smw")));
        bin.AddRange(LpzTests.Record(0, "S-1", [10, 1], [11]));
        bin.AddRange(LpzTests.Record(4, "S-2", [10], [12]));
        bin.AddRange(LpzTests.Record(9001, "S-3", [10], [13]));
        bin.AddRange(LpzTests.Record(9002, "S-4", [10], [14]));
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(dir, "test.lpz"), System.IO.Compression.ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=test.bin\r\nSMWFileName=test.smw\r\n"));
            Put("test.bin", bin.ToArray());
        }
        File.WriteAllBytes(Path.Combine(dir, "test.sig"), Sig((10, "a"), (11, "and_out"), (12, "xor_out"), (13, "nand_out"), (14, "mystery_out")));

        var survey = SymbolSurvey.Run([dir]);
        Assert.Equal(1, survey.CompiledFiles, "one compiled program");
        Assert.Equal(0, survey.SourceFiles - 1, "its source surveyed with it, not twice");
        Assert.Equal(1, survey.Matches[new SymbolSurvey.Match(1, "AND", 0)], "AND paired with type 0");
        Assert.Equal(1, survey.Matches[new SymbolSurvey.Match(7777, "NAND", 9001)], "NAND paired with type 9001");
        Assert.Equal("XOR", survey.SuggestedSymbolCodes()[555], "new code for a catalog symbol");
        Assert.Equal("NAND", survey.SuggestedSymbolCodes()[7777], "new code for a documented symbol");
        Assert.True(!survey.SuggestedSymbolCodes().ContainsKey(1), "known codes not suggested");
        Assert.Equal("NAND", survey.SuggestedPrimitiveTypes()[9001], "new type for a catalog symbol");
        Assert.True(!survey.SuggestedPrimitiveTypes().ContainsKey(9002), "no behavior to point an unknown symbol's type at");
        var report = survey.Report();
        Assert.True(report.Contains("Mystery Symbol") && report.Contains("new symbol, no behavior yet"), "unknown symbol reported");
        Assert.True(report.Contains("NOT SUPPORTED"), "unsupported compiled types reported");
    }
}
