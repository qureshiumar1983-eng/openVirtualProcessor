using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>One program that was loaded recently.</summary>
/// <param name="Path">For a program loaded from a file on this computer: where it was.</param>
/// <param name="HasCopy">For an upload: OpenVP kept its bytes, so it can be loaded again.</param>
public sealed record RecentEntry(string Id, string Name, string Kind, int Slot, DateTimeOffset Time, string? Path, string[]? Files, bool HasCopy, long Size);

/// <summary>
/// The last programs loaded (<see cref="MaxEntries"/>), kept in <c>data/recent/</c> so the console can load one again in
/// a click. A program loaded from a path is remembered by its path. An upload is remembered by name and its files are kept
/// (the last version of each), up to a size cap per program and in total; anything bigger is remembered without its bytes.
/// Loading the same path or file name again replaces the old entry.
/// </summary>
public sealed class RecentPrograms
{
    public const int MaxEntries = 10;
    public const long DefaultMaxCopyBytes = 100L * 1024 * 1024, DefaultMaxTotalBytes = 400L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex IdRe = new("^[0-9a-f]{12}$", RegexOptions.Compiled);
    private readonly string _root, _index;
    private readonly int _max;
    private readonly long _maxCopy, _maxTotal;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private List<RecentEntry> _items = new();

    public RecentPrograms(string dataDirectory, int max = MaxEntries, long maxCopyBytes = DefaultMaxCopyBytes, long maxTotalBytes = DefaultMaxTotalBytes, Func<DateTimeOffset>? clock = null)
    {
        _root = Path.Combine(dataDirectory, "recent");
        _index = Path.Combine(_root, "recent.json");
        _max = Math.Max(1, max); _maxCopy = maxCopyBytes; _maxTotal = maxTotalBytes;
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        try { if (File.Exists(_index)) _items = JsonSerializer.Deserialize<List<RecentEntry>>(File.ReadAllText(_index), Json) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _items = new(); }
        _items = _items.Where(i => IdRe.IsMatch(i.Id)).ToList();
    }

    private string Dir(string id) => Path.Combine(_root, "files", id);

    /// <summary>Newest first.</summary>
    public List<RecentEntry> List() { lock (_lock) return _items.OrderByDescending(i => i.Time).ToList(); }

    public RecentEntry? Get(string? id) { lock (_lock) return _items.FirstOrDefault(i => i.Id == id); }

    /// <summary>Record a load. <paramref name="path"/> is set for a file loaded from this computer; <paramref name="files"/> are the uploaded files (the program first).</summary>
    public RecentEntry Add(string name, string kind, int slot, string? path, IReadOnlyList<(string Name, byte[] Data)>? files, string[]? extra = null)
    {
        lock (_lock)
        {
            var key = Key(name, path);
            foreach (var old in _items.Where(i => Key(i.Name, i.Path) == key).ToList()) RemoveLocked(old);
            var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            long size = files?.Sum(f => (long)f.Data.Length) ?? 0;
            var copy = false;
            if (files is { Count: > 0 } && size <= _maxCopy)
            {
                try
                {
                    var dir = Dir(id);
                    Directory.CreateDirectory(dir);
                    foreach (var (n, data) in files) File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(n)), data);
                    File.WriteAllText(Path.Combine(dir, "program.txt"), Path.GetFileName(files[0].Name));
                    copy = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    try { Directory.Delete(Dir(id), true); } catch { }
                }
            }
            var entry = new RecentEntry(id, name, kind, slot, _now(), path, extra is { Length: > 0 } ? extra : null, copy, size);
            _items.Add(entry);
            while (_items.Count > _max) RemoveLocked(_items.OrderBy(i => i.Time).First());
            TrimTotalLocked();
            Save();
            return _items.First(i => i.Id == id);
        }
    }

    /// <summary>The uploaded files of an entry (the program first), or null if no copy was kept.</summary>
    public List<(string Name, byte[] Data)>? ReadCopy(string? id)
    {
        lock (_lock)
        {
            var e = _items.FirstOrDefault(i => i.Id == id);
            if (e is not { HasCopy: true }) return null;
            var dir = Dir(e.Id);
            try
            {
                var main = Path.GetFileName(File.ReadAllText(Path.Combine(dir, "program.txt")).Trim());
                var list = new List<(string, byte[])>();
                list.Add((main, File.ReadAllBytes(Path.Combine(dir, main))));
                foreach (var f in Directory.EnumerateFiles(dir).Where(f => Path.GetFileName(f) != main && Path.GetFileName(f) != "program.txt").OrderBy(f => f, StringComparer.Ordinal))
                    list.Add((Path.GetFileName(f), File.ReadAllBytes(f)));
                return list;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    public bool Remove(string? id)
    {
        lock (_lock)
        {
            var e = _items.FirstOrDefault(i => i.Id == id);
            if (e == null) return false;
            RemoveLocked(e);
            Save();
            return true;
        }
    }

    /// <summary>Adds entries from an export (their paths only, no kept copies) that the list doesn't have yet. Returns how many.</summary>
    public int MergePaths(IEnumerable<RecentEntry> entries)
    {
        lock (_lock)
        {
            var added = 0;
            foreach (var e in entries.Where(e => e.Path != null).OrderBy(e => e.Time))
            {
                if (_items.Any(i => Key(i.Name, i.Path) == Key(e.Name, e.Path))) continue;
                var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
                _items.Add(e with { Id = id, HasCopy = false });
                added++;
            }
            while (_items.Count > _max) RemoveLocked(_items.OrderBy(i => i.Time).First());
            if (added > 0) Save();
            return added;
        }
    }

    private static string Key(string name, string? path) => path != null ? "p:" + path.ToLowerInvariant() : "n:" + name.ToLowerInvariant();

    private void RemoveLocked(RecentEntry e)
    {
        _items.Remove(e);
        try { if (Directory.Exists(Dir(e.Id))) Directory.Delete(Dir(e.Id), true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // Over the total cap: the oldest entries lose their bytes first (they stay in the list).
    private void TrimTotalLocked()
    {
        while (_items.Where(i => i.HasCopy).Sum(i => i.Size) > _maxTotal)
        {
            var oldest = _items.Where(i => i.HasCopy).OrderBy(i => i.Time).First();
            try { if (Directory.Exists(Dir(oldest.Id))) Directory.Delete(Dir(oldest.Id), true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _items[_items.IndexOf(oldest)] = oldest with { HasCopy = false };
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_root);
            var tmp = _index + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
            File.Move(tmp, _index, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* kept in memory until the next save */ }
    }
}
