using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Hosting;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenVP.Core;
using OpenVP.Core.Security;
using OpenVP.Server;

namespace OpenVP.Tests;

public static class SecurityHttpsOidcTests
{
    /// <summary>The certificate a server presents on a new connection (its SHA-256 fingerprint, with colons).</summary>
    private static string PresentedFingerprint(int port)
    {
        using var tcp = new TcpClient("127.0.0.1", port);
        using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        ssl.AuthenticateAsClient("localhost");
        return CertificateTools.Fingerprint(new X509Certificate2(ssl.RemoteCertificate!));
    }

    [Test]
    public static void Https_listens_with_a_self_signed_certificate_and_a_new_one_is_used_without_a_restart()
    {
        var a = new OpsTestServer();
        var dir = a.Dir; var httpsPort = IntegrationServiceTests.FreePort();
        try
        {
            Assert.True(!a.Get("/api/https").Json.GetProperty("hasCertificate").GetBoolean(), "no certificate to begin with");
            Assert.Equal(400, a.Put("/api/https", new { enabled = true, port = httpsPort }).Status, "can't turn HTTPS on without a certificate");
            var made = a.Post("/api/https/self-signed", new { host = "pc-7.example" });
            Assert.Equal(200, made.Status, "make a certificate: " + made.Text);
            var st = made.Json;
            Assert.True(st.GetProperty("hasCertificate").GetBoolean() && st.GetProperty("source").GetString() == "self-signed", "it is saved");
            var fp1 = st.GetProperty("fingerprint").GetString()!;
            var names = st.GetProperty("names").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.True(names.Contains("openvp.local") && names.Contains("localhost") && names.Contains("127.0.0.1") && names.Contains("pc-7.example"), "SANs cover the host, openvp.local and the addresses: " + string.Join(",", names));
            Assert.Equal(200, a.Get("/api/https/certificate").Status, "the public certificate can be downloaded to trust it");
            var en = a.Put("/api/https", new { enabled = true, port = httpsPort, redirect = false });
            Assert.True(en.Status == 200 && en.Json.GetProperty("restartNeeded").GetBoolean() && !en.Json.GetProperty("listening").GetBoolean(), "enabling needs a restart: the port is chosen at start-up");
            Assert.Equal(400, a.Put("/api/https", new { port = a.Port }).Status, "the port can't be the console's own");
            a.App.StopAsync().GetAwaiter().GetResult(); a.Http.Dispose();

            using var b = new OpsTestServer(existingData: dir);
            var info = b.Get("/api/info").Json.GetProperty("https");
            Assert.True(info.GetProperty("listening").GetBoolean() && info.GetProperty("port").GetInt32() == httpsPort, "after the restart it listens on HTTPS");
            Assert.True(!b.Get("/api/https").Json.GetProperty("restartNeeded").GetBoolean(), "and nothing is waiting for a restart");
            Assert.Equal(fp1, PresentedFingerprint(httpsPort), "the port presents the saved certificate");
            var r = b.Http.GetAsync($"https://127.0.0.1:{httpsPort}/api/info").GetAwaiter().GetResult();
            Assert.True(r.IsSuccessStatusCode && r.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("\"secure\":true"), "the console answers over https");
            Assert.Equal(200, b.Http.GetAsync($"http://127.0.0.1:{b.Port}/api/info").GetAwaiter().GetResult().StatusCode.GetHashCode() == 200 ? 200 : 0, "plain http still works (CIP and the desktop app don't change)");

            // A user logging in over https gets a Secure cookie.
            Assert.Equal(200, b.Post("/api/users", new { name = "root", password = "root-password-1", role = "admin" }).Status, "a user");
            using var sc = new SecClient(b, null, $"https://127.0.0.1:{httpsPort}");
            var login = sc.Post("/api/login", new { user = "root", password = "root-password-1" });
            Assert.Equal(200, login.Status, "login over https");
            Assert.True(sc.LastResponse!.Headers.GetValues("Set-Cookie").First(x => x.StartsWith("openvp_session=")).Contains("secure", StringComparison.OrdinalIgnoreCase), "the session cookie is Secure on https");

            // WebSockets work over wss with the same permissions.
            using (var wss = new ClientWebSocket())
            {
                wss.Options.Cookies = sc.Jar;
                wss.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                wss.ConnectAsync(new Uri($"wss://127.0.0.1:{httpsPort}/ws/trace?slot=1"), CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(wss.State == WebSocketState.Open, "the debugger stream opens over wss for a signed-in user");
                wss.Abort();
            }
            using (var wss2 = new ClientWebSocket())
            {
                wss2.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                var denied = false;
                try { wss2.ConnectAsync(new Uri($"wss://127.0.0.1:{httpsPort}/ws/trace?slot=1"), CancellationToken.None).GetAwaiter().GetResult(); } catch (WebSocketException) { denied = true; }
                Assert.True(denied, "and is refused to a stranger");
            }

            // A replacement certificate is picked up by the next connection, no restart.
            var fp2 = b.Post("/api/https/self-signed").Json.GetProperty("fingerprint").GetString()!;
            Assert.True(fp1 != fp2, "a different certificate");
            Assert.Equal(fp2, PresentedFingerprint(httpsPort), "new connections get it at once");

            // An uploaded PFX works the same way.
            var (pfx, pw, thumb) = OpsTestServer.SelfSigned();
            using var form = new MultipartFormDataContent { { new ByteArrayContent(File.ReadAllBytes(pfx)), "file", "mine.pfx" }, { new StringContent(pw), "password" } };
            var up = b.Http.PostAsync(b.Url + "/api/https/upload", form).GetAwaiter().GetResult();
            Assert.True(up.IsSuccessStatusCode, "upload: " + up.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(thumb.ToUpperInvariant(), PresentedFingerprint(httpsPort).Replace(":", ""), "the uploaded certificate is served");
            using var bad = new MultipartFormDataContent { { new ByteArrayContent(File.ReadAllBytes(pfx)), "file", "mine.pfx" }, { new StringContent("wrong"), "password" } };
            Assert.Equal(400, (int)b.Http.PostAsync(b.Url + "/api/https/upload", bad).GetAwaiter().GetResult().StatusCode, "a wrong PFX password is refused");
            try { File.Delete(pfx); } catch { }
        }
        finally { try { a.App.StopAsync().GetAwaiter().GetResult(); } catch { } }
    }

    [Test]
    public static void Http_to_https_redirect_only_touches_pages_from_other_machines()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-redir").FullName;
        var settings = new SecuritySettings(dir);
        settings.Update(s => { s.Https.Enabled = true; s.Https.Redirect = true; });
        var mgr = new HttpsManager(dir, settings); mgr.MarkListening(8443);
        var mw = OpenVpServer.HttpsRedirect(settings, mgr);
        (bool Passed, int Status, string? Location) Run(string path, string ip = "192.168.1.5", string method = "GET", bool https = false, bool tunnel = false)
        {
            var c = new DefaultHttpContext();
            c.Connection.RemoteIpAddress = IPAddress.Parse(ip); c.Request.Host = new HostString("pc.lan", 8080); c.Request.Path = path; c.Request.Method = method; c.Request.IsHttps = https;
            if (tunnel) c.Items[TunnelGate.ItemKey] = true;
            var passed = false;
            mw(c, _ => { passed = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            return (passed, c.Response.StatusCode, c.Response.Headers.Location.ToString());
        }
        var r = Run("/index.html");
        Assert.True(!r.Passed && r.Status == 302 && r.Location == "https://pc.lan:8443/index.html", "a page from another machine is sent to https: " + r.Location);
        Assert.True(Run("/", "127.0.0.1").Passed, "the desktop app and this computer stay on http://localhost");
        foreach (var p in new[] { "/api/slots", "/ws/trace", "/hooks/abc", "/healthz", "/metrics", "/s/abc", "/.well-known/acme-challenge/x", "/signin-oidc" })
            Assert.True(Run(p).Passed, p + " is never redirected (clients that can't follow it, or would lose their key)");
        Assert.True(Run("/index.html", method: "POST").Passed && Run("/index.html", https: true).Passed && Run("/index.html", tunnel: true).Passed, "not POSTs, not https, not the tunnel");
        settings.Update(s => s.Https.Redirect = false);
        Assert.True(Run("/index.html").Passed, "off means off");
    }

    [Test]
    public static void The_acme_challenge_answers_only_while_a_request_is_running_and_the_domain_is_checked()
    {
        using var s = new OpsTestServer();
        using var anon = new SecClient(s);
        Assert.Equal(404, anon.Get("/.well-known/acme-challenge/abc").Status, "no challenge, no answer");
        s.App.Services.GetRequiredService<AcmeService>().Offer("abc", "abc.keyauthorization");
        var r = anon.Get("/.well-known/acme-challenge/abc");
        Assert.True(r.Status == 200 && r.Text == "abc.keyauthorization", "Let's Encrypt (no login) gets the key authorization: " + r.Text);
        Assert.Equal(404, anon.Get("/.well-known/acme-challenge/other").Status, "only the offered token");
        Assert.True(AcmeService.DomainProblem("openvp.example.com") == null && AcmeService.DomainProblem("192.168.1.5") != null && AcmeService.DomainProblem("pc.local") != null && AcmeService.DomainProblem("localhost") != null && AcmeService.DomainProblem("") != null, "only public host names are accepted");
        Assert.Equal(400, s.Post("/api/https/acme", new { domain = "192.168.1.5", email = "a@b.example" }).Status, "an address is refused before anything is asked of Let's Encrypt");
        Assert.Equal(400, s.Post("/api/https/acme", new { domain = "openvp.example.com", email = "not-an-email" }).Status, "so is a bad e-mail address");
        using var viewer = new SecClient(s);
        s.Post("/api/users", new { name = "v", password = "viewer-password-1", role = "viewer" }); viewer.Login("v", "viewer-password-1");
        Assert.Equal(403, viewer.Post("/api/https/acme", new { domain = "openvp.example.com", email = "a@b.example" }).Status, "and only an Admin may ask");
    }

    // ------------------------------------------------------------------ single sign-on

    /// <summary>A tiny OpenID Connect provider: discovery, JWKS, an authorize endpoint that signs the person in at once, and a token endpoint with PKCE and a client secret.</summary>
    private sealed class TestIdp : IDisposable
    {
        public readonly string Url; public readonly WebApplication App; private readonly RSA _rsa = RSA.Create(2048);
        public string User = "sam", Email = "sam@example.com", Name = "Sam Lee"; public string[] Roles = ["av-team"];
        public const string ClientId = "openvp-test", ClientSecret = "s3cret-value";
        private readonly Dictionary<string, (string Nonce, string Challenge, string User, string Email, string Name, string[] Roles)> _codes = new();
        public int TokenCalls;

        public TestIdp()
        {
            Url = "http://127.0.0.1:" + IntegrationServiceTests.FreePort();
            var b = WebApplication.CreateBuilder(); b.Logging.ClearProviders(); b.WebHost.UseUrls(Url);
            App = b.Build();
            var key = new RsaSecurityKey(_rsa) { KeyId = "k1" };
            App.MapGet("/.well-known/openid-configuration", () => Results.Json(new
            {
                issuer = Url, authorization_endpoint = Url + "/authorize", token_endpoint = Url + "/token", jwks_uri = Url + "/jwks", response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" }, id_token_signing_alg_values_supported = new[] { "RS256" }, scopes_supported = new[] { "openid", "profile", "email" },
                code_challenge_methods_supported = new[] { "S256" }, token_endpoint_auth_methods_supported = new[] { "client_secret_post" }
            }));
            App.MapGet("/jwks", () =>
            {
                var p = _rsa.ExportParameters(false);
                return Results.Json(new { keys = new[] { new { kty = "RSA", kid = "k1", use = "sig", alg = "RS256", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } } });
            });
            App.MapGet("/authorize", (HttpContext c) =>
            {
                var q = c.Request.Query;
                var code = Guid.NewGuid().ToString("N");
                _codes[code] = (q["nonce"].ToString(), q["code_challenge"].ToString(), User, Email, Name, Roles);
                return Results.Redirect($"{q["redirect_uri"]}?code={code}&state={Uri.EscapeDataString(q["state"].ToString())}");
            });
            App.MapPost("/token", (HttpContext c) =>
            {
                TokenCalls++;
                var f = c.Request.Form;
                if (f["client_id"] != ClientId || f["client_secret"] != ClientSecret || !_codes.Remove(f["code"].ToString(), out var grant)) return Results.Json(new { error = "invalid_client" }, statusCode: 400);
                var verifier = f["code_verifier"].ToString();
                if (Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != grant.Challenge) return Results.Json(new { error = "invalid_grant", error_description = "PKCE failed" }, statusCode: 400);
                var desc = new SecurityTokenDescriptor
                {
                    Issuer = Url, Audience = ClientId, IssuedAt = DateTime.UtcNow, NotBefore = DateTime.UtcNow.AddMinutes(-1), Expires = DateTime.UtcNow.AddHours(1),
                    Claims = new Dictionary<string, object> { ["sub"] = "id-" + grant.User, ["nonce"] = grant.Nonce, ["preferred_username"] = grant.User, ["email"] = grant.Email, ["name"] = grant.Name, ["roles"] = grant.Roles },
                    SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
                };
                return Results.Json(new { access_token = "unused", token_type = "Bearer", expires_in = 3600, id_token = new JsonWebTokenHandler().CreateToken(desc) });
            });
            App.StartAsync().GetAwaiter().GetResult();
        }
        public void Dispose() { try { App.StopAsync().GetAwaiter().GetResult(); } catch { } _rsa.Dispose(); }
    }

    /// <summary>Follows redirects by hand, keeping cookies, until the response is no longer a redirect or goes back to OpenVP's own page.</summary>
    private static (int Status, string Final) Follow(SecClient c, string startUrl, int max = 12)
    {
        var url = startUrl;
        for (var i = 0; i < max; i++)
        {
            var r = c.H.GetAsync(url).GetAwaiter().GetResult();
            if ((int)r.StatusCode is >= 300 and < 400 && r.Headers.Location != null)
            {
                url = new Uri(new Uri(url), r.Headers.Location).ToString();
                continue;
            }
            return ((int)r.StatusCode, url);
        }
        return (-1, url);
    }

    private static void ConfigureSso(OpsTestServer s, TestIdp idp, object? extra = null)
    {
        Assert.Equal(200, s.Put("/api/oidc", new
        {
            enabled = true, authority = idp.Url, clientId = TestIdp.ClientId, clientSecret = TestIdp.ClientSecret, roleClaim = "roles", userClaim = "preferred_username", allowedDomains = new[] { "example.com" },
            mappings = new[] { new { value = "av-admins", role = "admin" }, new { value = "av-team", role = "programmer" } }
        }).Status, "configure single sign-on");
    }

    [Test]
    public static void Single_sign_on_signs_a_person_in_through_a_provider_and_maps_their_role()
    {
        using var idp = new TestIdp();
        using var s = new OpsTestServer(true, more: "--OpenVP:OidcAllowHttp=true");
        Assert.Equal(400, s.Put("/api/oidc", new { enabled = true, authority = idp.Url, clientId = "x" }).Status, "needs a local Admin first, so the provider isn't the only way in");
        Assert.Equal(200, s.Post("/api/users", new { name = "root", password = "root-password-1", role = "admin" }).Status, "a local Admin");
        using var anon = new SecClient(s);
        var pub = anon.Get("/api/info").Json.GetProperty("oidc");
        Assert.True(!pub.GetProperty("enabled").GetBoolean(), "off by default");
        Assert.Equal(404, anon.Get("/api/login/oidc").Status, "so the start address says it isn't set up");
        ConfigureSso(s, idp);
        var cfg = s.Get("/api/oidc").Json;
        Assert.True(cfg.GetProperty("hasSecret").GetBoolean() && !s.Get("/api/oidc").Text.Contains(TestIdp.ClientSecret), "the client secret is stored and never returned");
        Assert.True(anon.Get("/api/info").Json.GetProperty("oidc").GetProperty("enabled").GetBoolean(), "the login page can now show the button");

        // 1. a person in the programmer group
        var res = Follow(anon, s.Url + "/api/login/oidc?next=/security.html");
        Assert.True(res.Status == 200 && res.Final.EndsWith("/security.html"), "the flow ends on the page asked for: " + res);
        var me = anon.Get("/api/info").Json.GetProperty("me");
        Assert.True(me.GetProperty("name").GetString() == "sam" && me.GetProperty("role").GetString() == "programmer" && me.GetProperty("canChangePassword").ValueKind == JsonValueKind.False, "signed in as the provider's user with the mapped role: " + me);
        Assert.Equal(200, anon.Post("/api/slots/1/restart").Status is 200 or 422 ? 200 : 0, "and may do what a Programmer may");
        Assert.Equal(403, anon.Get("/api/users").Status, "but not what an Admin may");
        var users = s.Get("/api/users").Json.EnumerateArray().First(u => u.GetProperty("name").GetString() == "sam");
        Assert.Equal("oidc", users.GetProperty("source").GetString(), "the user is listed, marked as single sign-on");
        Assert.True(s.Get("/api/audit?action=login&limit=20").Text.Contains("single sign-on"), "the sign-in is audited");

        // 2. role follows the provider at the next sign-in
        idp.Roles = ["av-admins"];
        using var again = new SecClient(s);
        Follow(again, s.Url + "/api/login/oidc");
        Assert.Equal("admin", again.Get("/api/info").Json.GetProperty("me").GetProperty("role").GetString(), "the role is re-read at every sign-in");

        // 3. a person with no mapped group, and one from the wrong domain, are refused and get no session
        idp.User = "stranger"; idp.Email = "x@example.com"; idp.Roles = ["nothing"];
        using var refused = new SecClient(s);
        var r3 = Follow(refused, s.Url + "/api/login/oidc");
        Assert.True(r3.Final.Contains("/login.html") && Uri.UnescapeDataString(r3.Final).Contains("group"), "sent back to the login page with the reason: " + r3.Final);
        Assert.Equal(401, refused.Get("/api/slots").Status, "no session");
        idp.Roles = ["av-admins"]; idp.Email = "x@evil.test";
        using var refused2 = new SecClient(s);
        Follow(refused2, s.Url + "/api/login/oidc");
        Assert.Equal(401, refused2.Get("/api/slots").Status, "a domain that isn't allowed gets nothing, whatever its groups");
        Assert.True(s.Get("/api/audit?action=login.failed&limit=20").Text.Contains("refused"), "refusals are audited");

        // 4. a forged callback (no state/correlation) gets nothing
        using var forged = new SecClient(s);
        var f = forged.H.GetAsync(s.Url + "/signin-oidc?code=abc&state=zzz").GetAwaiter().GetResult();
        Assert.True(forged.Get("/api/slots").Status == 401, "a made-up callback signs nobody in (" + (int)f.StatusCode + ")");

        // 5. local users and the access key still work beside it
        using var local = new SecClient(s); Assert.Equal(200, local.Login("root", "root-password-1"), "a local user still signs in");
        Assert.Equal(200, s.Get("/api/slots").Status, "and the access key");

        // 6. turning it off closes the door at once
        s.Put("/api/oidc", new { enabled = false });
        Assert.Equal(404, anon.Get("/api/login/oidc").Status, "off again");
    }

    // ------------------------------------------------------------------ telnet / SSH console

    [Test]
    public static void The_console_asks_for_a_user_once_users_exist_and_limits_each_role()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-con").FullName;
        using var p = new Processor(dir, 3);
        var users = new UserStore(dir); var locks = new SlotLocks(dir); var audit = new AuditLog(dir);
        var auth = new UserConsoleAuth(() => "the-key", users, locks, audit);
        Assert.True(!auth.RequiresUsername, "no users: no user name prompt, the key is the password, as before");
        Assert.True(auth.Authenticate(null, "the-key", "10.0.0.1").Ok && !auth.Authenticate("x", "wrong", "10.0.0.1").Ok, "the key works and a wrong password doesn't");
        users.Create("viewer", "viewer-password", Role.Viewer); users.Create("tech", "tech-password-1", Role.Technician); users.Create("prog", "prog-password-1", Role.Programmer, [2]); users.Create("root", "root-password-1", Role.Admin);
        Assert.True(auth.RequiresUsername, "users exist: it asks for a user name");
        var shell = new ConsoleShell(p);
        ConsoleSession Login(string user, string pw)
        {
            var r = auth.Authenticate(user, pw, "10.0.0.2");
            Assert.True(r.Ok, user + " logs in: " + r.Message);
            return new ConsoleSession { Remote = "10.0.0.2", User = r.User, Access = r.Access };
        }
        string Run(ConsoleSession s, string cmd) => shell.Execute(s, cmd).Output;
        var viewer = Login("viewer", "viewer-password"); var tech = Login("tech", "tech-password-1"); var prog = Login("prog", "prog-password-1");
        Assert.True(Run(viewer, "slots").Contains("1") && !Run(viewer, "slots").Contains("Access denied"), "a Viewer can look");
        Assert.True(Run(viewer, "stopprog -p:1").Contains("Access denied"), "but not stop");
        Assert.True(!Run(tech, "stopprog -p:1").Contains("Access denied"), "a Technician stops");
        Assert.True(Run(tech, "progload -p:1").Contains("Access denied"), "but doesn't load programs");
        Assert.True(Run(tech, "prog 1 help").Contains("Access denied"), "or run a program's own commands");
        Assert.True(Run(prog, "startprog -p:1").Contains("slot 1") && Run(prog, "startprog -p:1").Contains("Access denied"), "a Programmer limited to slot 2 can't touch slot 1: " + Run(prog, "startprog -p:1"));
        Assert.True(Run(prog, "stopprog").Contains("limited to some slots"), "and has to name a slot");
        Assert.True(!Run(prog, "stopprog -p:2").Contains("Access denied"), "but may use slot 2");
        var admin = Login("root", "root-password-1");
        Assert.True(!Run(admin, "stopprog -p:1").Contains("Access denied"), "an Admin may do everything");
        var viaKey = auth.Authenticate("anyone", "the-key", "10.0.0.3");
        Assert.True(viaKey.Ok && viaKey.Access == null, "the access key still works as the password, with full rights");
        locks.Lock(2, Principal.ForRole(Role.Admin, "root", "1.1.1.1"), "show", null);
        Assert.True(Run(prog, "stopprog -p:2").Contains("locked by root"), "a locked slot is refused in the console as well: " + Run(prog, "stopprog -p:2"));
        for (var i = 0; i < 5; i++) auth.Authenticate("viewer", "bad-password", "10.0.0.9");
        Assert.True(auth.Authenticate("viewer", "viewer-password", "10.0.0.9").Message!.Contains("Too many") || auth.Authenticate("viewer", "viewer-password", "10.0.0.9").Message!.Contains("locked"), "wrong passwords are limited");
        var log = audit.Query(new AuditFilter { Limit = 100 }).Select(e => e.Action).ToList();
        Assert.True(log.Contains("login") && log.Contains("login.failed"), "console logins are audited");
    }

    // ------------------------------------------------------------------ presence

    [Test]
    public static void Presence_shows_who_has_a_slot_open_and_warns_through_the_same_websocket()
    {
        var (s, clients) = (new OpsTestServer(), new Dictionary<string, SecClient>());
        using var _s = s;
        foreach (var (n, role) in new[] { ("sam", "programmer"), ("kim", "technician") })
        {
            s.Post("/api/users", new { name = n, password = n + "-password-1", role });
            var c = new SecClient(s); c.Login(n, n + "-password-1"); clients[n] = c;
        }
        try
        {
            async Task<(ClientWebSocket Ws, List<string> Frames)> Open(SecClient c, string path)
            {
                var ws = new ClientWebSocket(); ws.Options.Cookies = c.Jar;
                await ws.ConnectAsync(new Uri(s.Url.Replace("http", "ws") + path), CancellationToken.None);
                var frames = new List<string>();
                _ = Task.Run(async () =>
                {
                    var buf = new byte[65536];
                    try { while (ws.State == WebSocketState.Open) { var r = await ws.ReceiveAsync(buf, CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) break; lock (frames) frames.Add(Encoding.UTF8.GetString(buf, 0, r.Count)); } } catch { }
                });
                return (ws, frames);
            }
            var sam = Open(clients["sam"], "/ws/trace?slot=1").GetAwaiter().GetResult();      // the debugger
            Assert.Eventually(() => { lock (sam.Frames) return sam.Frames.Any(f => f.Contains("\"t\":\"presence\"")); }, "sam gets a presence frame on connect");
            var kim = Open(clients["kim"], "/ws/trace?slot=1&trace=0").GetAwaiter().GetResult(); // the console's log view
            Assert.Eventually(() => { lock (sam.Frames) return sam.Frames.Any(f => f.Contains("\"name\":\"kim\"") && f.Contains("\"kind\":\"viewing\"")); }, "sam is told kim is viewing");
            Assert.Eventually(() => { lock (kim.Frames) return kim.Frames.Any(f => f.Contains("\"name\":\"sam\"") && f.Contains("\"kind\":\"debugging\"") && f.Contains("\"me\":false")); }, "kim is told sam is debugging, and that sam isn't her");
            var p = clients["kim"].Get("/api/presence").Json.GetProperty("slots").GetProperty("1");
            Assert.True(p.GetArrayLength() == 2 && p.EnumerateArray().Any(e => e.GetProperty("name").GetString() == "sam" && e.GetProperty("kind").GetString() == "debugging" && e.GetProperty("initials").GetString() == "S"), "GET /api/presence lists both");
            var tile = clients["kim"].Get("/api/slots/1").Json.GetProperty("presence");
            Assert.True(tile.GetArrayLength() == 2 && tile.EnumerateArray().Any(e => e.GetProperty("name").GetString() == "sam" && !e.GetProperty("me").GetBoolean()), "the Status board tile carries it, marking who is you");
            sam.Ws.Abort();
            Assert.Eventually(() => clients["kim"].Get("/api/slots/1").Json.GetProperty("presence").GetArrayLength() == 1, "and sam leaves the list when the window closes");
            kim.Ws.Dispose(); sam.Ws.Dispose();
        }
        finally { foreach (var c in clients.Values) c.Dispose(); }
    }
}
