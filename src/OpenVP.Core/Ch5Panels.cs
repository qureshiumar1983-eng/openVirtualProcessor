using System.IO.Compression;
using System.Text.Json;

namespace OpenVP.Core;

public sealed record Ch5Info(int Slot, uint IpId, string Name, DateTimeOffset UploadedAt, int Files, long Bytes);

/// <summary>Thrown for an archive that is refused (not a zip, too big, unsafe paths, no index.html).</summary>
public sealed class Ch5Exception(string message) : Exception(message);

/// <summary>
/// HTML5 / CH5 panel projects uploaded as a <c>.ch5z</c> (a zip). Each is extracted to
/// <c>data/panels/slotNN/ipidXX/</c> and served to people holding a share link for that slot and IP ID.
/// Extraction refuses paths that leave the folder (zip-slip), absolute paths, and archives that expand past
/// the limits. The project's own files are never trusted to name where they go.
/// </summary>
public sealed class Ch5Panels
{
    public const long MaxArchiveBytes = 100L * 1024 * 1024;
    public const long MaxExtractedBytes = 300L * 1024 * 1024;
    public const int MaxFiles = 10_000;

    private readonly string _root;

    public Ch5Panels(string dataDirectory) => _root = Path.GetFullPath(Path.Combine(dataDirectory, "panels"));

    public string DirFor(int slot, uint ipId) => Path.Combine(_root, $"slot{slot:00}", $"ipid{ipId:X2}");
    private string MetaFor(int slot, uint ipId) => DirFor(slot, ipId) + ".json";

    public bool Exists(int slot, uint ipId) => File.Exists(Path.Combine(DirFor(slot, ipId), "index.html"));

    public Ch5Info? Get(int slot, uint ipId)
    {
        if (!Exists(slot, ipId)) return null;
        try
        {
            var m = JsonSerializer.Deserialize<Ch5Info>(File.ReadAllText(MetaFor(slot, ipId)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (m != null) return m;
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new Ch5Info(slot, ipId, "", DateTimeOffset.MinValue, 0, 0);
    }

    public IReadOnlyList<Ch5Info> List()
    {
        var res = new List<Ch5Info>();
        if (!Directory.Exists(_root)) return res;
        foreach (var sd in Directory.GetDirectories(_root, "slot*"))
        {
            if (!int.TryParse(Path.GetFileName(sd)[4..], out var slot)) continue;
            foreach (var pd in Directory.GetDirectories(sd, "ipid*"))
            {
                if (!uint.TryParse(Path.GetFileName(pd)[4..], System.Globalization.NumberStyles.HexNumber, null, out var ip)) continue;
                var info = Get(slot, ip);
                if (info != null) res.Add(info);
            }
        }
        return res.OrderBy(i => i.Slot).ThenBy(i => i.IpId).ToList();
    }

    public bool Remove(int slot, uint ipId)
    {
        var dir = DirFor(slot, ipId);
        var had = Directory.Exists(dir);
        if (had) Directory.Delete(dir, true);
        try { File.Delete(MetaFor(slot, ipId)); } catch (IOException) { }
        return had;
    }

    /// <summary>
    /// Extract a project archive for this slot and IP ID, replacing any project already there. The archive is
    /// extracted to a scratch folder first and swapped in only if it is good.
    /// </summary>
    public Ch5Info Install(int slot, uint ipId, Stream archive, string? name = null)
    {
        if (slot < 1) throw new ArgumentException("Slot must be 1 or more.");
        if (ipId is 0 or > 0xFE) throw new ArgumentException("IP ID must be 01-FE.");
        var parent = Path.GetDirectoryName(DirFor(slot, ipId))!;
        Directory.CreateDirectory(parent);
        var scratch = Path.Combine(parent, $".incoming-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(scratch);
            var (files, bytes) = Extract(archive, scratch);
            var indexDir = FindIndexDir(scratch) ?? throw new Ch5Exception("The archive has no index.html. A CH5 project's start page is index.html.");
            var final = DirFor(slot, ipId);
            if (Directory.Exists(final)) Directory.Delete(final, true);
            // If the project sits inside one folder in the zip, serve from that folder.
            Directory.Move(indexDir, final);
            var info = new Ch5Info(slot, ipId, string.IsNullOrWhiteSpace(name) ? "" : name.Trim()[..Math.Min(name.Trim().Length, 120)], DateTimeOffset.UtcNow, files, bytes);
            File.WriteAllText(MetaFor(slot, ipId), JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return info;
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch (IOException) { }
        }
    }

    private static string? FindIndexDir(string root)
    {
        if (File.Exists(Path.Combine(root, "index.html"))) return root;
        // Shallowest index.html (fewest folders), at most 3 levels down.
        return Directory.EnumerateFiles(root, "index.html", SearchOption.AllDirectories)
            .Select(f => Path.GetDirectoryName(f)!)
            .Where(d => Path.GetRelativePath(root, d).Split(Path.DirectorySeparatorChar).Length <= 3)
            .OrderBy(d => d.Length)
            .FirstOrDefault();
    }

    /// <summary>The safe relative form of a zip entry name, or null for directories. Throws Ch5Exception for names that escape.</summary>
    public static string? SafeEntryPath(string entryName)
    {
        var n = entryName.Replace('\\', '/');
        if (n.Length == 0) return null;
        if (n.Contains('\0')) throw new Ch5Exception($"Unsafe path in archive: {Printable(entryName)}");
        if (n.StartsWith('/') || (n.Length >= 2 && n[1] == ':')) throw new Ch5Exception($"Absolute path in archive: {Printable(entryName)}");
        var parts = n.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
            if (p == ".." || p.Contains(':')) throw new Ch5Exception($"Path leaves the project folder: {Printable(entryName)}");
        if (n.EndsWith('/')) return null; // a folder entry
        return string.Join('/', parts.Where(p => p != "."));
    }

    private static string Printable(string s) => new(s.Select(c => char.IsControl(c) ? '?' : c).Take(120).ToArray());

    private static (int files, long bytes) Extract(Stream archive, string dest)
    {
        ZipArchive zip;
        try
        {
            if (archive.CanSeek && archive.Length > MaxArchiveBytes) throw new Ch5Exception($"The archive is bigger than {MaxArchiveBytes / 1048576} MB.");
            zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException) { throw new Ch5Exception("That isn't a .ch5z (zip) archive."); }
        using (zip)
        {
            if (zip.Entries.Count > MaxFiles) throw new Ch5Exception($"The archive has more than {MaxFiles} files.");
            var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
            // Check every name before writing anything.
            var plan = new List<(ZipArchiveEntry entry, string target)>();
            foreach (var e in zip.Entries)
            {
                var rel = SafeEntryPath(e.FullName);
                if (rel == null) continue;
                var target = Path.GetFullPath(Path.Combine(dest, rel));
                if (!target.StartsWith(root, StringComparison.Ordinal)) throw new Ch5Exception($"Path leaves the project folder: {Printable(e.FullName)}");
                plan.Add((e, target));
            }
            long total = 0;
            var files = 0;
            foreach (var (e, target) in plan)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // The header's size can lie, so count what actually comes out.
                using var src = e.Open();
                using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    total += n;
                    if (total > MaxExtractedBytes) throw new Ch5Exception($"The project is bigger than {MaxExtractedBytes / 1048576} MB once unpacked.");
                    dst.Write(buf, 0, n);
                }
                files++;
            }
            return (files, total);
        }
    }

    /// <summary>
    /// The file for a request path inside the project, or null if there is none or the path tries to leave the
    /// folder. An empty path means index.html.
    /// </summary>
    public string? ResolveFile(int slot, uint ipId, string? relative)
    {
        var dir = Path.GetFullPath(DirFor(slot, ipId));
        var rel = (relative ?? "").Replace('\\', '/').Trim('/');
        if (rel.Length == 0) rel = "index.html";
        if (rel.Contains('\0') || rel.Split('/').Any(p => p == ".." || p.Contains(':'))) return null;
        var full = Path.GetFullPath(Path.Combine(dir, rel));
        if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        return File.Exists(full) ? full : null;
    }
}
