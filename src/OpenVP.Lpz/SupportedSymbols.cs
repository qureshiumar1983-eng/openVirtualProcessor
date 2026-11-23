using System.Text.RegularExpressions;

namespace OpenVP.Lpz;

/// <summary>
/// Whether an entry of the unsupported-symbols log (docs/unsupported.md) is something OpenVP runs now. The server
/// gives this to <c>UnsupportedLog.SetSupportCheck</c> so rows written by an older build disappear from the file
/// and from the compatibility dashboard once the catalog learns the symbol.
/// </summary>
public static partial class SupportedSymbols
{
    [GeneratedRegex(@"^(?<name>.*?)\s*\(SmC (?<code>-?\d+)\)\s*$")]
    private static partial Regex SymbolName();

    [GeneratedRegex(@"^type (?<type>-?\d+)\b")]
    private static partial Regex TypeName();

    /// <summary>True for a "SIMPL symbol" row whose library name or SmC code the catalog has, and for a
    /// "Compiled primitive" row whose type it can run. Hardware, sub-devices, user modules and everything else
    /// are never counted as supported here.</summary>
    public static bool IsSupported(string kind, string name)
    {
        if (kind.Equals("SIMPL symbol", StringComparison.OrdinalIgnoreCase) && SymbolName().Match(name) is { Success: true } m)
            return LogicSymbolCatalog.ByName(m.Groups["name"].Value) != null
                || (int.TryParse(m.Groups["code"].Value, out var code) && LogicSymbolCatalog.BySmc(code) != null);
        if (kind.Equals("Compiled primitive", StringComparison.OrdinalIgnoreCase) && TypeName().Match(name) is { Success: true } t)
            return int.TryParse(t.Groups["type"].Value, out var type) && PrimitiveLibrary.Supports(type);
        return false;
    }
}
