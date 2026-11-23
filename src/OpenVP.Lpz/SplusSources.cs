using System.IO.Compression;
using System.Text;

namespace OpenVP.Lpz;

/// <summary>A SIMPL+ source file the console can show: where it was found, its name and size.</summary>
public sealed record SplusSourceFile(string Name, string Origin, long Size);

/// <summary>A compiled SIMPL+ module in a program and the source file that goes with it, when there is one.</summary>
public sealed record SplusModuleSource(string Dll, long Size, string? Source);

/// <summary>
/// Finds the SIMPL+ source (<c>.usp</c> and its includes) that goes with a program, so the console can show it.
/// Two places are looked at: the folder the program is in (a source file loaded or copied along with it) and the
/// <c>*_archive.zip</c> inside a compiled <c>.lpz</c>. A caller never passes a path: it names a file from the list
/// returned by <see cref="List"/>, which holds plain file names only, so nothing outside those two places can be read.
/// </summary>
public static class SplusSources
{
    public const long MaxBytes = 1_000_000;
    private static readonly string[] Extensions = { ".usp", ".usl", ".inc", ".h" };
    private const int MaxFiles = 500;

    public static bool IsSourceName(string name) => Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    /// <summary>A name that is just a file name: no folders, no dots-only parts, no characters a file system treats specially.</summary>
    public static bool IsPlainName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 150 && name != "." && name != ".." &&
        name.IndexOfAny(new[] { '/', '\\', ':', '\0', '*', '?', '"', '<', '>', '|' }) < 0 && IsSourceName(name);

    /// <summary>Every source file found for the program; the program's folder wins over the archive for the same name.</summary>
    public static IReadOnlyList<SplusSourceFile> List(string? programPath)
    {
        var found = new Dictionary<string, SplusSourceFile>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(programPath)) return Array.Empty<SplusSourceFile>();
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(programPath));
            if (dir != null && Directory.Exists(dir))
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    if (IsPlainName(f.Name) && f.Length <= MaxBytes && found.Count < MaxFiles && f.LinkTarget == null)
                        found[f.Name] = new SplusSourceFile(f.Name, "folder", f.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var e in ArchiveEntries(programPath))
            if (IsPlainName(e.Name) && e.Length <= MaxBytes && found.Count < MaxFiles && !found.ContainsKey(e.Name))
                found[e.Name] = new SplusSourceFile(e.Name, "archive", e.Length);
        return found.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The text of one file from <see cref="List"/>, or null when there is no such file.</summary>
    public static string? Read(string? programPath, string? name, out SplusSourceFile? file)
    {
        file = null;
        if (!IsPlainName(name)) return null;
        file = List(programPath).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (file == null) return null;
        var found = file;
        try
        {
            if (file.Origin == "folder")
                return Decode(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(programPath!))!, file.Name)));
            using var zip = OpenArchive(programPath);
            var entry = zip?.Entries.FirstOrDefault(e => e.Length <= MaxBytes && string.Equals(e.Name, found.Name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            using var s = entry.Open(); using var ms = new MemoryStream(); s.CopyTo(ms);
            return Decode(ms.ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { file = null; return null; }
    }

    /// <summary>The program's compiled SIMPL+ modules, each with its source file when one matches by name
    /// (<c>S2_Volume.dll</c> and <c>Volume.dll</c> both match <c>Volume.usp</c>).</summary>
    public static IReadOnlyList<SplusModuleSource> Modules(IEnumerable<SplusModule> modules, IReadOnlyList<SplusSourceFile> sources)
    {
        var usp = sources.Where(s => s.Name.EndsWith(".usp", StringComparison.OrdinalIgnoreCase)).ToList();
        return modules.Select(m => new SplusModuleSource(m.FileName, m.Size, Match(m.FileName, usp)?.Name)).ToList();
    }

    private static SplusSourceFile? Match(string dll, List<SplusSourceFile> usp)
    {
        var stem = Path.GetFileNameWithoutExtension(dll);
        var bare = System.Text.RegularExpressions.Regex.Replace(stem, @"^S\d+_", "");
        return usp.FirstOrDefault(s => string.Equals(Path.GetFileNameWithoutExtension(s.Name), stem, StringComparison.OrdinalIgnoreCase))
            ?? usp.FirstOrDefault(s => string.Equals(Path.GetFileNameWithoutExtension(s.Name), bare, StringComparison.OrdinalIgnoreCase));
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }   // SIMPL+ files are often ANSI
    }

    private static ZipArchive? OpenArchive(string? programPath)
    {
        if (programPath == null || !programPath.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase) || !File.Exists(programPath)) return null;
        try
        {
            using var outer = ZipFile.OpenRead(programPath);
            var a = outer.Entries.FirstOrDefault(e => e.Name.EndsWith("_archive.zip", StringComparison.OrdinalIgnoreCase) && e.Length <= 200_000_000);
            if (a == null) return null;
            using var s = a.Open(); var ms = new MemoryStream(); s.CopyTo(ms); ms.Position = 0;
            return new ZipArchive(ms);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<ZipArchiveEntry> ArchiveEntries(string? programPath)
    {
        // The archive is read into memory, so the entries are listed from a copy that stays open for the caller.
        var zip = OpenArchive(programPath);
        return zip == null ? Array.Empty<ZipArchiveEntry>() : zip.Entries.ToList();
    }
}
