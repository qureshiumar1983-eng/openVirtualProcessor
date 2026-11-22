using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>One saved copy of a slot: its program files plus its NVRAM and User folders.</summary>
public sealed record SnapshotInfo(
    string Id, DateTimeOffset Time, string? Program, string? File, string Reason, long Size, string[]? Skipped = null);

/// <summary>
/// Snapshots of a slot, kept under <c>data/snapshots/slotNN/&lt;id&gt;/</c>: <c>program/</c> (the slot's uploaded files),
/// <c>nvram/</c>, <c>user/</c> and a small <c>snapshot.json</c>. The slot's <c>slot.json</c> (Room ID) is never copied or
/// restored. Callers serialize access per slot (the server holds the slot's upload lock).
/// </summary>
public sealed class SlotSnapshots
{
    public const int DefaultKeep = 10;
    public const string BeforeLoad = "before load", BeforeRollBack = "before roll back", BeforeUnload = "before unload", Manual = "manual";

    private static readonly Regex IdPattern = new(@"^\d{8}T\d{6}Z-\d{3}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string dataDirectory;
    private readonly int keep;

    public SlotSnapshots(string dataDirectory, int keep = DefaultKeep)
    {
        this.dataDirectory = dataDirectory;
        this.keep = Math.Max(1, keep);
    }

    /// <summary>True for an id this class generates; anything else (paths, "..", empty) is refused.</summary>
    public static bool IsValidId(string? id) => id != null && IdPattern.IsMatch(id);

    private string UploadsDir(int slot) => Path.Combine(dataDirectory, "uploads", $"slot{slot:00}");
    private string SlotDir(int slot) => Path.Combine(dataDirectory, $"program{slot:00}");
    private string Root(int slot) => Path.Combine(dataDirectory, "snapshots", $"slot{slot:00}");

    private string? SnapDir(int slot, string? id) => IsValidId(id) ? Path.Combine(Root(slot), id!) : null;

    /// <summary>
    /// Saves the slot's program files, NVRAM and User folders. Returns null when there is nothing to save (no program
    /// loaded and no files). Files that can't be read are skipped and listed in <see cref="SnapshotInfo.Skipped"/>.
    /// </summary>
    public SnapshotInfo? Take(int slot, string? programName, string? programFile, string reason)
    {
        var sources = new (string Name, string Path)[]
        {
            ("program", UploadsDir(slot)),
            ("nvram", Path.Combine(SlotDir(slot), "nvram")),
            ("user", Path.Combine(SlotDir(slot), "user")),
        };
        var any = sources.Any(s => Directory.Exists(s.Path) && Directory.EnumerateFileSystemEntries(s.Path).Any());
        if (programName == null && !any) return null;

        Directory.CreateDirectory(Root(slot));
        var now = DateTimeOffset.UtcNow;
        var id = NewId(slot, now);
        var dir = Path.Combine(Root(slot), id);
        var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        try
        {
            var skipped = new List<string>();
            long size = 0;
            foreach (var (name, src) in sources)
                size += Copy(src, Path.Combine(tmp, name), name, skipped);
            var info = new SnapshotInfo(id, now, programName, programFile, reason, size, skipped.Count == 0 ? null : skipped.ToArray());
            File.WriteAllText(Path.Combine(tmp, "snapshot.json"), JsonSerializer.Serialize(info, Json));
            Directory.Move(tmp, dir);
            Prune(slot);
            return info;
        }
        catch
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            throw;
        }
    }

    /// <summary>The slot's snapshots, newest first.</summary>
    public List<SnapshotInfo> List(int slot)
    {
        var list = new List<SnapshotInfo>();
        if (!Directory.Exists(Root(slot))) return list;
        foreach (var d in Directory.EnumerateDirectories(Root(slot)))
        {
            var id = Path.GetFileName(d);
            if (IsValidId(id) && Read(d) is { } info) list.Add(info);
        }
        return list.OrderByDescending(i => i.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>One snapshot, or null if the id is invalid or unknown.</summary>
    public SnapshotInfo? Get(int slot, string? id) => SnapDir(slot, id) is { } d && Directory.Exists(d) ? Read(d) : null;

    /// <summary>
    /// Replaces the slot's uploaded program files, NVRAM and User folders with the snapshot's. The slot's program must
    /// be unloaded first so it has let go of its files. <c>slot.json</c> is left alone. Returns null for an unknown id.
    /// </summary>
    public SnapshotInfo? Restore(int slot, string? id)
    {
        var dir = SnapDir(slot, id);
        if (dir == null || !Directory.Exists(dir) || Read(dir) is not { } info) return null;
        var targets = new (string Name, string Path)[]
        {
            ("program", UploadsDir(slot)),
            ("nvram", Path.Combine(SlotDir(slot), "nvram")),
            ("user", Path.Combine(SlotDir(slot), "user")),
        };
        foreach (var (name, target) in targets)
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            Copy(Path.Combine(dir, name), target, name, new List<string>());
        }
        return info;
    }

    /// <summary>The path of the program file inside the restored uploads folder, or null if the snapshot has none.</summary>
    public string? RestoredProgramPath(int slot, SnapshotInfo info) =>
        string.IsNullOrEmpty(info.File) ? null : Path.Combine(UploadsDir(slot), Path.GetFileName(info.File));

    /// <summary>The snapshot's folder, or null if the id is invalid or unknown.</summary>
    public string? PathOf(int slot, string? id) => SnapDir(slot, id) is { } d && Directory.Exists(d) ? d : null;

    /// <summary>Moves the snapshot into <paramref name="trash"/> instead of deleting it. Null if there is no such snapshot.</summary>
    public TrashEntry? Delete(int slot, string? id, Trash trash)
    {
        var info = Get(slot, id);
        return info == null || PathOf(slot, id) is not { } dir ? null
            : trash.Move("snapshot", $"snapshot of {info.Program ?? "the slot"} from {info.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss}", slot, dir);
    }

    public bool Delete(int slot, string? id)
    {
        var dir = SnapDir(slot, id);
        if (dir == null || !Directory.Exists(dir)) return false;
        Directory.Delete(dir, true);
        return true;
    }

    /// <summary>Deletes all but the newest snapshots of the slot.</summary>
    public void Prune(int slot)
    {
        foreach (var old in List(slot).Skip(keep))
            try { Delete(slot, old.Id); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string NewId(int slot, DateTimeOffset now)
    {
        var stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        // One more than the highest counter already used in this second, so ids keep sorting oldest to newest
        // even after older snapshots were pruned.
        var last = 0;
        if (Directory.Exists(Root(slot)))
            foreach (var d in Directory.EnumerateDirectories(Root(slot), stamp + "-*"))
                if (int.TryParse(Path.GetFileName(d).AsSpan(stamp.Length + 1, 3), out var c)) last = Math.Max(last, c);
        if (last < 999) return $"{stamp}-{last + 1:000}";
        throw new IOException("Too many snapshots in one second.");
    }

    private static SnapshotInfo? Read(string dir)
    {
        try { return JsonSerializer.Deserialize<SnapshotInfo>(File.ReadAllText(Path.Combine(dir, "snapshot.json")), Json); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    // Copies a folder tree. A file that can't be read is skipped and noted; links are not followed.
    private static long Copy(string src, string dst, string label, List<string> skipped)
    {
        Directory.CreateDirectory(dst);
        if (!Directory.Exists(src)) return 0;
        long total = 0;
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(src).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add($"{label}: {e.Message}"); return 0; }
        foreach (var e in entries)
        {
            var name = Path.GetFileName(e);
            try
            {
                var attrs = File.GetAttributes(e);
                if ((attrs & FileAttributes.ReparsePoint) != 0) { skipped.Add($"{label}/{name}: link not followed"); continue; }
                if ((attrs & FileAttributes.Directory) != 0) total += Copy(e, Path.Combine(dst, name), $"{label}/{name}", skipped);
                else
                {
                    var target = Path.Combine(dst, name);
                    File.Copy(e, target, true);
                    total += new FileInfo(target).Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add($"{label}/{name}: {ex.Message}");
            }
        }
        return total;
    }
}
