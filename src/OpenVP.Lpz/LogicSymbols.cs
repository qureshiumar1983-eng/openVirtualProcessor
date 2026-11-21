using System.Text.Json;

namespace OpenVP.Lpz;

/// <summary>How OpenVP knows a symbol's pins and behavior.</summary>
public enum SymbolEvidence
{
    /// <summary>Matched against compiled programs that ship their source (docs/lpz-format.md): the compiled
    /// primitive type, its pin order and its parameters were checked against real .lpz/.smw pairs.</summary>
    Compiled,
    /// <summary>Written from the SIMPL Windows symbol descriptions only. Pin order and parameter units are
    /// best guesses until a real program confirms them; the symbol is matched by name (and by SmC code where a
    /// real program has shown it), and its compiled type is unknown until a real program supplies it.</summary>
    Documented,
    /// <summary>The compiled type and its pins were seen in real programs, but the behavior is inferred from how
    /// those programs wire and name its pins (usually because it sits inside a macro whose source isn't
    /// available). The primitive's summary says what the inference rests on.</summary>
    Inferred,
}

/// <summary>
/// One SIMPL Windows logic symbol: its library name, how to run it, and how it is identified in source
/// (.smw SmC codes) and compiled (.lpz primitive types) programs.
/// </summary>
public sealed record LogicSymbol(
    string Name,
    string Category,
    Func<Primitive> Create,
    SymbolEvidence Evidence,
    string Pins,
    int[] SmCodes,
    int[] LpzTypes,
    string[] Aliases)
{
    /// <summary>For the .smw engine: whether an unconnected input pin reads as the constant 1 instead of 0
    /// (what the compiler ties it to). AND and NAND ignore unconnected inputs; Equate's enable defaults on.</summary>
    public Func<int, bool> UnwiredHigh { get; init; } = static _ => false;
}

/// <summary>
/// Every SIMPL Windows logic symbol OpenVP can run, in one place, for both engines: the compiled-program
/// engine looks symbols up by primitive type, the .smw interpreter by SmC code or name. Each symbol has a
/// single implementation (a <see cref="Primitive"/>), so a program behaves the same whether it is loaded
/// from its source or compiled. docs/symbols.md lists them (and is checked against this table by the tests).
/// </summary>
public static class LogicSymbolCatalog
{
    private static LogicSymbol C(string name, string category, Func<Primitive> create, string pins, int[] smc, int[] lpz, params string[] aliases) =>
        new(name, category, create, SymbolEvidence.Compiled, pins, smc, lpz, aliases);

    private static LogicSymbol D(string name, string category, Func<Primitive> create, string pins, params string[] aliases) =>
        new(name, category, create, SymbolEvidence.Documented, pins, [], [], aliases);

    private static LogicSymbol I(string name, string category, Func<Primitive> create, string pins, int[] smc, int[] lpz, params string[] aliases) =>
        new(name, category, create, SymbolEvidence.Inferred, pins, smc, lpz, aliases);

    private static readonly Func<int, bool> AllHigh = static _ => true;

    public static readonly IReadOnlyList<LogicSymbol> All =
    [
        // ---------------------------------------------------------------- digital
        C("AND", "Digital", () => new AndPrim(), "in: i1..iN · out: o", [1], [0]) with { UnwiredHigh = AllHigh },
        D("NAND", "Digital", () => new NandPrim(), "in: i1..iN · out: o") with { SmCodes = [2], UnwiredHigh = AllHigh },
        C("OR", "Digital", () => new OrPrim(false), "in: i1..iN · out: o", [3], [2]),
        C("NOR", "Digital", () => new OrPrim(true), "in: i1..iN · out: o", [4], [3]),
        C("XOR", "Digital", () => new XorPrim(), "in: i1..iN · out: o (odd number high)", [5], [4], "Exclusive OR"),
        D("XNOR", "Digital", () => new XnorPrim(), "in: i1..iN · out: o (even number high)"),
        C("NOT", "Digital", () => new NotPrim(), "in: i1..iN · out: o1..oN", [205], [1]),
        C("Multiple NOT", "Digital", () => new NotPrim(), "in: i1..iN · out: o1..oN", [418], [206]),
        C("Buffer", "Digital", () => new BufferPrim(), "in: enable, i1..iN · out: o1..oN (low while disabled)", [20], [18], "Digital Buffer"),
        C("Truth Table", "Digital", () => new TruthTablePrim(), "in: i1..iN · out: o1..oM · params: rows", [28], [19]),
        C("Transition Gate", "Digital", () => new TransitionPrim(false), "in: i1..iN · out: pulse on any rising edge", [6], [6]),
        C("Transition Gate (falling)", "Digital", () => new TransitionPrim(true), "in: i1..iN · out: pulse on any falling edge", [], [7]),
        C("Set/Reset Latch", "Digital", () => new SrLatchPrim(), "in: set, reset · out: out, out*", [12], [9], "SR Latch"),
        C("Toggle", "Digital", () => new TogglePrim(), "in: clock, reset, set · out: out, out*", [13], [10]),
        C("Interlock", "Digital", () => new InterlockPrim(), "in: clear, i1..iN · out: o1..oN", [19], [11]),
        C("Interlock-Toggle", "Digital", () => new InterlockTogglePrim(), "in: i1..iN, clear · out: o1..oN", [419], [207]),
        C("Ring Counter", "Digital", () => new RingCounterPrim(), "in: clock, reset · out: o1..oN", [17, 451], [23]),
        C("Decade", "Digital", () => new DecadePrim(), "in: analog · out: units 0-9, tens 0-9, hundreds 0-9", [78], [52]),
        C("Button Presser", "Digital", () => new FollowPrim(), "in: i · out: o1..oN follow the input", [99], [138]),
        C("Logic Wave Pulse", "Digital", () => new WavePulsePrim(), "in: i1..iN · out: one-wave pulse", [409, 1309], [204]),
        C("Logic Wave Delay", "Digital", () => new WaveDelayPrim(), "in: i1..iN · out: o1..oN · params: waves", [408], [203]),

        // ---------------------------------------------------------------- timers
        C("Oscillator", "Timers", () => new OscillatorPrim(), "in: enable · out: o · params: high time, low time", [8], [14]),
        C("One Shot", "Timers", () => new OneShotPrim(false), "in: trigger, -, cancel · out: o, o* · params: time", [9], [15]),
        C("Retriggerable One Shot", "Timers", () => new OneShotPrim(true), "in: trigger, -, cancel · out: o, o* · params: time", [10], [17]),
        C("Pulse Stretcher", "Timers", () => new StretchPrim(), "in: i · out: o · params: time", [11], [16]),
        C("Delay", "Timers", () => new DelayPrim(), "in: i · out: o · params: time", [16], [27]),
        I("Delay (Analog Time)", "Timers", () => new AnalogTimeDelayPrim(), "in: start, cancel, time (analog, 1/100 s) · out: o", [], [200]),
        D("Long Delay", "Timers", () => new DelayPrim(), "in: i · out: o · params: time"),
        C("Multiple One Shots", "Timers", () => new MultiOneShotPrim(), "in: t1..tN · out: o1..oN · params: time", [21], [29]),
        C("Debounce", "Timers", () => new DebouncePrim(), "in: i · out: o · params: time", [50], [56]),
        C("Stepper", "Timers", () => new StepperPrim(), "in: start · out: busy, o1..oN · params: delay, duration, ...", [98], [139]),
        C("Stepper with Progress & Reset", "Timers", () => new StepperProgressPrim(), "in: start, reset · out: running, ...", [777], [241]),

        // ---------------------------------------------------------------- analog
        C("Analog Initialize", "Analog", () => new InitPrim(), "in: trigger(s) · out: value(s) · params: values", [32], [38], "Analog Init", "Serial Initialize"),
        C("Analog Equate", "Analog", () => new EquatePrim(), "in: value, enable · out: o1..oN · params: values", [38], [51], "Equate") with { UnwiredHigh = static p => p == 2 },
        C("Analog Buffer", "Analog", () => new HoldBufferPrim(Core.SigKind.Analog), "in: enable, a1..aN · out: a1..aN (hold while disabled)", [46], [67]),
        C("Analog Scaler", "Analog", () => new AnalogScalerPrim(), "in: a · out: a · params: divisor, multiplier, offset", [24], [30]),
        C("Analog Scaler w/o Zero Pass", "Analog", () => new AnalogScalerSignedPrim(), "in: a · out: a", [65], [99]),
        C("Analog Scaler with I/O Limits", "Analog", () => new AnalogScalerLimitsPrim(), "in: a · out: a · params: in lo, in hi, out lo, out hi", [541], [224]),
        C("Analog RAM", "Analog", () => new AnalogRamPrim(), "in: a1..aN, store, recall, bank selects · out: a1..aN", [34], [36]),
        C("Analog Step", "Analog", () => new AnalogStepPrim(), "in: clock, reset · out: a · params: values", [45], [37]),
        C("Analog Ramp", "Analog", () => new AnalogRampPrim(), "in: up, down · out: a · params: full-range time", [22], [20]),
        C("Analog Ramp (Bounds Limited)", "Analog", () => new BoundedRampPrim(), "in: up, down · out: a · params: time, lower, upper, initial", [540], [223]),
        C("Analog Rate Limiter", "Analog", () => new RateLimiterPrim(), "in: a · out: a · params: full-range time", [66], [80]),
        C("Analog Flip", "Analog", () => new AnalogFlipPrim(), "in: a · out: 65535 - a", [58], [87]),
        C("Analog Sum", "Analog", () => new AnalogSumPrim(), "in: a1..aN, or digitals · out: sum (a high digital counts 1)", [57, 61], [100], "Digital Sum"),
        C("Analog DivMod", "Analog", () => new DivModPrim(), "in: a · out: quotient, remainder · params: divisor", [114], [136]),
        I("Analog Compare", "Analog", () => new AnalogComparePrim(), "in: a, b · out: a > b, a = b, a < b", [35], [45]),
        I("Preset", "Analog", () => new PresetPrim(), "in: trigger · out: v1..vN · params: -, v1..vN", [], [21]),
        I("Numeric Keypad", "Analog", () => new NumericKeypadPrim(), "in: 0-9, clear, up, down, enter · out: value, entered · params: max, min", [], [41]),
        C("Analog Comparison (Full Set)", "Analog", () => new ComparePrim(), "in: a, b · out: =, ≠, <, ≤, >, ≥", [492], [220]),
        C("Analog Increment", "Analog", () => new AnalogIncrementPrim(), "in: up, down, ... · out: a · params: step, hold, repeat, ...", [543], [230]),
        C("Analog Increment with Optional Feedback", "Analog", () => new AnalogIncrementPrim(), "in: up, down, -, feedback · out: a", [605], [236]),
        C("Analog Limiter (provisional)", "Analog", () => new AnalogClampPrim(), "in: lower, upper, value · out: clamped value", [], [237]),
        C("Analog to Digital", "Analog", () => new AnalogToDigitalPrim(), "in: a · out: bits, MSB first", [84], [106]),
        D("Digital to Analog", "Analog", () => new DigitalToAnalogPrim(), "in: bits, MSB first · out: a", "Binary to Analog"),
        D("Analog 2's Offset Converter", "Analog", () => new TwosOffsetPrim(), "in: a1..aN · out: a1..aN with bit 15 flipped"),
        D("Analog Negation", "Analog", () => new AnalogNegatePrim(), "in: a1..aN · out: -a1..-aN (two's complement)"),
        D("Analog Debounce", "Analog", () => new AnalogDebouncePrim(), "in: a · out: a once stable · params: time"),

        // ---------------------------------------------------------------- serial
        C("Serial Buffer", "Serial", () => new HoldBufferPrim(Core.SigKind.Serial), "in: enable, s1..sN · out: s1..sN (hold while disabled)", [56], [88]),
        C("Serial Send", "Serial", () => new SerialSendPrim(), "in: t1..tN · out: s · params: strings", [89], [58]),
        C("Multiple Serial Send", "Serial", () => new SerialSendPrim(), "in: trigger · out: s1..sN · params: strings", [1477], []),
        C("Multiple Discrete Serial Send", "Serial", () => new MultiSerialSendPrim(), "in: t1..tN · out: s1..sN · params: strings", [3106], [266]),
        C("Serial I/O", "Serial", () => new SerialIoPrim(), "in: rx, t1..tN · out: tx, m1..mN · params: strings", [101], [97]),
        C("Serial Gather", "Serial", () => new SerialGatherPrim(), "in: s · out: s · params: delimiter, max length", [62], [93]),
        C("Serial Concatenation", "Serial", () => new SerialMergePrim(), "in: s1..sN · out: s (whatever arrives)", [126], [122]),
        I("ASCII Serial Decoder", "Serial", () => new AsciiSerialDecoderPrim(), "in: text, play · out: busy, then char n at pin n + 2 · params: time per char", [54], [65]),
        I("ASCII Keypad", "Serial", () => new AsciiKeypadPrim(), "in: clear, enable, backspace, then char n at pin n + 4 · out: text · params: max length", [123], [109]),
        I("Digit Keypad", "Serial", () => new DigitKeypadPrim(), "in: clear, select, 0-9, *, # · out: text · params: max length", [], [63]),
        I("Password Memory", "Serial", () => new PasswordMemoryPrim(), "in: text, store, recall, enter · out: stored text · params: default", [], [66]),
        I("Password Match", "Serial", () => new PasswordMatchPrim(), "in: text, enter, enable, reference · out: match · params: password", [], [144]),
        D("Serial Force", "Serial", () => new NoOpPrim(), "in: s1..sN (no outputs; does nothing)") with { SmCodes = [470] },
        D("Serial Substring", "Serial", () => new SerialSubstringPrim(), "in: s · out: s · params: start (1-based), length"),
        C("Serial to Analog", "Serial", () => new SerialToAnalogPrim(), "in: s · out: a", [96], [103]),
        C("Analog to Serial", "Serial", () => new AnalogToSerialPrim(), "in: trigger, a1..aN · out: s · params: strings", [77], [82]),
        C("Serial/Analog One-Shot", "Serial", () => new ChangePulsePrim(), "in: s or a · out: pulse · params: time", [72], [114]),
        C("String Length", "Serial", () => new StringLengthPrim(), "in: s · out: a", [2506], [254]),
        C("Make String Permanent", "Serial", () => new PermanentPrim(), "in: s1..sN (kept across restarts)", [858], [205]),
        C("Intersystem Communications", "Serial", () => new XsigPrim(), "in: xsig, joins · out: xsig, joins · params: offset", [327], [85]),

        // ---------------------------------------------------------------- system
        C("Clock Driver", "System", () => new ClockPrim(), "out: time of day", [43, 3078], [62], "Clock Driver w/out DST"),
        C("When", "System", () => new WhenPrim(), "in: time of day · out: pulse · params: time, when", [74], [61]),
        C("Ethernet Offline Manager", "System", () => new ConstantLowPrim(), "out: offline / activity (always low)", [511, 633, 1069], [156], "Activity Detection"),
        D("Poll Manager", "System", () => new ConstantLowPrim(), "in: ... · out: offline (always low)") with { SmCodes = [345] },
        C("Console", "System", () => new ConsolePrim(), "in: command · out: reply", [], [202]),
        C("Set System Clock", "System", () => new NoOpPrim(), "in: hour up, hour down, minute up, minute down (follows the host clock; does nothing)", [154], [121]),
        C("Time-of-day Relay (provisional)", "System", () => new TimeOfDayRelayPrim(), "in: s · out: s", [], [101]),
        C("Touchpanel Sleep/Wake Manager", "System", () => new NoOpPrim(), "in: sleep, wake (does nothing)", [392], [161]),
    ];

    private static readonly Dictionary<string, LogicSymbol> ByNameMap = BuildByName();
    private static readonly Dictionary<int, LogicSymbol> BySmcMap = All.SelectMany(s => s.SmCodes.Select(c => (c, s))).ToDictionary(p => p.c, p => p.s);
    private static readonly Dictionary<int, LogicSymbol> ByTypeMap = All.SelectMany(s => s.LpzTypes.Select(t => (t, s))).ToDictionary(p => p.t, p => p.s);

    private static Dictionary<string, LogicSymbol> BuildByName()
    {
        var map = new Dictionary<string, LogicSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in All)
        {
            map.Add(s.Name, s);
            foreach (var a in s.Aliases) map.Add(a, s);
        }
        return map;
    }

    /// <summary>By library name or alias, ignoring case and surrounding spaces.</summary>
    /// <remarks>Some library names end in a period in .smw files ("Activity Detection."); that is ignored.</remarks>
    public static LogicSymbol? ByName(string? name)
    {
        if (name == null) return null;
        var n = name.Trim();
        return ByNameMap.TryGetValue(n, out var s) || ByNameMap.TryGetValue(n.TrimEnd('.').TrimEnd(), out s) ? s : null;
    }
    /// <summary>By .smw symbol code (SmC).</summary>
    public static LogicSymbol? BySmc(int code) => BySmcMap.GetValueOrDefault(code);
    /// <summary>By compiled primitive type.</summary>
    public static LogicSymbol? ByLpzType(int type) => ByTypeMap.GetValueOrDefault(type);

    /// <summary>The symbol table as Markdown: docs/symbols.md is this, and the tests keep it in step.</summary>
    public static string RenderMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# Logic symbols\n\n");
        sb.Append("Generated from `src/OpenVP.Lpz/LogicSymbols.cs` (`LogicSymbolCatalog`); the tests fail if this file and the catalog disagree. ");
        sb.Append("Regenerate with `OPENVP_UPDATE_GOLDEN=1 dotnet run --project tests/OpenVP.Tests Docs_symbols`.\n\n");
        sb.Append("Every symbol has one implementation, used both for `.smw` source and compiled `.lpz` programs.\n\n");
        sb.Append("- **compiled**: checked against compiled programs that ship their source: primitive type, pin order and parameters.\n");
        sb.Append("- **documented**: written from the SIMPL Windows symbol descriptions. It runs from `.smw` (matched by name). Pin order and parameter units are best guesses, ");
        sb.Append("and the compiled type is unknown until a real program shows it. To run one in a compiled program, name its type in `primitives.json`.\n");
        sb.Append("- **inferred**: the compiled type and pins were seen in real programs (a private test set, not in this repo), but the behavior is inferred from how those programs wire and name the pins, usually because the symbol sits inside a macro whose source isn't available. ");
        sb.Append("Each one's code comment (`src/OpenVP.Lpz/Primitives4.cs`) says what the inference rests on.\n\n");
        int Count(SymbolEvidence e) => All.Count(x => x.Evidence == e);
        sb.Append($"{All.Count} symbols: {Count(SymbolEvidence.Compiled)} compiled, {Count(SymbolEvidence.Inferred)} inferred, {Count(SymbolEvidence.Documented)} documented.\n");
        foreach (var group in All.GroupBy(x => x.Category))
        {
            sb.Append($"\n## {group.Key}\n\n");
            sb.Append("| Symbol | Evidence | SmC | Compiled type | Pins |\n|---|---|---|---|---|\n");
            foreach (var x in group)
            {
                var name = x.Aliases.Length > 0 ? $"{x.Name} (also: {string.Join(", ", x.Aliases)})" : x.Name;
                string List(int[] v) => v.Length == 0 ? "?" : string.Join(", ", v);
                sb.Append($"| {name} | {x.Evidence.ToString().ToLowerInvariant()} | {List(x.SmCodes)} | {List(x.LpzTypes)} | {x.Pins.Replace("|", "\\|")} |\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads a code map such as symbols.json or primitives.json: <c>{ "1234": "NAND" }</c>. Numbers that aren't
    /// keys and names the catalog doesn't know are skipped and reported in <paramref name="problems"/>.
    /// </summary>
    public static Dictionary<int, LogicSymbol> LoadMap(string path, List<string>? problems = null)
    {
        var result = new Dictionary<int, LogicSymbol>();
        if (!File.Exists(path)) return result;
        Dictionary<string, string>? raw;
        try { raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)); }
        catch (JsonException e) { problems?.Add($"{System.IO.Path.GetFileName(path)}: {e.Message}"); return result; }
        foreach (var (k, v) in raw ?? new())
        {
            if (!int.TryParse(k, out var code)) { problems?.Add($"{System.IO.Path.GetFileName(path)}: \"{k}\" is not a number"); continue; }
            if (ByName(v) is { } sym) result[code] = sym;
            else problems?.Add($"{System.IO.Path.GetFileName(path)}: no symbol named \"{v}\"");
        }
        return result;
    }
}
