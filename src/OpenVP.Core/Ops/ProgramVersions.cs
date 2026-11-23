using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Ops;

/// <summary>One program as it was loaded into a slot: the program file plus the files that came with it (.sig, sidecars).</summary>
/// <param name="Id">The first 12 hex digits of <paramref name="Hash"/>. Loading the same files again is the same version.</param>
/// <param name="Hash">SHA-256 over every file's name and content.</param>
/// <param name="How">How it first got into the slot: <c>upload</c>, <c>path</c>, <c>auto-reload</c>, <c>roll back</c>, <c>import</c>, <c>version</c>, <c>push</c>, <c>demo</c> or <c>recent</c>.</param>
public sealed record ProgramVersion(
    string Id, string Name, string File, string Hash, long Size, string[] Files, string? Kind,
    DateTimeOffset FirstLoaded, DateTimeOffset LoadedAt, int Loads, string How, string Who, string LastHow, string LastWho, string? Note);

/// <summary>
/// Every program that goes into a slot is kept as a version, in <c>data/versions/slotNN/</c> (an <c>index.json</c> and one folder of files per version),
/// so an older program can be loaded again, downloaded, annotated and compared. The same files loaded again are the same version (matched by hash), which
/// only gets its load time and count updated. Each slot keeps its newest <see cref="Keep"/> versions (default 30); older ones, and their files, are deleted,
/// except the one loaded last.
/// </summary>
public sealed class ProgramVersions
{
    public const int DefaultKeep = 30;
    public const long MaxBundleBytes = 400L * 1024 * 1024;
    public const int MaxNoteLength = 500;
    private static readonly Regex IdRe = new("^[0-9a-f]{12}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private sealed class IndexDto { public List<ProgramVersion> Versions { get; set; } = new(); }
    private sealed class SettingsDto { public int Keep { get; set; } = DefaultKeep; }

    private readonly string _root;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private int _keep;

    public ProgramVersions(string dataDirectory, int? keep = null, Func<DateTimeOffset>? clock = null)
    {
        _root = Path.Combine(dataDirectory, "versions");
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _keep = keep ?? Math.Clamp(IntegrationFiles.Load<SettingsDto>(Path.Combine(_root, "settings.json")).Keep, 1, 1000);
    }

    /// <summary>How many versions each slot keeps.</summary>
    public int Keep => _keep;

    public void SetKeep(int keep)
    {
        if (keep is < 1 or > 1000) throw new ArgumentException("Keep between 1 and 1000 versions per slot.");
        lock (_gate)
        {
            _keep = keep;
            IntegrationFiles.Save(Path.Combine(_root, "settings.json"), new SettingsDto { Keep = keep });
            foreach (var d in Directory.Exists(_root) ? Directory.EnumerateDirectories(_root, "slot*") : [])
                if (int.TryParse(Path.GetFileName(d)[4..], out var s)) PruneLocked(s, null);
        }
    }

    /// <summary>Reads the keep setting from disk again (after an import).</summary>
    public void ReloadSettings() { lock (_gate) _keep = Math.Clamp(IntegrationFiles.Load<SettingsDto>(Path.Combine(_root, "settings.json")).Keep, 1, 1000); }

    public static bool IsValidId(string? id) => id != null && IdRe.IsMatch(id);

    private string SlotDir(int slot) => Path.Combine(_root, $"slot{slot:00}");
    private string IndexPath(int slot) => Path.Combine(SlotDir(slot), "index.json");
    private string VersionDir(int slot, string id) => Path.Combine(SlotDir(slot), id);

    private List<ProgramVersion> Read(int slot) => IntegrationFiles.Load<IndexDto>(IndexPath(slot)).Versions;
    private void Write(int slot, List<ProgramVersion> list) => IntegrationFiles.Save(IndexPath(slot), new IndexDto { Versions = list });

    /// <summary>A hash of the files that doesn't depend on their order.</summary>
    public static string HashOf(IEnumerable<(string Name, byte[] Data)> files)
    {
        var sb = new StringBuilder();
        foreach (var f in files.OrderBy(f => f.Name, StringComparer.Ordinal))
            sb.Append(Path.GetFileName(f.Name)).Append('\0').Append(Convert.ToHexString(SHA256.HashData(f.Data)).ToLowerInvariant()).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>Records that these files (the program first) were loaded into the slot. Returns the version (new, or the existing one with the same hash).</summary>
    public ProgramVersion Record(int slot, IReadOnlyList<(string Name, byte[] Data)> files, string how, string who, string? kind = null)
    {
        if (files.Count == 0) throw new ArgumentException("No files.");
        var total = files.Sum(f => (long)f.Data.Length);
        var hash = HashOf(files);
        var id = hash[..12];
        var now = _now();
        lock (_gate)
        {
            var list = Read(slot);
            var existing = list.FirstOrDefault(v => v.Id == id);
            if (existing != null)
            {
                var upd = existing with { LoadedAt = now, Loads = existing.Loads + 1, LastHow = how, LastWho = who, Kind = kind ?? existing.Kind };
                list[list.IndexOf(existing)] = upd;
                Write(slot, list);
                if (!Directory.Exists(VersionDir(slot, id))) Store(slot, id, files); // the files were deleted by hand: put them back
                return upd;
            }
            if (total > MaxBundleBytes) throw new IOException($"Too big to keep as a version ({total / 1048576} MB; the limit is {MaxBundleBytes / 1048576} MB).");
            Store(slot, id, files);
            var main = Path.GetFileName(files[0].Name);
            var v = new ProgramVersion(id, Path.GetFileNameWithoutExtension(main), main, hash, total, files.Select(f => Path.GetFileName(f.Name)).ToArray(), kind, now, now, 1, how, who, how, who, null);
            list.Add(v);
            Write(slot, list);
            PruneLocked(slot, id);
            return v;
        }
    }

    /// <summary>Records the files in a folder (the slot's upload folder after a roll back or import). The program file is <paramref name="main"/>.</summary>
    public ProgramVersion? RecordFolder(int slot, string folder, string main, string how, string who, string? kind = null)
    {
        if (!Directory.Exists(folder)) return null;
        var files = new List<(string, byte[])>();
        var mainPath = Path.Combine(folder, Path.GetFileName(main));
        if (!File.Exists(mainPath)) return null;
        files.Add((Path.GetFileName(main), File.ReadAllBytes(mainPath)));
        foreach (var f in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal))
            if (Path.GetFileName(f) != Path.GetFileName(main)) files.Add((Path.GetFileName(f), File.ReadAllBytes(f)));
        return Record(slot, files, how, who, kind);
    }

    private void Store(int slot, string id, IReadOnlyList<(string Name, byte[] Data)> files)
    {
        var dir = VersionDir(slot, id);
        var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        foreach (var (name, data) in files) File.WriteAllBytes(Path.Combine(tmp, Path.GetFileName(name)), data);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(tmp, dir);
    }

    private void PruneLocked(int slot, string? keepId)
    {
        var list = Read(slot);
        if (list.Count <= _keep) return;
        var newest = list.OrderByDescending(v => v.LoadedAt).ThenByDescending(v => v.FirstLoaded).ToList();
        var protectedIds = new HashSet<string> { newest[0].Id };
        if (keepId != null) protectedIds.Add(keepId);
        var drop = newest.Skip(_keep).Where(v => !protectedIds.Contains(v.Id)).ToList();
        foreach (var v in drop)
        {
            try { if (Directory.Exists(VersionDir(slot, v.Id))) Directory.Delete(VersionDir(slot, v.Id), true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            list.Remove(v);
        }
        Write(slot, list);
    }

    /// <summary>Adds the versions found in a folder laid out like <c>data/versions/slotNN</c> (an import) that the slot doesn't have yet.</summary>
    public int Merge(int slot, string folder)
    {
        var index = Path.Combine(folder, "index.json");
        if (!File.Exists(index)) return 0;
        var added = 0;
        lock (_gate)
        {
            var list = Read(slot);
            foreach (var v in IntegrationFiles.Load<IndexDto>(index).Versions)
            {
                if (!IsValidId(v.Id) || list.Any(x => x.Id == v.Id) || !Directory.Exists(Path.Combine(folder, v.Id))) continue;
                Directory.CreateDirectory(SlotDir(slot));
                var dst = VersionDir(slot, v.Id);
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                CopyFolder(Path.Combine(folder, v.Id), dst);
                list.Add(v with { Files = v.Files.Select(Path.GetFileName).Where(f => f != null).Select(f => f!).ToArray() });
                added++;
            }
            if (added > 0) { Write(slot, list); PruneLocked(slot, null); }
        }
        return added;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
    }

    /// <summary>The slot's versions, most recently loaded first.</summary>
    public List<ProgramVersion> List(int slot)
    {
        lock (_gate) return Read(slot).OrderByDescending(v => v.LoadedAt).ThenByDescending(v => v.FirstLoaded).ToList();
    }

    public ProgramVersion? Get(int slot, string? id)
    {
        if (!IsValidId(id)) return null;
        lock (_gate) return Read(slot).FirstOrDefault(v => v.Id == id);
    }

    /// <summary>The version's folder, or null if the id is unknown or its files are gone.</summary>
    public string? FolderOf(int slot, string? id) => Get(slot, id) != null && Directory.Exists(VersionDir(slot, id!)) ? VersionDir(slot, id!) : null;

    /// <summary>The files (the program first), or null if the version or its files are gone.</summary>
    public List<(string Name, byte[] Data)>? ReadFiles(int slot, string? id)
    {
        var v = Get(slot, id);
        var dir = FolderOf(slot, id);
        if (v == null || dir == null) return null;
        try
        {
            var list = new List<(string, byte[])>();
            foreach (var name in v.Files) list.Add((name, File.ReadAllBytes(Path.Combine(dir, Path.GetFileName(name)))));
            return list;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Sets or clears (empty) the note. Returns null for an unknown version.</summary>
    public ProgramVersion? SetNote(int slot, string? id, string? note)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > MaxNoteLength }) throw new ArgumentException($"A note can be at most {MaxNoteLength} characters.");
        if (note != null && note.Any(c => char.IsControl(c) && c is not ('\n' or '\t'))) throw new ArgumentException("The note can't contain control characters.");
        lock (_gate)
        {
            var list = Read(slot);
            var v = list.FirstOrDefault(x => x.Id == id);
            if (v == null) return null;
            var upd = v with { Note = note };
            list[list.IndexOf(v)] = upd;
            Write(slot, list);
            return upd;
        }
    }

    public bool Delete(int slot, string? id)
    {
        lock (_gate)
        {
            var list = Read(slot);
            var v = list.FirstOrDefault(x => x.Id == id);
            if (v == null) return false;
            list.Remove(v);
            Write(slot, list);
            try { if (Directory.Exists(VersionDir(slot, v.Id))) Directory.Delete(VersionDir(slot, v.Id), true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return true;
        }
    }

    /// <summary>The versions as a zip of the program files, for download. Null if the files are gone.</summary>
    public byte[]? ZipOf(int slot, string? id)
    {
        var files = ReadFiles(slot, id);
        if (files == null) return null;
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var (name, data) in files)
            {
                using var s = z.CreateEntry(name, System.IO.Compression.CompressionLevel.Fastest).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }
}
