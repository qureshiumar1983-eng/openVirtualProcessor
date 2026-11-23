using System.Security.Cryptography;
using System.Text;

namespace OpenVP.Core.Security;

public sealed class SessionRecord
{
    public string Hash { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public string Ip { get; set; } = "";
}

public sealed class SessionFile { public List<SessionRecord> Sessions { get; set; } = new(); }

/// <summary>
/// Browser sessions: a random 256-bit id in an HttpOnly cookie, kept here only as a SHA-256 hash. A session ends after
/// <see cref="Idle"/> without a request or <see cref="Absolute"/> after login, whichever comes first. Kept in <c>data/security/sessions.json</c>
/// so a restart doesn't log everyone out.
/// </summary>
public sealed class SessionStore
{
    private readonly object _gate = new();
    private readonly string? _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly List<SessionRecord> _sessions;
    private DateTimeOffset _savedAt;

    public TimeSpan Idle { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan Absolute { get; set; } = TimeSpan.FromHours(12);

    public SessionStore(string? dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _file = dataDirectory == null ? null : Path.Combine(dataDirectory, "security", "sessions.json");
        _sessions = _file == null ? new() : LoadSafely(_file);
        _savedAt = _now();
    }

    // A damaged sessions file only logs people out; it never weakens anything.
    private static List<SessionRecord> LoadSafely(string f)
    {
        try { return SecurityFiles.Load<SessionFile>(f).Sessions; } catch (InvalidDataException) { return new(); }
    }

    private static string HashOf(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private void Save() { if (_file != null) { try { SecurityFiles.Save(_file, new SessionFile { Sessions = _sessions }); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } _savedAt = _now(); } }

    private bool Expired(SessionRecord s, DateTimeOffset now) => now - s.LastSeen > Idle || now - s.Created > Absolute;

    public string Create(string userId, string userName, string ip)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        lock (_gate)
        {
            var now = _now();
            _sessions.RemoveAll(s => Expired(s, now));
            _sessions.Add(new SessionRecord { Hash = HashOf(token), UserId = userId, UserName = userName, Created = now, LastSeen = now, Ip = ip });
            Save();
        }
        return token;
    }

    /// <summary>The session for this cookie value, refreshed, or null if there is none or it has timed out.</summary>
    public SessionRecord? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var h = HashOf(token);
        lock (_gate)
        {
            var s = _sessions.FirstOrDefault(x => x.Hash == h);
            if (s == null) return null;
            var now = _now();
            if (Expired(s, now)) { _sessions.Remove(s); Save(); return null; }
            s.LastSeen = now;
            if (now - _savedAt > TimeSpan.FromMinutes(1)) Save();
            return s;
        }
    }

    public void Destroy(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        var h = HashOf(token);
        lock (_gate) { if (_sessions.RemoveAll(x => x.Hash == h) > 0) Save(); }
    }

    /// <summary>Ends every session of a user (password changed, account disabled or deleted). <paramref name="except"/> keeps one.</summary>
    public int DestroyFor(string userId, string? exceptToken = null)
    {
        var keep = exceptToken == null ? null : HashOf(exceptToken);
        lock (_gate) { var n = _sessions.RemoveAll(x => x.UserId == userId && x.Hash != keep); if (n > 0) Save(); return n; }
    }

    public int Count { get { lock (_gate) return _sessions.Count(s => !Expired(s, _now())); } }
    public List<SessionRecord> List() { lock (_gate) return _sessions.Where(s => !Expired(s, _now())).ToList(); }
}
