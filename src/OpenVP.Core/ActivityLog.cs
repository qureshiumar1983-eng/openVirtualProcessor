using System.Text;
using System.Text.Json;

namespace OpenVP.Core;

/// <summary>
/// One line of the activity feed: what happened, to which slot, and who did it.
/// </summary>
/// <param name="Time">When it happened (UTC).</param>
/// <param name="Action">A short machine-readable kind, e.g. <c>load</c>, <c>unload</c>, <c>start</c>, <c>rollback</c>, <c>share.create</c>, <c>internet.on</c>, <c>crash</c>, <c>restore</c>.</param>
/// <param name="Text">What to show, e.g. "Loaded Room.lpz into slot 3".</param>
/// <param name="Slot">The slot it was about, or null for the whole processor.</param>
/// <param name="Who">Who did it, as far as OpenVP can tell: "this computer", an address, "share link Boardroom", or "auto".</param>
/// <param name="User">A signed-in user name. Always null until OpenVP has user accounts; kept so the file format doesn't change then.</param>
/// <param name="Detail">Anything extra (a file name, a reason).</param>
public sealed record ActivityEntry(DateTimeOffset Time, string Action, string Text, int? Slot, string Who, string? User = null, string? Detail = null);

/// <summary>Names for the common "who"s, so the strings are the same everywhere.</summary>
public static class Actors
{
    public const string ThisComputer = "this computer";
    public const string Auto = "auto";
    public const string Unknown = "unknown";

    /// <summary>The address of a remote client, or <see cref="ThisComputer"/> when the request came from the OpenVP computer itself.</summary>
    public static string FromAddress(string? address, bool isThisComputer) =>
        isThisComputer ? ThisComputer : string.IsNullOrWhiteSpace(address) ? Unknown : address.Trim();

    /// <summary>Someone using a shared-panel link.</summary>
    public static string ShareLink(string? label) => string.IsNullOrWhiteSpace(label) ? "share link" : "share link " + label.Trim();
}

/// <summary>
/// A processor-wide feed of who did what and when. Entries are kept in memory and appended as JSON lines to
/// <c>activity.jsonl</c> in the data folder; only the last <see cref="Max"/> are kept (the file is trimmed when it grows
/// a tenth past that). Safe to use from any thread.
/// </summary>
public sealed class ActivityLog
{
    public const int DefaultMax = 5000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly object _gate = new();
    private readonly List<ActivityEntry> _entries = new();
    private readonly string? _file;
    private int _linesInFile;

    public int Max { get; }

    /// <summary>Raised (outside the lock) for each new entry.</summary>
    public event Action<ActivityEntry>? Added;

    /// <param name="dataDirectory">Where <c>activity.jsonl</c> lives; null keeps the feed in memory only.</param>
    /// <param name="max">How many entries to keep (at least 10).</param>
    public ActivityLog(string? dataDirectory, int max = DefaultMax)
    {
        Max = Math.Max(10, max);
        if (dataDirectory == null) return;
        _file = Path.Combine(dataDirectory, "activity.jsonl");
        try
        {
            if (!File.Exists(_file)) return;
            foreach (var line in File.ReadLines(_file, Encoding.UTF8))
            {
                _linesInFile++;
                if (line.Length == 0) continue;
                try
                {
                    var e = JsonSerializer.Deserialize<ActivityEntry>(line, Json);
                    if (e != null && !string.IsNullOrEmpty(e.Action)) _entries.Add(e with { Who = string.IsNullOrEmpty(e.Who) ? Actors.Unknown : e.Who, Text = e.Text ?? "" });
                }
                catch (JsonException) { /* a damaged line is skipped, the rest of the feed is fine */ }
            }
            if (_entries.Count > Max) _entries.RemoveRange(0, _entries.Count - Max);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _entries.Clear(); }
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>Adds an entry now. Never throws: a feed that can't be saved still works in memory.</summary>
    public ActivityEntry Add(string action, string text, int? slot = null, string? who = null, string? user = null, string? detail = null) =>
        Add(new ActivityEntry(DateTimeOffset.UtcNow, action, text, slot, string.IsNullOrWhiteSpace(who) ? Actors.ThisComputer : who.Trim(), user, detail));

    public ActivityEntry Add(ActivityEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > Max) _entries.RemoveRange(0, _entries.Count - Max);
            Save(entry);
        }
        try { Added?.Invoke(entry); } catch { }
        return entry;
    }

    // Called with the lock held.
    private void Save(ActivityEntry entry)
    {
        if (_file == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            if (_linesInFile + 1 > Max + Math.Max(1, Max / 10))
            {
                // Rewrite with just what is kept (the new entry is already in the list).
                var tmp = _file + ".tmp";
                File.WriteAllLines(tmp, _entries.Select(e => JsonSerializer.Serialize(e, Json)), Encoding.UTF8);
                File.Move(tmp, _file, overwrite: true);
                _linesInFile = _entries.Count;
            }
            else
            {
                File.AppendAllText(_file, JsonSerializer.Serialize(entry, Json) + "\n", Encoding.UTF8);
                _linesInFile++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* keep it in memory */ }
    }

    /// <summary>The newest entries first. <paramref name="slot"/> keeps only entries about that slot; <paramref name="limit"/> is capped at <see cref="Max"/>.</summary>
    public IReadOnlyList<ActivityEntry> Query(int? slot = null, int limit = 200)
    {
        limit = Math.Clamp(limit, 1, Max);
        var list = new List<ActivityEntry>(Math.Min(limit, 256));
        lock (_gate)
        {
            for (var i = _entries.Count - 1; i >= 0 && list.Count < limit; i--)
                if (slot == null || _entries[i].Slot == slot) list.Add(_entries[i]);
        }
        return list;
    }
}
