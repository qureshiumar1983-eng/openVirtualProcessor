using OpenVP.Core;
using OpenVP.Lpz;

namespace OpenVP.Simpl;

// What the program contains, for the readiness check and the summary under the program name.
public sealed partial class SimplRuntime : IProgramAnalysis
{
    string? IProgramAnalysis.Blocker => _file.Symbols.Count == 0
        ? "No SIMPL Windows symbols were found in this file, so it isn't a SIMPL Windows program (or it is empty)." : null;

    IReadOnlyList<ReadinessProblem> IProgramAnalysis.Problems() =>
        Unsupported.Select(s => new ReadinessProblem("SIMPL symbol", $"{s.Name} (SmC {s.Code})", $"pins in={s.Inputs.Count} out={s.Outputs.Count}")).ToList();

    public ProgramSummary Summarize()
    {
        var panels = new List<string>();
        var eiscs = new List<string>();
        var other = 0;
        foreach (var d in _devices.Where(d => d.Registered))
        {
            var id = $"{d.IpId:X2}";
            switch (DeviceKinds.Classify(d.Symbol.Name))
            {
                case DeviceClass.Panel: panels.Add(id); break;
                case DeviceClass.Eisc: eiscs.Add(id); break;
                default: other++; break;
            }
        }
        var signals = 0;
        for (var h = 1; h < _kindKnown.Length; h++) if (_kindKnown[h] && !_constant[h]) signals++;
        return new ProgramSummary(Kind,
            panels.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(), eiscs.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(), other,
            _ports.Where(p => p.Kind == PortKind.Com).Select(p => p.Path).Distinct().Count(),
            _ports.Where(p => p.Kind == PortKind.Ir).Select(p => p.Path).Distinct().Count(),
            _borrowedModules.Count, _compiled == null ? 0 : LpzRuntime.CountMacros(_compiled.Logic), signals);
    }
}
