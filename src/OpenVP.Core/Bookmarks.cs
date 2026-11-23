using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenVP.Core;

/// <summary>Starred signals and notes for one program.</summary>
public sealed record Bookmarks
{
    public List<string> Starred { get; init; } = new();
    public Dictionary<string, string> Notes { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Starred signals and per-signal notes, kept on the server per <b>program name</b> (<c>data/bookmarks/&lt;name&gt;.json</c>),
/// so they survive reloading the program, moving it to another slot and restarting OpenVP, and are the same on every computer
/// that opens the debugger.
/// </summary>
public sealed class BookmarkStore
{
    public const int MaxStarred = 2000, MaxNotes = 2000, MaxNoteLength = 500;
    private readonly string _dir;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public BookmarkStore(string dataDirectory) => _dir = Path.Combine(dataDirectory, "bookmarks");

    /// <summary>The file for a program name: readable characters kept, plus a short hash so two names that sanitize alike stay apart.</summary>
    public static string FileNameFor(string program)
    {
        var clean = new string((program ?? "").Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray()).Trim('.');
        if (clean.Length > 60) clean = clean[..60];
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(program ?? ""))).ToLowerInvariant()[..8];
        return $"{(clean.Length == 0 ? "program" : clean)}-{hash}.json";
    }

    private string PathFor(string program) => Path.Combine(_dir, FileNameFor(program));

    public Bookmarks Get(string program)
    {
        lock (_gate) return Read(program);
    }

    private Bookmarks Read(string program)
    {
        try
        {
            var p = PathFor(program);
            return File.Exists(p) ? JsonSerializer.Deserialize<Bookmarks>(File.ReadAllText(p), Json) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    private void Write(string program, Bookmarks b)
    {
        Directory.CreateDirectory(_dir);
        var p = PathFor(program);
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(b, Json));
        File.Move(tmp, p, overwrite: true);
    }

    /// <summary>Star or unstar a signal.</summary>
    public Bookmarks SetStar(string program, string signal, bool starred)
    {
        Check(program, signal);
        lock (_gate)
        {
            var b = Read(program);
            var set = new HashSet<string>(b.Starred, StringComparer.Ordinal);
            if (starred) { if (set.Count >= MaxStarred && !set.Contains(signal)) throw new InvalidOperationException($"At most {MaxStarred} starred signals."); set.Add(signal); }
            else set.Remove(signal);
            b = b with { Starred = set.Order(StringComparer.Ordinal).ToList() };
            Write(program, b);
            return b;
        }
    }

    /// <summary>Set a signal's note; an empty note removes it.</summary>
    public Bookmarks SetNote(string program, string signal, string? note)
    {
        Check(program, signal);
        note = (note ?? "").Trim();
        if (note.Length > MaxNoteLength) throw new ArgumentException($"A note can be at most {MaxNoteLength} characters.");
        lock (_gate)
        {
            var b = Read(program);
            var notes = new Dictionary<string, string>(b.Notes, StringComparer.Ordinal);
            if (note.Length == 0) notes.Remove(signal);
            else { if (notes.Count >= MaxNotes && !notes.ContainsKey(signal)) throw new InvalidOperationException($"At most {MaxNotes} notes."); notes[signal] = note; }
            b = b with { Notes = notes };
            Write(program, b);
            return b;
        }
    }

    private static void Check(string program, string signal)
    {
        if (string.IsNullOrWhiteSpace(program)) throw new ArgumentException("There is no program in that slot.");
        if (string.IsNullOrEmpty(signal) || signal.Length > 300) throw new ArgumentException("Give a signal name.");
    }
}

/// <summary>Finds signals by name across all slots.</summary>
public static class SignalSearch
{
    public sealed record Hit(int Slot, string Program, string Signal, string Kind, string Value);

    /// <summary>Signals whose name contains every word of <paramref name="query"/> (case-insensitive; <c>*</c> matches anything), names that start with the text first.</summary>
    public static IReadOnlyList<Hit> Find(Processor p, string query, int max = 50)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(w => w.ToLowerInvariant()).ToArray();
        if (words.Length == 0) return Array.Empty<Hit>();
        var res = new List<(int Rank, Hit Hit)>();
        foreach (var slot in p.Slots)
        {
            var rt = slot.Runtime;
            if (rt == null) continue;
            IReadOnlyList<SignalInfo> list;
            try { list = rt.GetSignals(); } catch { continue; }
            foreach (var s in list)
            {
                var n = s.Name.ToLowerInvariant();
                if (!words.All(w => Matches(n, w))) continue;
                var rank = n == words[0] ? 0 : n.StartsWith(words[0], StringComparison.Ordinal) ? 1 : 2;
                res.Add((rank, new Hit(slot.Number, rt.ProgramName, s.Name, s.Kind.ToString(), ValueText(s))));
                if (res.Count > 5000) break;
            }
        }
        return res.OrderBy(r => r.Rank).ThenBy(r => r.Hit.Slot).ThenBy(r => r.Hit.Signal, StringComparer.OrdinalIgnoreCase).Take(max).Select(r => r.Hit).ToList();
    }

    private static bool Matches(string name, string word)
    {
        if (!word.Contains('*')) return name.Contains(word, StringComparison.Ordinal);
        var parts = word.Split('*', StringSplitOptions.RemoveEmptyEntries);
        var pos = 0;
        foreach (var part in parts)
        {
            var i = name.IndexOf(part, pos, StringComparison.Ordinal);
            if (i < 0) return false;
            pos = i + part.Length;
        }
        return true;
    }

    private static string ValueText(SignalInfo s) => s.Value switch { bool b => b ? "1" : "0", null => "", var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "" };
}
