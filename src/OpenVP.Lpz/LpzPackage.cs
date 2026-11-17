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
    public Dictionary<string, string> Boot { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<IpTableEntry> IpTable { get; } = new();
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

        foreach (var e in zip.Entries)
        {
            if (e.Name.EndsWith("_archive.zip", StringComparison.OrdinalIgnoreCase)) pkg.SourceArchive = e.Name;
            else if (e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !CrestronRuntime.Contains(e.Name) &&
                     !e.Name.StartsWith("SimplSharp", StringComparison.OrdinalIgnoreCase))
                pkg.SplusModules.Add(new SplusModule(e.Name, e.Length));
        }
        return pkg;
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
