using System.IO.Compression;
using System.Text;
using OpenVP.Lpz;

namespace OpenVP.Simpl;

/// <summary>
/// Surveys real SIMPL Windows programs to grow the logic symbol catalog. For every .smw (on its own, next to
/// a compiled program, or archived inside one) it counts the symbols by SmC code and library name and says
/// whether OpenVP runs them. For every .lpz it counts the compiled primitive types. Where a compiled program
/// comes with its source and signal names (.sig), it pairs each compiled record with the source symbol that
/// drives the same named signal, which identifies the symbol's SmC code and compiled type together.
/// The result is a report plus the symbols.json / primitives.json entries that would teach OpenVP the new codes.
/// </summary>
public sealed class SymbolSurvey
{
    public sealed class SourceSymbol
    {
        public int Code { get; init; }
        public string Name { get; init; } = "";
        public int Count { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CompiledType
    {
        public int Type { get; init; }
        public int Count { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A compiled record paired with the source symbol driving the same signal.</summary>
    public readonly record struct Match(int Code, string Name, int Type);

    public Dictionary<(int Code, string Name), SourceSymbol> Source { get; } = new();
    public Dictionary<int, CompiledType> Compiled { get; } = new();
    public Dictionary<Match, int> Matches { get; } = new();
    public List<string> Notes { get; } = new();
    public int SourceFiles { get; private set; }
    public int CompiledFiles { get; private set; }

    /// <summary>Survey every .smw and .lpz under the given files or folders.</summary>
    public static SymbolSurvey Run(IEnumerable<string> paths)
    {
        var survey = new SymbolSurvey();
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".smw", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase)));
            else if (File.Exists(p)) files.Add(p);
            else survey.Notes.Add($"not found: {p}");
        }
        // A .smw next to a .lpz of the same name is that program's source: surveyed with it, not twice.
        var lpzBases = files.Where(f => f.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.ChangeExtension(f, null)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (f.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase)) survey.AddCompiled(f);
                else if (!lpzBases.Contains(Path.ChangeExtension(f, null))) survey.AddSource(SmwFile.Load(f), Path.GetFileName(f));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                survey.Notes.Add($"{Path.GetFileName(f)}: {e.Message}");
            }
        }
        return survey;
    }

    private void AddSource(SmwFile smw, string file)
    {
        SourceFiles++;
        foreach (var s in smw.Symbols.Values)
        {
            if (s.Disabled) continue; // switched off in SIMPL Windows: never compiled
            if (!s.Inputs.Any(h => h > 0) && !s.Outputs.Any(h => h > 0)) continue; // folders, processor, empty slots
            var key = (s.Code, s.Name.Trim());
            if (!Source.TryGetValue(key, out var info)) Source[key] = info = new SourceSymbol { Code = s.Code, Name = s.Name.Trim() };
            info.Count++;
            info.Files.Add(file);
        }
    }

    private void AddCompiled(string path)
    {
        var file = Path.GetFileName(path);
        var pkg = LpzPackage.Open(path);
        CompiledFiles++;
        foreach (var sym in pkg.Logic.Symbols)
        {
            if (!Compiled.TryGetValue(sym.Type, out var info)) Compiled[sym.Type] = info = new CompiledType { Type = sym.Type };
            info.Count++;
            info.Files.Add(file);
        }

        // Source: next to the program, else archived inside it. Signal names likewise.
        SmwFile? smw = null;
        SigTable? sig = null;
        var smwPath = Path.ChangeExtension(path, ".smw");
        var sigPath = Path.ChangeExtension(path, ".sig");
        if (File.Exists(smwPath)) smw = SmwFile.Load(smwPath);
        if (File.Exists(sigPath)) sig = SigTable.Parse(File.ReadAllBytes(sigPath));
        if (smw == null || sig == null)
        {
            using var zip = ZipFile.OpenRead(path);
            var archive = zip.Entries.FirstOrDefault(e => e.Name.EndsWith("_archive.zip", StringComparison.OrdinalIgnoreCase));
            if (archive != null)
            {
                using var inner = new ZipArchive(new MemoryStream(ReadAll(archive)));
                var smwEntry = inner.Entries.FirstOrDefault(e => e.Name.EndsWith(".smw", StringComparison.OrdinalIgnoreCase));
                var sigEntry = inner.Entries.FirstOrDefault(e => e.Name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
                if (smw == null && smwEntry != null) smw = SmwFile.Parse(Encoding.Latin1.GetString(ReadAll(smwEntry)));
                // An archived .sig can be from the compile before; only use it if it agrees with this one.
                if (sig == null && sigEntry != null)
                {
                    var candidate = SigTable.Parse(ReadAll(sigEntry));
                    if (SigCheck.Matches(pkg.Logic, candidate, out _)) sig = candidate;
                    else Notes.Add($"{file}: the archived .sig is from another compile; put the .sig next to the program to pair symbols");
                }
            }
        }
        if (smw == null) { Notes.Add($"{file}: no source (.smw next to it or archived inside), so types can't be paired with symbols"); return; }
        AddSource(smw, file);
        if (sig == null) { Notes.Add($"{file}: no signal names (.sig), so types can't be paired with symbols"); return; }

        // Who drives each named signal in the source.
        var driver = new Dictionary<string, SmwSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in smw.Symbols.Values.OrderBy(s => s.Handle))
            foreach (var h in s.Outputs)
                if (h > 0 && smw.Signals.TryGetValue(h, out var sg) && sg.Name is { Length: > 0 } n && n != "0" && n != "1")
                    driver.TryAdd(n, s);
        foreach (var rec in pkg.Logic.Symbols)
        {
            foreach (var o in rec.Outputs)
            {
                if (o <= 1 || sig.NameOf(o) is not { } name || !driver.TryGetValue(name, out var src)) continue;
                var m = new Match(src.Code, src.Name.Trim(), rec.Type);
                Matches[m] = Matches.GetValueOrDefault(m) + 1;
                break;
            }
        }
    }

    private static byte[] ReadAll(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>The catalog symbol a source symbol would run as (by library name, then known code).</summary>
    public static LogicSymbol? Resolve(int code, string name) => LogicSymbolCatalog.ByName(name) ?? LogicSymbolCatalog.BySmc(code);

    /// <summary>SmC codes worth adding to symbols.json: the name is a catalog symbol, the code isn't known yet.</summary>
    public SortedDictionary<int, string> SuggestedSymbolCodes()
    {
        var map = new SortedDictionary<int, string>();
        foreach (var s in Source.Values)
            if (s.Code > 0 && LogicSymbolCatalog.ByName(s.Name) is { } sym && LogicSymbolCatalog.BySmc(s.Code) == null)
                map.TryAdd(s.Code, sym.Name);
        return map;
    }

    /// <summary>Compiled types worth adding to primitives.json: paired with a catalog symbol, type not known yet.</summary>
    public SortedDictionary<int, string> SuggestedPrimitiveTypes()
    {
        var map = new SortedDictionary<int, string>();
        foreach (var (m, _) in Matches.OrderByDescending(p => p.Value))
            if (m.Type >= 0 && !PrimitiveLibrary.Supports(m.Type) && !PrimitiveCatalog.Known.ContainsKey(m.Type) && Resolve(m.Code, m.Name) is { } sym)
                map.TryAdd(m.Type, sym.Name);
        return map;
    }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Surveyed {SourceFiles} source program(s) and {CompiledFiles} compiled program(s).");
        foreach (var n in Notes) sb.AppendLine($"  note: {n}");

        var runs = Source.Values.Where(s => Resolve(s.Code, s.Name) != null).ToList();
        var missing = Source.Values.Where(s => Resolve(s.Code, s.Name) == null).ToList();
        sb.AppendLine();
        sb.AppendLine($"Source symbols: {Source.Count} kinds; {runs.Count} run in OpenVP, {missing.Count} don't (devices are listed too: they run through the IP table, not the catalog).");
        sb.AppendLine();
        sb.AppendLine("  SmC    Count  Runs as                                   Name");
        foreach (var s in Source.Values.OrderBy(s => Resolve(s.Code, s.Name) == null ? 0 : 1).ThenByDescending(s => s.Count).ThenBy(s => s.Name))
        {
            var r = Resolve(s.Code, s.Name);
            var how = r == null ? "-" : r.Name + (r.Evidence == SymbolEvidence.Documented ? " (documented)" : "");
            sb.AppendLine($"  {s.Code,5}  {s.Count,5}  {how,-40}  {s.Name}");
        }

        if (Compiled.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Compiled types:");
            sb.AppendLine("   Type  Count  Status");
            foreach (var t in Compiled.Values.OrderBy(t => t.Type))
            {
                var status = t.Type < 0 || t.Type == 31 ? "hardware / header" :
                    LogicSymbolCatalog.ByLpzType(t.Type) is { } s ? $"runs as {s.Name}" :
                    PrimitiveCatalog.Known.TryGetValue(t.Type, out var k) && k.Implemented ? $"runs ({k.Name})" : "NOT SUPPORTED";
                sb.AppendLine($"  {t.Type,5}  {t.Count,5}  {status}");
            }
        }

        if (Matches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Pairs found (source symbol -> compiled type, by the signals they drive):");
            sb.AppendLine("  SmC    Type  Count  Name                                      Catalog");
            foreach (var (m, n) in Matches.OrderBy(p => p.Key.Type).ThenByDescending(p => p.Value))
            {
                var cat = LogicSymbolCatalog.ByLpzType(m.Type);
                var src = Resolve(m.Code, m.Name);
                var verdict = cat == null ? src == null ? "new symbol, no behavior yet" : $"new type for {src.Name}" :
                    src == null ? $"type known as {cat.Name}; add this name as an alias?" :
                    cat == src ? "agrees" : $"CONFLICT: type is {cat.Name}, source says {src.Name}";
                sb.AppendLine($"  {m.Code,5}  {m.Type,5}  {n,5}  {m.Name,-40}  {verdict}");
            }
        }

        var codes = SuggestedSymbolCodes();
        var types = SuggestedPrimitiveTypes();
        sb.AppendLine();
        sb.AppendLine("symbols.json (new SmC codes for catalog symbols):");
        sb.AppendLine(Json(codes));
        sb.AppendLine("primitives.json (new compiled types for catalog symbols):");
        sb.AppendLine(Json(types));
        return sb.ToString();
    }

    private static string Json(SortedDictionary<int, string> map) =>
        map.Count == 0 ? "  { }" : "{\n" + string.Join(",\n", map.Select(p => $"  \"{p.Key}\": {System.Text.Json.JsonSerializer.Serialize(p.Value)}")) + "\n}";
}
