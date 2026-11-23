using System.Security.Cryptography;
using System.Text;

namespace OpenVP.Core.Security;

public sealed class TokenRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Scope Scope { get; set; }
    public int[]? Slots { get; set; }
    public string Hash { get; set; } = "";
    /// <summary>The first characters of the token, so it can be recognised in a list ("ovp_AbCd").</summary>
    public string Prefix { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset? LastUsed { get; set; }
    public string? LastIp { get; set; }
    public bool Revoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public int Uses { get; set; }
}

public sealed class TokenFile { public List<TokenRecord> Tokens { get; set; } = new(); }

public enum TokenCheck { Ok, Unknown, Revoked, Expired }
public sealed record TokenResult(TokenCheck Check, TokenRecord? Token, bool FirstUseToday = false);

/// <summary>
/// Named API tokens in <c>data/security/tokens.json</c>. Only a SHA-256 hash of each token is kept (a token is 256 random bits, so a plain
/// hash is enough); the value is shown once when it is made. A token has a <see cref="Scope"/>, an optional slot restriction and an optional expiry.
/// </summary>
public sealed class TokenStore
{
    public const string Prefix = "ovp_";
    private readonly object _gate = new();
    private readonly string? _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly TokenFile _data;
    private DateTimeOffset _savedAt;
    private readonly Dictionary<string, DateOnly> _usedOn = new();

    public TokenStore(string? dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _file = dataDirectory == null ? null : Path.Combine(dataDirectory, "security", "tokens.json");
        _data = _file == null ? new() : SecurityFiles.Load<TokenFile>(_file);
        _savedAt = _now();
    }

    public static bool LooksLikeToken(string? s) => s != null && s.StartsWith(Prefix, StringComparison.Ordinal) && s.Length >= 20;
    private static string HashOf(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    private void Save() { if (_file != null) { SecurityFiles.Save(_file, _data); _savedAt = _now(); } }
    private static TokenRecord Copy(TokenRecord t) => new()
    {
        Id = t.Id, Name = t.Name, Scope = t.Scope, Slots = t.Slots?.ToArray(), Hash = t.Hash, Prefix = t.Prefix, Created = t.Created, CreatedBy = t.CreatedBy, Expires = t.Expires,
        LastUsed = t.LastUsed, LastIp = t.LastIp, Revoked = t.Revoked, RevokedAt = t.RevokedAt, Uses = t.Uses
    };

    public List<TokenRecord> List() { lock (_gate) return _data.Tokens.Select(Copy).OrderByDescending(t => t.Created).ToList(); }
    public TokenRecord? Get(string id) { lock (_gate) return _data.Tokens.FirstOrDefault(t => t.Id == id) is { } t ? Copy(t) : null; }

    /// <summary>Makes a token. The returned secret is the only time its value exists in clear.</summary>
    public (TokenRecord Token, string Secret) Create(string name, Scope scope, int[]? slots, DateTimeOffset? expires, string? createdBy)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 64) throw new ArgumentException("Give the token a name (up to 64 characters).");
        if (expires != null && expires <= _now()) throw new ArgumentException("The expiry has to be in the future.");
        var secret = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var t = new TokenRecord
        {
            Id = SecurityFiles.NewId(), Name = name, Scope = scope, Slots = slots?.Distinct().Order().ToArray(), Hash = HashOf(secret), Prefix = secret[..8],
            Created = _now(), CreatedBy = createdBy, Expires = expires
        };
        lock (_gate) { _data.Tokens.Add(t); Save(); }
        return (Copy(t), secret);
    }

    public bool Revoke(string id)
    {
        lock (_gate)
        {
            var t = _data.Tokens.FirstOrDefault(x => x.Id == id);
            if (t == null) return false;
            if (!t.Revoked) { t.Revoked = true; t.RevokedAt = _now(); Save(); }
            return true;
        }
    }

    /// <summary>Removes a revoked or expired token from the list for good.</summary>
    public bool Remove(string id)
    {
        lock (_gate) { var n = _data.Tokens.RemoveAll(x => x.Id == id && (x.Revoked || (x.Expires != null && x.Expires <= _now()))); if (n > 0) Save(); return n > 0; }
    }

    /// <summary>Looks a presented token up. <see cref="TokenResult.FirstUseToday"/> is true the first time it is used on a calendar day (UTC), for the audit log.</summary>
    public TokenResult Validate(string? secret, string? ip = null)
    {
        if (!LooksLikeToken(secret)) return new(TokenCheck.Unknown, null);
        var h = HashOf(secret!);
        lock (_gate)
        {
            TokenRecord? t = null;
            foreach (var x in _data.Tokens)
                if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(x.Hash), Encoding.ASCII.GetBytes(h))) t = x;
            if (t == null) return new(TokenCheck.Unknown, null);
            var now = _now();
            if (t.Revoked) return new(TokenCheck.Revoked, Copy(t));
            if (t.Expires != null && t.Expires <= now) return new(TokenCheck.Expired, Copy(t));
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var first = !_usedOn.TryGetValue(t.Id, out var d) || d != today;
            if (first && t.LastUsed is { } lu && DateOnly.FromDateTime(lu.UtcDateTime) == today) first = false; // used earlier today, before a restart
            _usedOn[t.Id] = today;
            t.LastUsed = now; t.LastIp = ip; t.Uses++;
            if (first || now - _savedAt > TimeSpan.FromMinutes(1)) Save();
            return new(TokenCheck.Ok, Copy(t), first);
        }
    }

    /// <summary>Writes the last-used times (called now and then, and at shutdown).</summary>
    public void Flush() { lock (_gate) Save(); }
}
