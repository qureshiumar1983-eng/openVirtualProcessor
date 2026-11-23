namespace OpenVP.Core.Security;

/// <summary>
/// Things a caller may be allowed to do. Every endpoint, WebSocket and console command is mapped to one of these
/// (see EndpointPolicy in the server); a role or a token scope is a set of them.
/// </summary>
public enum Perm
{
    /// <summary>Read slots, signals, logs, metrics, the IP table, alerts, files and the activity feed.</summary>
    View,
    /// <summary>Set or pulse signals (what a panel does).</summary>
    Signal,
    /// <summary>Start, stop and restart a slot's program.</summary>
    Operate,
    /// <summary>Identify clients and run read-only logins from the IP table.</summary>
    IpTable,
    /// <summary>Make and revoke shared-panel links.</summary>
    Share,
    /// <summary>Acknowledge and mute alerts.</summary>
    AlertAck,
    /// <summary>Lock a slot ("live event, don't touch") and unlock a lock you made.</summary>
    LockSlot,
    /// <summary>Load, unload and roll back programs; versions; check a program.</summary>
    LoadProgram,
    /// <summary>The debugger: stepping, watches, monitors, traces, bookmarks.</summary>
    Debug,
    /// <summary>Edit, upload, rename and delete NVRAM and User files.</summary>
    Files,
    /// <summary>Record and replay.</summary>
    Recording,
    /// <summary>Take and delete snapshots.</summary>
    Snapshot,
    /// <summary>Device simulators, COM ports, sockets, redirects and the IP ID remap.</summary>
    Simulator,
    /// <summary>A slot's integrations (MQTT, OSC, Art-Net lists, webhooks, incoming hooks).</summary>
    Integration,
    /// <summary>Run console commands in a slot.</summary>
    Console,
    /// <summary>Create, change and run schedules.</summary>
    Schedule,
    /// <summary>Everything else: users, tokens, HTTPS, single sign-on, audit log, export and import, backups, push targets, git, alerts setup, the internet tunnel and global settings.</summary>
    Admin,
    /// <summary>Open a remote-EISC link to one of this processor's EISCs (the other OpenVP's outbound WebSocket). Nothing else: it reads and sets no other signal.</summary>
    Eisc,
}

public enum Role { Viewer, Technician, Programmer, Admin }

/// <summary>What an API token may do. <see cref="Operator"/> is narrower than a Technician: it has no IP table, sharing or alert acknowledgement.
/// <see cref="EiscLink"/> is for the other end of a remote EISC link: it can open the link WebSocket and do nothing else.</summary>
public enum Scope { ReadOnly, Operator, Programmer, Admin, EiscLink }

public static class Permissions
{
    private static readonly Perm[] ViewerSet = [Perm.View];
    private static readonly Perm[] OperatorSet = [Perm.View, Perm.Signal, Perm.Operate];
    private static readonly Perm[] TechnicianSet = [.. OperatorSet, Perm.IpTable, Perm.Share, Perm.AlertAck, Perm.LockSlot];
    private static readonly Perm[] ProgrammerSet = [.. TechnicianSet, Perm.LoadProgram, Perm.Debug, Perm.Files, Perm.Recording, Perm.Snapshot, Perm.Simulator, Perm.Integration, Perm.Console, Perm.Schedule];
    private static readonly Perm[] AdminSet = Enum.GetValues<Perm>();
    private static readonly Perm[] EiscSet = [Perm.Eisc];

    public static IReadOnlyCollection<Perm> ForRole(Role r) => r switch { Role.Viewer => ViewerSet, Role.Technician => TechnicianSet, Role.Programmer => ProgrammerSet, _ => AdminSet };

    public static IReadOnlyCollection<Perm> ForScope(Scope s) => s switch { Scope.ReadOnly => ViewerSet, Scope.Operator => OperatorSet, Scope.Programmer => ProgrammerSet, Scope.EiscLink => EiscSet, _ => AdminSet };

    public static bool TryParseRole(string? s, out Role r) => Enum.TryParse(s?.Trim(), true, out r) && Enum.IsDefined(r);
    public static bool TryParseScope(string? s, out Scope r)
    {
        var t = s?.Trim().Replace("-", "").Replace("_", "");
        return Enum.TryParse(t, true, out r) && Enum.IsDefined(r);
    }

    public static string ScopeName(Scope s) => s switch { Scope.ReadOnly => "read-only", Scope.Operator => "operator", Scope.Programmer => "programmer", Scope.EiscLink => "eisc-link", _ => "admin" };
    public static string RoleName(Role r) => r.ToString().ToLowerInvariant();
}

public enum PrincipalKind { Anonymous, Local, Key, Token, User }

/// <summary>Who is making a request, and what they may do. Built once per request by the server.</summary>
public sealed class Principal
{
    private static readonly AsyncLocal<Principal?> CurrentValue = new();
    /// <summary>The caller of the request running on this async flow (set by the server's middleware); null on a slot thread, a timer or at start-up.</summary>
    public static Principal? Current { get => CurrentValue.Value; set => CurrentValue.Value = value; }

    public PrincipalKind Kind { get; init; }
    /// <summary>A user name, a token name, "access key", or "this computer".</summary>
    public string Name { get; init; } = "";
    public Role? Role { get; init; }
    public Scope? Scope { get; init; }
    public IReadOnlySet<Perm> Perms { get; init; } = new HashSet<Perm>();
    /// <summary>Slots this caller is limited to; null means every slot.</summary>
    public IReadOnlyList<int>? Slots { get; init; }
    public string? UserId { get; init; }
    public string? TokenId { get; init; }
    public string Ip { get; init; } = "";

    public bool Has(Perm p) => Perms.Contains(p);
    public bool IsAdmin => Perms.Contains(Perm.Admin);
    public bool AllSlots => Slots == null;
    public bool CanSlot(int n) => Slots == null || Slots.Contains(n);
    public bool Authenticated => Kind != PrincipalKind.Anonymous;

    /// <summary>The identity used to remember who made a slot lock ("user:sam", "token:CI", "key").</summary>
    public string LockId => Kind switch { PrincipalKind.User => "user:" + Name, PrincipalKind.Token => "token:" + Name, PrincipalKind.Key => "key", PrincipalKind.Local => "local", _ => "anonymous" };

    /// <summary>What goes in the activity feed's "user" field. Null for a plain local request, whose "who" already says "this computer".</summary>
    public string? ActivityUser => Kind switch { PrincipalKind.User => Name, PrincipalKind.Token => "token " + Name, PrincipalKind.Key => "access key", _ => null };

    /// <summary>"Sam", "token CI", "access key", "this computer": for the audit log and the presence list.</summary>
    public string Display => Kind switch { PrincipalKind.Token => "token " + Name, PrincipalKind.Local => "this computer", _ => Name };

    public static Principal ForRole(Role role, string name, string ip, IReadOnlyList<int>? slots = null, string? userId = null) =>
        new() { Kind = PrincipalKind.User, Name = name, Role = role, Perms = new HashSet<Perm>(Permissions.ForRole(role)), Slots = slots, UserId = userId, Ip = ip };

    public static Principal ForScope(Scope scope, string name, string ip, IReadOnlyList<int>? slots, string tokenId) =>
        new() { Kind = PrincipalKind.Token, Name = name, Scope = scope, Perms = new HashSet<Perm>(Permissions.ForScope(scope)), Slots = slots, TokenId = tokenId, Ip = ip };

    public static Principal AccessKey(string ip) =>
        new() { Kind = PrincipalKind.Key, Name = "access key", Role = Security.Role.Admin, Perms = new HashSet<Perm>(Permissions.ForRole(Security.Role.Admin)), Ip = ip };

    public static Principal LocalMachine(string ip) =>
        new() { Kind = PrincipalKind.Local, Name = "this computer", Role = Security.Role.Admin, Perms = new HashSet<Perm>(Permissions.ForRole(Security.Role.Admin)), Ip = ip };

    public static Principal Anonymous(string ip) => new() { Kind = PrincipalKind.Anonymous, Name = "anonymous", Ip = ip };

    /// <summary>Initials for an avatar: "Sam Lee" is "SL", "sam" is "S", "192.168.1.5" is "1".</summary>
    public static string Initials(string name)
    {
        if (System.Net.IPAddress.TryParse(name, out _)) return name[..1];
        var parts = name.Split(new[] { ' ', '.', '-', '_', '@' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var s = parts.Length == 1 ? parts[0][..1] : parts[0][..1] + parts[1][..1];
        return s.ToUpperInvariant();
    }
}

/// <summary>Small file helpers for the security stores: atomic writes, owner-only on Linux and macOS.</summary>
internal static class SecurityFiles
{
    public static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads the file. A missing file gives a new value; a damaged one throws, because a store that quietly started empty would switch security off.</summary>
    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T(); }
        catch (System.Text.Json.JsonException e) { throw new InvalidDataException($"{path} is damaged ({e.Message}). Fix it or move it away; OpenVP won't start without it, because starting empty would switch that protection off.", e); }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(value, Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, true);
    }

    public static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
}
