using System.Security.Cryptography;
using OpenVP.Core;
using OpenVP.Core.Ops;
using OpenVP.Lpz;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

namespace OpenVP.Server;

/// <summary>
/// Reads what a program contains, for comparing two of them, without starting anything. Like <see cref="ReadinessChecker"/> it loads a copy of the program into a throwaway one-slot
/// processor that is never started, and takes the signals, devices and summary the runtime found. For a SIMPL Windows source file it also reads every symbol (name, type, parameters,
/// wiring) from the file itself; for a compiled <c>.lpz</c> it adds the IP table and the SIMPL+ modules from the package.
/// </summary>
public sealed class ProgramProfiler
{
    private readonly string _dataDirectory;
    private readonly SemaphoreSlim _gate = new(2, 2);

    public ProgramProfiler(string dataDirectory) { _dataDirectory = dataDirectory; }

    /// <summary>Profiles the program <paramref name="main"/> in <paramref name="folder"/> (with its .sig and sidecar files).</summary>
    public ProgramProfile Build(string label, string folder, string main)
    {
        var name = Path.GetFileName(main);
        var notes = new List<string>();
        var files = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new ProfileFile(Path.GetFileName(f), new FileInfo(f).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))[..16])).ToList()
            : new List<ProfileFile>();
        var scratch = Directory.CreateTempSubdirectory("openvp-profile").FullName;
        _gate.Wait();
        try
        {
            var copy = Path.Combine(scratch, "program");
            Directory.CreateDirectory(copy);
            foreach (var f in Directory.EnumerateFiles(folder)) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)));
            var path = Path.Combine(copy, name);
            if (!File.Exists(path)) throw new FileNotFoundException($"{name} isn't in the version's files.");

            string kind = Path.GetExtension(name).ToLowerInvariant() switch { ".smw" => "SIMPL Windows", ".lpz" => "SIMPL Windows", _ => "SIMPL#" };
            string? summary = null;
            var devices = new List<ProfileDevice>();
            var signals = new List<ProfileSignal>();
            var symbols = new List<ProfileSymbol>();
            var modules = new List<string>();

            using (var p = new Processor(Path.Combine(scratch, "data"), 1))
            {
                p.AddLoader(new SimplSharpLoader());
                p.AddLoader(new SimplLoader(Path.Combine(_dataDirectory, "symbols.json")));
                p.AddLoader(new LpzLoader(Path.Combine(_dataDirectory, "primitives.json")));
                try
                {
                    p.LoadProgram(1, path, start: false);
                    var rt = p.Slot(1).Runtime!;
                    kind = rt.Kind;
                    try { foreach (var s in rt.GetSignals()) signals.Add(new ProfileSignal(s.Name, s.Kind.ToString())); } catch (Exception) { }
                    foreach (var d in p.Slot(1).Devices.OrderBy(d => d.IpId)) devices.Add(new ProfileDevice($"{d.IpId:X2}", d.Type ?? "", d.Description ?? ""));
                    if (rt is IProgramAnalysis a)
                    {
                        var sum = a.Summarize();
                        summary = sum.Text;
                        if (devices.Count == 0)
                        {
                            foreach (var id in sum.PanelIpIds) devices.Add(new ProfileDevice(id, "Touch panel", ""));
                            foreach (var id in sum.EiscIpIds) devices.Add(new ProfileDevice(id, "EISC", ""));
                        }
                        if (!sum.DevicesKnown)
                        {
                            notes.Add("A SIMPL# program creates its devices and signals when it runs, so only the device types it refers to are compared, not its IP IDs or signals.");
                            foreach (var u in sum.Uses ?? []) modules.Add("uses " + u);
                        }
                    }
                }
                catch (Exception e)
                {
                    notes.Add($"{name} couldn't be read as a program ({Readiness.Explain(e)}), so only its files are compared.");
                }
            }

            if (path.EndsWith(".smw", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var smw = SmwFile.Load(path);
                    foreach (var s in smw.Symbols.Values.OrderBy(s => s.Handle))
                    {
                        if (s.Disabled) continue;
                        string Names(IEnumerable<int> hs) => string.Join(",", hs.Where(h => h != 0).Select(smw.SignalName));
                        symbols.Add(new ProfileSymbol(s.Name, s.Code, string.Join(" | ", s.Parameters), $"in({Names(s.Inputs)}) out({Names(s.Outputs)})"));
                    }
                }
                catch (Exception e) { notes.Add($"The symbols in {name} couldn't be read: {e.Message}"); }
            }
            else if (path.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var pkg = LpzPackage.Open(path);
                    foreach (var m in pkg.SplusModules) modules.Add(m.FileName);
                    foreach (var ip in pkg.IpTable)
                    {
                        var id = $"{ip.IpId:X2}";
                        var i = devices.FindIndex(d => d.IpId.Equals(id, StringComparison.OrdinalIgnoreCase));
                        var model = pkg.DeviceModels.GetValueOrDefault(ip.IpId) ?? "";
                        if (i >= 0) devices[i] = devices[i] with { Address = ip.Address };
                        else devices.Add(new ProfileDevice(id, model, "", ip.Address));
                    }
                    notes.Add("A compiled program has no symbol list to compare; load the .smw source next to the .lpz to compare symbols.");
                }
                catch (Exception e) { notes.Add($"The package {name} couldn't be read: {e.Message}"); }
            }

            return new ProgramProfile(label, kind, Path.GetFileNameWithoutExtension(name), summary, devices, signals, symbols, modules, files, notes);
        }
        finally
        {
            _gate.Release();
            try { Directory.Delete(scratch, true); } catch { }
        }
    }
}
