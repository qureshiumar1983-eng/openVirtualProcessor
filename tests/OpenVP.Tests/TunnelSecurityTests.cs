using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>
/// The internet tunnel (cloudflared) points at a second listener, and everything arriving there must look
/// remote and see only /s/... The first tests check the pieces; the last starts the real server with Kestrel on
/// two ports and checks it end to end.
/// </summary>
public static class TunnelSecurityTests
{
    private static DefaultHttpContext Ctx(string path = "/api/slots", string host = "localhost", string ip = "127.0.0.1", int localPort = 8099, params (string, string)[] headers)
    {
        var c = new DefaultHttpContext();
        c.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        c.Connection.LocalPort = localPort;
        c.Request.Host = new HostString(host);
        c.Request.Path = path;
        foreach (var (k, v) in headers) c.Request.Headers[k] = v;
        return c;
    }

    [Test]
    public static void Plain_local_request_is_still_local()
    {
        Assert.True(ApiAuth.IsDirectLocal(Ctx()), "loopback + localhost");
        Assert.True(ApiAuth.IsDirectLocal(Ctx(host: "127.0.0.1:8080")), "loopback + 127.0.0.1");
        Assert.True(ApiAuth.IsDirectLocal(Ctx(ip: "::1", host: "[::1]:8080")), "ipv6 loopback");
    }

    [Test]
    public static void Proxy_headers_make_a_request_not_local()
    {
        foreach (var h in new[] { "X-Forwarded-For", "Forwarded", "X-Real-IP", "Cf-Connecting-IP", "Cf-Ray", "CF-Visitor", "cf-connecting-ip", "CF-RAY" })
            Assert.True(!ApiAuth.IsDirectLocal(Ctx(headers: (h, "1.2.3.4"))), $"{h} means a proxy is in the way");
        Assert.True(!ApiAuth.IsDirectLocal(Ctx(host: "evil.example")), "other host name (DNS rebinding)");
        Assert.True(!ApiAuth.IsDirectLocal(Ctx(ip: "192.168.1.9")), "other machine");
    }

    [Test]
    public static void A_tunnel_request_is_never_local_whatever_its_address_or_host()
    {
        var gate = new TunnelGate(8085);
        var c = Ctx(path: "/s/abc", host: "localhost", ip: "127.0.0.1", localPort: 8085);
        var passed = false;
        gate.Handle(c, _ => { passed = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
        Assert.True(passed, "/s/ is let through");
        Assert.True(c.Items.ContainsKey(TunnelGate.ItemKey), "and marked as a tunnel request");
        Assert.True(!ApiAuth.IsDirectLocal(c), "from 127.0.0.1 with Host: localhost, yet not local");

        var auth = new ApiAuth("secret-key", localRequiresKey: false, panelsRequireKey: false);
        Assert.True(!auth.IsAuthenticated(c), "so it does not get the keyless local pass");
        Assert.True(auth.IsAuthenticated(Ctx()), "(a normal local request on the main port still does)");
        var ipv6 = Ctx(path: "/s/abc", host: "[::1]", ip: "::1", localPort: 8085);
        gate.Handle(ipv6, _ => Task.CompletedTask).GetAwaiter().GetResult();
        Assert.True(!ApiAuth.IsDirectLocal(ipv6), "ipv6 loopback too");
    }

    [Test]
    public static void The_gate_serves_only_share_paths_on_the_tunnel_port()
    {
        var gate = new TunnelGate(8085);
        string[] shut =
        {
            "/", "/index.html", "/api/slots", "/api/info", "/api/shares", "/api/tunnel", "/api/login", "/ws/trace", "/ws/metrics", "/ws/panel",
            "/debugger.html", "/paths.html", "/help.html", "/metrics.html", "/panel.html", "/openvp-crcomlib.js", "/qrcode.js", "/share/pin.html",
            "/s", "/s/", "/sx", "/settings", "/apis/slots", "/ws"
        };
        foreach (var p in shut)
        {
            var c = Ctx(path: p, localPort: 8085);
            var passed = false;
            gate.Handle(c, _ => { passed = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Assert.True(!passed && c.Response.StatusCode == 404, $"{p} is 404 on the tunnel port (got {c.Response.StatusCode}, passed={passed})");
        }
        foreach (var p in new[] { "/s/token", "/s/token/ws", "/s/token/app/index.html", "/S/token", "/s/token/status" })
        {
            var passed = false;
            gate.Handle(Ctx(path: p, localPort: 8085), _ => { passed = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Assert.True(passed, $"{p} is served on the tunnel port");
        }
        // The same paths on the console's own port are not touched by the gate.
        var main = Ctx(path: "/api/slots", localPort: 8099);
        var through = false;
        gate.Handle(main, _ => { through = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
        Assert.True(through && !main.Items.ContainsKey(TunnelGate.ItemKey), "console port unaffected");
        Assert.True(!new TunnelGate(0).IsTunnelRequest(Ctx(localPort: 0)), "port 0 means no tunnel");
    }

    // ------------------------------------------------------------------ the real thing

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static string? RepoWwwroot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "OpenVP.sln"))) return Path.Combine(d.FullName, "src", "OpenVP.Server", "wwwroot");
        return null;
    }

    [Test]
    public static void Real_server_tunnel_port_serves_only_shared_panels()
    {
        var www = RepoWwwroot();
        if (www == null || !Directory.Exists(www)) return;   // not run from a source checkout
        var dir = Directory.CreateTempSubdirectory("openvp-tunnel-e2e").FullName;
        int main = FreePort(), tun = FreePort();
        var app = OpenVpServer.Create(new[]
        {
            $"--urls=http://127.0.0.1:{main}", $"--OpenVP:TunnelPort={tun}", $"--OpenVP:DataDirectory={dir}",
            "--OpenVP:CipEnabled=false", "--OpenVP:UnsupportedLog=off", $"--webroot={www}", "--OpenVP:Slots=2",
        });
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            var key = File.ReadAllText(Path.Combine(dir, "apikey.txt")).Trim();
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
            int Get(int port, string path, string? host = null, string? bearer = null, (string, string)[]? headers = null)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{path}");
                if (host != null) req.Headers.Host = host;
                if (bearer != null) req.Headers.Authorization = new("Bearer", bearer);
                foreach (var (k, v) in headers ?? Array.Empty<(string, string)>()) req.Headers.TryAddWithoutValidation(k, v);
                return (int)http.Send(req).StatusCode;
            }

            // The console's own port: localhost needs no key, a proxy header or another host name does.
            Assert.Equal(200, Get(main, "/api/slots", host: "localhost"), "main port: local request is fine");
            Assert.Equal(401, Get(main, "/api/slots", host: "localhost", headers: new[] { ("Cf-Connecting-IP", "9.9.9.9") }), "main port: Cloudflare header means not local");
            Assert.Equal(401, Get(main, "/api/slots", host: "localhost", headers: new[] { ("Cf-Ray", "abc") }), "main port: Cf-Ray");
            Assert.Equal(401, Get(main, "/api/slots", host: "example.com"), "main port: other host name");
            Assert.Equal(200, Get(main, "/api/slots", host: "example.com", bearer: key), "main port: with the key");
            Assert.Equal(200, Get(main, "/index.html"), "main port: console page");

            // The tunnel port: nothing but /s/..., no matter the Host header or the key.
            foreach (var host in new string?[] { null, "localhost", "127.0.0.1", "[::1]", "abc.trycloudflare.com" })
                foreach (var path in new[] { "/", "/index.html", "/api/slots", "/api/info", "/api/shares", "/api/tunnel", "/api/network", "/ws/trace", "/ws/metrics", "/ws/panel?ipid=03",
                                             "/debugger.html", "/paths.html", "/help.html", "/metrics.html", "/panel.html", "/openvp-crcomlib.js", "/qrcode.js", "/share/pin.html", "/apikey.txt" })
                    Assert.Equal(404, Get(tun, path, host: host), $"tunnel port, Host {host ?? "(default)"}: {path} is 404");
            Assert.Equal(404, Get(tun, "/api/slots", host: "localhost", bearer: key), "tunnel port: 404 even with the right key");
            Assert.Equal(404, Get(tun, "/api/slots", host: "localhost", headers: new[] { ("X-OpenVP-Key", key) }), "tunnel port: 404 even with the key header");
            Assert.Equal(404, Get(tun, "/%2e%2e/api/slots"), "tunnel port: encoded dot segments");
            Assert.Equal(404, Get(tun, "/s/../api/slots"), "tunnel port: dot segments out of /s/");
            Assert.Equal(404, Get(tun, "/s"), "tunnel port: /s alone");

            // A share works on both ports, and a dead link is a 410.
            var create = http.PostAsync($"http://127.0.0.1:{main}/api/shares", new StringContent("{\"slot\":1,\"ipId\":\"03\",\"label\":\"t\",\"expires\":\"1h\"}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            var json = System.Text.Json.JsonDocument.Parse(create.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
            string token = json.GetProperty("token").GetString()!, id = json.GetProperty("id").GetString()!;
            Assert.Equal(200, Get(tun, "/s/" + token, host: "localhost"), "tunnel port: the share page");
            Assert.Equal(200, Get(tun, $"/s/{token}/openvp-crcomlib.js"), "tunnel port: the shim, from the link's own address");
            Assert.Equal(200, Get(tun, $"/s/{token}/status"), "tunnel port: status");
            Assert.Equal(410, Get(tun, "/s/not-a-real-token"), "tunnel port: unknown link");

            // Only the share's own WebSocket can be opened through the tunnel.
            async Task<string> WsTry(int port, string path)
            {
                using var ws = new ClientWebSocket();
                try { await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}{path}"), CancellationToken.None); }
                catch (WebSocketException e) { return "refused: " + e.Message; }
                var buf = new byte[1024];
                var r = await ws.ReceiveAsync(buf, CancellationToken.None);
                var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch (WebSocketException) { }
                return text;
            }
            var hello = WsTry(tun, $"/s/{token}/ws?ipid=0A&slot=2").GetAwaiter().GetResult();
            Assert.True(hello.Contains("\"hello\"") && hello.Contains("\"ipid\":3") && hello.Contains("\"slot\":1"), "tunnel ws: share's own slot and IP ID, query ignored: " + hello);
            Assert.True(WsTry(tun, "/ws/panel?ipid=03&slot=1").GetAwaiter().GetResult().StartsWith("refused"), "tunnel ws: /ws/panel refused");
            Assert.True(WsTry(tun, "/ws/trace").GetAwaiter().GetResult().StartsWith("refused"), "tunnel ws: /ws/trace refused");
            Assert.True(WsTry(tun, "/ws/metrics").GetAwaiter().GetResult().StartsWith("refused"), "tunnel ws: /ws/metrics refused");

            // Revoke.
            var del = http.DeleteAsync($"http://127.0.0.1:{main}/api/shares/{id}").GetAwaiter().GetResult();
            Assert.Equal(200, (int)del.StatusCode, "revoked");
            Assert.Equal(410, Get(tun, "/s/" + token), "revoked link on the tunnel port");
            Assert.Equal(410, Get(main, "/s/" + token), "revoked link on the console port");
        }
        finally
        {
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Test]
    public static void Tunnel_port_equal_to_the_console_port_is_refused()
    {
        Assert.True(OpenVpServer.CheckTunnelPort("http://0.0.0.0:8080", 8080) != null, "same port");
        Assert.True(OpenVpServer.CheckTunnelPort("http://*:8080;https://localhost:9000", 9000) != null, "second url");
        Assert.True(OpenVpServer.CheckTunnelPort("http://+:8080", 8080) != null, "plus");
        var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        try { Assert.True(OpenVpServer.CheckTunnelPort("http://0.0.0.0:8080", ((IPEndPoint)busy.LocalEndpoint).Port) != null, "port already in use"); }
        finally { busy.Stop(); }
        Assert.True(OpenVpServer.CheckTunnelPort("http://0.0.0.0:8080", FreePort()) == null, "free port is fine");
    }
}
