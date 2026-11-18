using System.IO.Compression;
using System.Text;
using OpenVP.Simpl;

namespace OpenVP.Lpz;

/// <summary>
/// Collects the compiled record types OpenVP can't run yet across one or many .lpz programs, with example
/// records: pins by signal name (from the .sig), parameters, and, when the program ships its source, the
/// SIMPL symbol that drives the same output signals. That's what it takes to identify and implement a type.
/// </summary>
public sealed class LpzSurvey
{
    public sealed record Example(string Program, string Path, string[] Inputs, string[] Outputs, string[] Params, string? Source);

    public sealed class TypeStats(int type)
    {
        public int Type { get; } = type;
        public int Count { get; set; }
        public HashSet<string> Programs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Example> Examples { get; } = new();
        /// <summary>Source symbols matched to this type, with how often (the strongest hint to what it is).</summary>
        public Dictionary<string, int> SourceMatches { get; } = new();
        internal HashSet<string> Shapes { get; } = new();
    }

    private readonly Dictionary<int, TypeStats> _types = new();
    private readonly List<string> _errors = new();

    public int MaxExamples { get; init; } = 4;
    /// <summary>Also collect types OpenVP already runs (to double-check them against real programs).</summary>
    public bool IncludeRunnable { get; init; }
    public int Programs { get; private set; }
    public int Records { get; private set; }
    public IReadOnlyDictionary<int, TypeStats> Types => _types;
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Add one program. Uses <paramref name="sigPath"/>, else a .sig next to the .lpz, else the one in its source archive.</summary>
    public void Add(string lpzPath, string? sigPath = null)
    {
        LpzPackage pkg;
        try { pkg = LpzPackage.Open(lpzPath); }
        catch (Exception e) { _errors.Add($"{Path.GetFileName(lpzPath)}: {e.Message}"); return; }

        sigPath ??= FindSig(lpzPath, pkg);
        if (sigPath != null)
        {
            try { pkg.UseSignals(SigTable.Parse(File.ReadAllBytes(sigPath))); }
            catch (Exception e) { _errors.Add($"{Path.GetFileName(sigPath)}: {e.Message}"); }
        }
        Add(pkg, ReadSourceSmw(lpzPath));
    }

    public void Add(LpzPackage pkg, SmwFile? source = null)
    {
        Programs++;
        var name = pkg.ProgramName;
        var sig = pkg.Signals;
        var drivers = source == null ? null : SourceDrivers(source);

        foreach (var sym in pkg.Logic.Symbols)
        {
            Records++;
            if (sym.Type == 31) continue;
            // Hardware records with nothing to do (processor, slots, cards) count as running too.
            if (!IncludeRunnable && PrimitiveCatalog.Known.TryGetValue(sym.Type, out var k) && k.Implemented) continue;
            if (sym.Type == -48) continue; // SIMPL+ modules: a load problem, reported by the runtime, not a missing primitive

            if (!_types.TryGetValue(sym.Type, out var st)) _types[sym.Type] = st = new TypeStats(sym.Type);
            st.Count++;
            st.Programs.Add(name);

            string? match = null;
            if (drivers != null && sig != null)
                foreach (var o in sym.Outputs)
                    if (o > 1 && sig.NameOf(o) is string n && drivers.TryGetValue(n, out var src)) { match = src; break; }
            if (match != null) st.SourceMatches[match] = st.SourceMatches.GetValueOrDefault(match) + 1;

            // Keep examples that differ in shape (pin counts, parameter count, source match), so a handful shows the variety.
            var shape = $"{sym.Inputs.Length}/{sym.Outputs.Length}/{sym.Params.Count}/{match}";
            if (st.Examples.Count < MaxExamples && st.Shapes.Add(shape))
                st.Examples.Add(new Example(name, sym.Path,
                    sym.Inputs.Select(i => SigName(sig, i)).ToArray(),
                    sym.Outputs.Select(i => SigName(sig, i)).ToArray(),
                    sym.Params.Select(p => p.ToString()).Concat(OtherFields(sym)).ToArray(),
                    match));
        }
    }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"OpenVP .lpz survey: {Programs} program(s), {Records} compiled records.");
        var list = _types.Values.OrderByDescending(t => t.Programs.Count).ThenByDescending(t => t.Count).ToList();
        if (list.Count == 0)
        {
            sb.AppendLine(IncludeRunnable ? "No records found." : "Every record type in these programs runs in OpenVP.");
        }
        else
        {
            sb.AppendLine(IncludeRunnable ? "" : "Record types OpenVP can't run yet, most widespread first.");
            sb.AppendLine("Pins show signal names from the .sig (#n when unnamed; 0/1 are the constants).");
            foreach (var t in list)
            {
                PrimitiveCatalog.Known.TryGetValue(t.Type, out var info);
                sb.AppendLine();
                sb.AppendLine($"== type {t.Type}{(info != null ? $" ({info.Name}{(info.Implemented ? ", runs" : "")})" : " (unidentified)")}: " +
                              $"{t.Count} record(s) in {t.Programs.Count} program(s)");
                if (t.SourceMatches.Count > 0)
                    sb.AppendLine("   source symbols driving the same signals: " +
                                  string.Join(", ", t.SourceMatches.OrderByDescending(m => m.Value).Select(m => $"{m.Key} ×{m.Value}")));
                foreach (var e in t.Examples)
                {
                    sb.AppendLine($"   - {e.Program} {e.Path}{(e.Source != null ? $"  [source: {e.Source}]" : "")}");
                    sb.AppendLine($"       in  ({e.Inputs.Length}): {string.Join(", ", e.Inputs)}");
                    sb.AppendLine($"       out ({e.Outputs.Length}): {string.Join(", ", e.Outputs)}");
                    if (e.Params.Length > 0) sb.AppendLine($"       params: {string.Join(", ", e.Params)}");
                }
            }
        }
        if (_errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Couldn't read:");
            foreach (var e in _errors) sb.AppendLine("   " + e);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ helpers

    private static string SigName(SigTable? sig, int index) => index switch
    {
        0 => "0",
        1 => "1",
        _ => sig?.NameOf(index) is string n ? ShortName(n) : $"#{index}"
    };

    /// <summary>Module-internal names look like "::local:S-2.1:S-5.0000ABCD"; keep the readable part.</summary>
    private static string ShortName(string n)
    {
        if (!n.StartsWith("::", StringComparison.Ordinal)) return n;
        var end = n.IndexOf(":S-", 2, StringComparison.Ordinal);
        return end > 2 ? n[..end] : n;
    }

    /// <summary>Fields that aren't the path, parameters or parameter count, shown as tag=value.</summary>
    private static IEnumerable<string> OtherFields(BinSymbol sym) =>
        sym.Fields.Where(f => f.Tag is not (2 or 3 or 4 or 15))
            .Select(f => $"f{f.Tag}={(f.Value.Length == 4 ? BitConverter.ToInt32(f.Value).ToString() : Convert.ToHexString(f.Value))}");

    /// <summary>Output signal name → "Symbol name (SmC n, params...)" for every symbol in the source.</summary>
    private static Dictionary<string, string> SourceDrivers(SmwFile smw)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in smw.Symbols.Values)
        {
            var label = $"{s.Name} (SmC {s.Code}{(s.Parameters.Count > 0 ? "; " + string.Join(", ", s.Parameters.Take(6)) + (s.Parameters.Count > 6 ? ", …" : "") : "")})";
            foreach (var h in s.Outputs)
            {
                var name = smw.SignalName(h);
                if (name.Length == 0 || name.StartsWith('#') || name is "0" or "1") continue; // unnamed or constant
                map.TryAdd(name, label);
            }
        }
        return map;
    }

    private static string? FindSig(string lpzPath, LpzPackage pkg)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(lpzPath))!;
        foreach (var candidate in new[] { Path.ChangeExtension(lpzPath, ".sig"), Path.Combine(dir, pkg.ProgramName + ".sig") })
            if (File.Exists(candidate)) return candidate;
        return null; // the package may still have one in its source archive (already loaded by Open)
    }

    /// <summary>The top-level .smw from the program's source archive, if it ships one.</summary>
    public static SmwFile? ReadSourceSmw(string lpzPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(lpzPath);
            var archive = zip.Entries.FirstOrDefault(e => e.Name.EndsWith("_archive.zip", StringComparison.OrdinalIgnoreCase));
            if (archive == null) return null;
            using var ms = new MemoryStream();
            using (var s = archive.Open()) s.CopyTo(ms);
            ms.Position = 0;
            using var inner = new ZipArchive(ms);
            var smw = inner.Entries.Where(e => e.Name.EndsWith(".smw", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Length).FirstOrDefault();
            if (smw == null) return null;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var r = new StreamReader(smw.Open(), Encoding.GetEncoding(1252)); // .smw files are Windows-1252
            return SmwFile.Parse(r.ReadToEnd());
        }
        catch (Exception) { return null; }
    }
}
