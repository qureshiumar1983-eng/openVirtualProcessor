using System.Text;

namespace OpenVP.Lpz;

/// <summary>Human-readable summary of a compiled program and how much of it OpenVP can run.</summary>
public static class LpzReport
{
    public static string Build(LpzPackage pkg, SigTable? sig = null)
    {
        var sb = new StringBuilder();
        var logic = pkg.Logic;
        sb.AppendLine($"Program:   {pkg.ProgramName}");
        sb.AppendLine($"Target:    {pkg.Target}   compiled {pkg.Boot.GetValueOrDefault("CompileDateTime")} with {pkg.Boot.GetValueOrDefault("SourceEnvironmentVersion")}");
        sb.AppendLine($"Logic:     {logic.Symbols.Count} compiled symbols (boot.bt says {pkg.Boot.GetValueOrDefault("NumberSymbols")}), " +
                      $"signals up to #{logic.MaxSignal} (boot.bt says {pkg.Boot.GetValueOrDefault("NumberSignals")})");
        sb.AppendLine($"IP table:  {string.Join(", ", pkg.IpTable.Select(e => e.IpId.ToString("X2")))}");
        sb.AppendLine($"SIMPL+ / SIMPL# assemblies: {pkg.SplusModules.Count}");
        sb.AppendLine($"Source archive inside: {(pkg.SourceArchive != null ? "yes" : "no")}");
        if (sig != null) sb.AppendLine($"Signal names (.sig): {sig.Entries.Count}");

        var byType = logic.Symbols.GroupBy(s => s.Type).OrderByDescending(g => g.Count()).ToList();
        var runnable = logic.Symbols.Count(s => PrimitiveCatalog.Known.TryGetValue(s.Type, out var k) && k.Implemented);
        sb.AppendLine();
        sb.AppendLine($"Runnable today: {runnable} of {logic.Symbols.Count} symbols ({100.0 * runnable / Math.Max(1, logic.Symbols.Count):0.#}%)");
        sb.AppendLine();
        sb.AppendLine("Type   Count  Status          Working name");
        foreach (var g in byType)
        {
            PrimitiveCatalog.Known.TryGetValue(g.Key, out var info);
            var status = info == null ? "unidentified" : info.Implemented ? "runs" : "identified";
            sb.AppendLine($"{g.Key,5}  {g.Count(),5}  {status,-14}  {info?.Name}");
        }
        return sb.ToString();
    }
}
