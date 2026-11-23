namespace OpenVP.Core.Security;

public sealed class RoleMapping
{
    /// <summary>The claim value to look for ("openvp-admins"). Compared without regard to case.</summary>
    public string Value { get; set; } = "";
    public Role Role { get; set; } = Role.Viewer;
}

public sealed class OidcSettings
{
    public bool Enabled { get; set; }
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string Scopes { get; set; } = "openid profile email";
    /// <summary>The claim that holds the person's groups or roles ("roles", "groups").</summary>
    public string RoleClaim { get; set; } = "roles";
    public List<RoleMapping> Mappings { get; set; } = new();
    /// <summary>The role for a person who signs in but matches no mapping. Null refuses them.</summary>
    public Role? DefaultRole { get; set; }
    /// <summary>If not empty, only e-mail addresses at these domains may sign in.</summary>
    public List<string> AllowedDomains { get; set; } = new();
    public string UserClaim { get; set; } = "preferred_username";
    public string ButtonText { get; set; } = "Sign in with single sign-on";
}

public sealed class AcmeSettings
{
    public bool Enabled { get; set; }
    public string? Domain { get; set; }
    public string? Email { get; set; }
    public bool Staging { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public string? LastResult { get; set; }
}

public sealed class HttpsSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 8443;
    /// <summary>Send browsers that open a page over http to https (not from this computer, and never API calls, hooks, /healthz or CIP).</summary>
    public bool Redirect { get; set; }
    /// <summary><c>self-signed</c>, <c>upload</c> or <c>acme</c>.</summary>
    public string? Source { get; set; }
    public string? Subject { get; set; }
    public string? Fingerprint { get; set; }
    public DateTimeOffset? NotAfter { get; set; }
    public List<string> Names { get; set; } = new();
    public AcmeSettings Acme { get; set; } = new();
}

public sealed class SecuritySettingsFile
{
    /// <summary>After users exist: may requests from this computer (localhost) still skip the login? On by default so the desktop app keeps working.</summary>
    public bool LocalExemption { get; set; } = true;
    public int SessionIdleMinutes { get; set; } = 60;
    public int SessionAbsoluteHours { get; set; } = 12;
    public int LockoutAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int AuditMaxMegabytes { get; set; } = 5;
    public int AuditMaxDays { get; set; } = 90;
    public OidcSettings Oidc { get; set; } = new();
    public HttpsSettings Https { get; set; } = new();
}

/// <summary>The settings behind users, sessions, single sign-on and HTTPS, in <c>data/security/settings.json</c>. Secrets (the OIDC client secret, the certificate password) are in the secret store, never here.</summary>
public sealed class SecuritySettings
{
    private readonly object _gate = new();
    private readonly string _file;
    private SecuritySettingsFile _data;

    public SecuritySettings(string dataDirectory)
    {
        _file = Path.Combine(dataDirectory, "security", "settings.json");
        _data = SecurityFiles.Load<SecuritySettingsFile>(_file);
    }

    public SecuritySettingsFile Get() { lock (_gate) return Clone(_data); }

    public void Update(Action<SecuritySettingsFile> change)
    {
        lock (_gate)
        {
            var c = Clone(_data);
            change(c);
            c.SessionIdleMinutes = Math.Clamp(c.SessionIdleMinutes, 5, 60 * 24 * 30);
            c.SessionAbsoluteHours = Math.Clamp(c.SessionAbsoluteHours, 1, 24 * 90);
            c.LockoutAttempts = Math.Clamp(c.LockoutAttempts, 3, 50);
            c.LockoutMinutes = Math.Clamp(c.LockoutMinutes, 1, 24 * 60);
            c.AuditMaxMegabytes = Math.Clamp(c.AuditMaxMegabytes, 1, 1000);
            c.AuditMaxDays = Math.Clamp(c.AuditMaxDays, 1, 3650);
            SecurityFiles.Save(_file, c);
            _data = c;
        }
    }

    private static SecuritySettingsFile Clone(SecuritySettingsFile s) =>
        System.Text.Json.JsonSerializer.Deserialize<SecuritySettingsFile>(System.Text.Json.JsonSerializer.Serialize(s, SecurityFiles.Json), SecurityFiles.Json)!;
}
