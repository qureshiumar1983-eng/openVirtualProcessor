using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>
/// A file or folder the program looked for in <c>\NVRAM</c> or <c>\USER</c> that wasn't there,
/// usually a config file nobody has put in place yet.
/// </summary>
/// <param name="Root">The top folder: <c>nvram</c> or <c>user</c>.</param>
/// <param name="Folder">The folder inside <paramref name="Root"/> the file belongs in, with '/' between
/// parts (<c>config/rooms</c>), or "" for the top folder itself. For a missing folder, the folder itself.</param>
/// <param name="Name">The file name, or "" when the program looked for a folder.</param>
/// <param name="ProgramPath">The path as the program sees it: <c>\USER\config\rooms.json</c>.</param>
/// <param name="FolderPath">The folder as the program sees it, ending in a backslash: <c>\USER\config\</c>.</param>
/// <param name="DiskFolder">The same folder on this machine's disk.</param>
public sealed record MissingFile(string Root, string Folder, string Name, string ProgramPath, string FolderPath, string DiskFolder, DateTimeOffset Time)
{
    public bool IsFolder => Name.Length == 0;

    /// <summary>True when the folder is there and only the file is missing.</summary>
    public bool FolderExists => Directory.Exists(DiskFolder);
}

/// <summary>
/// Remembers the files and folders a slot's program looked for and didn't find, so the console can
/// say which folders to create and where to put each file. Each one is logged once, with the folder
/// to create, and drops off the list once it's there.
/// </summary>
public sealed class MissingFileLog
{
    /// <summary>At most this many are kept; a program probing for hundreds of files shouldn't flood the console.</summary>
    public const int Limit = 50;

    private readonly ConcurrentDictionary<string, MissingFile> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string> _slotDirectory;
    private readonly Action<string, string> _log;

    public MissingFileLog(Func<string> slotDirectory, Action<string, string> log)
    {
        _slotDirectory = slotDirectory;
        _log = log;
    }

    /// <summary>The ones still missing, oldest first.</summary>
    public IReadOnlyList<MissingFile> Items
    {
        get
        {
            var list = new List<MissingFile>();
            foreach (var (key, m) in _items)
            {
                var full = Path.Combine(m.DiskFolder, m.Name);
                if (m.IsFolder ? Directory.Exists(full) : File.Exists(full)) _items.TryRemove(key, out _);
                else list.Add(m);
            }
            return list.OrderBy(m => m.Time).ToList();
        }
    }

    public void Clear() => _items.Clear();

    /// <summary>
    /// The program looked for <paramref name="programPath"/> (<c>\USER\config\rooms.json</c>, <c>/nvram/x</c>, or a
    /// path already inside the slot folder) and may not have found it. Checks again, and if it really isn't there
    /// and it's in <c>\NVRAM</c> or <c>\USER</c>, remembers it and logs where to put it. Never throws.
    /// </summary>
    public MissingFile? Report(string? programPath, bool isFolder = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(programPath)) return null;
            var slotDir = Path.GetFullPath(_slotDirectory());
            var full = SlotFiles.Map(slotDir, programPath);
            if (File.Exists(full) || Directory.Exists(full)) return null;

            var parts = Path.GetRelativePath(slotDir, full).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !SlotFiles.BrowsableRoots.Contains(parts[0])) return null;
            var root = parts[0];
            var inner = parts.Skip(1).ToArray();
            var folderParts = isFolder ? inner : inner[..^1];
            var name = isFolder ? "" : inner[^1];
            var folder = string.Join('/', folderParts);
            var key = root + "/" + string.Join('/', inner) + (isFolder ? "/" : "");

            if (_items.ContainsKey(key) || _items.Count >= Limit) return null;
            var top = "\\" + root.ToUpperInvariant() + "\\";
            var folderPath = top + string.Concat(folderParts.Select(p => p + "\\"));
            var diskFolder = Path.Combine([slotDir, root, .. folderParts]);
            var m = new MissingFile(root, folder, name, folderPath + name, folderPath, diskFolder, DateTimeOffset.Now);
            if (!_items.TryAdd(key, m)) return null;
            _log("Notice", Describe(m));
            return m;
        }
        catch (Exception e) when (e is ArgumentException or UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>What to tell the person running the program: the folder to create and where to put the file.</summary>
    public static string Describe(MissingFile m)
    {
        var tab = m.Root == "nvram" ? "NVRAM" : "User";
        var where = $"in the web console (Files, {tab} tab) or on disk at {m.DiskFolder}";
        if (m.IsFolder)
            return $"The program looked for the folder {m.FolderPath} but it isn't there. Create it {where}.";
        if (Directory.Exists(m.DiskFolder))
            return $"The program looked for {m.ProgramPath} but it isn't there. Put {m.Name} in {m.FolderPath} {where}.";
        return $"The program looked for {m.ProgramPath} but it isn't there. Create the folder {m.FolderPath} and put {m.Name} in it, {where}.";
    }
}
