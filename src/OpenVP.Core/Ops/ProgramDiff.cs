using System.Text;

namespace OpenVP.Core.Ops;

public sealed record ProfileDevice(string IpId, string Type, string Description, string? Address = null);
public sealed record ProfileSignal(string Name, string Kind);
/// <param name="Name">The symbol's name in the program.</param>
/// <param name="Code">Its SIMPL type code (SmC), the symbol's type.</param>
/// <param name="Params">The symbol's parameters, as text.</param>
/// <param name="Wiring">The signals wired to it (in and out), as text.</param>
public sealed record ProfileSymbol(string Name, int Code, string Params, string Wiring);
public sealed record ProfileFile(string Name, long Size, string Hash);

/// <summary>What a program contains, read without starting it: the input to <see cref="ProgramDiff"/>.</summary>
public sealed record ProgramProfile(
    string Label, string Kind, string ProgramName, string? SummaryText,
    IReadOnlyList<ProfileDevice> Devices, IReadOnlyList<ProfileSignal> Signals, IReadOnlyList<ProfileSymbol> Symbols,
    IReadOnlyList<string> Modules, IReadOnlyList<ProfileFile> Files, IReadOnlyList<string> Notes)
{
    /// <summary>True when the symbol list is real (a SIMPL Windows source file), not just empty.</summary>
    public bool HasSymbols => Symbols.Count > 0;
}

/// <summary>One difference. <paramref name="Change"/> is <c>added</c>, <c>removed</c> or <c>changed</c>.</summary>
public sealed record DiffItem(string Name, string Change, string? Detail = null);

public sealed record DiffSection(string Title, IReadOnlyList<DiffItem> Items)
{
    public int Added => Items.Count(i => i.Change == "added");
    public int Removed => Items.Count(i => i.Change == "removed");
    public int Changed => Items.Count(i => i.Change == "changed");
}

public sealed record DiffReport(string A, string B, bool Identical, IReadOnlyList<DiffSection> Sections, IReadOnlyList<string> Summary, IReadOnlyList<string> Notes)
{
    /// <summary>The report as plain text.</summary>
    public string Text()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Comparing {A} with {B}");
        sb.AppendLine(Identical ? "No differences found." : string.Join(" ", Summary));
        foreach (var s in Sections.Where(s => s.Items.Count > 0))
        {
            sb.AppendLine().AppendLine($"{s.Title} ({s.Added} added, {s.Removed} removed, {s.Changed} changed)");
            foreach (var i in s.Items) sb.AppendLine($"  {(i.Change == "added" ? "+" : i.Change == "removed" ? "-" : "~")} {i.Name}{(i.Detail is { Length: > 0 } ? " — " + i.Detail : "")}");
        }
        foreach (var n in Notes) sb.AppendLine().AppendLine("Note: " + n);
        return sb.ToString();
    }
}

/// <summary>Compares two <see cref="ProgramProfile"/>s: devices and IP IDs, signals, symbols (SIMPL Windows source), SIMPL+ modules and files.</summary>
public static class ProgramDiff
{
    public static DiffReport Compare(ProgramProfile a, ProgramProfile b)
    {
        var sections = new List<DiffSection>
        {
            Devices(a, b), Signals(a, b), Symbols(a, b), Modules(a, b), Files(a, b)
        };
        var notes = new List<string>();
        if (a.Kind != b.Kind) notes.Add($"The programs are different kinds ({a.Kind} and {b.Kind}), so most of the list will differ.");
        foreach (var n in a.Notes.Concat(b.Notes).Distinct()) notes.Add(n);
        if (a.HasSymbols != b.HasSymbols) notes.Add("Only one of the two has a symbol list (a SIMPL Windows source file), so symbols aren't compared. Signals, devices and modules are.");
        var summary = new List<string>();
        foreach (var s in sections.Where(s => s.Items.Count > 0))
        {
            var parts = new List<string>();
            if (s.Added > 0) parts.Add($"{s.Added} added");
            if (s.Removed > 0) parts.Add($"{s.Removed} removed");
            if (s.Changed > 0) parts.Add($"{s.Changed} changed");
            summary.Add($"{s.Title}: {string.Join(", ", parts)}.");
        }
        return new DiffReport(a.Label, b.Label, sections.All(s => s.Items.Count == 0), sections, summary, notes);
    }

    private static string Id(string ipId) => ipId.ToUpperInvariant();

    private static DiffSection Devices(ProgramProfile a, ProgramProfile b)
    {
        var items = new List<DiffItem>();
        var da = a.Devices.GroupBy(d => Id(d.IpId)).ToDictionary(g => g.Key, g => g.First());
        var db = b.Devices.GroupBy(d => Id(d.IpId)).ToDictionary(g => g.Key, g => g.First());
        foreach (var k in db.Keys.Except(da.Keys).Order()) items.Add(new DiffItem($"IP ID {k}", "added", Describe(db[k])));
        foreach (var k in da.Keys.Except(db.Keys).Order()) items.Add(new DiffItem($"IP ID {k}", "removed", Describe(da[k])));
        foreach (var k in da.Keys.Intersect(db.Keys).Order())
        {
            var x = da[k]; var y = db[k];
            var d = new List<string>();
            if (x.Type != y.Type) d.Add($"type {x.Type} to {y.Type}");
            if (x.Description != y.Description) d.Add($"{x.Description} to {y.Description}");
            if (x.Address != y.Address) d.Add($"address {x.Address ?? "none"} to {y.Address ?? "none"}");
            if (d.Count > 0) items.Add(new DiffItem($"IP ID {k}", "changed", string.Join("; ", d)));
        }
        return new DiffSection("Devices and IP IDs", items);
    }

    private static string Describe(ProfileDevice d) => string.Join(", ", new[] { d.Type, d.Description, d.Address }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static DiffSection Signals(ProgramProfile a, ProgramProfile b)
    {
        var items = new List<DiffItem>();
        var sa = a.Signals.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.Ordinal);
        var sb = b.Signals.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.Ordinal);
        foreach (var k in sb.Keys.Except(sa.Keys).Order(StringComparer.Ordinal)) items.Add(new DiffItem(k, "added", sb[k].ToLowerInvariant()));
        foreach (var k in sa.Keys.Except(sb.Keys).Order(StringComparer.Ordinal)) items.Add(new DiffItem(k, "removed", sa[k].ToLowerInvariant()));
        foreach (var k in sa.Keys.Intersect(sb.Keys).Order(StringComparer.Ordinal))
            if (sa[k] != sb[k]) items.Add(new DiffItem(k, "changed", $"{sa[k].ToLowerInvariant()} to {sb[k].ToLowerInvariant()}"));
        return new DiffSection("Signals", items);
    }

    private static DiffSection Symbols(ProgramProfile a, ProgramProfile b)
    {
        var items = new List<DiffItem>();
        if (!a.HasSymbols || !b.HasSymbols) return new DiffSection("Symbols", items);
        var ga = a.Symbols.GroupBy(s => (s.Name, s.Code)).ToDictionary(g => g.Key, g => g.ToList());
        var gb = b.Symbols.GroupBy(s => (s.Name, s.Code)).ToDictionary(g => g.Key, g => g.ToList());
        string Label((string Name, int Code) k) => k.Code == 0 ? k.Name : $"{k.Name} (type {k.Code})";
        foreach (var k in gb.Keys.Union(ga.Keys).OrderBy(k => k.Name, StringComparer.Ordinal).ThenBy(k => k.Code))
        {
            var la = ga.GetValueOrDefault(k) ?? new(); var lb = gb.GetValueOrDefault(k) ?? new();
            if (la.Count == 0) items.Add(new DiffItem(Label(k), "added", lb.Count > 1 ? $"{lb.Count} of them" : null));
            else if (lb.Count == 0) items.Add(new DiffItem(Label(k), "removed", la.Count > 1 ? $"{la.Count} of them" : null));
            else if (la.Count != lb.Count) items.Add(new DiffItem(Label(k), lb.Count > la.Count ? "added" : "removed", $"{la.Count} to {lb.Count} of them"));
            else
            {
                // Same number of them: match the ones that are identical, then pair up what is left over (a changed parameter or a rewired pin).
                var left = la.ToList();
                var right = new List<ProfileSymbol>();
                foreach (var y in lb)
                {
                    var i = left.FindIndex(x => x.Params == y.Params && x.Wiring == y.Wiring);
                    if (i >= 0) left.RemoveAt(i); else right.Add(y);
                }
                for (var i = 0; i < left.Count && i < right.Count && i < 25; i++)
                {
                    var d = new List<string>();
                    if (left[i].Params != right[i].Params) d.Add($"parameters {Quote(left[i].Params)} to {Quote(right[i].Params)}");
                    if (left[i].Wiring != right[i].Wiring) d.Add(WiringChange(left[i].Wiring, right[i].Wiring));
                    items.Add(new DiffItem(Label(k), "changed", string.Join("; ", d)));
                }
            }
        }
        return new DiffSection("Symbols", items);
    }

    private static string Quote(string s)
    {
        if (s.Length == 0) return "(none)";
        var t = s.Length > 60 ? s[..60] + "…" : s;
        return t.StartsWith('"') ? t : "\"" + t + "\"";
    }

    private static readonly System.Text.RegularExpressions.Regex WireRe = new(@"^in\((?<i>.*)\) out\((?<o>.*)\)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // "in(a,b) out(c)" before and after: which pins now go to other signals.
    private static string WiringChange(string a, string b)
    {
        var ma = WireRe.Match(a); var mb = WireRe.Match(b);
        if (!ma.Success || !mb.Success) return $"wiring {a} to {b}";
        var parts = new List<string>();
        foreach (var (label, ga, gb) in new[] { ("input", ma.Groups["i"].Value, mb.Groups["i"].Value), ("output", ma.Groups["o"].Value, mb.Groups["o"].Value) })
        {
            var la = ga.Split(',', StringSplitOptions.RemoveEmptyEntries); var lb = gb.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var gone = la.Except(lb).ToList(); var added = lb.Except(la).ToList();
            if (gone.Count == 0 && added.Count == 0) { if (ga != gb) parts.Add($"{label} pins reordered"); continue; }
            parts.Add($"{label}{(Math.Max(gone.Count, added.Count) > 1 ? "s" : "")} {(gone.Count > 0 ? "no longer " + string.Join(", ", gone) : "")}{(gone.Count > 0 && added.Count > 0 ? ", now " : added.Count > 0 ? "now " : "")}{string.Join(", ", added)}");
        }
        return "wiring: " + string.Join("; ", parts);
    }

    private static DiffSection Modules(ProgramProfile a, ProgramProfile b)
    {
        var items = new List<DiffItem>();
        foreach (var m in b.Modules.Except(a.Modules, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)) items.Add(new DiffItem(m, "added"));
        foreach (var m in a.Modules.Except(b.Modules, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)) items.Add(new DiffItem(m, "removed"));
        return new DiffSection("Modules and macros", items);
    }

    private static DiffSection Files(ProgramProfile a, ProgramProfile b)
    {
        var items = new List<DiffItem>();
        var fa = a.Files.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var fb = b.Files.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var k in fb.Keys.Except(fa.Keys).Order()) items.Add(new DiffItem(k, "added", Size(fb[k].Size)));
        foreach (var k in fa.Keys.Except(fb.Keys).Order()) items.Add(new DiffItem(k, "removed", Size(fa[k].Size)));
        foreach (var k in fa.Keys.Intersect(fb.Keys).Order())
            if (fa[k].Hash != fb[k].Hash) items.Add(new DiffItem(k, "changed", fa[k].Size == fb[k].Size ? $"content changed ({Size(fa[k].Size)})" : $"{Size(fa[k].Size)} to {Size(fb[k].Size)}"));
        return new DiffSection("Files", items);
    }

    private static string Size(long b) => b < 1024 ? $"{b} bytes" : b < 1048576 ? $"{b / 1024.0:0.#} KB" : $"{b / 1048576.0:0.#} MB";
}
