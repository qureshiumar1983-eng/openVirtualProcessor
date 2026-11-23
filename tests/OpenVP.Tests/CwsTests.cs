using System.Net;
using Microsoft.AspNetCore.Http;
using System.Text;
using Crestron.SimplSharp.WebScripting;
using OpenVP.Core;
using OpenVP.Server;
using OpenVP.SimplSharp;

namespace OpenVP.Tests;

/// <summary>A program's web-scripting (CWS) server, served by the console at /cws/{slot}/... behind the permission system.</summary>
public static class CwsTests
{
    private sealed class Fn(Action<HttpCwsContext> f) : IHttpCwsHandler { public void ProcessRequest(HttpCwsContext c) => f(c); }

    /// <summary>Runs the shim in this process as if it were slot n's program (the test starts the real sample so the slot is Running).</summary>
    private sealed class AsSlot : IDisposable
    {
        public readonly List<HttpCwsServer> Servers = new();
        public AsSlot(ProgramSlot slot) { OpenVPBridge.Slot = slot; }
        public HttpCwsServer Serve(string path, Action<HttpCwsServer> setup)
        {
            var s = new HttpCwsServer(path);
            setup(s);
            Assert.True(s.Register(), "registered");
            Servers.Add(s);
            return s;
        }
        public void Dispose() { foreach (var s in Servers) s.Dispose(); OpenVPBridge.Slot = null; }
    }

    private static HttpCwsServer Api(AsSlot a) => a.Serve("api", s =>
    {
        s.Routes.Add(new HttpCwsRoute("devices/{id}")
        {
            RouteHandler = new Fn(c =>
            {
                var id = c.Request.RouteData.Values["id"];
                c.Response.StatusCode = 200;
                c.Response.ContentType = "application/json";
                c.Response.AppendHeader("X-Test", "yes");
                c.Response.Write($"{{\"id\":\"{id}\",\"method\":\"{c.Request.HttpMethod}\",\"q\":\"{c.Request.QueryString["x"]}\",\"path\":\"{c.Request.Path}\"}}", true);
            })
        });
        s.Routes.Add(new HttpCwsRoute("echo")
        {
            RouteHandler = new Fn(c =>
            {
                using var r = new Crestron.SimplSharp.CrestronIO.StreamReader(c.Request.InputStream);
                c.Response.ContentType = "text/plain";
                c.Response.Write("got:" + r.ReadToEnd() + "|" + c.Request.ContentType, true);
            })
        });
        s.Routes.Add(new HttpCwsRoute("files/{*rest}") { RouteHandler = new Fn(c => c.Response.Write("rest=" + c.Request.RouteData.Values["rest"], true)) });
        s.Routes.Add(new HttpCwsRoute("maybe/{opt?}") { RouteHandler = new Fn(c => c.Response.Write("opt=" + (c.Request.RouteData.Values.TryGetValue("opt", out var o) ? o : "(none)"), true)) });
        s.Routes.Add(new HttpCwsRoute("boom") { RouteHandler = new Fn(_ => throw new InvalidOperationException("kaput")) });
        s.Routes.Add(new HttpCwsRoute("gone") { RouteHandler = new Fn(c => { c.Response.StatusCode = 410; c.Response.StatusDescription = "Gone Fishing"; c.Response.Write("bye", true); }) });
        s.Routes.Add(new HttpCwsRoute("go") { RouteHandler = new Fn(c => c.Response.Redirect("/cws/1/api/echo")) });
    });

    private static OpsTestServer Server(bool localRequiresKey = true)
    {
        var s = new OpsTestServer(localRequiresKey);
        s.P.LoadProgram(1, Paths.SampleDll);
        Assert.Equal(SlotState.Running, s.P.Slot(1).State, "sample running");
        return s;
    }

    [Test]
    public static void Routes_params_methods_headers_and_bodies_work()
    {
        using var s = Server();
        using var a = new AsSlot(s.P.Slot(1));
        Api(a);
        var r = s.Get("/cws/1/api/devices/42?x=hello%20there");
        Assert.Equal(200, r.Status, "route matched: " + r.Text);
        Assert.Equal("42", r.Json.GetProperty("id").GetString(), "{id}");
        Assert.Equal("GET", r.Json.GetProperty("method").GetString(), "method");
        Assert.Equal("hello there", r.Json.GetProperty("q").GetString(), "query string");
        Assert.Equal("/cws/api/devices/42", r.Json.GetProperty("path").GetString(), "Request.Path looks like a processor's");
        var post = s.Post("/cws/1/api/devices/7");
        Assert.Equal("POST", post.Json.GetProperty("method").GetString(), "POST reaches the same route");

        var echo = s.Http.Send(new HttpRequestMessage(HttpMethod.Put, s.Url + "/cws/1/api/echo") { Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json") });
        Assert.Equal("got:{\"a\":1}|application/json; charset=utf-8", echo.Content.ReadAsStringAsync().Result, "the body and content type arrive");
        var dev = s.Http.Send(new HttpRequestMessage(HttpMethod.Get, s.Url + "/cws/1/api/devices/1"));
        Assert.Equal("yes", dev.Headers.GetValues("X-Test").Single(), "response header");
        Assert.True(dev.Content.Headers.ContentType!.MediaType == "application/json", "content type");

        Assert.Equal("rest=a/b/c.txt", s.Get("/cws/1/api/files/a/b/c.txt").Text, "{*rest} takes the remaining segments");
        Assert.Equal("opt=(none)", s.Get("/cws/1/api/maybe").Text, "optional segment absent");
        Assert.Equal("opt=zz", s.Get("/cws/1/api/maybe/zz").Text, "optional segment present");
        Assert.Equal("opt=1", s.Get("/cws/1/API/MAYBE/1").Text, "routes ignore case");

        var gone = s.Http.Send(new HttpRequestMessage(HttpMethod.Get, s.Url + "/cws/1/api/gone"));
        Assert.Equal(410, (int)gone.StatusCode, "status code");
        Assert.Equal("Gone Fishing", gone.ReasonPhrase, "status description");
        var go = s.Http.Send(new HttpRequestMessage(HttpMethod.Get, s.Url + "/cws/1/api/go"));
        Assert.Equal(302, (int)go.StatusCode, "redirect");
        Assert.Equal("/cws/1/api/echo", go.Headers.Location!.OriginalString, "redirect target");

        Assert.Equal(404, s.Get("/cws/1/api/nothing/here").Status, "an unknown route under the server is a 404");
        Assert.Equal(404, s.Get("/cws/1/other/x").Status, "a path no server serves is a 404");
        var boom = s.Get("/cws/1/api/boom");
        Assert.Equal(500, boom.Status, "a throwing handler is a 500");
        Assert.True(boom.Text.Contains("kaput"), "with its message");
    }

    [Test]
    public static void Server_level_handler_event_unregister_and_slot_states()
    {
        using var s = Server();
        using var a = new AsSlot(s.P.Slot(1));
        var root = a.Serve("", srv => srv.HttpRequestHandler = new Fn(c => c.Response.Write("root:" + c.Request.Path, true)));
        Assert.Equal("root:/cws/anything/x", s.Get("/cws/1/anything/x").Text, "a root server with a catch-all handler");
        Assert.Equal("root:/cws/", s.Get("/cws/1").Text, "the slot root");
        var ev = a.Serve("evt", srv => srv.ReceivedRequestEvent += (_, e) => e.Context.Response.Write("event", true));
        Assert.Equal("event", s.Get("/cws/1/evt/z").Text, "ReceivedRequestEvent without routes; the longest path wins over the root server");
        ev.Unregister();
        Assert.Equal("root:/cws/evt/z", s.Get("/cws/1/evt/z").Text, "after Unregister the root server answers again");
        root.Unregister();
        Assert.Equal(404, s.Get("/cws/1/evt/z").Status, "nothing registered");
        Assert.Equal(503, s.Get("/cws/2/x").Status, "a slot with no program isn't running");
        Assert.Equal(404, s.Get("/cws/99/x").Status, "no such slot");
        Assert.True(!s.P.Slot(1).Io.Cws.Any, "registry empty");
    }

    [Test]
    public static void Unloading_the_program_removes_its_servers_and_a_stopped_slot_answers_503()
    {
        using var s = Server();
        using var a = new AsSlot(s.P.Slot(1));
        Api(a);
        Assert.True(s.Get("/cws/1/api/devices/1").Status == 200, "served");
        Assert.True(s.Get("/api/slots/1").Json.GetProperty("cws").EnumerateArray().Select(x => x.GetString()).Contains("api"), "the slot lists its CWS paths");
        s.P.Slot(1).Stop();
        Assert.Equal(503, s.Get("/cws/1/api/devices/1").Status, "stopped slot");
        s.P.Slot(1).Unload();
        Assert.True(!s.P.Slot(1).Io.Cws.Any, "unloading clears the servers");
    }

    [Test]
    public static void Without_users_nothing_changes_the_key_still_gates_other_machines_and_this_computer_needs_none()
    {
        using var s = new OpsTestServer(localRequiresKey: false);
        s.P.LoadProgram(1, Paths.SampleDll);
        using var a = new AsSlot(s.P.Slot(1));
        Api(a);
        using var anon = new SecClient(s);
        Assert.Equal(200, anon.Get("/cws/1/api/devices/3").Status, "this computer, no login (the desktop app)");
        Assert.Equal(401, anon.Call(HttpMethod.Get, "/cws/1/api/devices/3", null, ("X-Forwarded-For", "1.2.3.4")).Status, "a proxied request is not local, so it needs the key");
        using var keyed = new SecClient(s, s.Key);
        Assert.Equal(200, keyed.Call(HttpMethod.Get, "/cws/1/api/devices/3", null, ("X-Forwarded-For", "1.2.3.4")).Status, "the key works");
    }

    [Test]
    public static void Permissions_signal_to_call_and_slot_public_setting_opens_it()
    {
        var s = new OpsTestServer(localRequiresKey: true);
        try
        {
            s.P.LoadProgram(1, Paths.SampleDll);
            s.P.LoadProgram(2, Paths.SampleDll);
            using var a = new AsSlot(s.P.Slot(1));
            Api(a);
            Assert.Equal(200, s.Post("/api/users", new { name = "view", password = "viewer-password-1", role = "viewer" }).Status, "viewer");
            Assert.Equal(200, s.Post("/api/users", new { name = "oper", password = "operator-password-1", role = "technician" }).Status, "technician");
            Assert.Equal(200, s.Post("/api/users", new { name = "room2", password = "room2-password-1", role = "technician", slots = new[] { 2 } }).Status, "slot-limited");
            using var anon = new SecClient(s);
            using var viewer = new SecClient(s); Assert.Equal(200, viewer.Login("view", "viewer-password-1"), "login");
            using var oper = new SecClient(s); Assert.Equal(200, oper.Login("oper", "operator-password-1"), "login");
            using var room2 = new SecClient(s); Assert.Equal(200, room2.Login("room2", "room2-password-1"), "login");

            Assert.Equal(401, anon.Get("/cws/1/api/devices/1").Status, "nobody is not signed in");
            Assert.Equal(403, viewer.Get("/cws/1/api/devices/1").Status, "a viewer can't call a program's web API (it is Signal)");
            Assert.Equal(200, oper.Get("/cws/1/api/devices/1").Status, "an operator can");
            Assert.Equal(403, room2.Get("/cws/1/api/devices/1").Status, "a user limited to slot 2 can't use slot 1's CWS");
            using var key = new SecClient(s, s.Key);
            Assert.Equal(200, key.Get("/cws/1/api/devices/1").Status, "the access key (Admin)");
            Assert.Equal(403, oper.Call(HttpMethod.Get, "/cws/1/api/devices/1", null, ("Origin", "https://evil.example")).Status, "a page on another site can't drive it");

            // The slot's own switch: public means anyone, with no sign-in, and only for that slot.
            Assert.Equal(200, s.Put("/api/slots/1/settings", new { cwsPublic = true }).Status, "turn on");
            Assert.True(s.Get("/api/slots/1").Json.GetProperty("cwsPublic").GetBoolean(), "reported");
            Assert.Equal(200, anon.Get("/cws/1/api/devices/1").Status, "public: no sign-in");
            Assert.Equal(200, anon.Call(HttpMethod.Get, "/cws/1/api/devices/1", null, ("Origin", "https://other.example")).Status, "public: any site");
            Assert.Equal(401, anon.Get("/cws/2/x").Status, "slot 2 is still private");
            Assert.Equal(401, anon.Get("/api/slots").Status, "and the console API is untouched");
            Assert.Equal(200, s.Put("/api/slots/1/settings", new { cwsPublic = false }).Status, "turn off");
            Assert.Equal(401, anon.Get("/cws/1/api/devices/1").Status, "private again");
            Assert.Equal(403, viewer.Put("/api/slots/1/settings", new { cwsPublic = true }).Status, "a viewer can't open it");
        }
        finally { s.Dispose(); }
    }

    [Test]
    public static void Tunnel_port_never_serves_cws()
    {
        Assert.True(!TunnelGate.PathAllowed(new PathString("/cws/1/api/devices/1")), "the gate lets only /s paths through");
        Assert.True(!TunnelGate.PathAllowed(new PathString("/cws")), "not the bare path either");
        Assert.True(TunnelGate.PathAllowed(new PathString("/s/abc")), "shared panels still pass");
        // And a real server with a tunnel listener: the request on the tunnel port is a 404 even for a public slot.
        var www = Path.Combine(Paths.RepoRoot, "src", "OpenVP.Server", "wwwroot");
        var main = IntegrationServiceTests.FreePort();
        var tunnel = IntegrationServiceTests.FreePort();
        var dir = Directory.CreateTempSubdirectory("openvp-cws-tunnel").FullName;
        var app = OpenVpServer.Create([$"--urls=http://127.0.0.1:{main}", $"--OpenVP:TunnelPort={tunnel}", $"--OpenVP:DataDirectory={dir}", "--OpenVP:CipEnabled=false", "--OpenVP:UnsupportedLog=off", "--OpenVP:Slots=2", "--OpenVP:Mdns=false", "--OpenVP:ApiKey=off"]);
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            var p = app.Services.GetService(typeof(Processor)) as Processor;
            p!.LoadProgram(1, Paths.SampleDll);
            p.Slot(1).UpdateSettings(c => c with { CwsPublic = true });
            using var a = new AsSlot(p.Slot(1));
            Api(a);
            using var h = new HttpClient();
            Assert.Equal(200, (int)h.GetAsync($"http://127.0.0.1:{main}/cws/1/api/devices/1").Result.StatusCode, "the main port serves a public slot");
            foreach (var path in new[] { "/cws/1/api/devices/1", "/cws/1", "/cws/1/" })
                Assert.Equal(404, (int)h.GetAsync($"http://127.0.0.1:{tunnel}{path}").Result.StatusCode, "tunnel port: " + path);
        }
        finally { app.StopAsync().GetAwaiter().GetResult(); try { Directory.Delete(dir, true); } catch { } }
    }
}
