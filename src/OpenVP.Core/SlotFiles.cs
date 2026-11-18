namespace OpenVP.Core;

/// <summary>
/// A slot's file system, laid out like a processor's: <c>\NVRAM</c> and <c>\USER</c> (plus <c>\HTML</c>
/// and <c>\ROMDISK</c>), each a folder under the slot's directory. Programs reach them with processor
/// paths; the web console reaches them through <see cref="Resolve"/>.
/// </summary>
public static class SlotFiles
{
    /// <summary>The folders the web console lets you browse and fill: NVRAM and the built-in user folder.</summary>
    public static readonly IReadOnlyList<string> BrowsableRoots = ["nvram", "user"];

    /// <summary>Top-level processor folders. Matched case-insensitively and stored lower-case, so
    /// <c>\NVRAM\x</c>, <c>/nvram/x</c> and <c>\Nvram\x</c> are the same file on Linux too.</summary>
    private static readonly string[] KnownRoots = ["nvram", "user", "html", "romdisk"];

    /// <summary>Creates the slot's NVRAM and user folders if they're missing.</summary>
    public static void EnsureRoots(string slotDirectory)
    {
        foreach (var r in BrowsableRoots) Directory.CreateDirectory(Path.Combine(slotDirectory, r));
    }

    /// <summary>
    /// Maps a path a program uses (<c>\NVRAM\settings.json</c>, <c>/user/config/rooms.xml</c>, or a path
    /// already under the slot folder) to a full path inside <paramref name="slotDirectory"/>.
    /// Throws <see cref="UnauthorizedAccessException"/> for anything that would land outside it.
    /// </summary>
    public static string Map(string slotDirectory, string? path)
    {
        var root = Path.GetFullPath(slotDirectory);
        if (string.IsNullOrEmpty(path)) return root;
        var p = path.Replace('\\', '/');
        var rootSlash = root.Replace('\\', '/');
        if (p.StartsWith(rootSlash, StringComparison.Ordinal))
            p = p[rootSlash.Length..]; // already a slot path (from GetApplicationDirectory and friends)
        p = p.TrimStart('/');
        var cut = p.IndexOf('/');
        var first = cut < 0 ? p : p[..cut];
        foreach (var known in KnownRoots)
            if (first.Equals(known, StringComparison.OrdinalIgnoreCase)) { p = known + (cut < 0 ? "" : p[cut..]); break; }
        return Inside(root, Path.Combine(root, p), path);
    }

    /// <summary>
    /// The full path of <paramref name="relative"/> inside one of the browsable roots, for the web console.
    /// Throws <see cref="ArgumentException"/> for an unknown root and <see cref="UnauthorizedAccessException"/>
    /// for a path that climbs out of it.
    /// </summary>
    public static string Resolve(string slotDirectory, string? root, string? relative)
    {
        var r = (root ?? "").Trim().ToLowerInvariant();
        if (!BrowsableRoots.Contains(r)) throw new ArgumentException($"Folder must be one of: {string.Join(", ", BrowsableRoots)}.");
        var top = Path.GetFullPath(Path.Combine(slotDirectory, r));
        Directory.CreateDirectory(top);
        var rel = (relative ?? "").Replace('\\', '/').Trim('/');
        return rel.Length == 0 ? top : Inside(top, Path.Combine(top, rel), relative!);
    }

    /// <summary>A file or folder name the console accepts: no separators, no "." or "..", nothing the OS rejects.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim() == name && name is not ("." or "..")
        && name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>A program name made safe to use as a folder name.</summary>
    public static string FolderName(string? programName)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string((programName ?? "").Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return s.Length == 0 ? "Program" : s;
    }

    /// <summary>
    /// What SIMPL+ MakeProgramFilename returns: the program's folder inserted after the top folder,
    /// so <c>\NVRAM\config.txt</c> becomes <c>\NVRAM\{program}\config.txt</c>. A bare name goes
    /// under <c>\USER\{program}\</c>.
    /// </summary>
    public static string ProgramFilename(string? name, string? programName)
    {
        var folder = FolderName(programName);
        var p = (name ?? "").Replace('/', '\\').TrimStart('\\');
        var cut = p.IndexOf('\\');
        var first = cut < 0 ? p : p[..cut];
        foreach (var known in KnownRoots)
            if (first.Equals(known, StringComparison.OrdinalIgnoreCase))
                return "\\" + first + "\\" + folder + (cut < 0 ? "\\" : p[cut..]);
        return "\\USER\\" + folder + "\\" + p;
    }

    private static string Inside(string top, string candidate, string original)
    {
        var full = Path.GetFullPath(candidate);
        if (full != top && !full.StartsWith(top + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Path '{original}' is outside the program folder.");
        return full;
    }
}
