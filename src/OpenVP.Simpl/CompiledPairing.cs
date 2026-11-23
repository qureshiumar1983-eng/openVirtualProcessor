using OpenVP.Lpz;

namespace OpenVP.Simpl;

/// <summary>
/// How a .smw symbol finds its compiled records (see SimplRuntime.Compiled.cs): a symbol's compiled form sits at its
/// position in the symbol tree below the Logic folder (S-3.2.1 = Logic's 3rd child, its 2nd child, that one's 1st
/// child). The runtime's borrowing and the wiring check (<see cref="WiringCheck"/>) share this.
/// </summary>
public static class CompiledPairing
{
    /// <summary>The top-level "Logic" folder symbol, or null.</summary>
    public static SmwSymbol? LogicRoot(SmwFile file) =>
        file.Symbols.Values.FirstOrDefault(s => (s.Parent <= 0 || !file.Symbols.ContainsKey(s.Parent)) && s.Name.Trim() == "Logic");

    /// <summary>Compiled path (S-1, S-3.2, ...) of every symbol under Logic, by symbol handle.</summary>
    public static Dictionary<int, string> SymbolPaths(SmwFile file)
    {
        var paths = new Dictionary<int, string>();
        var logic = LogicRoot(file);
        if (logic == null) return paths;
        var seen = new HashSet<int>();
        void Walk(int handle, string path)
        {
            if (!seen.Add(handle) || !file.Symbols.TryGetValue(handle, out var s)) return;
            paths[handle] = path;
            for (int i = 0; i < s.Children.Count; i++) Walk(s.Children[i], $"{path}.{i + 1}");
        }
        for (int i = 0; i < logic.Children.Count; i++) Walk(logic.Children[i], $"S-{i + 1}");
        return paths;
    }

    /// <summary>The compiled records of each symbol path: a primitive or SIMPL+ module is the one record at its path,
    /// a macro is every record under "path:".</summary>
    public static Dictionary<string, List<BinSymbol>> Scopes(LpzPackage pkg)
    {
        var scopes = new Dictionary<string, List<BinSymbol>>(StringComparer.Ordinal);
        foreach (var r in pkg.Logic.Symbols)
        {
            if (!r.Path.StartsWith("S-", StringComparison.Ordinal)) continue;
            var colon = r.Path.IndexOf(':');
            var key = colon < 0 ? r.Path : r.Path[..colon];
            (scopes.TryGetValue(key, out var l) ? l : scopes[key] = new()).Add(r);
        }
        return scopes;
    }
}
