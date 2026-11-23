using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Core.Security;

namespace OpenVP.Server;

/// <summary>Sign-in, users, API tokens, security settings, HTTPS, single sign-on, the audit log, slot locks and presence.</summary>
public static class SecurityEndpoints
{
    private static string? Str(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool? Bool(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static int? Int(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    private static bool Has(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out _);
    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    /// <summary>Slots from a JSON array; null for "all" (no array, null, or an empty array).</summary>
    private static int[]? SlotsOf(JsonElement b, string k, Processor p)
    {
        if (!b.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _)).Select(e => e.GetInt32()).Distinct().Order().ToArray();
        if (list.Length == 0) return null;
        if (list.Any(n => !p.IsValidSlot(n))) throw new ArgumentException($"Slots must be numbers 1–{p.Slots.Count}.");
        return list;
    }

    public static void SetSessionCookie(HttpContext ctx, string token) =>
        ctx.Response.Cookies.Append(Authz.SessionCookie, token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps, IsEssential = true, Path = "/" });

    private static string SafeNext(string? next) =>
        !string.IsNullOrEmpty(next) && next[0] == '/' && !next.StartsWith("//") && !next.Contains('\\') && !next.Contains('\n') && !next.Contains('\r') ? next : "/";

    private static object UserDto(UserRecord u, DateTimeOffset now) => new
    {
        id = u.Id, name = u.Name, display = u.Display, role = Permissions.RoleName(u.Role), slots = u.Slots, source = u.Source, disabled = u.Disabled, created = u.Created,
        lastLogin = u.LastLogin, passwordChanged = u.PasswordChanged, locked = u.LockedUntil is { } l && l > now, lockedUntil = u.LockedUntil is { } l2 && l2 > now ? l2 : (DateTimeOffset?)null
    };

    private static object TokenDto(TokenRecord t, DateTimeOffset now) => new
    {
        id = t.Id, name = t.Name, scope = Permissions.ScopeName(t.Scope), slots = t.Slots, prefix = t.Prefix, created = t.Created, createdBy = t.CreatedBy, expires = t.Expires,
        lastUsed = t.LastUsed, lastIp = t.LastIp, uses = t.Uses, revoked = t.Revoked, revokedAt = t.RevokedAt,
        status = t.Revoked ? "revoked" : t.Expires != null && t.Expires <= now ? "expired" : "active"
    };

    public static void Map(WebApplication app, Authz z, Processor processor, PresenceTracker presence, HttpsManager https, AcmeService acme, OidcSupport oidc, SecretStore secrets, ActivityLog activity, Func<int?> cipPort, Func<IEnumerable<int>> usedPorts)
    {
        // ---------------------------------------------------------------- who am I, sign in, sign out
        app.MapGet("/api/info", (HttpContext ctx) => new
        {
            name = "OpenVP", version = "0.1.0", slots = processor.Slots.Count, cipPort = cipPort(),
            auth = z.LoginRequired, authenticated = z.Resolve(ctx).Authenticated, users = z.Users.Any, me = z.Me(ctx),
            oidc = oidc.Public(), localExempt = z.LocalExempt,
            https = new { enabled = https.Listening, port = https.BoundPort, listening = https.Listening, secure = ctx.Request.IsHttps }
        });

        app.MapPost("/api/login", (HttpContext ctx, JsonElement body) =>
        {
            var ip = Authz.IpOf(ctx);
            if (z.LoginThrottle.LockedFor(ip) is { } left)
                return Results.Json(new { error = $"Too many wrong tries from this address. Try again in {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} minute(s)." }, statusCode: StatusCodes.Status429TooManyRequests);
            var key = Str(body, "key");
            var user = Str(body, "user") ?? Str(body, "username");
            var password = Str(body, "password");
            if (user == null && key != null)
            {
                if (!z.Key.Enabled) return Results.Ok(new { ok = true });
                if (!z.Key.Matches(key))
                {
                    z.LoginThrottle.Failed(ip);
                    z.Audit.Add("login.failed", "Wrong access key", null, ip);
                    return Error(StatusCodes.Status401Unauthorized, "Wrong access key.");
                }
                z.LoginThrottle.Succeeded(ip);
                z.Key.SetCookie(ctx, key);
                z.Audit.Add("login", "Signed in with the access key", "access key", ip);
                return Results.Ok(new { ok = true, kind = "key" });
            }
            if (user == null || password == null) return Error(400, "Send {\"user\":\"...\",\"password\":\"...\"} or {\"key\":\"...\"}.");
            var r = z.Users.Authenticate(user, password);
            switch (r.Outcome)
            {
                case LoginOutcome.Ok:
                {
                    z.LoginThrottle.Succeeded(ip);
                    var u = r.User!;
                    SetSessionCookie(ctx, z.Sessions.Create(u.Id, u.Name, ip));
                    z.Audit.Add("login", $"{u.Name} signed in ({Permissions.RoleName(u.Role)})", u.Name, ip);
                    return Results.Ok(new { ok = true, kind = "user", user = new { name = u.Name, role = Permissions.RoleName(u.Role) } });
                }
                case LoginOutcome.Locked:
                    z.Audit.Add("login.locked", $"The account {user.Trim()} is locked for {Math.Max(1, (int)Math.Ceiling((r.LockedFor ?? TimeSpan.Zero).TotalMinutes))} minute(s) after wrong passwords", user.Trim(), ip);
                    return Results.Json(new { error = $"This account is locked for {Math.Max(1, (int)Math.Ceiling((r.LockedFor ?? TimeSpan.Zero).TotalMinutes))} minute(s) after too many wrong passwords." }, statusCode: StatusCodes.Status429TooManyRequests);
                case LoginOutcome.Disabled:
                    z.Audit.Add("login.failed", $"Sign-in refused: {user.Trim()} is disabled", user.Trim(), ip);
                    return Error(StatusCodes.Status401Unauthorized, "This account is disabled. Ask an Admin.");
                default:
                    z.LoginThrottle.Failed(ip);
                    z.Audit.Add("login.failed", $"Wrong user name or password for {user.Trim()}", user.Trim(), ip);
                    return Error(StatusCodes.Status401Unauthorized, "Wrong user name or password.");
            }
        });

        app.MapPost("/api/logout", (HttpContext ctx) =>
        {
            var p = z.Resolve(ctx);
            var token = ctx.Request.Cookies[Authz.SessionCookie];
            if (token != null) z.Sessions.Destroy(token);
            z.Key.ClearCookie(ctx);
            ctx.Response.Cookies.Delete(Authz.SessionCookie, new CookieOptions { Path = "/" });
            if (p.Authenticated && p.Kind is PrincipalKind.User or PrincipalKind.Key) z.Audit.Add("logout", $"{p.Display} signed out", p.ActivityUser, p.Ip);
            return Results.Ok();
        });

        app.MapPost("/api/me/password", (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            if (p.Kind != PrincipalKind.User || p.UserId == null) return Error(400, "Only a signed-in user has a password to change.");
            if (!z.Users.VerifyPassword(p.UserId, Str(body, "current")))
            {
                z.Audit.Add("password.failed", $"{p.Name} gave the wrong current password when changing their password", p.Name, p.Ip);
                return Error(403, "The current password is wrong.");
            }
            try { z.Users.SetPassword(p.UserId, Str(body, "new") ?? ""); }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (InvalidOperationException e) { return Error(400, e.Message); }
            z.Sessions.DestroyFor(p.UserId, ctx.Request.Cookies[Authz.SessionCookie]); // every other browser is signed out
            z.Audit.Add("password.change", $"{p.Name} changed their own password", p.Name, p.Ip);
            return Results.Ok(new { ok = true });
        });

        // ---------------------------------------------------------------- single sign-on (OpenID Connect)
        app.MapGet("/api/login/oidc", (HttpContext ctx) =>
        {
            if (!oidc.Ready) return Error(404, "Single sign-on isn't set up.");
            return Results.Challenge(new AuthenticationProperties { RedirectUri = "/api/login/oidc/done?next=" + Uri.EscapeDataString(SafeNext(ctx.Request.Query["next"])) }, [OidcSupport.Scheme]);
        });
        app.MapGet("/api/login/oidc/done", async (HttpContext ctx) =>
        {
            var ip = Authz.IpOf(ctx);
            string Fail(string why) => "/login.html?error=" + Uri.EscapeDataString(why);
            var res = await ctx.AuthenticateAsync(OidcSupport.TempScheme);
            await ctx.SignOutAsync(OidcSupport.TempScheme);
            if (!res.Succeeded || res.Principal == null || !oidc.Ready) return Results.Redirect(Fail("Single sign-on didn't finish. Try again."));
            var map = OidcMapper.Map(OidcSupport.ClaimsOf(res.Principal), oidc.Current);
            if (!map.Ok)
            {
                z.Audit.Add("login.failed", $"Single sign-on refused{(map.Name != null ? " for " + map.Name : "")}: {map.Error}", map.Name, ip);
                return Results.Redirect(Fail(map.Error ?? "Single sign-on was refused."));
            }
            UserRecord u;
            try { u = z.Users.UpsertSso(map.Name!, map.Role, map.Display); }
            catch (InvalidOperationException e) { z.Audit.Add("login.failed", $"Single sign-on refused for {map.Name}: {e.Message}", map.Name, ip); return Results.Redirect(Fail(e.Message)); }
            SetSessionCookie(ctx, z.Sessions.Create(u.Id, u.Name, ip));
            z.Audit.Add("login", $"{u.Name} signed in through single sign-on ({Permissions.RoleName(u.Role)})", u.Name, ip);
            return Results.Redirect(SafeNext(ctx.Request.Query["next"]));
        });

        app.MapGet("/api/oidc", () => oidc.Status());
        app.MapPut("/api/oidc", (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            try
            {
                var wantOn = Bool(body, "enabled");
                if (wantOn == true && !z.Users.List().Any(u => u.Role == Role.Admin && u.Source == "local" && !u.Disabled))
                    return Error(400, "Make a local Admin user first. Single sign-on is an extra way in, and you need one that doesn't depend on the provider.");
                var mappings = new List<RoleMapping>();
                if (body.TryGetProperty("mappings", out var ms) && ms.ValueKind == JsonValueKind.Array)
                    foreach (var m in ms.EnumerateArray())
                    {
                        var v = Str(m, "value")?.Trim();
                        if (string.IsNullOrEmpty(v)) continue;
                        if (!Permissions.TryParseRole(Str(m, "role"), out var role)) return Error(400, $"“{Str(m, "role")}” isn't a role (admin, programmer, technician or viewer).");
                        mappings.Add(new RoleMapping { Value = v, Role = role });
                    }
                Role? dflt = null;
                var setDefault = Has(body, "defaultRole");
                if (setDefault && Str(body, "defaultRole") is { Length: > 0 } dr) { if (!Permissions.TryParseRole(dr, out var d)) return Error(400, "defaultRole isn't a role."); dflt = d; }
                var authority = Str(body, "authority")?.Trim();
                if (authority is { Length: > 0 } && !Uri.TryCreate(authority, UriKind.Absolute, out var au)) return Error(400, "The authority must be a full address like https://login.example.com/realms/main.");
                if (authority is { Length: > 0 } && Uri.TryCreate(authority, UriKind.Absolute, out var au2) && au2.Scheme != "https" && !oidc.AllowHttp) return Error(400, "The authority must use https.");
                z.Settings.Update(s =>
                {
                    var o = s.Oidc;
                    if (wantOn != null) o.Enabled = wantOn.Value;
                    if (authority != null) o.Authority = authority.Length == 0 ? null : authority;
                    if (Str(body, "clientId") is { } cid) o.ClientId = cid.Trim().Length == 0 ? null : cid.Trim();
                    if (Str(body, "scopes") is { Length: > 0 } sc) o.Scopes = sc.Trim();
                    if (Str(body, "roleClaim") is { Length: > 0 } rc) o.RoleClaim = rc.Trim();
                    if (Str(body, "userClaim") is { Length: > 0 } uc) o.UserClaim = uc.Trim();
                    if (Str(body, "buttonText") is { Length: > 0 } bt) o.ButtonText = bt.Trim();
                    if (body.TryGetProperty("mappings", out var m2) && m2.ValueKind == JsonValueKind.Array) o.Mappings = mappings;
                    if (setDefault) o.DefaultRole = dflt;
                    if (body.TryGetProperty("allowedDomains", out var ad) && ad.ValueKind == JsonValueKind.Array)
                        o.AllowedDomains = ad.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim().TrimStart('@')).Where(e => e.Length > 0).Distinct().ToList();
                });
                if (Has(body, "clientSecret")) secrets.Set(OidcSupport.SecretName, Str(body, "clientSecret") is { Length: > 0 } cs ? cs : null);
                oidc.Reset();
                z.Audit.Add("settings.oidc", "Changed the single sign-on settings" + (wantOn != null ? (wantOn == true ? " (turned on)" : " (turned off)") : ""), p.ActivityUser ?? p.Name, p.Ip);
                return Results.Ok(oidc.Status());
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        // ---------------------------------------------------------------- users
        app.MapGet("/api/users", () => z.Users.List().Select(u => UserDto(u, DateTimeOffset.UtcNow)));
        app.MapPost("/api/users", (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            try
            {
                if (!Permissions.TryParseRole(Str(body, "role"), out var role)) return Error(400, "Choose a role: admin, programmer, technician or viewer.");
                var u = z.Users.Create(Str(body, "name") ?? "", Str(body, "password"), role, SlotsOf(body, "slots", processor), "local", Str(body, "display"));
                z.Audit.Add("user.create", $"Created the user {u.Name} ({Permissions.RoleName(u.Role)}{(u.Slots != null ? ", slots " + string.Join(",", u.Slots) : "")})", p.ActivityUser ?? p.Name, p.Ip);
                return Results.Ok(UserDto(u, DateTimeOffset.UtcNow));
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });
        app.MapPut("/api/users/{id}", (HttpContext ctx, string id, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            try
            {
                Role? role = null;
                if (Has(body, "role")) { if (!Permissions.TryParseRole(Str(body, "role"), out var r)) return Error(400, "Choose a role: admin, programmer, technician or viewer."); role = r; }
                var before = z.Users.Get(id);
                if (before == null) return Error(404, "There's no such user.");
                if (id == p.UserId && (Bool(body, "disabled") == true)) return Error(400, "You can't disable your own account.");
                var u = z.Users.Update(id, role, Has(body, "slots"), SlotsOf(body, "slots", processor), Bool(body, "disabled"), Str(body, "display"));
                if (u.Disabled) z.Sessions.DestroyFor(id);
                var what = new List<string>();
                if (before.Role != u.Role) what.Add($"role {Permissions.RoleName(before.Role)} to {Permissions.RoleName(u.Role)}");
                if (!(before.Slots ?? []).SequenceEqual(u.Slots ?? [])) what.Add("slots " + (u.Slots == null ? "all" : string.Join(",", u.Slots)));
                if (before.Disabled != u.Disabled) what.Add(u.Disabled ? "disabled" : "enabled");
                z.Audit.Add("user.update", $"Changed the user {u.Name}: {(what.Count == 0 ? "no change" : string.Join("; ", what))}", p.ActivityUser ?? p.Name, p.Ip);
                return Results.Ok(UserDto(u, DateTimeOffset.UtcNow));
            }
            catch (KeyNotFoundException e) { return Error(404, e.Message); }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
        });
        app.MapDelete("/api/users/{id}", (HttpContext ctx, string id) =>
        {
            var p = z.Resolve(ctx);
            if (id == p.UserId) return Error(400, "You can't delete your own account.");
            try
            {
                var u = z.Users.Get(id);
                z.Users.Delete(id);
                z.Sessions.DestroyFor(id);
                z.Audit.Add("user.delete", $"Deleted the user {u?.Name}", p.ActivityUser ?? p.Name, p.Ip);
                return Results.Ok(new { deleted = id });
            }
            catch (KeyNotFoundException e) { return Error(404, e.Message); }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
        });
        app.MapPost("/api/users/{id}/password", (HttpContext ctx, string id, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            try
            {
                var u = z.Users.Get(id) ?? throw new KeyNotFoundException("There's no such user.");
                z.Users.SetPassword(id, Str(body, "password") ?? "");
                z.Sessions.DestroyFor(id);
                z.Audit.Add("password.reset", $"Set a new password for {u.Name}", p.ActivityUser ?? p.Name, p.Ip);
                return Results.Ok(new { ok = true });
            }
            catch (KeyNotFoundException e) { return Error(404, e.Message); }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (InvalidOperationException e) { return Error(400, e.Message); }
        });
        app.MapPost("/api/users/{id}/unlock", (HttpContext ctx, string id) =>
        {
            var p = z.Resolve(ctx);
            var u = z.Users.Get(id);
            if (u == null) return Error(404, "There's no such user.");
            z.Users.Unlock(id);
            z.Audit.Add("user.unlock", $"Unlocked the account {u.Name}", p.ActivityUser ?? p.Name, p.Ip);
            return Results.Ok(new { ok = true });
        });
        app.MapPost("/api/users/{id}/signout", (HttpContext ctx, string id) =>
        {
            var p = z.Resolve(ctx);
            var u = z.Users.Get(id);
            if (u == null) return Error(404, "There's no such user.");
            var n = z.Sessions.DestroyFor(id);
            z.Audit.Add("user.signout", $"Signed {u.Name} out everywhere ({n} session{(n == 1 ? "" : "s")})", p.ActivityUser ?? p.Name, p.Ip);
            return Results.Ok(new { ended = n });
        });

        // ---------------------------------------------------------------- API tokens
        app.MapGet("/api/tokens", () => z.Tokens.List().Select(t => TokenDto(t, DateTimeOffset.UtcNow)));
        app.MapPost("/api/tokens", (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            try
            {
                if (!Permissions.TryParseScope(Str(body, "scope"), out var scope)) return Error(400, "Choose a scope: read-only, operator, programmer or admin.");
                DateTimeOffset? expires = null;
                if (Str(body, "expires") is { Length: > 0 } es) { if (!DateTimeOffset.TryParse(es, out var d)) return Error(400, "The expiry isn't a date and time."); expires = d; }
                else if (Int(body, "expiresInDays") is { } days && days > 0) expires = DateTimeOffset.UtcNow.AddDays(days);
                var (t, secret) = z.Tokens.Create(Str(body, "name") ?? "", scope, SlotsOf(body, "slots", processor), expires, p.ActivityUser ?? p.Name);
                z.Audit.Add("token.create", $"Created the token “{t.Name}” ({Permissions.ScopeName(t.Scope)}{(t.Slots != null ? ", slots " + string.Join(",", t.Slots) : "")}{(t.Expires != null ? ", expires " + t.Expires.Value.ToString("yyyy-MM-dd") : "")})", p.ActivityUser ?? p.Name, p.Ip);
                var dto = JsonSerializer.SerializeToNode(TokenDto(t, DateTimeOffset.UtcNow), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
                dto["token"] = secret; // shown once; only its hash is kept
                return Results.Json(dto);
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });
        app.MapDelete("/api/tokens/{id}", (HttpContext ctx, string id) =>
        {
            var p = z.Resolve(ctx);
            var t = z.Tokens.Get(id);
            if (t == null) return Error(404, "There's no such token.");
            if (t.Revoked || (t.Expires != null && t.Expires <= DateTimeOffset.UtcNow))
            {
                z.Tokens.Remove(id);
                return Results.Ok(new { removed = id });
            }
            z.Tokens.Revoke(id);
            z.Audit.Add("token.revoke", $"Revoked the token “{t.Name}”", p.ActivityUser ?? p.Name, p.Ip);
            return Results.Ok(new { revoked = id });
        });

        // ---------------------------------------------------------------- security settings
        app.MapGet("/api/security", () => SecurityStatus(z, https));
        app.MapPut("/api/security", (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            z.Settings.Update(s =>
            {
                if (Bool(body, "localExemption") is { } le) s.LocalExemption = le;
                if (Int(body, "sessionIdleMinutes") is { } a) s.SessionIdleMinutes = a;
                if (Int(body, "sessionAbsoluteHours") is { } b) s.SessionAbsoluteHours = b;
                if (Int(body, "lockoutAttempts") is { } c) s.LockoutAttempts = c;
                if (Int(body, "lockoutMinutes") is { } d) s.LockoutMinutes = d;
                if (Int(body, "auditMaxMegabytes") is { } e) s.AuditMaxMegabytes = e;
                if (Int(body, "auditMaxDays") is { } f) s.AuditMaxDays = f;
            });
            z.ApplySettings();
            z.Audit.Add("settings.security", "Changed the security settings" + (Bool(body, "localExemption") is { } le2 ? $" (localhost without a login: {(le2 ? "on" : "off")})" : ""), p.ActivityUser ?? p.Name, p.Ip);
            return Results.Ok(SecurityStatus(z, https));
        });

        // ---------------------------------------------------------------- HTTPS
        app.MapGet("/api/https", () => https.Status());
        app.MapPut("/api/https", (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            var port = Int(body, "port");
            if (port is < 1 or > 65535) return Error(400, "The port must be between 1 and 65535.");
            var current = z.Settings.Get().Https;
            var wantPort = port ?? current.Port;
            if (usedPorts().Contains(wantPort) && !(https.Listening && https.BoundPort == wantPort)) return Error(400, $"Port {wantPort} is already used by OpenVP's own web console or tunnel listener. Choose another.");
            if (Bool(body, "enabled") == true && !https.HasCertificate) return Error(400, "Make a self-signed certificate or upload one first.");
            z.Settings.Update(s =>
            {
                if (Bool(body, "enabled") is { } en) s.Https.Enabled = en;
                s.Https.Port = wantPort;
                if (Bool(body, "redirect") is { } rd) s.Https.Redirect = rd;
            });
            z.Audit.Add("https.settings", $"Changed the HTTPS settings (enabled {z.Settings.Get().Https.Enabled.ToString().ToLowerInvariant()}, port {wantPort}, redirect {z.Settings.Get().Https.Redirect.ToString().ToLowerInvariant()})", p.ActivityUser ?? p.Name, p.Ip);
            return Results.Ok(https.Status());
        });
        app.MapPost("/api/https/self-signed", (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            var cert = https.CreateSelfSigned(Str(body, "host"));
            z.Audit.Add("https.certificate", $"Made a self-signed certificate (SHA-256 {CertificateTools.Fingerprint(cert)})", p.ActivityUser ?? p.Name, p.Ip);
            return Results.Ok(https.Status());
        });
        app.MapPost("/api/https/upload", async (HttpContext ctx) =>
        {
            var p = z.Resolve(ctx);
            try
            {
                string? password; System.Security.Cryptography.X509Certificates.X509Certificate2 cert;
                if (ctx.Request.HasFormContentType)
                {
                    var form = await ctx.Request.ReadFormAsync();
                    password = form["password"].ToString();
                    var f = form.Files["file"] ?? form.Files.FirstOrDefault();
                    if (f == null) return Error(400, "Choose a certificate file (.pfx or .p12), or a PEM certificate and its key.");
                    if (f.Length > 1_000_000) return Error(400, "That file is too big to be a certificate.");
                    await using var ms = new MemoryStream(); await f.CopyToAsync(ms);
                    var name = f.FileName.ToLowerInvariant();
                    if (name.EndsWith(".pfx") || name.EndsWith(".p12")) cert = CertificateTools.FromPfx(ms.ToArray(), password);
                    else
                    {
                        var certPem = Encoding.UTF8.GetString(ms.ToArray());
                        var kf = form.Files["key"];
                        string? keyPem = null;
                        if (kf != null) { await using var ks = new MemoryStream(); await kf.CopyToAsync(ks); keyPem = Encoding.UTF8.GetString(ks.ToArray()); }
                        keyPem ??= form["keyPem"].ToString();
                        if (string.IsNullOrWhiteSpace(keyPem) && certPem.Contains("PRIVATE KEY")) keyPem = certPem; // one file with both
                        if (string.IsNullOrWhiteSpace(keyPem)) return Error(400, "A PEM certificate needs its private key too.");
                        cert = CertificateTools.FromPem(certPem, keyPem, password);
                    }
                }
                else
                {
                    var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                    password = Str(body, "password");
                    if (Str(body, "certPem") is { Length: > 0 } cp && Str(body, "keyPem") is { Length: > 0 } kp) cert = CertificateTools.FromPem(cp, kp, password);
                    else if (Str(body, "pfx") is { Length: > 0 } b64) cert = CertificateTools.FromPfx(Convert.FromBase64String(b64), password);
                    else return Error(400, "Send certPem and keyPem, or a pfx (base64) with its password.");
                }
                https.Install(cert, "upload");
                z.Audit.Add("https.certificate", $"Uploaded a certificate for {cert.Subject} (SHA-256 {CertificateTools.Fingerprint(cert)})", p.ActivityUser ?? p.Name, p.Ip);
                return Results.Ok(https.Status());
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (FormatException) { return Error(400, "The pfx isn't valid base64."); }
        });
        app.MapGet("/api/https/certificate", () =>
        {
            var der = https.PublicCertificate();
            return der == null ? Error(404, "There's no certificate yet.") : Results.File(der, "application/x-x509-ca-cert", "openvp.cer");
        });

        app.MapPost("/api/https/acme", async (HttpContext ctx, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            var domain = Str(body, "domain")?.Trim() ?? "";
            var email = Str(body, "email")?.Trim() ?? "";
            if (AcmeService.DomainProblem(domain) is { } dp) return Error(400, dp);
            if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return Error(400, "Enter an e-mail address for Let's Encrypt's expiry notices.");
            var staging = Bool(body, "staging") ?? false;
            z.Settings.Update(s => { s.Https.Acme.Domain = domain; s.Https.Acme.Email = email; s.Https.Acme.Staging = staging; s.Https.Acme.Enabled = Bool(body, "enabled") ?? false; });
            string msg; var ok = true;
            try { msg = await acme.IssueAsync(domain, email, staging, ctx.RequestAborted); }
            catch (Exception e) { msg = e.Message; ok = false; }
            z.Settings.Update(s => { s.Https.Acme.LastAttempt = DateTimeOffset.UtcNow; s.Https.Acme.LastResult = msg; });
            z.Audit.Add(ok ? "https.acme" : "https.acme.failed", (ok ? "Got a Let's Encrypt certificate: " : "Let's Encrypt certificate request failed: ") + msg, p.ActivityUser ?? p.Name, p.Ip);
            return ok ? Results.Ok(https.Status()) : Error(502, msg);
        });
        // The answer Let's Encrypt asks for on port 80 while a certificate is being requested.
        app.MapGet("/.well-known/acme-challenge/{token}", (string token) => acme.Answer(token) is { } a ? Results.Text(a, "text/plain") : Results.NotFound());

        // ---------------------------------------------------------------- audit log
        AuditFilter FilterOf(HttpRequest r, int dflt) => new()
        {
            User = r.Query["user"], Action = r.Query["action"], Text = r.Query["q"],
            Slot = int.TryParse(r.Query["slot"], out var s) ? s : null,
            From = DateTimeOffset.TryParse(r.Query["from"], out var f) ? f : null,
            To = DateTimeOffset.TryParse(r.Query["to"], out var t) ? t.AddDays(t.TimeOfDay == TimeSpan.Zero ? 1 : 0) : null,
            Limit = int.TryParse(r.Query["limit"], out var l) ? l : dflt,
        };
        app.MapGet("/api/audit", (HttpContext ctx) =>
        {
            var entries = z.Audit.Query(FilterOf(ctx.Request, 200));
            return Results.Ok(new { entries, head = z.Audit.Head, lastSeq = z.Audit.LastSeq });
        });
        app.MapPost("/api/audit/verify", (HttpContext ctx) =>
        {
            var p = z.Resolve(ctx);
            var v = z.Audit.Verify();
            z.Audit.Add("audit.verify", v.Ok ? "Verified the audit log: intact" : "Verified the audit log: BROKEN. " + v.Message, p.ActivityUser ?? p.Name, p.Ip);
            return Results.Ok(v);
        });
        app.MapGet("/api/audit/download", (HttpContext ctx) =>
        {
            var p = z.Resolve(ctx);
            var csv = AuditLog.Csv(z.Audit.Query(FilterOf(ctx.Request, 100_000)));
            z.Audit.Add("audit.download", "Downloaded the audit log as CSV", p.ActivityUser ?? p.Name, p.Ip);
            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"openvp-audit-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        });

        // ---------------------------------------------------------------- slot locks and presence
        app.MapGet("/api/locks", () => z.Locks.All().Select(Authz.LockDto));
        app.MapPost("/api/slots/{n:int}/lock", (HttpContext ctx, int n, JsonElement body) =>
        {
            var p = z.Resolve(ctx);
            if (!processor.IsValidSlot(n)) return Error(404, $"Slot must be 1–{processor.Slots.Count}.");
            try
            {
                DateTimeOffset? expires = null;
                if (Str(body, "expires") is { Length: > 0 } es) { if (!DateTimeOffset.TryParse(es, out var d)) return Error(400, "The expiry isn't a date and time."); expires = d; }
                else if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("hours", out var hv) && hv.ValueKind == JsonValueKind.Number && hv.TryGetDouble(out var hrs) && hrs > 0) expires = DateTimeOffset.UtcNow.AddHours(hrs);
                var l = z.Locks.Lock(n, p, Str(body, "reason"), expires);
                activity.Add("lock", $"Locked slot {n}{(l.Reason != null ? ": " + l.Reason : "")}{(l.Expires != null ? $" (until {l.Expires.Value.ToLocalTime():yyyy-MM-dd HH:mm})" : "")}", n, p.Kind == PrincipalKind.Local ? Actors.ThisComputer : Authz.IpOf(ctx));
                return Results.Ok(Authz.LockDto(l));
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
        });
        app.MapDelete("/api/slots/{n:int}/lock", (HttpContext ctx, int n) =>
        {
            var p = z.Resolve(ctx);
            if (!processor.IsValidSlot(n)) return Error(404, $"Slot must be 1–{processor.Slots.Count}.");
            try
            {
                var was = z.Locks.Get(n);
                if (!z.Locks.Unlock(n, p)) return Error(404, "That slot isn't locked.");
                activity.Add("unlock", $"Unlocked slot {n}{(was != null && was.ById != p.LockId ? $" (it was locked by {was.By})" : "")}", n, p.Kind == PrincipalKind.Local ? Actors.ThisComputer : Authz.IpOf(ctx));
                return Results.Ok(new { unlocked = n });
            }
            catch (UnauthorizedAccessException e)
            {
                z.Audit.Add("lock.denied", $"{p.Display} tried to unlock slot {n}: {e.Message}", p.ActivityUser ?? p.Name, p.Ip, n);
                return Error(403, e.Message);
            }
        });
        app.MapGet("/api/presence", (HttpContext ctx) =>
        {
            var me = Authz.PresenceKey(z.Resolve(ctx));
            var d = new Dictionary<string, object>();
            for (var n = 1; n <= processor.Slots.Count; n++)
            {
                if (!(Principal.Current?.CanSlot(n) ?? true)) continue;
                var list = presence.ForSlot(n);
                if (list.Count > 0) d[n.ToString()] = list.Select(e => new { name = e.Name, initials = e.Initials, kind = e.Kind, since = e.Since, me = e.Key == me });
            }
            return new { slots = d };
        });
    }

    private static object SecurityStatus(Authz z, HttpsManager https)
    {
        var s = z.Settings.Get();
        return new
        {
            users = z.Users.Count, localExemption = s.LocalExemption, localExempt = z.LocalExempt, localRequiresKey = z.Key.LocalRequiresKey, keyEnabled = z.Key.Enabled,
            sessionIdleMinutes = s.SessionIdleMinutes, sessionAbsoluteHours = s.SessionAbsoluteHours, lockoutAttempts = s.LockoutAttempts, lockoutMinutes = s.LockoutMinutes,
            auditMaxMegabytes = s.AuditMaxMegabytes, auditMaxDays = s.AuditMaxDays, sessions = z.Sessions.Count, tokens = z.Tokens.List().Count(t => !t.Revoked)
        };
    }
}
