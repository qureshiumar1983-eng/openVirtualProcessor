using System.IO.Compression;
using System.Text;

namespace OpenVP.Lpz;

/// <summary>An IP table entry from the .dip file.</summary>
public sealed record IpTableEntry(int IpId, string Address);

/// <summary>A SIMPL+ module compiled to a .NET assembly inside the .lpz.</summary>
public sealed record SplusModule(string FileName, long Size);

/// <summary>
/// A compiled SIMPL Windows program (.lpz). It is a zip holding:
///   boot.bt        INI: logic file name, symbol/signal counts, target rack, versions
///   *.bin          the compiled logic (see <see cref="BinProgram"/>)
///   *.rte, *.cdm, *.spp, *.cloud, *.dsc   routing, device and SIMPL+ parameter tables
///   *.dip          INI: IP table
///   *.dll          SIMPL+ modules compiled to .NET, plus SIMPL# libraries they use
///   SplusManagerApp.exe, SplusLibrary.dll, SplusObjects.dll, SimplSharp*.dll   Crestron's runtime (not used by OpenVP)
///   *_archive.zip  optional: the program's source (.smw, .umc, .usp) when archived at compile
/// </summary>
public sealed class LpzPackage
{
    private static readonly HashSet<string> CrestronRuntime = new(StringComparer.OrdinalIgnoreCase)
    {
        "SplusManagerApp.exe", "SplusLibrary.dll", "SplusObjects.dll", "Newtonsoft.Json.Compact.dll",
        "SimplSharpData.dat", "SimplSharpData.dat.der"
    };

    public string Path { get; private init; } = string.Empty;

    /// <summary>Use a .sig from outside the package (written next to the .smw at compile time).</summary>
    public void UseSignals(SigTable? table) => Signals = table;
    public Dictionary<string, string> Boot { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<IpTableEntry> IpTable { get; } = new();
    /// <summary>Device model per IP ID, from the .dsc file (e.g. 03 → TS-1080).</summary>
    public Dictionary<int, string> DeviceModels { get; } = new();
    /// <summary>Signal names: the .sig inside the source archive, if the package has one.</summary>
    public SigTable? Signals { get; private set; }
    public List<SplusModule> SplusModules { get; } = new();
    public List<string> Files { get; } = new();
    public BinProgram Logic { get; private set; } = new();
    public string? SourceArchive { get; private set; }

    public string ProgramName => System.IO.Path.GetFileNameWithoutExtension(Boot.GetValueOrDefault("SMWFileName") ?? Path);
    public string Target => Boot.GetValueOrDefault("RackType") ?? "?";

    public static LpzPackage Open(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var pkg = new LpzPackage { Path = path };
        foreach (var e in zip.Entries) pkg.Files.Add(e.FullName);

        var boot = zip.Entries.FirstOrDefault(e => e.Name.Equals("boot.bt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Not a compiled SIMPL Windows program: boot.bt is missing.");
        foreach (var (k, v) in ReadIni(boot)) pkg.Boot[k] = v;

        var binName = pkg.Boot.GetValueOrDefault("BinFileName");
        var bin = zip.Entries.FirstOrDefault(e => e.Name.Equals(binName, StringComparison.OrdinalIgnoreCase))
            ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("No logic (.bin) file in the package.");
        pkg.Logic = BinProgram.Parse(ReadAll(bin));

        var dip = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".dip", StringComparison.OrdinalIgnoreCase));
        if (dip != null)
        {
            var ini = ReadIni(dip).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; ini.TryGetValue($"id{i}", out var id); i++)
                pkg.IpTable.Add(new IpTableEntry(Convert.ToInt32(id, 16), ini.GetValueOrDefault($"addr{i}") ?? ""));
        }

        var dsc = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".dsc", StringComparison.OrdinalIgnoreCase));
        if (dsc != null)
        {
            // "Slot : 2.IP-ID : 03;3;Touchpanels(Enet);TS-1080;;TS-1080;-12"
            foreach (var line in Encoding.Latin1.GetString(ReadAll(dsc)).Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"IP-ID : ([0-9A-Fa-f]{2});[^;]*;[^;]*;([^;]*);");
                if (m.Success) pkg.DeviceModels[Convert.ToInt32(m.Groups[1].Value, 16)] = m.Groups[2].Value.Trim();
            }
        }

        foreach (var e in zip.Entries)
        {
            if (e.Name.EndsWith("_archive.zip", StringComparison.OrdinalIgnoreCase))
            {
                pkg.SourceArchive = e.Name;
                try
                {
                    using var inner = new ZipArchive(new MemoryStream(ReadAll(e)));
                    var sig = inner.Entries.FirstOrDefault(x => x.Name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
                    if (sig != null) pkg.Signals = SigTable.Parse(ReadAll(sig));
                }
                catch (InvalidDataException) { /* archive unreadable: names are optional */ }
            }
            else if (e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !CrestronRuntime.Contains(e.Name) &&
                     !e.Name.StartsWith("SimplSharp", StringComparison.OrdinalIgnoreCase))
                pkg.SplusModules.Add(new SplusModule(e.Name, e.Length));
        }
        return pkg;
    }

    /// <summary>Extract the program's own assemblies (SIMPL+ modules, SIMPL# libraries) to a folder.</summary>
    public void ExtractAssemblies(string dir)
    {
        Directory.CreateDirectory(dir);
        using var zip = ZipFile.OpenRead(Path);
        foreach (var e in zip.Entries)
        {
            // Everything but the SIMPL+ runtime itself: Crestron libraries OpenVP has facades for are
            // extracted too but never loaded (the load context prefers the facade).
            if (!e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                e.Name is "SplusLibrary.dll" or "SplusObjects.dll" or "SmartThreadPoolCE.dll") continue;
            var target = System.IO.Path.Combine(dir, e.Name);
            try { e.ExtractToFile(target, overwrite: true); }
            catch (IOException)
            {
                // A file still held by an earlier load (Windows): write beside it and swap in.
                var tmp = target + ".new";
                e.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, target, overwrite: true);
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

    private static IEnumerable<KeyValuePair<string, string>> ReadIni(ZipArchiveEntry e)
    {
        var text = Encoding.Latin1.GetString(ReadAll(e));
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq > 0 && !line.StartsWith('[')) yield return new(line[..eq], line[(eq + 1)..]);
        }
    }
}
