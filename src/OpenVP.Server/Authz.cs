using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Routing;
using OpenVP.Core;
using OpenVP.Core.Security;

namespace OpenVP.Server;

/// <summary>
/// Who is calling and what they may do. It replaces the access-key-only check with a <see cref="Principal"/>: the access key (an Admin, as always),
/// an API token (its scope and slots), a signed-in user (their role and slots), or, with no login at all, this computer. Then every endpoint is
/// checked against <see cref="EndpointPolicy"/> on the server, whatever the page hides.
///
/// While no user exists nothing changes: the access key and the localhost exemption work exactly as they did. Once a user exists the exemption
/// can be switched off in the settings (it is on by default so the desktop app keeps working), and the access key stays valid for Admins and scripts.
/// </summary>
public sealed class Authz
{
    public const string SessionCookie = "openvp_session";
    private const string ItemKey = "openvp.principal";

    public ApiAuth Key { get; }
    public UserStore Users { get; }
    public SessionStore Sessions { get; }
    public TokenStore Tokens { get; }
    public SecuritySettings Settings { get; }
    public SlotLocks Locks { get; }
    public AuditLog Audit { get; }
    public AuthThrottle LoginThrottle { get; }

    private readonly ConcurrentDictionary<string, DateTimeOffset> _denials = new();

    public Authz(ApiAuth key, UserStore users, SessionStore sessions, TokenStore tokens, SecuritySettings settings, SlotLocks locks, AuditLog audit, AuthThrottle? throttle = null)
    {
        Key = key; Users = users; Sessions = sessions; Tokens = tokens; Settings = settings; Locks = locks; Audit = audit;
        LoginThrottle = throttle ?? new AuthThrottle();
        ApplySettings();
    }

    /// <summary>Reads the timeouts and lock-out limits from the settings again (after they were changed).</summary>
    public void ApplySettings()
    {
        var s = Settings.Get();
        Sessions.Idle = TimeSpan.FromMinutes(s.SessionIdleMinutes);
        Sessions.Absolute = TimeSpan.FromHours(s.SessionAbsoluteHours);
        Users.MaxFailures = s.LockoutAttempts;
        Users.LockoutFor = TimeSpan.FromMinutes(s.LockoutMinutes);
        Audit.MaxBytes = s.AuditMaxMegabytes * 1024L * 1024;
        Audit.MaxAge = TimeSpan.FromDays(s.AuditMaxDays);
    }

    /// <summary>True when requests from this computer need no login: no user exists yet, or the setting that keeps the exemption is on.</summary>
    public bool LocalExempt => !Key.LocalRequiresKey && (!Users.Any || Settings.Get().LocalExemption);
    /// <summary>True when something must be shown to log in: the key is on, or users exist.</summary>
    public bool LoginRequired => Key.Enabled || Users.Any;

    public static string IpOf(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is { IsIPv4MappedToIPv6: true }) ip = ip.MapToIPv4();
        return ip?.ToString() ?? "?";
    }

    public Principal Resolve(HttpContext ctx)
    {
        if (ctx.Items[ItemKey] is Principal cached) return cached;
        var p = ResolveCore(ctx);
        ctx.Items[ItemKey] = p;
        return p;
    }

    private Principal ResolveCore(HttpContext ctx)
    {
        var ip = IpOf(ctx);
        var h = ctx.Request.Headers;
        var presented = new List<string>(2);
        var auth = h.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) presented.Add(auth[7..].Trim());
        var hdr = h[ApiAuth.HeaderName].ToString();
        if (hdr.Length > 0) presented.Add(hdr);
        foreach (var v in presented)
        {
            if (Key.Enabled && Key.Matches(v)) return Principal.AccessKey(ip);
            if (TokenStore.LooksLikeToken(v))
            {
                var r = Tokens.Validate(v, ip);
                if (r.Check == TokenCheck.Ok)
                {
                    var t = r.Token!;
                    if (r.FirstUseToday) Audit.Add("token.use", $"Token “{t.Name}” ({Permissions.ScopeName(t.Scope)}) was used (first use today)", "token " + t.Name, ip);
                    return Principal.ForScope(t.Scope, t.Name, ip, t.Slots, t.Id);
                }
                if (r.Check is TokenCheck.Revoked or TokenCheck.Expired)
                    Denied($"token:{r.Token!.Id}:{ip}", "token.denied", $"A {(r.Check == TokenCheck.Revoked ? "revoked" : "expired")} token was used: “{r.Token.Name}”", "token " + r.Token.Name, ip);
            }
        }
        var sess = Sessions.Validate(ctx.Request.Cookies[SessionCookie]);
        if (sess != null && Users.Get(sess.UserId) is { Disabled: false } u)
            return Principal.ForRole(u.Role, u.Name, ip, u.Slots, u.Id);
        if (Key.Enabled && Key.Matches(ctx.Request.Cookies[ApiAuth.CookieName])) return Principal.AccessKey(ip);
        if (LocalExempt && ApiAuth.IsDirectLocal(ctx)) return Principal.LocalMachine(ip);
        // Auth switched off with OpenVP:ApiKey=off and no users: open, as before. Once users exist "off" no longer opens anything.
        if (!Key.Enabled && !Users.Any) return Principal.LocalMachine(ip);
        return Principal.Anonymous(ip);
    }

    /// <summary>Writes to the audit log, once per minute for the same key (a page polling something it can't use mustn't flood the log).</summary>
    public void Denied(string dedupeKey, string action, string text, string? user, string ip, int? slot = null, string? detail = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (_denials.TryGetValue(dedupeKey, out var t) && now - t < TimeSpan.FromMinutes(1)) return;
        _denials[dedupeKey] = now;
        if (_denials.Count > 5000) foreach (var k in _denials.Where(kv => now - kv.Value > TimeSpan.FromMinutes(1)).Select(kv => kv.Key).ToList()) _denials.TryRemove(k, out _);
        Audit.Add(action, text, user, ip, slot, detail);
    }

    public static bool IsProtectedPath(PathString p, bool metricsPublic, bool panelsRequireKey) =>
        (!metricsPublic && p.StartsWithSegments("/metrics"))
        || (p.StartsWithSegments("/api") && !p.StartsWithSegments("/api/info") && !p.StartsWithSegments("/api/login") && !p.StartsWithSegments("/api/logout"))
        || p.StartsWithSegments("/ws/trace") || p.StartsWithSegments("/ws/metrics") || p.StartsWithSegments("/ws/eisc")
        || (panelsRequireKey && p.StartsWithSegments("/ws/panel"));

    private static async Task Refuse(HttpContext ctx, int status, string error, object? extra = null)
    {
        ctx.Response.StatusCode = status;
        if (status == StatusCodes.Status401Unauthorized) ctx.Response.Headers.WWWAuthenticate = "Bearer realm=\"OpenVP\"";
        if (ctx.Request.Path.StartsWithSegments("/ws")) return;
        await ctx.Response.WriteAsJsonAsync(extra == null ? new { error } : (object)new { error, detail = extra });
    }

    /// <summary>The middleware: cross-origin check, <c>?key=</c> login on pages, who is calling, then the endpoint's permission.</summary>
    public async Task Handle(HttpContext ctx, RequestDelegate next)
    {
        var path = ctx.Request.Path;
        var auth = Key;

        // A page on another site must never drive the API or open the debugger stream, whatever its cookies or the local exception.
        if ((IsProtectedPath(path, auth.MetricsPublic, auth.PanelsRequireKey) || path.StartsWithSegments("/api/login")) && ApiAuth.IsCrossOrigin(ctx))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Opening /?key=KEY (or any page with ?key=) logs the browser in with the access key and strips it from the address bar.
        if (auth.Enabled && HttpMethods.IsGet(ctx.Request.Method) && !path.StartsWithSegments("/api") && !path.StartsWithSegments("/ws") && !path.StartsWithSegments("/s") && !path.StartsWithSegments("/metrics") && !path.StartsWithSegments("/hooks")
            && ctx.Request.Query.TryGetValue("key", out var qk))
        {
            var rest = string.Join('&', ctx.Request.Query.Where(q => q.Key != "key")
                .SelectMany(q => q.Value.Select(v => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(v ?? "")}")));
            var ok = auth.Matches(qk.ToString());
            if (ok) { auth.SetCookie(ctx, qk.ToString()); Audit.Add("login", "Signed in with the access key (?key=)", "access key", IpOf(ctx)); }
            else Denied("badkey:" + IpOf(ctx), "login.failed", "Wrong access key in the address (?key=)", null, IpOf(ctx));
            ctx.Response.Redirect(path + (rest.Length > 0 ? "?" + rest : "") + (ok ? "" : (rest.Length > 0 ? "&" : "?") + "badkey=1"));
            return;
        }

        var p = Resolve(ctx);
        Principal.Current = p;

        var ep = ctx.GetEndpoint() as RouteEndpoint;
        if (ep == null)
        {
            // Static pages, scripts and 404s. Anything under a protected path that isn't an endpoint still wants a login first (so a stranger can't map the API).
            if (!p.Authenticated && IsProtectedPath(path, auth.MetricsPublic, auth.PanelsRequireKey))
            {
                await Refuse(ctx, StatusCodes.Status401Unauthorized, "OpenVP access key required. Log in on the web console, or send it as 'Authorization: Bearer KEY'.");
                return;
            }
            await next(ctx);
            return;
        }

        var policy = EndpointPolicy.For(ctx.Request.Method, ep) ?? EndpointPolicy.For("*", ep) ?? Policy.Needs(Perm.Admin); // no line: Admin only
        if (policy.Kind == "public" || (policy.Kind == "panel" && !auth.PanelsRequireKey) || (policy.Kind == "metrics" && auth.MetricsPublic))
        {
            await next(ctx);
            return;
        }
        var need = policy.Kind == "panel" ? Perm.Signal : policy.Kind == "metrics" ? Perm.View : policy.Perm;

        if (!p.Authenticated)
        {
            await Refuse(ctx, StatusCodes.Status401Unauthorized, Users.Any
                ? "Sign in first (or send the access key or an API token as 'Authorization: Bearer ...')."
                : "OpenVP access key required. Log in on the web console, or send it as 'Authorization: Bearer KEY'.");
            return;
        }

        var desc = $"{ctx.Request.Method} {path}";
        if (!p.Has(need))
        {
            Denied($"deny:{p.LockId}:{desc}", "permission.denied", $"{p.Display} tried {desc} (needs {need})", p.ActivityUser ?? p.Name, p.Ip, null, $"{(p.Role != null ? Permissions.RoleName(p.Role.Value) : p.Scope != null ? Permissions.ScopeName(p.Scope.Value) : "")}");
            await Refuse(ctx, StatusCodes.Status403Forbidden, $"Your account isn't allowed to do that ({NeedText(need)}).", new { needs = need.ToString() });
            return;
        }
        if (policy.AllSlots && !p.AllSlots)
        {
            Denied($"deny:{p.LockId}:{desc}", "permission.denied", $"{p.Display} (limited to some slots) tried {desc}", p.ActivityUser ?? p.Name, p.Ip);
            await Refuse(ctx, StatusCodes.Status403Forbidden, "Your account is limited to some slots, so it can't do that.");
            return;
        }
        if (ctx.GetRouteValue("n") is { } nv && int.TryParse(Convert.ToString(nv), out var slot))
        {
            if (!p.CanSlot(slot))
            {
                Denied($"deny:{p.LockId}:{slot}:{desc}", "permission.denied", $"{p.Display} tried {desc} on a slot it isn't allowed to use", p.ActivityUser ?? p.Name, p.Ip, slot);
                await Refuse(ctx, StatusCodes.Status403Forbidden, $"Your account isn't allowed to use slot {slot}.");
                return;
            }
            if (policy.Locks && Locks.Blocking(slot, p) is { } l)
            {
                Denied($"lock:{p.LockId}:{desc}", "lock.denied", $"{p.Display} tried {desc} but slot {slot} is locked by {l.By}", p.ActivityUser ?? p.Name, p.Ip, slot);
                await RefuseLocked(ctx, l);
                return;
            }
        }
        await next(ctx);
    }

    public static async Task RefuseLocked(HttpContext ctx, SlotLockRecord l)
    {
        ctx.Response.StatusCode = StatusCodes.Status423Locked;
        await ctx.Response.WriteAsJsonAsync(new { error = SlotLocks.Message(l), locked = LockDto(l) });
    }

    public static object LockDto(SlotLockRecord l) => new { slot = l.Slot, by = l.By, reason = l.Reason, since = l.Since, expires = l.Expires };

    private static string NeedText(Perm p) => p switch
    {
        Perm.View => "it can't even read",
        Perm.Signal => "it can't set signals",
        Perm.Operate => "it can't start, stop or restart programs",
        Perm.Admin => "only an Admin can",
        _ => "it needs the " + p + " permission"
    };

    /// <summary>The identity details the pages need: who am I, what may I do.</summary>
    public object Me(HttpContext ctx)
    {
        var p = Resolve(ctx);
        return new
        {
            name = p.Name, kind = p.Kind.ToString().ToLowerInvariant(), authenticated = p.Authenticated,
            role = p.Role == null ? null : Permissions.RoleName(p.Role.Value), scope = p.Scope == null ? null : Permissions.ScopeName(p.Scope.Value),
            perms = p.Perms.Select(x => x.ToString()).Order().ToArray(), slots = p.Slots, canLogout = p.Kind == PrincipalKind.User, canChangePassword = p.Kind == PrincipalKind.User && Users.Get(p.UserId ?? "") is { Source: "local" }
        };
    }

    /// <summary>What identifies this person in the presence list: the user name, else the IP address (so two tabs of one person count once).</summary>
    public static string PresenceKey(Principal p) => p.Kind is PrincipalKind.User or PrincipalKind.Token ? p.LockId : "ip:" + p.Ip;
    public static string PresenceName(Principal p) => p.Kind is PrincipalKind.User or PrincipalKind.Token ? p.Name : p.Ip;

    public static bool SameAddress(string a, string b) => IPAddress.TryParse(a, out var x) && IPAddress.TryParse(b, out var y) ? x.Equals(y) : a == b;
}
