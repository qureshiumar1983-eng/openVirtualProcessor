using System.IO.Compression;

namespace OpenVP.Core.Ops;

/// <summary>How big an archive may be. Checked on the entries' declared sizes and again on the bytes actually read, so a lying header can't get past it.</summary>
public sealed record ArchiveLimits(int MaxEntries = 100_000, long MaxEntryBytes = 2L << 30, long MaxTotalBytes = 8L << 30, long MaxManifestBytes = 4 << 20, int MaxRatio = 1000);

/// <summary>Opens a zip for import without trusting it: entry names are checked (no <c>..</c>, no absolute or drive paths), counts and sizes are limited, and extraction goes only to places inside the target folder.</summary>
public static class SafeZip
{
    public static readonly ArchiveLimits DefaultLimits = new();

    /// <summary>The entry name with <c>/</c> separators, or throws <see cref="ArchiveException"/> (<c>unsafe-path</c>) for a name that could land outside the folder it is extracted to.</summary>
    public static string SafeName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArchiveException("The file has an entry with no name.", "unsafe-path");
        var n = name.Replace('\\', '/');
        if (n.StartsWith('/') || n.Contains(':') || n.Any(c => c < 32))
            throw new ArchiveException($"The file has an entry with an unsafe name ('{Shown(name)}'). It looks like an attempt to write outside the data folder, so nothing was imported.", "unsafe-path");
        foreach (var seg in n.TrimEnd('/').Split('/'))
            if (seg is ".." or "." || seg.Length == 0 && n.TrimEnd('/').Length > 0)
                throw new ArchiveException($"The file has an entry with an unsafe name ('{Shown(name)}'). It looks like an attempt to write outside the data folder, so nothing was imported.", "unsafe-path");
        return n;
    }

    private static string Shown(string s) => new string(s.Select(c => c < 32 ? '?' : c).ToArray()) is { Length: > 80 } t ? t[..80] + "…" : new string(s.Select(c => c < 32 ? '?' : c).ToArray());

    /// <summary>Joins <paramref name="relative"/> onto <paramref name="root"/> and checks the result is still inside it.</summary>
    public static string SafeJoin(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (full != top && !full.StartsWith(top + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArchiveException($"The file has an entry that would land outside the data folder ('{Shown(relative)}'), so nothing was imported.", "unsafe-path");
        return full;
    }

    /// <summary>Checks every entry. Returns the entries by safe name (files only).</summary>
    public static Dictionary<string, ZipArchiveEntry> Validate(ZipArchive zip, ArchiveLimits limits)
    {
        if (zip.Entries.Count > limits.MaxEntries) throw new ArchiveException($"The file has {zip.Entries.Count:N0} entries; the limit is {limits.MaxEntries:N0}.", "too-big");
        var map = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (var e in zip.Entries)
        {
            var name = SafeName(e.FullName);
            if (name.EndsWith('/')) continue; // a folder entry
            if (e.Length > limits.MaxEntryBytes) throw new ArchiveException($"'{Shown(name)}' is {e.Length / 1048576:N0} MB uncompressed; the limit for one file is {limits.MaxEntryBytes / 1048576:N0} MB.", "too-big");
            total += e.Length;
            if (total > limits.MaxTotalBytes) throw new ArchiveException($"The file would unpack to more than {limits.MaxTotalBytes / 1048576:N0} MB, which is over the limit.", "too-big");
            if (e.Length > 16L << 20 && e.CompressedLength > 0 && e.Length / e.CompressedLength > limits.MaxRatio)
                throw new ArchiveException($"'{Shown(name)}' compresses suspiciously well ({e.Length / e.CompressedLength}:1), so it was refused as a possible zip bomb.", "too-big");
            if (!map.TryAdd(name, e)) throw new ArchiveException($"The file has two entries called '{Shown(name)}'.", "bad-archive");
        }
        return map;
    }

    /// <summary>Copies an entry to <paramref name="destination"/>, counting the bytes actually produced against <paramref name="entryLimit"/> and <paramref name="budget"/> (what is left of the total).</summary>
    public static long Extract(ZipArchiveEntry entry, string destination, long entryLimit, ref long budget)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        long written = 0;
        using var src = entry.Open();
        using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        var buf = new byte[81920];
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0)
        {
            written += n; budget -= n;
            if (written > entryLimit || budget < 0) throw new ArchiveException($"'{Shown(entry.FullName)}' unpacks to more than the limit, so nothing was imported.", "too-big");
            dst.Write(buf, 0, n);
        }
        return written;
    }

    /// <summary>The whole entry in memory (for small files such as the manifest), up to <paramref name="limit"/> bytes.</summary>
    public static byte[] ReadAll(ZipArchiveEntry entry, long limit)
    {
        using var src = entry.Open();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > limit) throw new ArchiveException($"'{Shown(entry.FullName)}' is bigger than expected.", "too-big");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }
}
