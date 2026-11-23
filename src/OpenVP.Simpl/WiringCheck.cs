using OpenVP.Core;
using OpenVP.Lpz;

namespace OpenVP.Simpl;

/// <summary>One pin pairing seen across a symbol type's instances: .smw pin → compiled pin, and the compiled pin
/// OpenVP's model has for that .smw pin (0 = the model ignores that .smw pin).</summary>
public sealed record WiringPair(string Direction, int SmwPin, int CompiledPin, int ExpectedCompiledPin, int Count);

/// <summary>One instance that disagrees, named by its symbol path (S-2.1), never by signal.</summary>
public sealed record WiringExample(string Path, string Problem);

/// <summary>The result for one symbol type (one compiled primitive type).</summary>
public sealed class WiringTypeReport
{
    public string Name { get; set; } = "";
    public int LpzType { get; set; }
    /// <summary>"agree", "disagree", "ambiguous" (every instance wired so a pairing can't be told), "no-evidence" (nothing
    /// named wired) or "unmodelled" (OpenVP has no model for the compiled type).</summary>
    public string Status { get; set; } = "no-evidence";
    public int Instances { get; set; }
    public int Agree { get; set; }
    public int Disagree { get; set; }
    public int Ambiguous { get; set; }
    public int NoEvidence { get; set; }
    /// <summary>Instances per kind of problem: order, count, kind, identity.</summary>
    public Dictionary<string, int> Problems { get; set; } = new();
    /// <summary>What OpenVP's model says about the pins (the catalog's description), when it has one.</summary>
    public string? Model { get; set; }
    /// <summary>The library names of the .smw symbols paired with this compiled type (for a type OpenVP doesn't model).</summary>
    public List<string> SmwNames { get; set; } = new();
    public List<WiringPair> Pairs { get; set; } = new();
    public List<WiringExample> Examples { get; set; } = new();
}

public sealed class WiringReport
{
    public string Program { get; set; } = "";
    public string Summary { get; set; } = "";
    /// <summary>Symbol types with at least one instance checked against a model.</summary>
    public int TypesChecked { get; set; }
    public int TypesAgree { get; set; }
    public int TypesDisagree { get; set; }
    public int TypesAmbiguous { get; set; }
    public int TypesNoEvidence { get; set; }
    public int TypesUnmodelled { get; set; }
    public int InstancesChecked { get; set; }
    /// <summary>Logic symbols that are a macro / a SIMPL+ module / have no compiled record at their path (not checked).</summary>
    public int Macros { get; set; }
    public int Modules { get; set; }
    public int Unpaired { get; set; }
    public List<WiringTypeReport> Types { get; set; } = new();
}

/// <summary>
/// Compares the pin order OpenVP assumes for each logic symbol with the one a real program shows. For a program that
/// ships its .smw, .lpz and .sig, a compiled record's input and output signals have names (from the .sig) that match
/// the .smw symbol's pin signals, so which compiled pin sits behind which .smw pin can be read off. OpenVP's model is:
/// the compiled record's order is the primitive's order, and the .smw lists the pins in the same order unless the
/// catalog's <see cref="LogicSymbol.SmwPinOrder"/> says otherwise. Anything observed that differs is reported.
/// Symbols are paired with their compiled records by <see cref="CompiledPairing"/>, as the runtime does.
/// </summary>
public static class WiringCheck
{
    /// <param name="model">The compiled type's catalog entry; defaults to <see cref="LogicSymbolCatalog.ByLpzType"/> (plus
    /// <paramref name="overrides"/>). A test can hand in a different one to see the check flag it.</param>
    public static WiringReport Analyze(SmwFile file, LpzPackage pkg, Func<int, LogicSymbol?>? model = null,
        IReadOnlyDictionary<int, LogicSymbol>? overrides = null)
    {
        model ??= t => LogicSymbolCatalog.ByLpzType(t) ?? (overrides != null && overrides.TryGetValue(t, out var o) ? o : null);
        var report = new WiringReport { Program = pkg.ProgramName };
        var sig = pkg.Signals ?? throw new InvalidOperationException("The wiring check needs the .sig (signal names).");
        var paths = CompiledPairing.SymbolPaths(file);
        var scopes = CompiledPairing.Scopes(pkg);
        var library = new SymbolLibrary();
        var types = new Dictionary<int, TypeAcc>();

        foreach (var s in file.Symbols.Values.OrderBy(x => x.Handle))
        {
            if (s.Disabled || !paths.TryGetValue(s.Handle, out var path)) continue;
            if (!scopes.TryGetValue(path, out var records)) { if (s.Children.Count == 0 && IsLogic(s)) report.Unpaired++; continue; }
            var rec = records.FirstOrDefault(r => r.Path == path);
            if (rec == null) { report.Macros++; continue; }
            if (rec.Type < 0 && rec.Type != -48) continue;               // hardware
            if (rec.Type == -48) { report.Modules++; continue; }
            if (rec.Type is 213 or 214 or 215) continue;                 // crosspoints run as their own symbols
            if (!types.TryGetValue(rec.Type, out var acc)) types[rec.Type] = acc = new TypeAcc(rec.Type, model(rec.Type));
            acc.Check(file, sig, s, path, rec, library);
        }

        foreach (var acc in types.Values.OrderBy(a => a.Report.Name, StringComparer.OrdinalIgnoreCase))
        {
            var t = acc.Finish();
            report.Types.Add(t);
            report.InstancesChecked += t.Status == "unmodelled" ? 0 : t.Instances;
            switch (t.Status)
            {
                case "unmodelled": report.TypesUnmodelled++; continue;
                case "agree": report.TypesAgree++; break;
                case "disagree": report.TypesDisagree++; break;
                case "ambiguous": report.TypesAmbiguous++; break;
                default: report.TypesNoEvidence++; break;
            }
            report.TypesChecked++;
        }
        report.Summary = SummaryOf(report);
        return report;
    }

    private static bool IsLogic(SmwSymbol s) => s.Inputs.Any(h => h > 0) || s.Outputs.Any(h => h > 0);

    private static string SummaryOf(WiringReport r)
    {
        if (r.TypesChecked == 0 && r.TypesUnmodelled == 0) return "No logic symbols with a compiled record to check.";
        var parts = new List<string>();
        if (r.TypesAgree > 0) parts.Add($"{r.TypesAgree} agree");
        if (r.TypesDisagree > 0) parts.Add(r.TypesDisagree == 1 ? "1 disagrees" : $"{r.TypesDisagree} disagree");
        if (r.TypesAmbiguous > 0) parts.Add($"{r.TypesAmbiguous} ambiguous");
        if (r.TypesNoEvidence > 0) parts.Add($"{r.TypesNoEvidence} without evidence");
        if (r.TypesUnmodelled > 0) parts.Add($"{r.TypesUnmodelled} not modelled");
        return $"{r.TypesChecked + r.TypesUnmodelled} symbol type{(r.TypesChecked + r.TypesUnmodelled == 1 ? "" : "s")} checked: {string.Join(", ", parts)}";
    }

    /// <summary>Companion files of a program: the .smw, .lpz and .sig that belong together (same name next to each other,
    /// else the only one of its kind in the folder). Null names what is missing.</summary>
    public static (string? Smw, string? Lpz, string? Sig, string? Missing) FindFiles(string programPath)
    {
        var full = Path.GetFullPath(programPath);
        var dir = Path.GetDirectoryName(full)!;
        string? Find(string ext)
        {
            var same = Path.ChangeExtension(full, ext);
            if (File.Exists(same)) return same;
            var all = Directory.GetFiles(dir, "*" + ext);
            return all.Length == 1 ? all[0] : null;
        }
        var smw = Find(".smw"); var lpz = Find(".lpz");
        var sig = lpz != null && File.Exists(Path.ChangeExtension(lpz, ".sig")) ? Path.ChangeExtension(lpz, ".sig") : Find(".sig");
        var missing = new List<string>();
        if (smw == null) missing.Add(".smw");
        if (lpz == null) missing.Add(".lpz");
        if (sig == null) missing.Add(".sig");
        return (smw, lpz, sig, missing.Count == 0 ? null : string.Join(", ", missing));
    }

    /// <summary>Loads the three files and analyses them. Throws <see cref="InvalidOperationException"/> naming what is missing.</summary>
    public static WiringReport AnalyzeFiles(string programPath)
    {
        var (smw, lpz, sig, missing) = FindFiles(programPath);
        if (missing != null) throw new InvalidOperationException($"The wiring check needs the .smw, .lpz and .sig together (missing: {missing}).");
        var pkg = LpzPackage.Open(lpz!);
        pkg.UseSignals(SigTable.Parse(File.ReadAllBytes(sig!)));
        if (!SigCheck.Matches(pkg.Logic, pkg.Signals!, out _))
            throw new InvalidOperationException("The .sig doesn't belong to this .lpz (its signal names don't match the compile).");
        var problems = new List<string>();
        var overrides = LogicSymbolCatalog.LoadMap(Path.ChangeExtension(lpz!, ".primitives.json"), problems);
        return Analyze(SmwFile.Load(smw!), pkg, null, overrides);
    }

    // ------------------------------------------------------------------ per type

    private sealed class TypeAcc
    {
        public WiringTypeReport Report { get; } = new();
        private readonly LogicSymbol? _logic;
        private readonly Primitive? _prim;
        private readonly Dictionary<(string, int, int, int), int> _pairs = new();
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

        public TypeAcc(int type, LogicSymbol? logic)
        {
            _logic = logic;
            Report.LpzType = type;
            Report.Name = logic?.Name ?? $"type {type}";
            Report.Model = logic?.Pins;
            try { _prim = logic?.Create(); } catch { _prim = null; }
        }

        public void Check(SmwFile file, SigTable sig, SmwSymbol s, string path, BinSymbol rec, SymbolLibrary library)
        {
            Report.Instances++;
            if (_logic == null)
            {
                if (_names.Add(s.Name.Trim()) && Report.SmwNames.Count < 5) Report.SmwNames.Add(s.Name.Trim());
                if (Report.Examples.Count < 5) Report.Examples.Add(new WiringExample(path, $"compiled type {rec.Type} has no model ({s.Inputs.Count} in, {s.Outputs.Count} out in the .smw; {rec.Inputs.Length} in, {rec.Outputs.Length} out compiled)"));
                return;
            }

            var problems = new List<string>();
            var details = new List<string>();
            int definite = 0, ambiguous = 0;

            // Which .smw pin each compiled input pin is, per the model.
            int[] inOrder = _logic.SmwPinOrder?.Invoke(s.Inputs.Count, s.Outputs.Count) ?? Enumerable.Range(1, s.Inputs.Count).ToArray();
            var expectedIn = new int[s.Inputs.Count + 1];          // smw pin → compiled pin
            for (int k = 0; k < inOrder.Length; k++)
                if (inOrder[k] >= 1 && inOrder[k] <= s.Inputs.Count) expectedIn[inOrder[k]] = k + 1;
            var expectedOut = Enumerable.Range(0, s.Outputs.Count + 1).ToArray();

            if (library.Resolve(s) is { } smwLogic && smwLogic.LpzTypes.Length > 0 && !smwLogic.LpzTypes.Contains(rec.Type))
            {
                problems.Add("identity");
                details.Add($"the .smw symbol is a {smwLogic.Name}, the compiled record a {_logic.Name}");
            }
            // The compiler drops trailing pins nothing is wired to, so fewer compiled pins than the .smw has is fine unless the
            // missing pin is wired in the .smw; more compiled pins than the model has (a spare, a pin it doesn't know) is not.
            if (rec.Inputs.Length > inOrder.Length || rec.Outputs.Length > s.Outputs.Count)
            {
                problems.Add("count");
                details.Add($"{rec.Inputs.Length} compiled input(s) / {rec.Outputs.Length} output(s), OpenVP expects at most {inOrder.Length} / {s.Outputs.Count} for a .smw with {s.Inputs.Count} / {s.Outputs.Count}");
            }
            for (int i = 1; i <= s.Inputs.Count; i++)
                if (IsWired(file, s.Inputs[i - 1]) && expectedIn[i] > rec.Inputs.Length)
                {
                    if (!problems.Contains("count")) problems.Add("count");
                    details.Add($"smw I{i} is wired but the compiled record has only {rec.Inputs.Length} input(s)");
                }
            for (int i = 1; i <= s.Outputs.Count; i++)
                if (IsWired(file, s.Outputs[i - 1]) && i > rec.Outputs.Length)
                {
                    if (!problems.Contains("count")) problems.Add("count");
                    details.Add($"smw O{i} is wired but the compiled record has only {rec.Outputs.Length} output(s)");
                }

            foreach (var (dir, smwPins, compiled, expected) in new[]
            {
                ("in", s.Inputs, rec.Inputs, expectedIn), ("out", s.Outputs, rec.Outputs, expectedOut)
            })
            {
                var smwByName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < smwPins.Count; i++)
                    if (smwPins[i] > 0 && file.Signals.TryGetValue(smwPins[i], out var sg) && sg.Name.Length > 0 && sg.Name is not ("0" or "1"))
                        (smwByName.TryGetValue(sg.Name, out var l) ? l : smwByName[sg.Name] = new()).Add(i + 1);
                var compByName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < compiled.Length; j++)
                    if (compiled[j] > 1 && sig.NameOf(compiled[j]) is { Length: > 0 } n && !n.StartsWith("::", StringComparison.Ordinal))
                        (compByName.TryGetValue(n, out var l) ? l : compByName[n] = new()).Add(j + 1);

                var bad = new List<string>();
                foreach (var (name, sp) in smwByName)
                {
                    if (!compByName.TryGetValue(name, out var cp)) continue;
                    if (sp.Count == 1 && cp.Count == 1)
                    {
                        definite++;
                        int i = sp[0], j = cp[0], e = expected[i];
                        _pairs[(dir, i, j, e)] = _pairs.GetValueOrDefault((dir, i, j, e)) + 1;
                        if (e != j) bad.Add($"smw {(dir == "in" ? "I" : "O")}{i}→compiled {j} (OpenVP expects {(e == 0 ? "none" : e.ToString())})");
                        // Kind: the compiled signal's kind against the model's pin kind and the .smw's pin group.
                        SigKind? observed = sig.Entries.TryGetValue(compiled[j - 1], out var en) ? KindOf(en) : null;
                        var modelKind = dir == "in" ? _prim?.InputKind(j) : _prim?.OutputKind(j);
                        if (observed != null && modelKind != null && modelKind != observed)
                        {
                            if (!problems.Contains("kind")) problems.Add("kind");
                            details.Add($"compiled {dir} pin {j} is {observed.ToString()!.ToLowerInvariant()}, OpenVP's model has {modelKind.ToString()!.ToLowerInvariant()}");
                        }
                    }
                    else
                    {
                        ambiguous++;
                        // The same signal on several pins: only the model's own pairing can be checked, and it must at least land on pins carrying that signal.
                        var targets = sp.Select(p => expected[p]).ToList();
                        if (targets.Any(t => t == 0 || !cp.Contains(t)) || targets.Distinct().Count() != targets.Count)
                            bad.Add($"a signal on smw {(dir == "in" ? "I" : "O")}{string.Join("+", sp)} is on compiled {string.Join("+", cp)}; OpenVP expects {string.Join("+", targets)}");
                    }
                }
                if (bad.Count > 0) { if (!problems.Contains("order")) problems.Add("order"); details.AddRange(bad); }
            }

            if (problems.Count > 0)
            {
                Report.Disagree++;
                foreach (var p in problems) Report.Problems[p] = Report.Problems.GetValueOrDefault(p) + 1;
                if (Report.Examples.Count < 5) Report.Examples.Add(new WiringExample(path, string.Join("; ", details.Take(6))));
            }
            else if (definite > 0) Report.Agree++;
            else if (ambiguous > 0) Report.Ambiguous++;
            else Report.NoEvidence++;
        }

        private static bool IsWired(SmwFile file, int handle) =>
            handle > 0 && file.Signals.TryGetValue(handle, out var sg) && sg.Name.Length > 0 && sg.Name is not ("0" or "1");

        private static SigKind KindOf(SigEntry e) => ((int)e.Kind & 3) switch { 1 => SigKind.Analog, 2 => SigKind.Serial, _ => SigKind.Digital };

        public WiringTypeReport Finish()
        {
            var r = Report;
            r.Status = _logic == null ? "unmodelled" : r.Disagree > 0 ? "disagree" : r.Agree > 0 ? "agree" : r.Ambiguous > 0 ? "ambiguous" : "no-evidence";
            r.Pairs = _pairs.Select(p => new WiringPair(p.Key.Item1, p.Key.Item2, p.Key.Item3, p.Key.Item4, p.Value))
                .OrderBy(p => p.Direction == "in" ? 0 : 1).ThenBy(p => p.SmwPin).ThenBy(p => p.CompiledPin).Take(60).ToList();
            return r;
        }
    }
}
