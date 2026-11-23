using System.Text;

namespace OpenVP.Core;

/// <summary>One thing a program used that OpenVP doesn't support yet.</summary>
/// <param name="Kind">What sort of thing it is: <c>SIMPL symbol</c>, <c>SIMPL# member</c>, <c>SIMPL+ module</c>...</param>
/// <param name="Name">The symbol, type or member, e.g. <c>Analog Ramp (SmC 64)</c> or <c>Crestron.SimplSharp.CrestronIO.File.Copy</c>.</param>
/// <param name="Detail">Anything that helps whoever builds it (pin counts, the error message). The latest one seen wins.</param>
/// <param name="Seen">How many program loads have hit it. A program loaded again in the same run isn't counted twice.</param>
public sealed record UnsupportedItem(string Kind, string Name, string Detail, int Seen, DateOnly FirstSeen, DateOnly LastSeen);

/// <summary>
/// The list of symbols, types and APIs programs used that OpenVP doesn't support yet, kept in a Markdown
/// file across runs. Run from a source checkout, the server keeps it at <c>docs/unsupported.md</c> in the repo, so
/// committing it shares the list with everyone and shows what to build next. Otherwise it's
/// <c>unsupported.md</c> in the data folder, to copy back into the repo.
/// Program names are never written to the file (they can be client names); only the symbols.
/// </summary>
public sealed class UnsupportedLog : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Kind, string Name), UnsupportedItem> _items = new();
    private readonly HashSet<(string Kind, string Name, string Program)> _countedThisRun = new();
    private readonly Timer _saveTimer;
    private bool _dirty;
    private string? _path;
    private Func<string, string, bool>? _isSupported;

    public UnsupportedLog(string? path = null)
    {
        _saveTimer = new Timer(_ => Flush());
        if (path != null) UseFile(path);
    }

    /// <summary>The file the list is kept in, or null to keep it in memory only.</summary>
    public string? FilePath { get { lock (_gate) return _path; } }

    /// <summary>
    /// Tells the log which things OpenVP supports now (kind, name), so rows written by an older build stop being
    /// listed: they are dropped when the file is read, left out of <see cref="Items"/> and the rewritten file, and
    /// never reported again. Items already held are pruned at once (and the file rewritten).
    /// </summary>
    public void SetSupportCheck(Func<string, string, bool>? isSupported)
    {
        lock (_gate)
        {
            _isSupported = isSupported;
            if (isSupported == null) return;
            var gone = _items.Keys.Where(k => isSupported(k.Kind, k.Name)).ToList();
            if (gone.Count == 0) return;
            foreach (var k in gone) _items.Remove(k);
            _dirty = true;
            if (_path != null) _saveTimer.Change(500, Timeout.Infinite);
        }
    }

    private bool Supported(string kind, string name) => _isSupported != null && _isSupported(kind, name);

    /// <summary>Raised (outside the lock) after a report adds a new item.</summary>
    public event Action<UnsupportedItem>? Added;

    /// <summary>Keep the list in this file from now on, merging in what the file already holds.</summary>
    public void UseFile(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_gate)
        {
            _path = full;
            if (!File.Exists(full)) return;
            try
            {
                foreach (var item in Parse(File.ReadAllLines(full)))
                {
                    if (Supported(item.Kind, item.Name)) { _dirty = true; _saveTimer.Change(500, Timeout.Infinite); continue; }
                    var key = (item.Kind, item.Name);
                    _items[key] = _items.TryGetValue(key, out var mine) ? Merge(mine, item) : item;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public IReadOnlyList<UnsupportedItem> Items
    {
        get { lock (_gate) return Sorted(_items.Values); }
    }

    /// <summary>Record that <paramref name="program"/> used something OpenVP doesn't support. Saved shortly after.</summary>
    public void Report(string kind, string name, string? detail = null, string? program = null)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name)) return;
        kind = Clean(kind);
        name = Clean(name);
        detail = Clean(detail ?? "");
        lock (_gate) if (Supported(kind, name)) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        UnsupportedItem? added = null;
        lock (_gate)
        {
            var key = (kind, name);
            var count = _countedThisRun.Add((kind, name, program ?? "")) ? 1 : 0;
            if (_items.TryGetValue(key, out var old))
            {
                var updated = old with
                {
                    Detail = detail.Length > 0 ? detail : old.Detail,
                    Seen = old.Seen + count,
                    LastSeen = today > old.LastSeen ? today : old.LastSeen
                };
                if (updated == old) return;
                _items[key] = updated;
            }
            else
            {
                added = _items[key] = new UnsupportedItem(kind, name, detail, Math.Max(count, 1), today, today);
            }
            _dirty = true;
            if (_path != null) _saveTimer.Change(500, Timeout.Infinite);
        }
        if (added != null) Added?.Invoke(added);
    }

    /// <summary>Write pending changes to the file now.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty || _path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, Render(_items.Values));
                File.Move(tmp, _path, overwrite: true);
                _dirty = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The list as the Markdown file holds it.</summary>
    public string Render()
    {
        lock (_gate) return Render(_items.Values);
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }

    // ------------------------------------------------------------------ file format

    private const string Header =
        "| Kind | Symbol / API | Details | Programs | First seen | Last seen |\n" +
        "|---|---|---|---:|---|---|";

    private static string Render(IEnumerable<UnsupportedItem> items)
    {
        var sb = new StringBuilder();
        sb.Append("# Unsupported symbols and APIs\n\n");
        sb.Append("OpenVP adds a row here whenever a program uses a symbol, type or API it doesn't support yet. ");
        sb.Append("Commit this file after running your programs so everyone can see what to build next; ");
        sb.Append("**Programs** counts the program loads that hit it, so the top of each kind is the most wanted. ");
        sb.Append("Only symbol names are recorded, never program names.\n\n");
        sb.Append("When you implement one, delete its row (the server also drops rows for symbols the catalog supports now, whether matched by library name, SmC code or compiled type). If a program still needs it, it comes back on the next run.\n\n");
        sb.Append("Merging: if two people's edits clash, keep both rows; for the same row, add the Programs counts and keep the earliest First seen and latest Last seen.\n\n");
        sb.Append(PanelSupportNotes.Markdown).Append("\n\n## Unsupported symbols and types seen in programs\n\n");
        sb.Append(Header).Append('\n');
        foreach (var i in Sorted(items))
            sb.Append($"| {Cell(i.Kind)} | {Cell(i.Name)} | {Cell(i.Detail)} | {i.Seen} | {i.FirstSeen:yyyy-MM-dd} | {i.LastSeen:yyyy-MM-dd} |\n");
        return sb.ToString();
    }

    private static List<UnsupportedItem> Sorted(IEnumerable<UnsupportedItem> items) =>
        items.OrderBy(i => i.Kind, StringComparer.OrdinalIgnoreCase)
             .ThenByDescending(i => i.Seen)
             .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
             .ToList();

    /// <summary>Reads the rows of a file written by <see cref="Render()"/>, skipping anything else (headers, git conflict markers).</summary>
    public static IEnumerable<UnsupportedItem> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<(string, string), UnsupportedItem>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith('|') || !line.EndsWith('|')) continue;
            var cells = SplitRow(line);
            if (cells.Count != 6 || cells[0] == "Kind" || cells[0].StartsWith("---")) continue;
            if (!int.TryParse(cells[3], out var seen)) continue;
            if (!DateOnly.TryParse(cells[4], out var first)) continue;
            if (!DateOnly.TryParse(cells[5], out var last)) last = first;
            var item = new UnsupportedItem(cells[0], cells[1], cells[2], Math.Max(seen, 1), first, last);
            var key = (item.Kind, item.Name);
            result[key] = result.TryGetValue(key, out var other) ? Merge(other, item) : item;
        }
        return result.Values;
    }

    private static UnsupportedItem Merge(UnsupportedItem a, UnsupportedItem b) => a with
    {
        Detail = a.Detail.Length > 0 ? a.Detail : b.Detail,
        Seen = a.Seen + b.Seen,
        FirstSeen = a.FirstSeen < b.FirstSeen ? a.FirstSeen : b.FirstSeen,
        LastSeen = a.LastSeen > b.LastSeen ? a.LastSeen : b.LastSeen
    };

    private static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var cur = new StringBuilder();
        for (int i = 1; i < line.Length - 1; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length - 1 && line[i + 1] == '|') { cur.Append('|'); i++; }
            else if (c == '|') { cells.Add(cur.ToString().Trim()); cur.Clear(); }
            else cur.Append(c);
        }
        cells.Add(cur.ToString().Trim());
        return cells;
    }

    private static string Cell(string s) => s.Replace("|", "\\|");

    private static string Clean(string s)
    {
        s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length > 300 ? s[..300] + "…" : s;
    }
}
