using System.Text;

namespace OpenVP.Core;

/// <summary>A file to copy into a slot: where it is on disk, and the name it gets in the slot's program folder.</summary>
public sealed record ProgramFile(string Source, string Name);

/// <summary>
/// A program file on this computer and the files that belong with it, so loading a program from disk brings
/// them along without anyone picking them: a compiled program's <c>.sig</c> (signal names), the <c>.lpz</c>
/// behind a <c>.smw</c>, and sidecars named after the program (<c>.openvp.json</c>, <c>.symbols.json</c>,
/// <c>.primitives.json</c>).
/// </summary>
public static class ProgramSource
{
    private static readonly string[] Sidecars = { ".openvp.json", ".symbols.json", ".primitives.json" };

    /// <summary>
    /// The program first, then everything that goes with it. Files in <paramref name="chosen"/> (picked by the
    /// user alongside the program) always come along; a chosen <c>.sig</c> or <c>.lpz</c> wins over the one
    /// found next to the program. The loaders look for a <c>.sig</c> under its program's name, so it is
    /// renamed to match (copies often get renamed, e.g. with a date added).
    /// </summary>
    public static IReadOnlyList<ProgramFile> Companions(string program, IEnumerable<string>? chosen = null)
    {
        program = Path.GetFullPath(program);
        var dir = Path.GetDirectoryName(program)!;
        var picked = (chosen ?? Array.Empty<string>()).Select(Path.GetFullPath)
            .Where(p => !SamePath(p, program)).Distinct(PathComparer).ToList();
        var list = new List<ProgramFile> { new(program, Path.GetFileName(program)) };
        void Add(string source, string name)
        {
            if (!File.Exists(source)) return;
            if (list.Any(f => SamePath(f.Source, source) || string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(new ProgramFile(source, name));
        }

        var ext = Path.GetExtension(program).ToLowerInvariant();
        string? lpz = ext == ".lpz" ? program : null;
        if (ext == ".smw")
        {
            // Same rule as the .smw runtime: the .lpz of the same name, else the only .lpz in the folder.
            lpz = picked.FirstOrDefault(IsLpz) ?? Path.ChangeExtension(program, ".lpz");
            if (!File.Exists(lpz)) lpz = Only(dir, "*.lpz");
            if (lpz != null) Add(lpz, Path.GetFileName(lpz));
        }
        if (lpz != null)
        {
            var sigs = picked.Where(p => p.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)).ToList();
            var sig = sigs.Count == 1 ? sigs[0] : SigFor(lpz);
            if (sig != null) Add(sig, Path.ChangeExtension(Path.GetFileName(lpz), ".sig"));
        }
        foreach (var p in picked) Add(p, Path.GetFileName(p));
        foreach (var main in new[] { program, lpz }.OfType<string>().Distinct(PathComparer))
            foreach (var s in Sidecars)
                Add(Path.ChangeExtension(main, s), Path.ChangeExtension(Path.GetFileName(main), s));
        return list;
    }

    /// <summary>The signal names for a compiled program: the <c>.sig</c> with its name, else the only <c>.sig</c> in its folder.</summary>
    public static string? SigFor(string lpz)
    {
        var sig = Path.ChangeExtension(lpz, ".sig");
        return File.Exists(sig) ? sig : Only(Path.GetDirectoryName(Path.GetFullPath(lpz))!, "*.sig");
    }

    private static bool IsLpz(string p) => p.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase);

    private static string? Only(string dir, string pattern)
    {
        var all = Directory.GetFiles(dir, pattern);
        return all.Length == 1 ? all[0] : null;
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    private static bool SamePath(string a, string b) => PathComparer.Equals(a, b);
}

/// <summary>
/// Watches a program's files on disk and raises <see cref="Changed"/> once they have changed and then stayed the
/// same for a moment: a compiler writes the .lpz and the .sig one after the other, and a big file over several
/// writes. It polls instead of using FileSystemWatcher, so it works on network shares and Docker bind mounts too.
/// Nothing is raised while the program file is missing or still open for writing.
/// </summary>
public sealed class SourceWatch : IDisposable
{
    private readonly Func<IEnumerable<string>> _files;
    private readonly long _settleMs;
    private readonly Timer _timer;
    private string _accepted;
    private string? _pending;
    private long _pendingSince;
    private int _busy;

    /// <param name="program">The program file.</param>
    /// <param name="files">The files that go with it, asked again on every poll so a .sig that appears later counts.</param>
    public SourceWatch(string program, Func<IEnumerable<string>> files, TimeSpan? poll = null, TimeSpan? settle = null)
    {
        Program = Path.GetFullPath(program);
        _files = files;
        _settleMs = (long)(settle ?? TimeSpan.FromSeconds(2)).TotalMilliseconds;
        // The files as they are now count as loaded: create the watch before reading them, so a change made while
        // they're being copied still triggers a reload.
        _accepted = TryStamp() ?? "";
        var p = poll ?? TimeSpan.FromSeconds(1);
        _timer = new Timer(_ => Tick(), null, p, p);
    }

    public string Program { get; }

    /// <summary>Raised on a thread-pool thread. Handlers must not throw.</summary>
    public event Action? Changed;

    private string? TryStamp()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var f in _files().Append(Program).Select(Path.GetFullPath).Distinct().Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(f);
                sb.Append(f).Append('|');
                if (info.Exists) sb.Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
                sb.Append('\n');
            }
            return sb.ToString();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var now = TryStamp();
            if (now == null) return;
            if (now == _accepted) { _pending = null; return; }
            if (now != _pending) { _pending = now; _pendingSince = Environment.TickCount64; return; }
            if (Environment.TickCount64 - _pendingSince < _settleMs || !CanRead(Program)) return;
            _accepted = now;
            _pending = null;
            Changed?.Invoke();
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>False while the file is missing or another program still has it open for writing (on Windows).</summary>
    private static bool CanRead(string path)
    {
        try { using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose() => _timer.Dispose();
}
