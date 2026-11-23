using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>The device REST endpoints against a real server in this process, and the console listeners' defaults.</summary>
public static class DeviceApiTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port;
    }

    [Test]
    public static void Simulator_terminal_redirect_remap_and_discovery_endpoints_work_on_a_real_server()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-dev-e2e").FullName;
        int port = FreePort();
        var app = OpenVpServer.Create(new[]
        {
            $"--urls=http://127.0.0.1:{port}", "--OpenVP:TunnelPort=0", $"--OpenVP:DataDirectory={dir}", "--OpenVP:CipEnabled=false",
            "--OpenVP:UnsupportedLog=off", "--OpenVP:Slots=2", "--OpenVP:Mdns=false",
        });
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            var processor = app.Services.GetRequiredService<Processor>();
            processor.AddLoader(new LpzLoader());
            processor.LoadProgram(1, LpzPortTests.Build());
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            JsonElement Send(HttpMethod m, string url, object? body = null, HttpStatusCode? expect = null)
            {
                var req = new HttpRequestMessage(m, url);
                if (body != null) req.Content = JsonContent.Create(body);
                var r = http.Send(req);
                var text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (expect != null) Assert.Equal(expect.Value, r.StatusCode, $"{m} {url}: {text}");
                else Assert.True(r.IsSuccessStatusCode, $"{m} {url} -> {(int)r.StatusCode}: {text}");
                return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
            }

            // Simulator from a profile, attached to the program's COM port.
            var list = Send(HttpMethod.Get, "/api/slots/1/simulators");
            Assert.Equal(4, list.GetProperty("profiles").GetArrayLength(), "four generic profiles");
            Assert.True(list.GetProperty("ports").EnumerateArray().Any(p => p.GetString() == "IP-ID-11/Slot-08/COM-01"), "the program's COM port is offered");
            var sim = Send(HttpMethod.Post, "/api/slots/1/simulators", new { profile = "projector", name = "Lobby projector", kind = "com", com = "IP-ID-11/Slot-08/COM-01" });
            var id = sim.GetProperty("id").GetString()!;
            Assert.True(sim.GetProperty("comActive").GetBoolean(), "attached to the open port");
            Send(HttpMethod.Post, "/api/slots/1/simulators", new { profile = "nope" }, HttpStatusCode.BadRequest);
            Send(HttpMethod.Post, "/api/slots/1/simulators", new { name = "x", rules = new[] { new { match = "(", regex = true } } }, HttpStatusCode.BadRequest);

            // The program sends; the reply and both directions show in the terminal feed.
            processor.Slot(1).Runtime!.TrySetSignal("#13", "PWR ON\r");
            Assert.Eventually(() => Send(HttpMethod.Get, "/api/slots/1/ports/traffic?key=IP-ID-11/Slot-08/COM-01").GetProperty("entries").GetArrayLength() >= 2, "TX and RX are listed");
            var traffic = Send(HttpMethod.Get, "/api/slots/1/ports/traffic?key=COM-01");
            var entries = traffic.GetProperty("entries").EnumerateArray().ToList();
            Assert.True(entries.Any(e => e.GetProperty("dir").GetString() == "tx" && e.GetProperty("text").GetString() == "PWR ON\r"), "tx entry");
            Assert.True(entries.Any(e => e.GetProperty("dir").GetString() == "rx" && e.GetProperty("origin").GetString() == "simulator"), "rx from the simulator");
            var after = traffic.GetProperty("last").GetInt64();
            Assert.Equal(0, Send(HttpMethod.Get, $"/api/slots/1/ports/traffic?key=COM-01&after={after}").GetProperty("entries").GetArrayLength(), "'after' returns only newer entries");
            var sims = Send(HttpMethod.Get, "/api/slots/1/simulators").GetProperty("simulators")[0];
            Assert.Equal("1", sims.GetProperty("state").GetProperty("power").GetString()!, "state is live");

            // The terminal gives the program RX, with escapes.
            Send(HttpMethod.Post, "/api/slots/1/ports/send", new { key = "COM-01", text = "hello\\x0D" });
            var panel = processor.Slot(1).FindDevice(3)!;
            Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 1)) == "hello\r", "typed RX arrives with the escape decoded");
            Send(HttpMethod.Post, "/api/slots/1/ports/send", new { key = "COM-09", text = "x" }, HttpStatusCode.NotFound);

            // Try a message on a copy.
            var tried = Send(HttpMethod.Post, $"/api/slots/1/simulators/{id}/try", new { text = "PWR?\\r" });
            Assert.Equal("PWR=1\\r", tried.GetProperty("replies")[0].GetProperty("text").GetString()!, "the copy keeps the current state");
            Send(HttpMethod.Post, $"/api/slots/1/simulators/{id}/state", new { key = "lamp", value = "99" });
            Send(HttpMethod.Put, $"/api/slots/1/simulators/{id}", new { enabled = false });
            Assert.True(!Send(HttpMethod.Get, "/api/slots/1/simulators").GetProperty("simulators")[0].GetProperty("enabled").GetBoolean(), "switched off");
            Send(HttpMethod.Delete, $"/api/slots/1/simulators/{id}");
            Send(HttpMethod.Delete, $"/api/slots/1/simulators/{id}", null, HttpStatusCode.NotFound);

            // A TCP simulator and a redirect to it.
            var tcp = Send(HttpMethod.Post, "/api/slots/1/simulators", new { profile = "projector", kind = "tcp", name = "TCP projector" });
            Assert.True(tcp.GetProperty("listening").GetBoolean(), "tcp simulator listens: " + tcp);
            var red = Send(HttpMethod.Post, "/api/slots/1/redirects", new { from = "192.168.1.50:23", to = "sim:" + tcp.GetProperty("id").GetString() });
            Send(HttpMethod.Post, "/api/slots/1/redirects", new { from = "", to = "x:1" }, HttpStatusCode.BadRequest);
            var go = processor.Slot(1).Io.Redirects.Resolve("192.168.1.50", 23);
            Assert.True(go.Redirected && go.Port == tcp.GetProperty("listenPort").GetInt32(), "the redirect points at the simulator's port");
            Send(HttpMethod.Delete, $"/api/slots/1/redirects/{red.GetProperty("id").GetString()}");

            // The socket table is listed (and starts being watched).
            var sock = processor.Slot(1).Io.Sockets.Open("TCPClient", "client", "tcp", "", "192.168.1.50:23");
            sock.Connected("127.0.0.1:5000");
            sock.Sent(Encoding.ASCII.GetBytes("PWR ON\r"), 0, 7);
            var socks = Send(HttpMethod.Get, "/api/slots/1/sockets");
            Assert.Equal(1, socks.GetArrayLength(), "one socket");
            Assert.Equal(7L, socks[0].GetProperty("bytesOut").GetInt64(), "bytes out");
            Assert.True(processor.Slot(1).Io.Sockets.Watched, "polling marks it watched");
            var st = Send(HttpMethod.Get, $"/api/slots/1/sockets/{sock.Id}/traffic");
            Assert.Equal("PWR ON\r", st.GetProperty("entries")[0].GetProperty("text").GetString()!, "socket traffic");

            // IP ID remap.
            Send(HttpMethod.Put, "/api/slots/1/ipremap", new { program = "03", @public = "0A" });
            Send(HttpMethod.Put, "/api/slots/1/ipremap", new { program = "03", @public = "ZZ" }, HttpStatusCode.BadRequest);
            var slots = Send(HttpMethod.Get, "/api/slots");
            var dev = slots[0].GetProperty("devices").EnumerateArray().First(d => d.GetProperty("ipId").GetString() == "03");
            Assert.True(dev.GetProperty("publicIpId").GetString() == "0A" && dev.GetProperty("remapped").GetBoolean(), "IP table shows the remap");
            Send(HttpMethod.Delete, "/api/slots/1/ipremap/03");
            Send(HttpMethod.Delete, "/api/slots/1/ipremap/03", null, HttpStatusCode.NotFound);

            // Discovery starts, reports, and ends.
            Send(HttpMethod.Post, "/api/discovery");
            Assert.Eventually(() => !Send(HttpMethod.Get, "/api/discovery").GetProperty("running").GetBoolean(), "scan ends", 40000);
            var consoles = Send(HttpMethod.Get, "/api/consoles");
            Assert.True(!consoles.GetProperty("telnet").GetProperty("enabled").GetBoolean() && !consoles.GetProperty("ssh").GetProperty("enabled").GetBoolean(), "telnet and SSH are off by default");
            Assert.True(http.GetAsync("/api/slots/1/simulators").Result.IsSuccessStatusCode, "local requests need no key");
        }
        finally { app.StopAsync().GetAwaiter().GetResult(); }
    }

    [Test]
    public static void Telnet_and_ssh_listeners_are_off_unless_enabled_and_need_the_key()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-con-e2e").FullName;
        int port = FreePort(), tel = FreePort();
        var app = OpenVpServer.Create(new[]
        {
            $"--urls=http://127.0.0.1:{port}", "--OpenVP:TunnelPort=0", $"--OpenVP:DataDirectory={dir}", "--OpenVP:CipEnabled=false",
            "--OpenVP:UnsupportedLog=off", "--OpenVP:Slots=2", "--OpenVP:Mdns=false", "--OpenVP:ApiKey=right-key", "--OpenVP:TelnetEnabled=true", $"--OpenVP:TelnetPort={tel}",
        });
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            using var c = new TcpClient("127.0.0.1", tel);
            var s = c.GetStream();
            s.ReadTimeout = 3000;
            var sb = new StringBuilder(); var buf = new byte[512];
            var end = Environment.TickCount64 + 3000;
            while (!sb.ToString().Contains("Password:") && Environment.TickCount64 < end) { var n = s.Read(buf, 0, buf.Length); sb.Append(Encoding.Latin1.GetString(buf, 0, n)); }
            s.Write(Encoding.ASCII.GetBytes("right-key\r\nver\r\n"));
            sb.Clear();
            end = Environment.TickCount64 + 4000;
            while (!sb.ToString().Contains("Slots: 2") && Environment.TickCount64 < end) { try { var n = s.Read(buf, 0, buf.Length); sb.Append(Encoding.Latin1.GetString(buf, 0, n)); } catch (IOException) { } }
            Assert.True(sb.ToString().Contains("OpenVP virtual control processor"), "the access key is the password: " + sb);
        }
        finally { app.StopAsync().GetAwaiter().GetResult(); }
    }
}
