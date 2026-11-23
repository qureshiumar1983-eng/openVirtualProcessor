using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenVP.Core.Security;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>A client with its own cookies (and optionally a token), separate from the test server's own key-carrying client.</summary>
public sealed class SecClient : IDisposable
{
    public readonly HttpClient H;
    public readonly CookieContainer Jar = new();
    public readonly string Url;
    public string? Bearer;

    public SecClient(OpsTestServer s, string? bearer = null, string? url = null)
    {
        Url = url ?? s.Url; Bearer = bearer;
        H = new HttpClient(new SocketsHttpHandler { CookieContainer = Jar, UseCookies = true, AllowAutoRedirect = false, SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true } }) { Timeout = TimeSpan.FromSeconds(60) };
    }

    public (int Status, JsonElement Json, string Text) Call(HttpMethod m, string path, object? body = null, params (string, string)[] headers)
    {
        var req = new HttpRequestMessage(m, Url + path);
        if (Bearer != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
        foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        if (body != null) req.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var r = H.Send(req);
        var text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        JsonElement j = default; try { if (text.Length > 0) j = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
        LastResponse = r;
        return ((int)r.StatusCode, j, text);
    }
    public HttpResponseMessage? LastResponse;
    public (int Status, JsonElement Json, string Text) Get(string path) => Call(HttpMethod.Get, path);
    public (int Status, JsonElement Json, string Text) Post(string path, object? body = null) => Call(HttpMethod.Post, path, body ?? new { });
    public (int Status, JsonElement Json, string Text) Put(string path, object body) => Call(HttpMethod.Put, path, body);
    public (int Status, JsonElement Json, string Text) Delete(string path) => Call(HttpMethod.Delete, path);
    public int Login(string user, string password) => Post("/api/login", new { user, password }).Status;

    /// <summary>The HTTP status of a WebSocket handshake (101 when it opened).</summary>
    public int WebSocket(string path, string? origin = null)
    {
        using var ws = new ClientWebSocket();
        ws.Options.Cookies = Jar;
        if (Bearer != null) ws.Options.SetRequestHeader("Authorization", "Bearer " + Bearer);
        if (origin != null) ws.Options.SetRequestHeader("Origin", origin);
        try { ws.ConnectAsync(new Uri(Url.Replace("http", "ws") + path), CancellationToken.None).GetAwaiter().GetResult(); }
        catch (WebSocketException e) { var m = System.Text.RegularExpressions.Regex.Match(e.Message + " " + e.InnerException?.Message, @"\b(4\d\d|5\d\d)\b"); return m.Success ? int.Parse(m.Value) : -1; }
        try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult(); } catch (Exception) { }
        return 101;
    }
    public void Dispose() => H.Dispose();
}

public static class SecurityApiTests
{
    private static readonly string[] Roles = ["viewer", "technician", "programmer", "admin"];

    private static (OpsTestServer S, Dictionary<string, SecClient> C) ServerWithUsers(bool localRequiresKey = true, params string[] more)
    {
        var s = new OpsTestServer(localRequiresKey, more: more);
        var c = new Dictionary<string, SecClient>();
        foreach (var r in Roles)
        {
            Assert.Equal(200, s.Post("/api/users", new { name = r, password = r + "-password-1", role = r }).Status, "create " + r);
            var cl = new SecClient(s);
            Assert.Equal(200, cl.Login(r, r + "-password-1"), "login " + r);
            c[r] = cl;
        }
        return (s, c);
    }

    // ------------------------------------------------------------------ the permission map

    private static readonly string[] ExpectedPublic =
    [
        "* /hooks/{token}", "POST /hooks/{token}", "* /s/{token}/ws", "GET /s/{token}", "GET /s/{token}/app", "GET /s/{token}/app/{**path}", "GET /s/{token}/openvp-crcomlib.js",
        "GET /s/{token}/status", "POST /s/{token}/pin", "GET /healthz", "GET /api/info", "POST /api/login", "POST /api/logout", "GET /api/login/oidc", "GET /api/login/oidc/done",
        "GET /.well-known/acme-challenge/{token}"
    ];

    [Test]
    public static void Every_registered_endpoint_declares_a_permission_or_is_explicitly_public()
    {
        using var s = new OpsTestServer();
        var eps = s.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.True(eps.Count > 200, "found the endpoints: " + eps.Count);
        var missing = new List<string>(); var pub = new SortedSet<string>(StringComparer.Ordinal); var seen = new HashSet<string>();
        foreach (var e in eps)
        {
            var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods is { Count: > 0 } m ? m.ToArray() : ["*"];
            foreach (var method in methods)
            {
                var key = EndpointPolicy.KeyOf(method, e.RoutePattern.RawText ?? "");
                seen.Add(key);
                var p = EndpointPolicy.For(method, e);
                if (p == null) missing.Add(key); else if (p.Kind == "public") pub.Add(key);
            }
        }
        Assert.True(missing.Count == 0, "endpoints with no line in the permission map (add one to PolicyTable): " + string.Join("; ", missing));
        var stale = EndpointPolicy.Table.Keys.Where(k => !seen.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "lines in the permission map that match no endpoint: " + string.Join("; ", stale));
        Assert.True(pub.SetEquals(ExpectedPublic), "the public endpoints are exactly the documented ones. Unexpected: " + string.Join("; ", pub.Except(ExpectedPublic)) + " Missing: " + string.Join("; ", ExpectedPublic.Except(pub)));
    }

    [Test]
    public static void An_endpoint_with_no_permission_line_is_admin_only()
    {
        // The server fails closed: a route the map doesn't know needs Perm.Admin, so a forgotten line never opens anything to a Viewer.
        Assert.True(EndpointPolicy.For("GET", "/api/not-in-the-table") == null, "unknown pattern has no policy");
        Assert.True(EndpointPolicy.For("GET", "/api/users")!.Perm == Perm.Admin, "users are admin");
        Assert.True(EndpointPolicy.For("POST", "/api/slots/{n:int}/unload")!.Locks, "unload is stopped by a lock");
        Assert.True(EndpointPolicy.For("GET", "/metrics")!.Kind == "metrics" && EndpointPolicy.For("*", "/ws/panel")!.Kind == "panel", "metrics and panels have their own rules");
    }

    // ------------------------------------------------------------------ no users: behaviour as before

    [Test]
    public static void With_no_users_the_access_key_and_the_localhost_exemption_work_as_before()
    {
        using var s = new OpsTestServer(localRequiresKey: false);
        using var anon = new SecClient(s);
        Assert.Equal(200, anon.Get("/api/slots").Status, "this computer needs no key");
        var info = anon.Get("/api/info").Json;
        Assert.True(!info.GetProperty("users").GetBoolean() && info.GetProperty("authenticated").GetBoolean(), "no users, and authenticated");
        Assert.True(anon.Get("/api/info").Json.GetProperty("me").GetProperty("kind").GetString() == "local", "as this computer");
        Assert.Equal(200, anon.Get("/api/users").Status, "this computer is an Admin, as it always was");
        using var proxied = new SecClient(s);
        Assert.Equal(401, proxied.Call(HttpMethod.Get, "/api/slots", null, ("X-Forwarded-For", "1.2.3.4")).Status, "a proxied request is never local");
        Assert.Equal(401, proxied.Call(HttpMethod.Get, "/api/slots", null, ("Cf-Ray", "x")).Status, "nor one with Cloudflare headers");
        using var keyed = new SecClient(s, s.Key);
        Assert.Equal(200, keyed.Call(HttpMethod.Get, "/api/slots", null, ("X-Forwarded-For", "1.2.3.4")).Status, "the key works through a proxy");
        using var hdr = new SecClient(s);
        Assert.Equal(200, hdr.Call(HttpMethod.Get, "/api/slots", null, ("X-OpenVP-Key", s.Key), ("X-Forwarded-For", "1.2.3.4")).Status, "and as X-OpenVP-Key");
        Assert.Equal(401, hdr.Call(HttpMethod.Get, "/api/slots", null, ("X-OpenVP-Key", "wrong"), ("X-Forwarded-For", "1.2.3.4")).Status, "a wrong key");
        Assert.Equal(404, anon.Get("/healthz2").Status == 404 ? 404 : 0, "unknown pages are 404");
    }

    [Test]
    public static void With_the_exemption_off_the_key_cookie_and_query_login_still_work()
    {
        using var s = new OpsTestServer(localRequiresKey: true);
        using var anon = new SecClient(s);
        Assert.Equal(401, anon.Get("/api/slots").Status, "no key, no entry");
        var r = anon.Get("/api/info"); Assert.True(r.Json.GetProperty("auth").GetBoolean() && !r.Json.GetProperty("authenticated").GetBoolean(), "info says to log in");
        Assert.Equal(401, anon.Post("/api/login", new { key = "wrong" }).Status, "wrong key");
        Assert.Equal(200, anon.Post("/api/login", new { key = s.Key }).Status, "right key");
        Assert.Equal(200, anon.Get("/api/slots").Status, "the cookie works");
        Assert.True(anon.Get("/api/info").Json.GetProperty("me").GetProperty("kind").GetString() == "key", "as the access key");
        Assert.Equal(200, anon.Get("/api/users").Status, "the key is an Admin");
        using var q = new SecClient(s);
        var rr = q.Get("/?key=" + s.Key); Assert.Equal(302, rr.Status, "?key= redirects");
        Assert.Equal(200, q.Get("/api/slots").Status, "and logs the browser in");
    }

    [Test]
    public static void Wrong_key_logins_from_one_address_are_limited()
    {
        using var s = new OpsTestServer();
        using var c = new SecClient(s);
        for (var i = 0; i < 5; i++) Assert.Equal(401, c.Post("/api/login", new { key = "nope" + i }).Status, "wrong key " + i);
        Assert.Equal(429, c.Post("/api/login", new { key = s.Key }).Status, "after five wrong tries even the right key waits");
    }

    // ------------------------------------------------------------------ the role matrix

    // (method, path, minimum role, body)
    private static readonly (string M, string P, string Min, object? B)[] Matrix =
    [
        ("GET", "/api/slots", "viewer", null), ("GET", "/api/slots/1/signals", "viewer", null), ("GET", "/api/log", "viewer", null), ("GET", "/api/metrics", "viewer", null), ("GET", "/api/slots/1/iptable", "viewer", null),
        ("GET", "/api/activity", "viewer", null), ("GET", "/api/slots/1/files", "viewer", null), ("GET", "/api/schedules", "viewer", null), ("GET", "/api/locks", "viewer", null), ("GET", "/api/presence", "viewer", null),
        ("GET", "/api/rack", "viewer", null), ("GET", "/api/slots/1/autopanel", "viewer", null), ("GET", "/api/slots/1/layouts", "viewer", null), ("GET", "/api/panels/sg", "viewer", null),
        ("GET", "/emulate/1/03/as/1/03/", "viewer", null), ("PUT", "/api/slots/1/layouts/t", "programmer", new { controls = new { } }), ("DELETE", "/api/slots/1/layouts/t", "programmer", null),
        ("DELETE", "/api/slots/1/panels/03/sg", "programmer", null),
        ("POST", "/api/slots/1/signals", "technician", new { name = "x", value = 1 }), ("POST", "/api/slots/1/start", "technician", null), ("POST", "/api/slots/1/stop", "technician", null),
        ("POST", "/api/slots/1/restart", "technician", null), ("POST", "/api/slots/1/iptable/03/identify", "technician", null), ("POST", "/api/shares", "technician", new { }),
        ("GET", "/api/shares", "technician", null), ("POST", "/api/alerts/nope/ack", "technician", null), 
        ("POST", "/api/slots/1/unload", "programmer", null), ("POST", "/api/slots/1/snapshots", "programmer", null), ("PUT", "/api/slots/1/settings", "programmer", new { name = "T" }),
        ("PUT", "/api/slots/1/watches", "programmer", new { watches = Array.Empty<object>() }), ("POST", "/api/slots/1/step", "programmer", new { enabled = false }),
        ("POST", "/api/slots/1/files/folder", "programmer", new { root = "user", path = "", name = "t" }), ("POST", "/api/slots/1/simulators", "programmer", new { }),
        ("POST", "/api/slots/1/recording/start", "programmer", null), ("POST", "/api/hooks", "programmer", new { }), ("GET", "/api/hooks", "programmer", null),
        ("POST", "/api/schedules", "programmer", new { }), ("POST", "/api/slots/1/console", "programmer", new { command = "help" }), ("PUT", "/api/slots/1/integrations", "programmer", new { }),
        ("POST", "/api/check", "programmer", null), ("POST", "/api/slots/bulk", "technician", new { slots = new[] { 1 }, action = "start" }),
        ("GET", "/api/users", "admin", null), ("GET", "/api/tokens", "admin", null), ("GET", "/api/audit", "admin", null), ("PUT", "/api/security", "admin", new { }), ("GET", "/api/https", "admin", null),
        ("GET", "/api/targets", "admin", null), ("GET", "/api/backups", "admin", null), ("PUT", "/api/alerts/config", "admin", new { }), ("POST", "/api/import/preview", "admin", null),
        ("POST", "/api/export", "admin", new { }), ("POST", "/api/tunnel/start", "admin", new { }), ("PUT", "/api/integrations/mqtt", "admin", new { }), ("GET", "/api/git", "admin", null),
        ("POST", "/api/users", "admin", new { name = "x" }), ("GET", "/api/oidc", "admin", null), ("POST", "/api/audit/verify", "admin", null),
    ];

    [Test]
    public static void Each_role_can_do_exactly_what_it_should_on_representative_endpoints()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            foreach (var (m, p, min, body) in Matrix)
            {
                foreach (var role in Roles)
                {
                    var allowed = Array.IndexOf(Roles, role) >= Array.IndexOf(Roles, min);
                    var r = clients[role].Call(new HttpMethod(m), p, body);
                    if (allowed) Assert.True(r.Status is not (401 or 403 or 423), $"{role} {m} {p} should get through, got {r.Status} {r.Text}");
                    else Assert.Equal(403, r.Status, $"{role} {m} {p} should be refused");
                }
                // and nobody at all gets in without signing in
                using var anon = new SecClient(s);
                Assert.Equal(401, anon.Call(new HttpMethod(m), p, body).Status, $"anonymous {m} {p}");
            }
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void Websockets_follow_the_same_permissions()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            using var anon = new SecClient(s);
            Assert.Equal(401, anon.WebSocket("/ws/trace?slot=1"), "the debugger stream needs a login");
            Assert.Equal(401, anon.WebSocket("/ws/metrics"), "so does the metrics stream");
            Assert.Equal(101, anon.WebSocket("/ws/panel?ipid=03&slot=1"), "browser panels stay open");
            foreach (var role in Roles)
            {
                Assert.Equal(101, clients[role].WebSocket("/ws/trace?slot=1"), role + " can watch the debugger stream");
                Assert.Equal(101, clients[role].WebSocket("/ws/metrics"), role + " can watch metrics");
            }
            Assert.Equal(403, clients["viewer"].WebSocket("/ws/trace?slot=1", "http://evil.test"), "another site can't open it, even with the cookie");
            using var tok = new SecClient(s, s.Key);
            Assert.Equal(101, tok.WebSocket("/ws/trace?slot=2"), "the access key opens it too");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void Panels_need_a_signal_permission_only_when_PanelsRequireKey_is_set()
    {
        var (s, clients) = ServerWithUsers(true, "--OpenVP:PanelsRequireKey=true");
        using var _s = s;
        try
        {
            using var anon = new SecClient(s);
            Assert.Equal(401, anon.WebSocket("/ws/panel?ipid=03&slot=1"), "closed to strangers");
            Assert.Equal(403, clients["viewer"].WebSocket("/ws/panel?ipid=03&slot=1"), "a Viewer can't press joins");
            Assert.Equal(101, clients["technician"].WebSocket("/ws/panel?ipid=03&slot=1"), "a Technician can");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void Metrics_follow_the_MetricsPublic_option_and_roles()
    {
        using (var s = new OpsTestServer())
        {
            using var anon = new SecClient(s);
            Assert.Equal(401, anon.Get("/metrics").Status, "needs the key by default");
            Assert.Equal(200, s.Get("/metrics").Status, "the key reads it");
        }
        using (var s = new OpsTestServer(more: "--OpenVP:MetricsPublic=true"))
        {
            using var anon = new SecClient(s);
            Assert.Equal(200, anon.Get("/metrics").Status, "MetricsPublic opens it");
        }
        var (s2, clients) = ServerWithUsers();
        using var _s = s2;
        Assert.Equal(200, clients["viewer"].Get("/metrics").Status, "a Viewer may scrape it");
        foreach (var c in clients.Values) c.Dispose();
    }

    // ------------------------------------------------------------------ slot restriction

    [Test]
    public static void A_user_or_token_limited_to_a_slot_sees_and_changes_only_that_slot()
    {
        using var s = new OpsTestServer();
        Assert.Equal(200, s.Post("/api/users", new { name = "room2", password = "room2-password", role = "programmer", slots = new[] { 2 } }).Status, "create");
        using var u = new SecClient(s); Assert.Equal(200, u.Login("room2", "room2-password"), "login");
        Assert.Equal(403, u.Get("/api/slots/1/signals").Status, "another slot is refused");
        Assert.Equal(403, u.Post("/api/slots/1/start").Status, "even to operate it");
        Assert.True(u.Get("/api/slots/2/signals").Status is not (401 or 403), "its own slot is fine");
        var list = u.Get("/api/slots").Json;
        Assert.True(list.GetArrayLength() == 1 && list[0].GetProperty("number").GetInt32() == 2, "the list holds only its slots");
        Assert.Equal(403, u.Post("/api/schedules", new { }).Status, "global things that reach every slot are closed to it");
        Assert.Equal(403, u.Get("/api/hooks").Status, "incoming hook tokens too");
        Assert.Equal(403, u.WebSocket("/ws/trace?slot=1"), "the debugger stream of another slot");
        Assert.Equal(101, u.WebSocket("/ws/trace?slot=2"), "and its own is open");
        var bulk = u.Post("/api/slots/bulk", new { slots = new[] { 1, 2 }, action = "stop" }).Json.GetProperty("results");
        Assert.True(!bulk[0].GetProperty("ok").GetBoolean() && bulk[1].GetProperty("ok").GetBoolean(), "a bulk action skips the slot it may not touch");
        Assert.True(u.Get("/api/devices").Json.GetArrayLength() == 0 && u.Get("/api/log?slot=1").Json.GetArrayLength() == 0, "lists are filtered");

        var t = s.Post("/api/tokens", new { name = "slot1only", scope = "programmer", slots = new[] { 1 } });
        using var tc = new SecClient(s, t.Json.GetProperty("token").GetString());
        Assert.True(tc.Get("/api/slots/1/signals").Status is not (401 or 403), "a token limited to slot 1 can use it");
        Assert.Equal(403, tc.Get("/api/slots/2/signals").Status, "and not slot 2");
        s.Put("/api/users/" + u.Get("/api/info").Json.GetProperty("me").GetProperty("name").GetString(), new { }); // (no-op: ids aren't names)
    }

    // ------------------------------------------------------------------ tokens

    [Test]
    public static void Tokens_work_in_both_headers_with_their_scope_and_stop_when_revoked_or_expired()
    {
        using var s = new OpsTestServer();
        string Make(string name, string scope, object? extra = null)
        {
            var r = s.Post("/api/tokens", extra == null ? new { name, scope } : extra);
            Assert.Equal(200, r.Status, "create " + name + ": " + r.Text);
            return r.Json.GetProperty("token").GetString()!;
        }
        var ro = Make("ro", "read-only"); var op = Make("op", "operator"); var pr = Make("pr", "programmer"); var ad = Make("ad", "admin");
        using var roC = new SecClient(s, ro); using var opC = new SecClient(s, op); using var prC = new SecClient(s, pr); using var adC = new SecClient(s, ad);
        Assert.Equal(200, roC.Get("/api/slots").Status, "read-only reads");
        Assert.Equal(403, roC.Post("/api/slots/1/start").Status, "but doesn't operate");
        Assert.Equal(403, opC.Post("/api/slots/1/unload").Status, "an operator doesn't unload");
        Assert.True(opC.Post("/api/slots/1/stop").Status is not (401 or 403), "an operator stops");
        Assert.True(opC.Post("/api/slots/1/signals", new { name = "x", value = 1 }).Status is not (401 or 403), "and sets signals");
        Assert.True(prC.Post("/api/slots/1/unload").Status is not (401 or 403), "a programmer unloads");
        Assert.Equal(403, prC.Get("/api/users").Status, "but isn't an admin");
        Assert.Equal(200, adC.Get("/api/users").Status, "an admin token is");
        using var hdr = new SecClient(s);
        Assert.Equal(200, hdr.Call(HttpMethod.Get, "/api/slots", null, ("X-OpenVP-Key", ro)).Status, "X-OpenVP-Key takes a token");
        Assert.Equal(401, hdr.Call(HttpMethod.Get, "/api/slots", null, ("X-OpenVP-Key", "ovp_" + new string('z', 43))).Status, "an unknown one is refused");

        var list = s.Get("/api/tokens").Json;
        var row = list.EnumerateArray().First(x => x.GetProperty("name").GetString() == "ro");
        Assert.True(row.GetProperty("lastUsed").ValueKind == JsonValueKind.String && !row.TryGetProperty("token", out _) && !row.TryGetProperty("hash", out _), "the list shows last use and never the value");
        Assert.True(!File.ReadAllText(Path.Combine(s.Dir, "security", "tokens.json")).Contains(ro), "only a hash is on disk");

        var id = row.GetProperty("id").GetString();
        Assert.Equal(200, s.Delete("/api/tokens/" + id).Status, "revoke");
        Assert.Equal(401, roC.Get("/api/slots").Status, "a revoked token stops at once");

        var short1 = s.Post("/api/tokens", new { name = "short", scope = "read-only", expires = DateTimeOffset.UtcNow.AddSeconds(2).ToString("O") }).Json.GetProperty("token").GetString();
        using var shC = new SecClient(s, short1);
        Assert.Equal(200, shC.Get("/api/slots").Status, "valid until its expiry");
        Thread.Sleep(2300);
        Assert.Equal(401, shC.Get("/api/slots").Status, "expired");
        Assert.Equal(400, s.Post("/api/tokens", new { name = "past", scope = "read-only", expires = "2001-01-01T00:00:00Z" }).Status, "an expiry in the past is refused");
        Assert.Equal(400, s.Post("/api/tokens", new { name = "x", scope = "root" }).Status, "an unknown scope");

        var audit = s.Get("/api/audit?limit=500").Json.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.True(audit.Contains("token.create") && audit.Contains("token.revoke") && audit.Contains("token.use") && audit.Contains("token.denied"), "token events are audited: " + string.Join(",", audit.Distinct()));
        Assert.Equal(1, s.Get("/api/audit?limit=500&action=token.use&q=ro").Json.GetProperty("entries").EnumerateArray().Count(e => e.GetProperty("user").GetString() == "token ro"), "the first use of a token each day is audited once");
    }

    // ------------------------------------------------------------------ users: login, sessions, lockout, passwords

    [Test]
    public static void Login_sets_a_strict_httponly_cookie_logout_ends_it_and_sessions_time_out()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            using var c = new SecClient(s);
            var r = c.Post("/api/login", new { user = "technician", password = "technician-password-1" });
            Assert.Equal(200, r.Status, "login");
            var cookie = c.LastResponse!.Headers.GetValues("Set-Cookie").First(x => x.StartsWith("openvp_session="));
            Assert.True(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "HttpOnly and SameSite=Strict: " + cookie);
            Assert.True(!cookie.Contains("secure", StringComparison.OrdinalIgnoreCase), "not Secure on plain http");
            var me = c.Get("/api/info").Json.GetProperty("me");
            Assert.True(me.GetProperty("name").GetString() == "technician" && me.GetProperty("role").GetString() == "technician" && me.GetProperty("canChangePassword").GetBoolean(), "who am I");
            Assert.Equal(200, c.Get("/api/slots").Status, "signed in");
            Assert.Equal(200, c.Post("/api/logout").Status, "logout");
            Assert.Equal(401, c.Get("/api/slots").Status, "and out");

            // Idle timeout (the server's SessionStore, shortened for the test).
            var z = s.App.Services.GetRequiredService<Authz>();
            z.Sessions.Idle = TimeSpan.FromMilliseconds(600);
            Assert.Equal(200, c.Login("technician", "technician-password-1"), "login again");
            Assert.Equal(200, c.Get("/api/slots").Status, "fine");
            Thread.Sleep(900);
            Assert.Equal(401, c.Get("/api/slots").Status, "the idle timeout signs it out");
            z.Sessions.Idle = TimeSpan.FromHours(1);
            z.Sessions.Absolute = TimeSpan.FromMilliseconds(700);
            Assert.Equal(200, c.Login("technician", "technician-password-1"), "login for the absolute timeout");
            Thread.Sleep(400); Assert.Equal(200, c.Get("/api/slots").Status, "busy but young");
            Thread.Sleep(500); Assert.Equal(401, c.Get("/api/slots").Status, "the absolute timeout ends even a busy session");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void A_session_cookie_is_refused_from_another_site()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            Assert.Equal(200, clients["admin"].Call(HttpMethod.Get, "/api/slots", null, ("Origin", s.Url)).Status, "same-origin pages work");
            Assert.Equal(403, clients["admin"].Call(HttpMethod.Get, "/api/slots", null, ("Origin", "http://evil.example")).Status, "a page on another site can't ride the cookie");
            Assert.Equal(403, clients["admin"].Call(HttpMethod.Post, "/api/login", new { user = "admin", password = "admin-password-1" }, ("Origin", "http://evil.example")).Status, "nor log in");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void Five_wrong_passwords_lock_an_account_and_an_admin_can_unlock_it()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            using var c = new SecClient(s);
            for (var i = 0; i < 4; i++) Assert.Equal(401, c.Login("viewer", "wrong-password-" + i), "wrong " + i);
            Assert.Equal(429, c.Login("viewer", "wrong-password-4"), "the fifth locks it");
            Assert.Equal(429, c.Login("viewer", "viewer-password-1"), "the right password is refused while it is locked");
            var users = s.Get("/api/users").Json; var v = users.EnumerateArray().First(u => u.GetProperty("name").GetString() == "viewer");
            Assert.True(v.GetProperty("locked").GetBoolean(), "the Users list says so");
            Assert.Equal(200, s.Post("/api/users/" + v.GetProperty("id").GetString() + "/unlock").Status, "unlock");
            using var c2 = new SecClient(s);
            Assert.Equal(200, c2.Login("viewer", "viewer-password-1"), "works again");
            var actions = s.Get("/api/audit?limit=100").Json.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            Assert.True(actions.Contains("login.failed") && actions.Contains("login.locked") && actions.Contains("login") && actions.Contains("user.unlock"), "logins, failures and the lock-out are in the audit log");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void Changing_your_password_signs_out_your_other_browsers()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            using var other = new SecClient(s); Assert.Equal(200, other.Login("technician", "technician-password-1"), "second browser");
            var me = clients["technician"];
            Assert.Equal(403, me.Post("/api/me/password", new { current = "wrong", @new = "brand-new-password" }).Status, "wrong current password");
            Assert.Equal(400, me.Post("/api/me/password", new { current = "technician-password-1", @new = "short" }).Status, "too short");
            Assert.Equal(200, me.Post("/api/me/password", new { current = "technician-password-1", @new = "brand-new-password" }).Status, "changed");
            Assert.Equal(200, me.Get("/api/slots").Status, "this browser stays signed in");
            Assert.Equal(401, other.Get("/api/slots").Status, "the other one is signed out");
            using var c = new SecClient(s);
            Assert.Equal(401, c.Login("technician", "technician-password-1"), "the old password is gone");
            Assert.Equal(200, c.Login("technician", "brand-new-password"), "the new one works");
            Assert.Equal(403, clients["viewer"].Get("/api/users").Status, "(and a viewer still can't list users)");
            // an admin resets a password
            var id = s.Get("/api/users").Json.EnumerateArray().First(u => u.GetProperty("name").GetString() == "viewer").GetProperty("id").GetString();
            Assert.Equal(200, s.Post($"/api/users/{id}/password", new { password = "reset-by-admin-1" }).Status, "admin reset");
            Assert.Equal(401, clients["viewer"].Get("/api/slots").Status, "their sessions ended");
            Assert.Equal(200, new SecClient(s).Login("viewer", "reset-by-admin-1"), "and the new password works");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void The_localhost_exemption_stays_on_by_default_and_can_be_turned_off_once_users_exist()
    {
        using var s = new OpsTestServer(localRequiresKey: false);
        using var anon = new SecClient(s);
        Assert.Equal(200, s.Post("/api/users", new { name = "root", password = "root-password-1", role = "admin" }).Status, "first user");
        Assert.Equal(200, anon.Get("/api/slots").Status, "this computer still needs no login (the desktop app keeps working)");
        Assert.True(anon.Get("/api/info").Json.GetProperty("localExempt").GetBoolean() && anon.Get("/api/info").Json.GetProperty("users").GetBoolean(), "info says users exist and the exemption is on");
        Assert.Equal(200, s.Put("/api/security", new { localExemption = false }).Status, "turn it off");
        Assert.Equal(401, anon.Get("/api/slots").Status, "now this computer signs in like everyone");
        Assert.Equal(200, s.Get("/api/slots").Status, "the access key still works");
        Assert.Equal(200, anon.Login("root", "root-password-1"), "and so does a user");
        Assert.Equal(200, anon.Get("/api/users").Status, "an Admin");
        s.Put("/api/security", new { localExemption = true });
        using var again = new SecClient(s);
        Assert.Equal(200, again.Get("/api/slots").Status, "back on");
    }

    [Test]
    public static void Users_exist_and_the_key_is_off_means_nobody_gets_in_without_signing_in()
    {
        using var s = new OpsTestServer(true, more: "--OpenVP:ApiKey=off");
        using var anon = new SecClient(s);
        Assert.Equal(200, anon.Get("/api/slots").Status, "with the key off and no users it is open, as before");
        Assert.Equal(200, anon.Post("/api/users", new { name = "root", password = "root-password-1", role = "admin" }).Status, "make the first user");
        Assert.Equal(401, anon.Get("/api/slots").Status, "now 'off' opens nothing");
        Assert.Equal(200, anon.Login("root", "root-password-1"), "sign in");
        Assert.Equal(200, anon.Get("/api/slots").Status, "works");
    }

    // ------------------------------------------------------------------ audit and activity

    [Test]
    public static void The_audit_log_records_security_events_for_admins_only_and_catches_tampering()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            clients["viewer"].Post("/api/slots/1/start"); // a permission denial
            clients["programmer"].Put("/api/slots/1/room", new { roomId = "AUDITROOM" });
            s.Put("/api/slots/2/room", new { roomId = "KEYROOM" });
            Assert.Equal(403, clients["programmer"].Get("/api/audit").Status, "only an Admin reads it");
            Assert.Equal(403, clients["programmer"].Get("/api/audit/download").Status, "or downloads it");
            var entries = s.Get("/api/audit?limit=500").Json.GetProperty("entries").EnumerateArray().ToList();
            string[] Actions(string a) => entries.Where(e => e.GetProperty("action").GetString() == a).Select(e => e.GetProperty("text").GetString()!).ToArray();
            Assert.True(Actions("login").Length >= 4 && Actions("user.create").Length == 4, "sign-ins and user creation");
            Assert.True(Actions("permission.denied").Any(t => t.Contains("viewer") && t.Contains("/api/slots/1/start")), "the denial names the user and the request");
            var settings = entries.First(e => e.GetProperty("action").GetString() == "settings" && e.GetProperty("text").GetString()!.Contains("AUDITROOM"));
            Assert.True(settings.GetProperty("user").GetString() == "programmer" && settings.GetProperty("ip").GetString() == "127.0.0.1", "a settings change carries the user and the address");
            var viaKey = entries.First(e => e.GetProperty("action").GetString() == "settings" && e.GetProperty("text").GetString()!.Contains("KEYROOM"));
            Assert.True(viaKey.GetProperty("user").GetString() == "access key", "and the access key is named as such");
            var act = s.Get("/api/activity?limit=10").Json.EnumerateArray().ToList();
            Assert.True(act.Any(e => e.GetProperty("user").GetString() == "programmer") && act.Any(e => e.GetProperty("user").GetString() == "access key"), "the activity feed's user field is filled from the signed-in identity");

            var csv = s.Http.GetAsync(s.Url + "/api/audit/download?action=login").GetAwaiter().GetResult();
            Assert.True(csv.Content.Headers.ContentType!.MediaType == "text/csv" && csv.Content.ReadAsStringAsync().GetAwaiter().GetResult().StartsWith("seq,time,user,ip,action"), "a CSV download");

            Assert.True(s.Post("/api/audit/verify").Json.GetProperty("ok").GetBoolean(), "the chain verifies");
            var file = Path.Combine(s.Dir, "audit", "audit.jsonl");
            var lines = File.ReadAllLines(file);
            var tampered = lines.ToArray(); var at = Array.FindIndex(tampered, l => l.Contains("signed in")); tampered[at] = tampered[at].Replace("signed in", "was never here");
            File.WriteAllLines(file, tampered);
            var v = s.Post("/api/audit/verify").Json;
            Assert.True(!v.GetProperty("ok").GetBoolean() && v.GetProperty("message").GetString()!.Contains("changed"), "a changed line is found: " + v);
            File.WriteAllLines(file, lines.Where((_, i) => i != 3));
            Assert.True(!s.Post("/api/audit/verify").Json.GetProperty("ok").GetBoolean(), "a removed line is found");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    // ------------------------------------------------------------------ slot locks

    [Test]
    public static void A_locked_slot_refuses_changes_from_anyone_but_an_admin_or_the_locker_but_panels_keep_working()
    {
        var (s, clients) = ServerWithUsers();
        using var _s = s;
        try
        {
            s.LoadSample(1);
            Assert.Equal(200, s.Post("/api/users", new { name = "kim", password = "kim-password-1", role = "programmer" }).Status, "a second programmer");
            using var kim = new SecClient(s); kim.Login("kim", "kim-password-1");
            var sam = clients["programmer"];
            Assert.Equal(403, clients["viewer"].Post("/api/slots/1/lock", new { reason = "x" }).Status, "a Viewer can't lock");
            var lk = sam.Post("/api/slots/1/lock", new { reason = "Live event until 17:00", hours = 2 });
            Assert.Equal(200, lk.Status, "a Programmer locks: " + lk.Text);
            var slot1 = sam.Get("/api/slots/1").Json;
            Assert.True(slot1.GetProperty("locked").GetProperty("by").GetString() == "programmer" && slot1.GetProperty("locked").GetProperty("reason").GetString()!.StartsWith("Live"), "the slot carries its lock (the Status board tile reads it)");
            Assert.True(slot1.GetProperty("locked").GetProperty("expires").ValueKind == JsonValueKind.String, "with its expiry");

            foreach (var (m, p, b) in new (string, string, object?)[]
                { ("POST", "/api/slots/1/restart", null), ("POST", "/api/slots/1/stop", null), ("POST", "/api/slots/1/unload", null), ("POST", "/api/slots/1/signals", new { name = "x", value = 1 }),
                  ("PUT", "/api/slots/1/signals/x", "1"), ("POST", "/api/slots/1/files/folder", new { root = "user", path = "", name = "t" }), ("POST", "/api/slots/1/snapshots/abc/restore", new { }),
                  ("POST", "/api/slots/1/versions/abc/load", new { }), ("POST", "/api/slots/1/program/path", new { path = "/x" }), ("POST", "/api/slots/1/signals/x/pulse", null) })
            {
                var r = kim.Call(new HttpMethod(m), p, b);
                Assert.Equal(423, r.Status, $"{m} {p} is locked for another programmer: {r.Text}");
                Assert.True(r.Json.GetProperty("error").GetString()!.Contains("programmer") && r.Json.GetProperty("error").GetString()!.Contains("Live event"), "the answer says who and why");
            }
            Assert.Equal(423, kim.Post("/api/slots/1/program?how=push").Status, "push-in is refused too");
            var bulk = kim.Post("/api/slots/bulk", new { slots = new[] { 1 }, action = "restart" }).Json.GetProperty("results")[0];
            Assert.True(!bulk.GetProperty("ok").GetBoolean() && bulk.GetProperty("locked").GetBoolean(), "bulk actions skip it");
            Assert.True(kim.Post("/api/slots/1/start").Status != 423, "starting isn't blocked");
            Assert.True(kim.Get("/api/slots/1/signals").Status == 200 && kim.WebSocket("/ws/trace?slot=1") == 101, "reading and watching still work");
            using var anon = new SecClient(s);
            Assert.Equal(101, anon.WebSocket("/ws/panel?ipid=03&slot=1"), "panels keep working");
            Assert.True(kim.Post("/api/slots/2/restart").Status != 423, "other slots are unaffected");
            Assert.Equal(403, kim.Delete("/api/slots/1/lock").Status, "kim can't unlock sam's lock");

            Assert.True(sam.Post("/api/slots/1/restart").Status is 200 or 422, "the locker still can");
            Assert.True(clients["admin"].Post("/api/slots/1/restart").Status is 200 or 422, "so can an Admin");
            Assert.True(s.Post("/api/slots/1/restart").Status is 200 or 422, "and the access key");
            var act = s.Get("/api/activity?limit=30").Json.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            Assert.True(act.Contains("lock"), "the lock is in the activity feed");
            var aud = s.Get("/api/audit?limit=300&action=lock").Json.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            Assert.True(aud.Contains("lock.denied") && aud.Contains("lock"), "and the audit log, with the refusals");

            Assert.Equal(200, clients["admin"].Delete("/api/slots/1/lock").Status, "an Admin unlocks");
            Assert.True(kim.Post("/api/slots/1/restart").Status is 200 or 422, "and then anyone may");
            Assert.True(kim.Get("/api/slots/1").Json.GetProperty("locked").ValueKind == JsonValueKind.Null, "the tile loses its lock");
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }

    [Test]
    public static void A_lock_with_an_expiry_ends_by_itself()
    {
        using var s = new OpsTestServer();
        Assert.Equal(200, s.Post("/api/slots/1/lock", new { reason = "short", expires = DateTimeOffset.UtcNow.AddSeconds(2).ToString("O") }).Status, "lock for two seconds");
        using var c = new SecClient(s);
        Assert.True(s.Get("/api/slots/1").Json.GetProperty("locked").ValueKind == JsonValueKind.Object, "locked now");
        Assert.Equal(400, s.Post("/api/slots/2/lock", new { expires = "2001-01-01T00:00:00Z" }).Status, "an expiry in the past is refused");
        Assert.Eventually(() => s.Get("/api/slots/1").Json.GetProperty("locked").ValueKind == JsonValueKind.Null, "the lock ends", 8000);
    }

    [Test]
    public static void Schedules_and_imports_leave_a_locked_slot_alone()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        var sched = s.Post("/api/schedules", new { name = "Nightly", mode = "daily", time = "03:30", action = new { type = "restart", slot = 1 } });
        Assert.Equal(200, sched.Status, "make a schedule: " + sched.Text);
        var id = sched.Json.GetProperty("schedules")[0].GetProperty("id").GetString();
        // lock it as a Programmer-level token (not an Admin), then run the schedule as a different, non-locking caller
        var pr = s.Post("/api/tokens", new { name = "locker", scope = "programmer" }).Json.GetProperty("token").GetString();
        using var prC = new SecClient(s, pr);
        Assert.Equal(200, prC.Post("/api/slots/1/lock", new { reason = "show" }).Status, "locked by the token");
        var run = prC.Post($"/api/schedules/{id}/run"); // the locker may change it
        Assert.True(run.Json.GetProperty("run").GetProperty("status").GetString() == "ok", "the locker's own manual run goes ahead: " + run.Text);
        var pr2 = s.Post("/api/tokens", new { name = "other", scope = "programmer" }).Json.GetProperty("token").GetString();
        using var pr2C = new SecClient(s, pr2);
        var skipped = pr2C.Post($"/api/schedules/{id}/run").Json.GetProperty("run");
        Assert.True(skipped.GetProperty("status").GetString() == "skipped" && skipped.GetProperty("message").GetString()!.Contains("locked by token locker"), "someone else's run is skipped, not failed: " + skipped);

        // Import (an export file, as a push of an export sends it): the locked slot is skipped unless an Admin says overrideLock.
        var exp = s.Http.PostAsync(s.Url + "/api/export", new StringContent("{\"slots\":[1]}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
        var bytes = exp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        Assert.True(exp.IsSuccessStatusCode && bytes.Length > 100, "an export to import");
        IReadOnlyList<string> Warnings(JsonElement j) => j.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();
        HttpResponseMessage Import(params (string, string)[] fields)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "x.openvp" } };
            foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
            return s.Http.PostAsync(s.Url + "/api/import", form).GetAwaiter().GetResult();
        }
        var r1 = Import(("slots", "1"));
        var j1 = JsonDocument.Parse(r1.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        Assert.True(r1.IsSuccessStatusCode && j1.GetProperty("slots").GetArrayLength() == 0 && Warnings(j1).Any(w => w.Contains("Slot 1 was skipped") && w.Contains("locked by token locker")), "the import skips the locked slot: " + j1);
        var r2 = Import(("slots", "1"), ("overrideLock", "true"));
        var j2 = JsonDocument.Parse(r2.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        Assert.True(r2.IsSuccessStatusCode && j2.GetProperty("slots").GetArrayLength() == 1, "an Admin who says overrideLock imports into it: " + j2);
    }

    [Test]
    public static void Auto_reload_pauses_while_the_slot_is_locked_and_catches_up_when_it_ends()
    {
        using var s = new OpsTestServer();
        var dir = Directory.CreateTempSubdirectory("openvp-watch").FullName;
        var src = Path.Combine(Paths.RepoRoot, "samples", "SimplWindows");
        File.Copy(Path.Combine(src, "SampleProgram.smw"), Path.Combine(dir, "SampleProgram.smw"));
        File.Copy(Path.Combine(src, "SampleProgram.openvp.json"), Path.Combine(dir, "SampleProgram.openvp.json"));
        var r = s.Post("/api/slots/1/program/path", new { path = Path.Combine(dir, "SampleProgram.smw"), watch = true, start = true });
        Assert.Equal(200, r.Status, "load from a path and watch it: " + r.Text);
        Assert.Equal(200, s.Post("/api/slots/1/lock", new { reason = "live" }).Status, "lock it");
        File.AppendAllText(Path.Combine(dir, "SampleProgram.smw"), "\r\n");
        Assert.Eventually(() => s.Get("/api/activity?limit=50").Json.EnumerateArray().Any(e => e.GetProperty("action").GetString() == "auto-reload.paused"), "auto-reload says it paused", 15000);
        Thread.Sleep(1500);
        Assert.True(!s.Get("/api/activity?limit=50").Json.EnumerateArray().Any(e => e.GetProperty("action").GetString() == "auto-reload"), "and did not reload");
        Assert.True(s.Get("/api/log?slot=1").Json.EnumerateArray().Any(e => e.GetProperty("message").GetString()!.Contains("auto-reload is paused")), "the slot log says so too");
        Assert.Equal(200, s.Delete("/api/slots/1/lock").Status, "unlock");
        Assert.Eventually(() => s.Get("/api/activity?limit=50").Json.EnumerateArray().Any(e => e.GetProperty("action").GetString() == "auto-reload"), "the change is loaded once the lock ends", 15000);
        try { Directory.Delete(dir, true); } catch { }
    }

    [Test]
    public static void Pushing_a_program_works_with_a_programmer_token_and_a_read_only_token_is_refused()
    {
        using var a = new OpsTestServer(); using var b = new OpsTestServer();
        a.LoadSample(1);
        string Tok(string scope) => b.Post("/api/tokens", new { name = scope, scope }).Json.GetProperty("token").GetString()!;
        var target = a.Post("/api/targets", new { name = "B", url = b.Url, key = Tok("programmer") });
        Assert.Equal(200, target.Status, "add the target with a programmer token: " + target.Text);
        var tid = target.Json.GetProperty("id").GetString();
        var test = a.Post($"/api/targets/{tid}/test").Json;
        Assert.True(test.GetProperty("ok").GetBoolean(), "the token is accepted by the other machine: " + test);
        var push = a.Post("/api/push", new { what = "version", slot = 1, version = "loaded", start = true, targets = new[] { new { id = tid, slots = new[] { 2 } } } });
        Assert.Equal(200, push.Status, "start the push: " + push.Text);
        var pid = push.Json.GetProperty("id").GetString();
        Assert.Eventually(() => a.Get("/api/push/" + pid).Json.GetProperty("done").GetBoolean(), "push finishes", 30000);
        Assert.Equal("Running", b.P.Slot(2).State.ToString(), "the program arrived and runs on the other machine");
        var who = b.Get("/api/activity?limit=10").Json.EnumerateArray().First(e => e.GetProperty("action").GetString() == "load").GetProperty("user").GetString();
        Assert.Equal("token programmer", who, "and the other machine recorded which token did it");

        Assert.Equal(200, a.Put($"/api/targets/{tid}", new { name = "B", url = b.Url, key = Tok("read-only") }).Status, "change the key");
        var weak = a.Post($"/api/targets/{tid}/test").Json;
        Assert.True(weak.GetProperty("message").GetString()!.Contains("programmer scope"), "the test warns that a read-only token can't push: " + weak);
        var push2 = a.Post("/api/push", new { what = "version", slot = 1, version = "loaded", start = true, targets = new[] { new { id = tid, slots = new[] { 3 } } } });
        var pid2 = push2.Json.GetProperty("id").GetString();
        Assert.Eventually(() => a.Get("/api/push/" + pid2).Json.GetProperty("done").GetBoolean(), "second push finishes", 30000);
        var res = a.Get("/api/push/" + pid2).Json.GetProperty("targets")[0];
        Assert.True(res.GetProperty("state").GetString() != "ok" && b.P.Slot(3).Runtime == null, "a read-only token can't load a program: " + res);

        // a locked slot on the other machine refuses the push as well
        Assert.Equal(200, b.Post("/api/slots/2/lock", new { reason = "show" }).Status, "lock slot 2 on B");
        Assert.Equal(200, a.Put($"/api/targets/{tid}", new { name = "B", url = b.Url, key = Tok("programmer") }).Status, "change the key back");
        var push3 = a.Post("/api/push", new { what = "version", slot = 1, version = "loaded", start = true, targets = new[] { new { id = tid, slots = new[] { 2 } } } });
        var pid3 = push3.Json.GetProperty("id").GetString();
        Assert.Eventually(() => a.Get("/api/push/" + pid3).Json.GetProperty("done").GetBoolean(), "third push finishes", 30000);
        var res3 = a.Get("/api/push/" + pid3).Json.ToString();
        Assert.True(res3.Contains("locked"), "the push into a locked slot is refused and says why: " + res3);
    }
}
