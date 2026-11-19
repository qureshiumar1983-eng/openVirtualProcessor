using OpenVP.Lpz;

namespace OpenVP.Simpl;

/// <summary>
/// Picks the logic symbol for each .smw symbol. Behaviors come from <see cref="LogicSymbolCatalog"/>, the
/// same primitives the compiled-program engine runs, so a symbol behaves the same from source or compiled.
/// The .smw format identifies symbols by a numeric code (SmC) that Crestron doesn't publish, so matching is
/// by code when it is known (the catalog, or symbols.json) and otherwise by the symbol's library name.
/// </summary>
public sealed class SymbolLibrary
{
    private readonly Dictionary<int, LogicSymbol> _byCode = new();

    /// <summary>Problems found reading code maps (unknown names, bad keys), for the program log.</summary>
    public List<string> Problems { get; } = new();

    public IEnumerable<string> KnownNames => LogicSymbolCatalog.All.Select(s => s.Name);

    /// <summary>Teach the library a symbol code, e.g. from symbols.json: 1234 → "NAND". Returns false if no
    /// symbol has that name.</summary>
    public bool MapCode(int code, string symbolName)
    {
        if (LogicSymbolCatalog.ByName(symbolName) is not { } s) return false;
        _byCode[code] = s;
        return true;
    }

    /// <summary>Read a symbols.json: <c>{ "1234": "NAND" }</c>. Missing files are fine.</summary>
    public void LoadMap(string jsonPath)
    {
        foreach (var (k, v) in LogicSymbolCatalog.LoadMap(jsonPath, Problems)) _byCode[k] = v;
    }

    /// <summary>The catalog symbol for a .smw symbol, or null if OpenVP has no behavior for it. A code from
    /// symbols.json wins, then the symbol's library name (Nm), then the catalog's known codes. The name comes
    /// before the code because codes are only known from the programs seen so far, while a name is exact.</summary>
    public LogicSymbol? Resolve(SmwSymbol s) =>
        _byCode.GetValueOrDefault(s.Code) ?? LogicSymbolCatalog.ByName(s.Name) ?? LogicSymbolCatalog.BySmc(s.Code);
}
