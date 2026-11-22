using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>Something that was deleted and can still be brought back.</summary>
/// <param name="Kind">What it was: <c>file</c>, <c>folder</c>, <c>snapshot</c> or <c>recording</c>.</param>
/// <param name="Items">Where each piece came from (full paths) and whether it was a folder.</param>
public sealed record TrashEntry(string Id, string Kind, string Label, int? Slot, DateTimeOffset Time, TrashItem[] Items);

public sealed record TrashItem(string Original, bool IsDirectory);

/// <summary>
/// A short-lived trash folder (<c>data/trash</c>) so a delete in the console can be undone. Deleted files, snapshots and
/// recordings are moved here instead of being removed; they are put back with <see cref="Restore"/> and purged for good
/// after <see cref="Ttl"/> (24 hours).
/// </summary>
public sealed class Trash
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);
    private static readonly Regex IdRe = new("^[0-9]{14}-[0-9a-f]{8}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _data, _root;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();

    public TimeSpan Ttl { get; }

    public Trash(string dataDirectory, TimeSpan? ttl = null, Func<DateTimeOffset>? clock = null)
    {
        _data = Path.GetFullPath(dataDirectory);
        _root = Path.Combine(_data, "trash");
        Ttl = ttl ?? DefaultTtl;
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static bool IsValidId(string? id) => id != null && IdRe.IsMatch(id);

    /// <summary>Moves files and folders into the trash as one entry. Missing sources are skipped; returns null if none existed.</summary>
    public TrashEntry? Move(string kind, string label, int? slot, params string[] sources)
    {
        lock (_lock)
        {
            Purge();
            var existing = sources.Select(Path.GetFullPath).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (existing.Count == 0) return null;
            var now = _now();
            var id = now.UtcDateTime.ToString("yyyyMMddHHmmss") + "-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var dir = Path.Combine(_root, id);
            Directory.CreateDirectory(dir);
            var items = new List<TrashItem>();
            try
            {
                for (var i = 0; i < existing.Count; i++)
                {
                    var src = existing[i];
                    var isDir = Directory.Exists(src);
                    var dst = Path.Combine(dir, i.ToString());
                    if (isDir) Directory.Move(src, dst); else File.Move(src, dst);
                    items.Add(new TrashItem(src, isDir));
                }
            }
            catch
            {
                // Put back what was already moved, so a failed delete leaves everything as it was.
                for (var i = 0; i < items.Count; i++)
                {
                    var from = Path.Combine(dir, i.ToString());
                    try { if (items[i].IsDirectory) Directory.Move(from, items[i].Original); else File.Move(from, items[i].Original); } catch { }
                }
                try { Directory.Delete(dir, true); } catch { }
                throw;
            }
            var entry = new TrashEntry(id, kind, label, slot, now, items.ToArray());
            File.WriteAllText(Path.Combine(dir, "trash.json"), JsonSerializer.Serialize(entry, Json));
            return entry;
        }
    }

    public TrashEntry? Get(string? id)
    {
        if (!IsValidId(id)) return null;
        try { return JsonSerializer.Deserialize<TrashEntry>(File.ReadAllText(Path.Combine(_root, id!, "trash.json")), Json); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>What is in the trash, newest first.</summary>
    public List<TrashEntry> List()
    {
        if (!Directory.Exists(_root)) return new();
        return Directory.EnumerateDirectories(_root).Select(d => Get(Path.GetFileName(d))).OfType<TrashEntry>().OrderByDescending(e => e.Time).ToList();
    }

    /// <summary>
    /// Puts an entry back where it came from and removes it from the trash. Returns null for an unknown or purged id. Throws
    /// <see cref="IOException"/> if something is already at the original place (nothing is overwritten) and
    /// <see cref="UnauthorizedAccessException"/> if a path in the entry is outside the data folder.
    /// </summary>
    public TrashEntry? Restore(string? id)
    {
        lock (_lock)
        {
            var entry = Get(id);
            if (entry == null) return null;
            var dir = Path.Combine(_root, entry.Id);
            foreach (var it in entry.Items)
            {
                var full = Path.GetFullPath(it.Original);
                if (!full.StartsWith(_data + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("That item doesn't belong to this data folder.");
                if (File.Exists(full) || Directory.Exists(full))
                    throw new IOException($"There is already something called '{Path.GetFileName(full)}' where this was. Rename or delete it first.");
            }
            var moved = new List<int>();
            try
            {
                for (var i = 0; i < entry.Items.Length; i++)
                {
                    var it = entry.Items[i];
                    Directory.CreateDirectory(Path.GetDirectoryName(it.Original)!);
                    var src = Path.Combine(dir, i.ToString());
                    if (it.IsDirectory) Directory.Move(src, it.Original); else File.Move(src, it.Original);
                    moved.Add(i);
                }
            }
            catch
            {
                foreach (var i in moved)
                {
                    var it = entry.Items[i]; var back = Path.Combine(dir, i.ToString());
                    try { if (it.IsDirectory) Directory.Move(it.Original, back); else File.Move(it.Original, back); } catch { }
                }
                throw;
            }
            try { Directory.Delete(dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return entry;
        }
    }

    /// <summary>Deletes entries older than the time to live. Returns how many.</summary>
    public int Purge()
    {
        lock (_lock)
        {
            if (!Directory.Exists(_root)) return 0;
            var n = 0;
            var cutoff = _now() - Ttl;
            foreach (var d in Directory.EnumerateDirectories(_root).ToList())
            {
                var id = Path.GetFileName(d);
                var e = Get(id);
                // An entry we can't read, or an unnamed leftover, is purged by its folder's age.
                var old = e != null ? e.Time < cutoff : Directory.GetLastWriteTimeUtc(d) < cutoff.UtcDateTime;
                if (!old) continue;
                try { Directory.Delete(d, true); n++; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return n;
        }
    }
}
