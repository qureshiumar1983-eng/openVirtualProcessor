using System.Text.RegularExpressions;

namespace OpenVP.Core.Security;

public sealed class UserRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Display { get; set; }
    public Role Role { get; set; } = Role.Viewer;
    /// <summary>Slots this user is limited to; null means all.</summary>
    public int[]? Slots { get; set; }
    public string? PasswordHash { get; set; }
    /// <summary><c>local</c> (password) or <c>oidc</c> (signed in through single sign-on; no password).</summary>
    public string Source { get; set; } = "local";
    public bool Disabled { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? LastLogin { get; set; }
    public DateTimeOffset? PasswordChanged { get; set; }
    public int Failed { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

public sealed class UserFile { public List<UserRecord> Users { get; set; } = new(); }

public enum LoginOutcome { Ok, Wrong, Locked, Disabled }
public sealed record LoginResult(LoginOutcome Outcome, UserRecord? User, TimeSpan? LockedFor = null);

/// <summary>
/// Local users in <c>data/security/users.json</c> (owner-only on Linux and macOS; passwords are hashes). Wrong passwords for an account are counted;
/// <see cref="MaxFailures"/> in a row lock the account for <see cref="LockoutFor"/>. A right password clears the count.
/// </summary>
public sealed class UserStore
{
    private static readonly Regex NameRule = new(@"^[A-Za-z0-9][A-Za-z0-9._@\- ]{0,63}$", RegexOptions.Compiled);
    private readonly object _gate = new();
    private readonly string _file;
    private readonly Func<DateTimeOffset> _now;
    private UserFile _data;

    public int MaxFailures { get; set; } = 5;
    public TimeSpan LockoutFor { get; set; } = TimeSpan.FromMinutes(15);

    public UserStore(string dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _file = Path.Combine(dataDirectory, "security", "users.json");
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _data = SecurityFiles.Load<UserFile>(_file);
    }

    public string FilePath => _file;
    /// <summary>True once any user exists. Until then OpenVP behaves as it always has (access key and the localhost exemption).</summary>
    public bool Any { get { lock (_gate) return _data.Users.Count > 0; } }
    public int Count { get { lock (_gate) return _data.Users.Count; } }

    public List<UserRecord> List() { lock (_gate) return _data.Users.Select(Copy).OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
    public UserRecord? Get(string id) { lock (_gate) return _data.Users.FirstOrDefault(u => u.Id == id) is { } u ? Copy(u) : null; }
    public UserRecord? FindByName(string? name) { lock (_gate) return Find(name) is { } u ? Copy(u) : null; }

    public static string? NameProblem(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Give the user a name." :
        !NameRule.IsMatch(name.Trim()) ? "A user name is 1-64 letters, digits, spaces, or . _ @ -, and starts with a letter or digit." : null;

    private UserRecord? Find(string? name) => name == null ? null : _data.Users.FirstOrDefault(u => string.Equals(u.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    private static UserRecord Copy(UserRecord u) => new()
    {
        Id = u.Id, Name = u.Name, Display = u.Display, Role = u.Role, Slots = u.Slots?.ToArray(), PasswordHash = u.PasswordHash, Source = u.Source, Disabled = u.Disabled,
        Created = u.Created, LastLogin = u.LastLogin, PasswordChanged = u.PasswordChanged, Failed = u.Failed, LockedUntil = u.LockedUntil
    };
    private void Save() => SecurityFiles.Save(_file, _data);

    private static int[]? CleanSlots(int[]? slots) => slots == null ? null : slots.Distinct().Order().ToArray();

    public UserRecord Create(string name, string? password, Role role, int[]? slots = null, string source = "local", string? display = null)
    {
        if (NameProblem(name) is { } np) throw new ArgumentException(np);
        if (source == "local" && PasswordHasher.Problem(password, name.Trim()) is { } pp) throw new ArgumentException(pp);
        lock (_gate)
        {
            if (Find(name) != null) throw new ArgumentException($"There's already a user called {name.Trim()}.");
            var u = new UserRecord
            {
                Id = SecurityFiles.NewId(), Name = name.Trim(), Display = display, Role = role, Slots = CleanSlots(slots), Source = source, Created = _now(),
                PasswordHash = source == "local" ? PasswordHasher.Hash(password!) : null, PasswordChanged = source == "local" ? _now() : null
            };
            _data.Users.Add(u);
            Save();
            return Copy(u);
        }
    }

    /// <summary>Changes any of role, slots (<paramref name="setSlots"/> true; null slots = all), disabled and display. Refuses to leave users without an enabled Admin.</summary>
    public UserRecord Update(string id, Role? role = null, bool setSlots = false, int[]? slots = null, bool? disabled = null, string? display = null)
    {
        lock (_gate)
        {
            var u = _data.Users.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("There's no such user.");
            var before = Copy(u);
            if (role != null) u.Role = role.Value;
            if (setSlots) u.Slots = CleanSlots(slots);
            if (disabled != null) { u.Disabled = disabled.Value; if (!u.Disabled) { u.Failed = 0; u.LockedUntil = null; } }
            if (display != null) u.Display = display.Length == 0 ? null : display.Trim();
            if (!HasAdminOrEmpty()) { Replace(u, before); throw new InvalidOperationException("There must be at least one enabled Admin."); }
            Save();
            return Copy(u);
        }
    }

    private static void Replace(UserRecord to, UserRecord from) { to.Role = from.Role; to.Slots = from.Slots; to.Disabled = from.Disabled; to.Display = from.Display; }
    private bool HasAdminOrEmpty() => _data.Users.Count == 0 || _data.Users.Any(u => u.Role == Role.Admin && !u.Disabled);

    public void SetPassword(string id, string newPassword)
    {
        lock (_gate)
        {
            var u = _data.Users.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("There's no such user.");
            if (u.Source != "local") throw new InvalidOperationException("This user signs in with single sign-on and has no password here.");
            if (PasswordHasher.Problem(newPassword, u.Name) is { } p) throw new ArgumentException(p);
            u.PasswordHash = PasswordHasher.Hash(newPassword);
            u.PasswordChanged = _now();
            u.Failed = 0; u.LockedUntil = null;
            Save();
        }
    }

    public void Unlock(string id)
    {
        lock (_gate) { if (_data.Users.FirstOrDefault(x => x.Id == id) is { } u) { u.Failed = 0; u.LockedUntil = null; Save(); } }
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var u = _data.Users.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("There's no such user.");
            _data.Users.Remove(u);
            if (!HasAdminOrEmpty()) { _data.Users.Add(u); throw new InvalidOperationException("There must be at least one enabled Admin. Delete the other users first, or make another user an Admin."); }
            Save();
        }
    }

    /// <summary>Checks a name and password. Counts the failure against the account; never says which of the two was wrong.</summary>
    public LoginResult Authenticate(string? name, string? password)
    {
        lock (_gate)
        {
            var u = Find(name);
            if (u == null || u.Source != "local") { PasswordHasher.Burn(); return new(LoginOutcome.Wrong, null); }
            var now = _now();
            if (u.LockedUntil is { } until && until > now) { PasswordHasher.Burn(); return new(LoginOutcome.Locked, null, until - now); }
            var ok = PasswordHasher.Verify(password ?? "", u.PasswordHash);
            if (!ok)
            {
                u.Failed++;
                TimeSpan? locked = null;
                if (u.Failed >= MaxFailures) { u.LockedUntil = now + LockoutFor; u.Failed = 0; locked = LockoutFor; }
                Save();
                return locked != null ? new(LoginOutcome.Locked, null, locked) : new(LoginOutcome.Wrong, null);
            }
            if (u.Disabled) return new(LoginOutcome.Disabled, null);
            u.Failed = 0; u.LockedUntil = null; u.LastLogin = now;
            if (PasswordHasher.NeedsRehash(u.PasswordHash!)) u.PasswordHash = PasswordHasher.Hash(password!);
            Save();
            return new(LoginOutcome.Ok, Copy(u));
        }
    }

    /// <summary>Checks the current password for a password change, without counting a failure.</summary>
    public bool VerifyPassword(string id, string? password)
    {
        lock (_gate) return _data.Users.FirstOrDefault(x => x.Id == id) is { } u && PasswordHasher.Verify(password ?? "", u.PasswordHash);
    }

    /// <summary>A single-sign-on user: made on first login, role and slots refreshed on every later one.</summary>
    public UserRecord UpsertSso(string name, Role role, string? display)
    {
        lock (_gate)
        {
            var u = _data.Users.FirstOrDefault(x => x.Source == "oidc" && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (u == null)
            {
                if (Find(name) != null) throw new InvalidOperationException($"A local user called {name} already exists; single sign-on won't take over that name.");
                u = new UserRecord { Id = SecurityFiles.NewId(), Name = name, Source = "oidc", Created = _now() };
                _data.Users.Add(u);
            }
            if (u.Disabled) throw new InvalidOperationException("This account is disabled.");
            u.Role = role; u.Display = display; u.LastLogin = _now();
            Save();
            return Copy(u);
        }
    }
}
