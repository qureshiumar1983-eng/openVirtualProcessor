using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>One shared panel: a secret link for one slot and IP ID.</summary>
public sealed class ShareLink
{
    /// <summary>Public handle used by the console (list, revoke). The token itself is the secret.</summary>
    public string Id { get; init; } = "";
    public string Token { get; init; } = "";
    public string Label { get; init; } = "";
    public int Slot { get; init; }
    public uint IpId { get; init; }
    /// <summary>PBKDF2 hash of the PIN and its salt (base64); null when the link has no PIN.</summary>
    public string? PinHash { get; init; }
    public string? PinSalt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsed { get; set; }
    public bool HasPin => PinHash != null;
}

public enum PinOutcome { Ok, Wrong, Locked }

public readonly record struct PinCheck(PinOutcome Outcome, int AttemptsLeft, TimeSpan RetryAfter);

/// <summary>
/// Shareable panel links (<c>data/shares.json</c>). A link is a random 192-bit token tied to one slot and
/// IP ID, with an optional PIN, an optional expiry and a label. Revoking deletes it. Wrong PINs are
/// rate-limited per link and per client. No web types in here, so it can be unit tested.
/// </summary>
public sealed class ShareStore
{
    public const int MaxClientTries = 5;
    public static readonly TimeSpan ClientLockout = TimeSpan.FromMinutes(5);
    /// <summary>Wrong tries across every client before the whole link locks. Higher than the per-client limit so
    /// one person guessing can't easily lock out the real recipient.</summary>
    public const int MaxLinkTries = 20;
    public static readonly TimeSpan LinkLockout = TimeSpan.FromMinutes(5);
    /// <summary>Failures older than this are forgotten.</summary>
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private const int Pbkdf2Iterations = 100_000;
    private static readonly Regex PinRe = new(@"^[0-9]{4,8}$", RegexOptions.Compiled);

    private sealed class Fails { public int Count; public DateTimeOffset Last; public DateTimeOffset LockedUntil; }

    private sealed class FileDto
    {
        public string Secret { get; set; } = "";
        public List<LinkDto> Links { get; set; } = new();
    }
    private sealed class LinkDto
    {
        public string Id { get; set; } = "";
        public string Token { get; set; } = "";
        public string Label { get; set; } = "";
        public int Slot { get; set; }
        public uint IpId { get; set; }
        public string? PinHash { get; set; }
        public string? PinSalt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? LastUsed { get; set; }
    }

    private readonly object _lock = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly List<ShareLink> _links = new();
    private readonly Dictionary<string, int> _connections = new();
    private readonly Dictionary<(string, string), Fails> _clientFails = new();
    private readonly Dictionary<string, Fails> _linkFails = new();
    private byte[] _secret;
    private DateTimeOffset _lastSaved;

    public ShareStore(string dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _path = Path.Combine(dataDirectory, "shares.json");
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _secret = RandomNumberGenerator.GetBytes(32);
        Load();
    }

    public string FilePath => _path;

    /// <summary>Reads the links from disk again (after an import).</summary>
    public void Reload()
    {
        lock (_lock) { _links.Clear(); Load(); }
    }

    /// <summary>"1h", "1d", "1w" or "never" (null/empty also means never). Throws ArgumentException for anything else.</summary>
    public static TimeSpan? ParseExpiry(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "never" => null,
        "1h" => TimeSpan.FromHours(1),
        "1d" => TimeSpan.FromDays(1),
        "1w" => TimeSpan.FromDays(7),
        _ => throw new ArgumentException("Expiry must be 1h, 1d, 1w or never.")
    };

    public static bool IsValidPin(string? pin) => pin != null && PinRe.IsMatch(pin);

    private static string NewToken() => B64Url(RandomNumberGenerator.GetBytes(24)); // 192 bits
    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public ShareLink Create(int slot, uint ipId, string? label, string? pin, TimeSpan? lifetime)
    {
        if (slot < 1) throw new ArgumentException("Slot must be 1 or more.");
        if (ipId is 0 or > 0xFE) throw new ArgumentException("IP ID must be 01-FE.");
        label = (label ?? "").Trim();
        if (label.Length > 80) label = label[..80];
        if (!string.IsNullOrEmpty(pin) && !IsValidPin(pin)) throw new ArgumentException("The PIN must be 4 to 8 digits.");
        if (lifetime is { } l && l <= TimeSpan.Zero) throw new ArgumentException("Expiry must be in the future.");
        string? hash = null, salt = null;
        if (!string.IsNullOrEmpty(pin))
        {
            var s = RandomNumberGenerator.GetBytes(16);
            salt = Convert.ToBase64String(s);
            hash = Convert.ToBase64String(Hash(pin, s));
        }
        var now = _now();
        var link = new ShareLink
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Token = NewToken(),
            Label = label,
            Slot = slot,
            IpId = ipId,
            PinHash = hash,
            PinSalt = salt,
            CreatedAt = now,
            ExpiresAt = lifetime == null ? null : now + lifetime
        };
        lock (_lock)
        {
            PruneLocked();
            _links.Add(link);
            SaveLocked();
        }
        return link;
    }

    private static byte[] Hash(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);

    private bool Live(ShareLink l) => l.ExpiresAt == null || l.ExpiresAt > _now();

    /// <summary>Links that are neither expired nor revoked, oldest first.</summary>
    public IReadOnlyList<ShareLink> Active()
    {
        lock (_lock)
        {
            PruneLocked();
            return _links.ToList();
        }
    }

    /// <summary>The live link with this token, or null (unknown, revoked or expired).</summary>
    public ShareLink? FindByToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        lock (_lock)
        {
            var l = _links.FirstOrDefault(x => x.Token == token);
            return l != null && Live(l) ? l : null;
        }
    }

    public ShareLink? FindById(string? id)
    {
        lock (_lock) { var l = _links.FirstOrDefault(x => x.Id == id); return l != null && Live(l) ? l : null; }
    }

    /// <summary>True while the link still exists and has not expired. Open sockets poll this.</summary>
    public bool IsActive(string id) => FindById(id) != null;

    /// <summary>How long a revoked link can still be brought back with <see cref="Undo"/>.</summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, (ShareLink Link, DateTimeOffset At)> _revoked = new();

    /// <summary>Brings back a link revoked in the last <see cref="UndoWindow"/>, with the same token, PIN and expiry (kept in memory only).
    /// Returns null if it wasn't revoked recently, has expired meanwhile, or was already brought back.</summary>
    public ShareLink? Undo(string id)
    {
        lock (_lock)
        {
            if (!_revoked.TryGetValue(id, out var r)) return null;
            _revoked.Remove(id);
            if (_now() - r.At > UndoWindow || !Live(r.Link) || _links.Any(l => l.Id == id || l.Token == r.Link.Token)) return null;
            _links.Add(r.Link);
            SaveLocked();
            return r.Link;
        }
    }

    public bool Revoke(string id)
    {
        lock (_lock)
        {
            var gone = _links.FirstOrDefault(x => x.Id == id);
            var n = _links.RemoveAll(x => x.Id == id);
            if (n == 0) return false;
            if (gone != null)
            {
                foreach (var k in _revoked.Where(k => _now() - k.Value.At > UndoWindow).Select(k => k.Key).ToList()) _revoked.Remove(k);
                _revoked[id] = (gone, _now());
            }
            _connections.Remove(id);
            foreach (var k in _clientFails.Keys.Where(k => k.Item1 == id).ToList()) _clientFails.Remove(k);
            _linkFails.Remove(id);
            SaveLocked();
            return true;
        }
    }

    private void PruneLocked()
    {
        var gone = _links.Where(l => !Live(l)).Select(l => l.Id).ToList();
        if (gone.Count == 0) return;
        _links.RemoveAll(l => gone.Contains(l.Id));
        foreach (var id in gone) _connections.Remove(id);
        SaveLocked();
    }

    // ------------------------------------------------------------------ PIN

    /// <summary>Checks a PIN guess. Wrong guesses count against the link and against <paramref name="client"/>
    /// (the caller's best idea of who is asking: an address). Locked means "don't even try".</summary>
    public PinCheck CheckPin(ShareLink link, string? pin, string client)
    {
        var now = _now();
        Fails cf, lf;
        lock (_lock)
        {
            if (_clientFails.Count > 5000) _clientFails.Clear();   // a flood of fake clients can't grow this forever
            if (!_clientFails.TryGetValue((link.Id, client), out cf!)) _clientFails[(link.Id, client)] = cf = new Fails();
            if (!_linkFails.TryGetValue(link.Id, out lf!)) _linkFails[link.Id] = lf = new Fails();
            if (cf.Count > 0 && now - cf.Last > FailureWindow) cf.Count = 0;
            if (lf.Count > 0 && now - lf.Last > FailureWindow) lf.Count = 0;
            var until = cf.LockedUntil > lf.LockedUntil ? cf.LockedUntil : lf.LockedUntil;
            if (until > now) return new PinCheck(PinOutcome.Locked, 0, until - now);
        }

        var ok = link.PinHash == null || (pin != null && VerifyPin(link, pin));
        lock (_lock)
        {
            if (ok)
            {
                cf.Count = 0;
                return new PinCheck(PinOutcome.Ok, MaxClientTries, TimeSpan.Zero);
            }
            cf.Count++; cf.Last = now;
            lf.Count++; lf.Last = now;
            if (lf.Count >= MaxLinkTries) { lf.LockedUntil = now + LinkLockout; lf.Count = 0; }
            if (cf.Count >= MaxClientTries) { cf.LockedUntil = now + ClientLockout; cf.Count = 0; return new PinCheck(PinOutcome.Locked, 0, ClientLockout); }
            if (lf.LockedUntil > now) return new PinCheck(PinOutcome.Locked, 0, lf.LockedUntil - now);
            return new PinCheck(PinOutcome.Wrong, MaxClientTries - cf.Count, TimeSpan.Zero);
        }
    }

    private static bool VerifyPin(ShareLink link, string pin)
    {
        if (!IsValidPin(pin)) { Hash("0000", Convert.FromBase64String(link.PinSalt!)); return false; } // keep the timing similar
        var h = Hash(pin, Convert.FromBase64String(link.PinSalt!));
        return CryptographicOperations.FixedTimeEquals(h, Convert.FromBase64String(link.PinHash!));
    }

    /// <summary>Is this client currently locked out of the link, and for how long?</summary>
    public bool IsLocked(ShareLink link, string client, out TimeSpan retryAfter)
    {
        var now = _now();
        lock (_lock)
        {
            var until = DateTimeOffset.MinValue;
            if (_clientFails.TryGetValue((link.Id, client), out var cf)) until = cf.LockedUntil;
            if (_linkFails.TryGetValue(link.Id, out var lf) && lf.LockedUntil > until) until = lf.LockedUntil;
            retryAfter = until > now ? until - now : TimeSpan.Zero;
            return until > now;
        }
    }

    /// <summary>The value for the "PIN accepted" cookie: only the server can make it, and it is only good for this link.</summary>
    public string ProofFor(ShareLink link)
    {
        using var h = new HMACSHA256(_secret);
        return B64Url(h.ComputeHash(Encoding.UTF8.GetBytes("openvp-share-pin|" + link.Token + "|" + link.PinHash)));
    }

    public bool ProofValid(ShareLink link, string? proof)
    {
        if (string.IsNullOrEmpty(proof)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(ProofFor(link)), Encoding.UTF8.GetBytes(proof));
    }

    /// <summary>True if the holder may use the link: it has no PIN, or the proof cookie is good.</summary>
    public bool Authorized(ShareLink link, string? proof) => !link.HasPin || ProofValid(link, proof);

    // ------------------------------------------------------------------ use

    public void Touch(string id)
    {
        lock (_lock)
        {
            var l = _links.FirstOrDefault(x => x.Id == id);
            if (l == null) return;
            var now = _now();
            l.LastUsed = now;
            if (now - _lastSaved > TimeSpan.FromSeconds(30)) SaveLocked();
        }
    }

    public int Connections(string id) { lock (_lock) return _connections.GetValueOrDefault(id); }

    /// <summary>Count one open connection until the returned object is disposed.</summary>
    public IDisposable OpenConnection(string id)
    {
        lock (_lock) _connections[id] = _connections.GetValueOrDefault(id) + 1;
        Touch(id);
        return new Closer(this, id);
    }

    private sealed class Closer(ShareStore s, string id) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            lock (s._lock)
            {
                var n = s._connections.GetValueOrDefault(id) - 1;
                if (n <= 0) s._connections.Remove(id); else s._connections[id] = n;
            }
            s.Touch(id);
        }
    }

    // ------------------------------------------------------------------ file

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(_path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (dto == null) return;
            if (!string.IsNullOrEmpty(dto.Secret))
            {
                try { var s = Convert.FromBase64String(dto.Secret); if (s.Length >= 16) _secret = s; } catch (FormatException) { }
            }
            foreach (var d in dto.Links)
            {
                if (string.IsNullOrEmpty(d.Token) || string.IsNullOrEmpty(d.Id)) continue;
                _links.Add(new ShareLink
                {
                    Id = d.Id, Token = d.Token, Label = d.Label ?? "", Slot = d.Slot, IpId = d.IpId,
                    PinHash = d.PinHash, PinSalt = d.PinSalt, CreatedAt = d.CreatedAt, ExpiresAt = d.ExpiresAt, LastUsed = d.LastUsed
                });
            }
            _links.RemoveAll(l => !Live(l));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged file: keep it for inspection and start empty rather than refuse to start.
            try { File.Move(_path, _path + ".bad", true); } catch { }
        }
    }

    private void SaveLocked()
    {
        _lastSaved = _now();
        var dto = new FileDto
        {
            Secret = Convert.ToBase64String(_secret),
            Links = _links.Select(l => new LinkDto
            {
                Id = l.Id, Token = l.Token, Label = l.Label, Slot = l.Slot, IpId = l.IpId, PinHash = l.PinHash, PinSalt = l.PinSalt,
                CreatedAt = l.CreatedAt, ExpiresAt = l.ExpiresAt, LastUsed = l.LastUsed
            }).ToList()
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            // Tokens are secrets, like apikey.txt: owner-only on Unix before the file gets its real name.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, _path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
