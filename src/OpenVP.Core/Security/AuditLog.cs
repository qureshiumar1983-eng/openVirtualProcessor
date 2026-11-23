using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenVP.Core.Security;

/// <summary>One line of the audit log. <see cref="Hash"/> covers every other field plus <see cref="Prev"/>, the hash of the line before, so changing, removing or reordering a line breaks the chain.</summary>
public sealed class AuditEntry
{
    public long Seq { get; set; }
    public DateTimeOffset Time { get; set; }
    public string? User { get; set; }
    public string Ip { get; set; } = "";
    public string Action { get; set; } = "";
    public string Text { get; set; } = "";
    public int? Slot { get; set; }
    public string? Detail { get; set; }
    public string Prev { get; set; } = "";
    public string Hash { get; set; } = "";
}

public sealed class AuditFilter
{
    public string? User { get; set; }
    public string? Action { get; set; }
    public string? Text { get; set; }
    public int? Slot { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Limit { get; set; } = 200;

    public bool Match(AuditEntry e) =>
        (string.IsNullOrEmpty(User) || string.Equals(e.User, User, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrEmpty(Action) || e.Action.StartsWith(Action, StringComparison.OrdinalIgnoreCase))
        && (Slot == null || e.Slot == Slot)
        && (From == null || e.Time >= From) && (To == null || e.Time <= To)
        && (string.IsNullOrEmpty(Text) || e.Text.Contains(Text, StringComparison.OrdinalIgnoreCase) || (e.Detail?.Contains(Text, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.User?.Contains(Text, StringComparison.OrdinalIgnoreCase) ?? false) || e.Ip.Contains(Text, StringComparison.OrdinalIgnoreCase));
}

public sealed record AuditVerify(bool Ok, long Entries, int Files, string? Message, string? File, long? Line, string? Head, long? HeadSeq);

/// <summary>
/// Tamper-evident log of security-relevant events: <c>data/audit/audit.jsonl</c>, append-only, one JSON object per line, each with the SHA-256 of
/// the line before it. <see cref="Verify"/> walks every file and says where the chain first breaks. When the file passes <see cref="MaxBytes"/> or its
/// first entry is older than <see cref="MaxAge"/> it is renamed to <c>audit-YYYYMMDD-HHMMSS.jsonl</c> and a new file carries on the chain; only the newest
/// <see cref="KeepFiles"/> are kept. <c>head.json</c> remembers the newest entry's number and hash, so cutting lines off the end is also caught.
/// This shows that the log was changed; it can't stop someone who can rewrite every file and head.json at once, so copy the head hash somewhere else if that matters.
/// </summary>
public sealed class AuditLog
{
    public const string Zero = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly Func<DateTimeOffset> _now;
    private long _seq;
    private string _head = Zero;
    private DateTimeOffset? _firstTime;

    public long MaxBytes { get; set; } = 5L * 1024 * 1024;
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(90);
    public int KeepFiles { get; set; } = 40;
    public event Action<AuditEntry>? Added;

    public AuditLog(string dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _dir = Path.Combine(dataDirectory, "audit");
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_dir);
        // Pick the chain up where the newest file ends.
        var last = ReadFile(CurrentFile).LastOrDefault() ?? Files().Where(f => f != CurrentFile).Select(f => ReadFile(f).LastOrDefault()).LastOrDefault(e => e != null);
        if (last != null) { _seq = last.Seq; _head = last.Hash; }
        _firstTime = ReadFile(CurrentFile).FirstOrDefault()?.Time;
    }

    public string DirectoryPath => _dir;
    public string CurrentFile => Path.Combine(_dir, "audit.jsonl");
    private string HeadFile => Path.Combine(_dir, "head.json");
    public string Head { get { lock (_gate) return _head; } }
    public long LastSeq { get { lock (_gate) return _seq; } }

    /// <summary>Archives oldest first, then the current file.</summary>
    private List<string> Files()
    {
        var all = System.IO.Directory.GetFiles(_dir, "audit-*.jsonl").Order(StringComparer.Ordinal).ToList();
        if (File.Exists(CurrentFile)) all.Add(CurrentFile);
        return all;
    }

    public static string ComputeHash(AuditEntry e)
    {
        // One JSON array of every field in a fixed order: no ambiguity about where one field ends.
        var canon = JsonSerializer.Serialize(new object?[] { e.Seq, e.Time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), e.User, e.Ip, e.Action, e.Text, e.Slot, e.Detail, e.Prev });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canon))).ToLowerInvariant();
    }

    public AuditEntry Add(string action, string text, string? user = null, string? ip = null, int? slot = null, string? detail = null)
    {
        AuditEntry e;
        lock (_gate)
        {
            RotateIfNeeded();
            e = new AuditEntry { Seq = _seq + 1, Time = _now().ToUniversalTime(), User = user, Ip = ip ?? "", Action = action, Text = text.Length > 600 ? text[..600] : text, Slot = slot, Detail = detail is { Length: > 600 } ? detail[..600] : detail, Prev = _head };
            e.Hash = ComputeHash(e);
            try
            {
                File.AppendAllText(CurrentFile, JsonSerializer.Serialize(e, Json) + "\n", Encoding.UTF8);
                if (!OperatingSystem.IsWindows() && _seq == 0) File.SetUnixFileMode(CurrentFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.WriteAllText(HeadFile, JsonSerializer.Serialize(new { seq = e.Seq, hash = e.Hash, time = e.Time }, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* an unwritable disk must not stop the server; the gap shows in Verify */ }
            _seq = e.Seq; _head = e.Hash; _firstTime ??= e.Time;
        }
        try { Added?.Invoke(e); } catch { }
        return e;
    }

    // Called with the lock held.
    private void RotateIfNeeded()
    {
        try
        {
            var f = new FileInfo(CurrentFile);
            if (!f.Exists || f.Length == 0) return;
            var tooBig = f.Length >= MaxBytes;
            var tooOld = _firstTime != null && _now() - _firstTime > MaxAge;
            if (!tooBig && !tooOld) return;
            var name = Path.Combine(_dir, $"audit-{_now().UtcDateTime:yyyyMMdd-HHmmss}-{_seq:D9}.jsonl");
            File.Move(CurrentFile, name, true);
            _firstTime = null;
            var archives = System.IO.Directory.GetFiles(_dir, "audit-*.jsonl").Order(StringComparer.Ordinal).ToList();
            foreach (var old in archives.Take(Math.Max(0, archives.Count - KeepFiles))) File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static List<AuditEntry> ReadFile(string path)
    {
        var list = new List<AuditEntry>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length == 0) continue;
            try { if (JsonSerializer.Deserialize<AuditEntry>(line, Json) is { } e) list.Add(e); } catch (JsonException) { }
        }
        return list;
    }

    /// <summary>Newest first. Reads the files from the newest backwards and stops once <see cref="AuditFilter.Limit"/> entries match.</summary>
    public List<AuditEntry> Query(AuditFilter? f = null)
    {
        f ??= new AuditFilter();
        var limit = Math.Clamp(f.Limit, 1, 100_000);
        var res = new List<AuditEntry>();
        lock (_gate)
        {
            foreach (var file in Enumerable.Reverse(Files()))
            {
                foreach (var e in Enumerable.Reverse(ReadFile(file)))
                {
                    if (f.Match(e)) res.Add(e);
                    if (res.Count >= limit) return res;
                }
            }
        }
        return res;
    }

    public static string Csv(IEnumerable<AuditEntry> entries)
    {
        static string Q(string? s)
        {
            s ??= "";
            if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s; // stops a spreadsheet running an entry as a formula
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder("seq,time,user,ip,action,slot,text,detail,hash\r\n");
        foreach (var e in entries)
            sb.Append(e.Seq).Append(',').Append(e.Time.ToString("O", CultureInfo.InvariantCulture)).Append(',').Append(Q(e.User)).Append(',').Append(Q(e.Ip)).Append(',').Append(Q(e.Action)).Append(',')
              .Append(e.Slot?.ToString() ?? "").Append(',').Append(Q(e.Text)).Append(',').Append(Q(e.Detail)).Append(',').Append(e.Hash).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Walks every file in order and checks each line's hash, its link to the line before and the numbering.</summary>
    public AuditVerify Verify()
    {
        lock (_gate)
        {
            var files = Files();
            long count = 0; string? prev = null; long? lastSeq = null;
            foreach (var file in files)
            {
                long line = 0;
                foreach (var raw in File.ReadLines(file, Encoding.UTF8))
                {
                    line++;
                    if (raw.Length == 0) continue;
                    AuditEntry? e;
                    try { e = JsonSerializer.Deserialize<AuditEntry>(raw, Json); } catch (JsonException) { e = null; }
                    var name = Path.GetFileName(file);
                    if (e == null) return new(false, count, files.Count, $"Line {line} of {name} isn't a valid entry.", name, line, _head, _seq);
                    if (e.Hash != ComputeHash(e)) return new(false, count, files.Count, $"Entry {e.Seq} (line {line} of {name}) was changed: its hash doesn't match its contents.", name, line, _head, _seq);
                    // The first entry we can see is the anchor (older files may have been rotated away); after that every entry must follow on.
                    if (prev != null && e.Prev != prev) return new(false, count, files.Count, $"Entry {e.Seq} (line {line} of {name}) doesn't follow the entry before it: a line was removed, added or moved.", name, line, _head, _seq);
                    if (lastSeq != null && e.Seq != lastSeq + 1) return new(false, count, files.Count, $"Entry numbers jump from {lastSeq} to {e.Seq} at line {line} of {name}: entries are missing.", name, line, _head, _seq);
                    prev = e.Hash; lastSeq = e.Seq; count++;
                }
            }
            // The newest entry must be the one head.json remembers.
            try
            {
                if (File.Exists(HeadFile))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(HeadFile));
                    var hs = doc.RootElement.GetProperty("seq").GetInt64();
                    var hh = doc.RootElement.GetProperty("hash").GetString();
                    if (hs != (lastSeq ?? 0) || (lastSeq != null && hh != prev))
                        return new(false, count, files.Count, hs > (lastSeq ?? 0) ? $"The log ends at entry {lastSeq ?? 0} but entry {hs} was written: the end of the log was cut off." : "The newest entry doesn't match the head record.", null, null, _head, _seq);
                }
                else if (count > 0) return new(false, count, files.Count, "head.json is missing, so cutting entries off the end can't be ruled out.", null, null, _head, _seq);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IOException) { return new(false, count, files.Count, "head.json is damaged.", null, null, _head, _seq); }
            return new(true, count, files.Count, count == 0 ? "The log is empty." : $"All {count} entries are intact.", null, null, _head, _seq);
        }
    }
}
