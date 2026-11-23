using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;
using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>The integration endpoints on a real server in this process: REST per signal and its stream, incoming hooks (and that the tunnel port refuses them),
/// /metrics and its access rules, and an MQTT round trip against MQTTnet's own in-process broker.</summary>
public static class IntegrationApiTests
{
    private static string? Www()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "OpenVP.sln"))) return Path.Combine(d.FullName, "src", "OpenVP.Server", "wwwroot");
        return null;
    }

    private sealed class Srv : IDisposable
    {
        public WebApplication App; public int Main, Tunnel; public string Dir, Key; public Processor P; public HttpClient Http;
        public Srv(params string[] extra)
        {
            Dir = Directory.CreateTempSubdirectory("openvp-int-e2e").FullName;
            Main = IntegrationServiceTests.FreePort(); Tunnel = IntegrationServiceTests.FreePort();
            var args = new List<string> { $"--urls=http://127.0.0.1:{Main}", $"--OpenVP:TunnelPort={Tunnel}", $"--OpenVP:DataDirectory={Dir}", "--OpenVP:CipEnabled=false", "--OpenVP:UnsupportedLog=off",
                "--OpenVP:Slots=2", "--OpenVP:Mdns=false" };
            if (Www() is { } w && Directory.Exists(w)) args.Add($"--webroot={w}");
            args.AddRange(extra);
            App = OpenVpServer.Create(args.ToArray());
            App.StartAsync().GetAwaiter().GetResult();
            Key = File.ReadAllText(Path.Combine(Dir, "apikey.txt")).Trim();
            P = App.Services.GetRequiredService<Processor>();
            P.LoadProgram(1, Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw"));
            Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{Main}"), Timeout = TimeSpan.FromSeconds(30) };
        }

        public HttpResponseMessage Raw(HttpMethod m, string path, string? body = null, int? port = null, string? host = null, bool key = false, string? contentType = "application/json")
        {
            var req = new HttpRequestMessage(m, $"http://127.0.0.1:{port ?? Main}{path}");
            if (host != null) req.Headers.Host = host;
            if (key) req.Headers.Authorization = new("Bearer", Key);
            if (body != null) req.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/json");
            return Http.Send(req);
        }
        public (int Status, JsonElement Json, string Text) Call(HttpMethod m, string path, object? body = null, int? port = null)
        {
            var r = Raw(m, path, body == null ? null : JsonSerializer.Serialize(body), port);
            var text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            JsonElement j = default; try { if (text.Length > 0) j = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
            return ((int)r.StatusCode, j, text);
        }
        public object Value(string name) => P.Slot(1).Runtime!.GetSignals().First(s => s.Name == name).Value;
        public void Dispose() { try { App.StopAsync().GetAwaiter().GetResult(); } catch { } Http.Dispose(); }
    }

    [Test]
    public static void Rest_per_signal_gets_sets_pulses_and_streams_changes()
    {
        using var s = new Srv();
        var (st, j, _) = s.Call(HttpMethod.Get, "/api/slots/1/signals/toggle_fb");
        Assert.Equal(200, st, "GET a signal"); Assert.Equal("digital", j.GetProperty("kind").GetString()!, "kind"); Assert.Equal(false, j.GetProperty("value").GetBoolean(), "value is a JSON bool");
        Assert.Equal("analog", s.Call(HttpMethod.Get, "/api/slots/1/signals/volume_level").Json.GetProperty("kind").GetString()!, "analog kind");
        Assert.Equal(404, s.Call(HttpMethod.Get, "/api/slots/1/signals/nope").Status, "unknown signal"); Assert.Equal(409, s.Call(HttpMethod.Get, "/api/slots/2/signals/x").Status, "empty slot"); Assert.Equal(404, s.Call(HttpMethod.Get, "/api/slots/9/signals/x").Status, "unknown slot");

        // PUT: JSON scalar, {"value":…}, and plain text.
        Assert.Equal(200, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/volume_level", "1234").StatusCode, "JSON number");
        Assert.Eventually(() => Convert.ToInt32(s.Value("volume_level")) == 1234, "analog set");
        Assert.Equal(200, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/volume_level", "{\"value\":\"100%\"}").StatusCode, "object with a percentage");
        Assert.Eventually(() => Convert.ToInt32(s.Value("volume_level")) == 65535, "100%");
        Assert.Equal(200, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/volume_level", "25%", contentType: "text/plain").StatusCode, "plain text");
        Assert.Eventually(() => Convert.ToInt32(s.Value("volume_level")) is 16383 or 16384, "25%");
        Assert.Equal(200, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/hello_text", "\"Hello curl\"").StatusCode, "JSON string");
        Assert.Eventually(() => (string)s.Value("hello_text") == "Hello curl", "serial set");
        Assert.Equal(200, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/toggle_press", "true").StatusCode, "digital true");
        Assert.Eventually(() => (bool)s.Value("toggle_fb"), "the press toggled the feedback");
        Assert.Equal(400, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/toggle_press", "banana").StatusCode, "bad value is 400");
        Assert.Equal(400, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/toggle_press", "").StatusCode, "no value is 400");
        Assert.Equal(404, (int)s.Raw(HttpMethod.Put, "/api/slots/1/signals/nope", "1").StatusCode, "unknown is 404");

        // POST pulse.
        s.Raw(HttpMethod.Put, "/api/slots/1/signals/toggle_press", "false");
        Assert.Equal(200, s.Call(HttpMethod.Post, "/api/slots/1/signals/toggle_press/pulse").Status, "pulse");
        Assert.Eventually(() => !(bool)s.Value("toggle_fb"), "the pulse toggled it back");
        Assert.Equal(400, s.Call(HttpMethod.Post, "/api/slots/1/signals/volume_level/pulse").Status, "pulse of an analog signal is refused");
        Assert.Equal(404, s.Call(HttpMethod.Post, "/api/slots/1/signals/nope/pulse").Status, "pulse of an unknown signal");

        // Needs the access key (from another host name).
        Assert.Equal(401, (int)s.Raw(HttpMethod.Get, "/api/slots/1/signals/toggle_fb", host: "example.com").StatusCode, "no key, not local: 401");
        Assert.Equal(200, (int)s.Raw(HttpMethod.Get, "/api/slots/1/signals/toggle_fb", host: "example.com", key: true).StatusCode, "with the key");

        // Server-sent events.
        var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{s.Main}/api/slots/1/signals/volume_level/stream");
        using var resp = s.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        Assert.Equal(200, (int)resp.StatusCode, "stream opens"); Assert.Equal("text/event-stream", resp.Content.Headers.ContentType!.MediaType!, "SSE content type");
        using var reader = new StreamReader(resp.Content.ReadAsStream());
        async Task<(string Evt, JsonElement Data)> Next()
        {
            string? evt = null; JsonElement data = default;
            while (true)
            {
                var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                if (line == null) throw new IOException("stream ended");
                if (line.StartsWith("event: ")) evt = line[7..];
                else if (line.StartsWith("data: ")) data = JsonDocument.Parse(line[6..]).RootElement.Clone();
                else if (line.Length == 0 && evt != null) return (evt, data);
            }
        }
        var first = Next().GetAwaiter().GetResult();
        Assert.Equal("value", first.Evt, "first event is the current value"); Assert.True(first.Data.GetProperty("value").GetInt32() is 16383 or 16384, "starts with the current value (25%)");
        s.Raw(HttpMethod.Put, "/api/slots/1/signals/volume_level", "777");
        var change = Next().GetAwaiter().GetResult();
        Assert.Equal("change", change.Evt, "then change events"); Assert.Equal(777, change.Data.GetProperty("value").GetInt32(), "with the new value");
        Assert.Equal("volume_level", change.Data.GetProperty("name").GetString()!, "and the name"); Assert.Equal("analog", change.Data.GetProperty("kind").GetString()!, "kind");
        Assert.True(s.P.Slot(1).Hub.SubscriberCount >= 1, "the stream is subscribed");
        resp.Dispose();
        Assert.Eventually(() => s.P.Slot(1).Hub.SubscriberCount == 0, "closing the stream unsubscribes, so the slot is untraced again", 8000);
    }

    [Test]
    public static void Incoming_hooks_need_no_key_but_are_never_served_on_the_tunnel_port()
    {
        using var s = new Srv();
        var created = s.Call(HttpMethod.Post, "/api/hooks", new { slot = 1, label = "Doorbell", allow = new[] { "volume_level", "toggle_press" } });
        Assert.Equal(200, created.Status, "create: " + created.Text);
        var token = created.Json.GetProperty("token").GetString()!;
        Assert.Equal("/hooks/" + token, created.Json.GetProperty("path").GetString()!, "path");
        Assert.Equal(401, (int)s.Raw(HttpMethod.Get, "/api/hooks", host: "example.com").StatusCode, "managing hooks needs the key");

        // From "another host" with no key at all, the token alone works.
        var ok = s.Raw(HttpMethod.Post, "/hooks/" + token, "{\"signal\":\"volume_level\",\"value\":4242}", host: "example.com");
        Assert.Equal(200, (int)ok.StatusCode, "POST /hooks/{token} without the key: " + ok.Content.ReadAsStringAsync().Result);
        Assert.Eventually(() => Convert.ToInt32(s.Value("volume_level")) == 4242, "the signal changed");
        Assert.Equal(200, (int)s.Raw(HttpMethod.Post, "/hooks/" + token, "{\"pulse\":\"toggle_press\"}", host: "example.com").StatusCode, "pulse");
        Assert.Equal(403, (int)s.Raw(HttpMethod.Post, "/hooks/" + token, "{\"signal\":\"hello_text\",\"value\":\"x\"}", host: "example.com").StatusCode, "outside the allow-list");
        Assert.Equal(404, (int)s.Raw(HttpMethod.Post, "/hooks/not-a-token", "{\"pulse\":\"toggle_press\"}", host: "example.com").StatusCode, "wrong token");
        Assert.Equal(405, (int)s.Raw(HttpMethod.Get, "/hooks/" + token, host: "example.com").StatusCode, "GET isn't allowed");
        Assert.Equal(400, (int)s.Raw(HttpMethod.Post, "/hooks/" + token, "{oops", host: "example.com").StatusCode, "bad JSON");

        // The tunnel port: 404 with the right token, with the key, with any Host header.
        foreach (var host in new string?[] { null, "localhost", "abc.trycloudflare.com" })
        {
            Assert.Equal(404, (int)s.Raw(HttpMethod.Post, "/hooks/" + token, "{\"pulse\":\"toggle_press\"}", port: s.Tunnel, host: host, key: true).StatusCode, $"tunnel port refuses /hooks/{{token}} (Host {host ?? "default"})");
            Assert.Equal(404, (int)s.Raw(HttpMethod.Get, "/hooks/" + token, port: s.Tunnel, host: host).StatusCode, "and GET");
            Assert.Equal(404, (int)s.Raw(HttpMethod.Get, "/metrics", port: s.Tunnel, host: host, key: true).StatusCode, $"tunnel port refuses /metrics even with the key (Host {host ?? "default"})");
            Assert.Equal(404, (int)s.Raw(HttpMethod.Get, "/api/integrations", port: s.Tunnel, host: host, key: true).StatusCode, "nor the integration API");
        }
        var before = Convert.ToInt32(s.Value("volume_level"));
        s.Raw(HttpMethod.Post, "/hooks/" + token, "{\"signal\":\"volume_level\",\"value\":1}", port: s.Tunnel);
        Thread.Sleep(150);
        Assert.Equal(before, Convert.ToInt32(s.Value("volume_level")), "nothing was changed through the tunnel port");
        Assert.True(TunnelGate.PathAllowed("/s/abc") && !TunnelGate.PathAllowed("/hooks/abc") && !TunnelGate.PathAllowed("/metrics"), "the gate allows only /s/");

        // Revoke over the API; the token dies at once.
        Assert.Equal(200, s.Call(HttpMethod.Delete, "/api/hooks/" + created.Json.GetProperty("id").GetString()).Status, "revoke");
        Assert.Equal(404, (int)s.Raw(HttpMethod.Post, "/hooks/" + token, "{\"pulse\":\"toggle_press\"}", host: "example.com").StatusCode, "revoked token");
        var act = s.Call(HttpMethod.Get, "/api/activity").Json.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.True(act.Contains("integration.hook.create") && act.Contains("integration.hook.revoke"), "the activity feed records both: " + string.Join(",", act));
    }

    [Test]
    public static void Metrics_endpoint_is_valid_prometheus_text_behind_the_key_unless_made_public_and_never_on_the_tunnel()
    {
        using (var s = new Srv())
        {
            var r = s.Raw(HttpMethod.Get, "/metrics");
            Assert.Equal(200, (int)r.StatusCode, "local request needs no key");
            Assert.True(r.Content.Headers.ContentType!.ToString().StartsWith("text/plain; version=0.0.4"), "content type: " + r.Content.Headers.ContentType);
            var samples = IntegrationCodecTests.ParsePrometheus(r.Content.ReadAsStringAsync().Result);
            Assert.True(samples.Any(x => x.Name == "openvp_slot_running" && x.Labels["slot"] == "1" && x.Value == 1), "slot 1 is running");
            Assert.True(samples.Any(x => x.Name == "openvp_slot_devices_online"), "devices online is there");
            Assert.Equal(401, (int)s.Raw(HttpMethod.Get, "/metrics", host: "example.com").StatusCode, "from another host with no key: 401");
            Assert.Equal(200, (int)s.Raw(HttpMethod.Get, "/metrics", host: "example.com", key: true).StatusCode, "with 'Authorization: Bearer KEY'");
            Assert.Equal(401, (int)s.Raw(HttpMethod.Get, "/metrics?key=" + s.Key, host: "example.com").StatusCode, "?key= isn't a login on /metrics (it would redirect a scraper)");
            Assert.Equal(404, (int)s.Raw(HttpMethod.Get, "/metrics", port: s.Tunnel, key: true).StatusCode, "never on the tunnel port");
            Assert.True(s.Call(HttpMethod.Get, "/api/integrations").Json.GetProperty("prometheus").GetProperty("scrapes").GetInt32() >= 2, "scrapes are counted");
            Assert.True(!s.Call(HttpMethod.Get, "/api/integrations").Json.GetProperty("prometheus").GetProperty("public").GetBoolean(), "protected by default");
        }
        using (var pub = new Srv("--OpenVP:MetricsPublic=true"))
        {
            Assert.Equal(200, (int)pub.Raw(HttpMethod.Get, "/metrics", host: "example.com").StatusCode, "MetricsPublic: no key needed from anywhere");
            Assert.Equal(401, (int)pub.Raw(HttpMethod.Get, "/api/slots", host: "example.com").StatusCode, "but the rest of the API still is protected");
            Assert.Equal(404, (int)pub.Raw(HttpMethod.Get, "/metrics", port: pub.Tunnel).StatusCode, "and even a public /metrics is never on the tunnel port");
        }
    }

    [Test]
    public static void Mqtt_round_trip_against_a_real_broker_sets_a_signal_and_the_panel_feedback_follows()
    {
        var bport = IntegrationServiceTests.FreePort();
        var factory = new MqttFactory();
        var broker = factory.CreateMqttServer(new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointBoundIPV6Address(IPAddress.None).WithDefaultEndpointPort(bport).Build());
        string? seenUser = null, seenPass = null;
        broker.ValidatingConnectionAsync += e =>
        {
            if (e.ClientId.StartsWith("openvp")) { seenUser = e.UserName; seenPass = e.Password; if (e.Password != "brokerpw") e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword; }
            return Task.CompletedTask;
        };
        broker.StartAsync().GetAwaiter().GetResult();
        try
        {
            using var s = new Srv();
            // An observer client that records what the bridge publishes (including retained messages).
            var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, (string Payload, bool Retain)>();
            var obs = factory.CreateMqttClient();
            obs.ApplicationMessageReceivedAsync += e => { seen[e.ApplicationMessage.Topic] = (Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment), e.ApplicationMessage.Retain); return Task.CompletedTask; };
            obs.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", bport).WithClientId("observer").Build()).GetAwaiter().GetResult();
            obs.SubscribeAsync(factory.CreateSubscribeOptionsBuilder().WithTopicFilter("e2e/#").Build()).GetAwaiter().GetResult();

            var put = s.Call(HttpMethod.Put, "/api/slots/1/integrations", new
            {
                mqtt = new[] { new { signal = "toggle_fb", set = false, discovery = false }, new { signal = "toggle_press", set = true, discovery = false }, new { signal = "volume_level", set = true, discovery = false } }
            });
            Assert.Equal(200, put.Status, "choose signals: " + put.Text);
            // Wrong password first: the bridge reports an error and keeps trying.
            var bad = s.Call(HttpMethod.Put, "/api/integrations/mqtt", new { enabled = true, host = "127.0.0.1", port = bport, baseTopic = "e2e/room", username = "bridge", password = "wrong" });
            Assert.Equal(200, bad.Status, "save: " + bad.Text);
            Assert.True(bad.Json.GetProperty("hasPassword").GetBoolean() && !bad.Text.Contains("wrong"), "the password is never in the response");
            Assert.Eventually(() => s.Call(HttpMethod.Get, "/api/integrations/mqtt").Json.GetProperty("status").GetProperty("state").GetString() == "error", "a refused login shows as an error", 8000);
            Assert.True(s.Call(HttpMethod.Get, "/api/integrations/mqtt").Json.GetProperty("status").GetProperty("error").GetString()!.Length > 0, "with a reason");
            var good = s.Call(HttpMethod.Put, "/api/integrations/mqtt", new { password = "brokerpw" });
            Assert.True(!good.Text.Contains("brokerpw"), "never returned");
            Assert.Eventually(() => s.Call(HttpMethod.Get, "/api/integrations/mqtt").Json.GetProperty("status").GetProperty("state").GetString() == "connected", "connected", 15000);
            Assert.Equal("bridge", seenUser!, "username reached the broker"); Assert.Equal("brokerpw", seenPass!, "password reached the broker");
            Assert.True(!File.ReadAllText(Path.Combine(s.Dir, "integrations", "global.json")).Contains("brokerpw"), "not saved in global.json");

            Assert.Eventually(() => seen.TryGetValue("e2e/room/status", out var v) && v.Payload == "online", "availability online");
            Assert.Eventually(() => seen.TryGetValue("e2e/room/slot01/toggle_fb", out var v) && v.Payload == "0", "current value published");
            Assert.Equal("running", seen["e2e/room/slot01/state"].Payload, "slot state");

            // Set from MQTT: press the button; the program's feedback changes and goes back out, and the panel device sees it.
            var panel = s.P.Slot(1).FindDevice(3)!;
            var fbJoin = new JoinKey(SigKind.Digital, 1);
            var wasOn = (bool)panel.GetFeedback(fbJoin);
            obs.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("e2e/room/slot01/toggle_press/set").WithPayload("pulse").Build()).GetAwaiter().GetResult();
            Assert.Eventually(() => seen["e2e/room/slot01/toggle_fb"].Payload == "1", "feedback published after the set");
            Assert.Eventually(() => (bool)s.Value("toggle_fb"), "the program's signal changed");
            Assert.True((bool)panel.GetFeedback(fbJoin) != wasOn || (bool)s.Value("toggle_fb"), "the panel's feedback join changed");
            obs.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("e2e/room/slot01/volume_level/set").WithPayload("75%").Build()).GetAwaiter().GetResult();
            Assert.Eventually(() => Convert.ToInt32(s.Value("volume_level")) is 49151 or 49152, "75% from MQTT");

            // Change from the program side, then see it on a brand new subscriber via the retained message.
            s.P.Slot(1).ForceSignal("volume_level", 321);
            Assert.Eventually(() => seen["e2e/room/slot01/volume_level"].Payload == "321", "program change published");
            var late = factory.CreateMqttClient(); string? lateVal = null; var lateRetained = false;
            late.ApplicationMessageReceivedAsync += e => { if (e.ApplicationMessage.Topic == "e2e/room/slot01/volume_level") { lateVal = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment); lateRetained = e.ApplicationMessage.Retain; } return Task.CompletedTask; };
            late.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", bport).WithClientId("late").Build()).GetAwaiter().GetResult();
            late.SubscribeAsync(factory.CreateSubscribeOptionsBuilder().WithTopicFilter("e2e/#").Build()).GetAwaiter().GetResult();
            Assert.Eventually(() => lateVal == "321", "a late subscriber gets the retained value"); Assert.True(lateRetained, "flagged as retained");

            // Switching it off says offline.
            s.Call(HttpMethod.Put, "/api/integrations/mqtt", new { enabled = false });
            Assert.Eventually(() => seen["e2e/room/status"].Payload == "offline", "offline when the bridge is turned off");
            var acts = s.Call(HttpMethod.Get, "/api/activity").Json.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            Assert.True(acts.Contains("integration.mqtt") && acts.Contains("integration.slot"), "config changes are in the activity feed");
            Assert.True(!s.Call(HttpMethod.Get, "/api/activity").Text.Contains("brokerpw"), "and the password isn't");
            obs.Dispose(); late.Dispose();
        }
        finally { broker.StopAsync().GetAwaiter().GetResult(); }
    }

    [Test]
    public static void Webhook_osc_and_artnet_settings_validate_and_are_applied_through_the_api()
    {
        using var s = new Srv();
        var bad = s.Call(HttpMethod.Put, "/api/slots/1/integrations", new { webhooks = new[] { new { url = "ftp://x", signal = "toggle_fb" } } });
        Assert.Equal(400, bad.Status, "a bad webhook URL is refused: " + bad.Text);
        using var srv = new IntegrationServiceTests.LocalHttp();
        var ok = s.Call(HttpMethod.Put, "/api/slots/1/integrations", new { webhooks = new[] { new { id = "w1", name = "hook", url = $"http://127.0.0.1:{srv.Port}/x", signal = "toggle_fb", secret = "shh", headers = new Dictionary<string, string> { ["X-A"] = "b" } } } });
        Assert.Equal(200, ok.Status, "saved: " + ok.Text);
        Assert.True(ok.Json.GetProperty("webhooks")[0].GetProperty("hasSecret").GetBoolean() && !ok.Text.Contains("shh"), "the secret is never returned");
        // Saving again without a secret keeps it.
        s.Call(HttpMethod.Put, "/api/slots/1/integrations", new { webhooks = new[] { new { id = "w1", name = "hook renamed", url = $"http://127.0.0.1:{srv.Port}/x", signal = "toggle_fb" } } });
        Assert.True(File.ReadAllText(Path.Combine(s.P.Slot(1).Directory, "devices", "integrations.json")).Contains("shh"), "the secret is kept when the form leaves it out");
        var test = s.Call(HttpMethod.Post, "/api/slots/1/webhooks/w1/test");
        Assert.Equal(200, test.Status, "test send"); Assert.True(test.Json.GetProperty("ok").GetBoolean(), "delivered: " + test.Text);
        var req = srv.Requests.Last();
        Assert.True(WebhookSigner.Verify("shh", req.Headers["X-OpenVP-Timestamp"], req.Body, req.Headers["X-OpenVP-Signature"]), "the test request is signed");
        Assert.Equal(404, s.Call(HttpMethod.Post, "/api/slots/1/webhooks/zzz/test").Status, "unknown webhook");
        Assert.True(s.Call(HttpMethod.Get, "/api/webhooks/deliveries?slot=1").Json.GetArrayLength() >= 1, "delivery log");

        Assert.Equal(400, s.Call(HttpMethod.Put, "/api/integrations/osc", new { listenPort = 70000 }).Status, "bad OSC port");
        Assert.Equal(400, s.Call(HttpMethod.Put, "/api/integrations/artnet", new { rateHz = 500 }).Status, "bad rate");
        Assert.Equal(400, s.Call(HttpMethod.Put, "/api/integrations/mqtt", new { enabled = true, host = "" }).Status, "MQTT needs a host");
        Assert.Equal(400, s.Call(HttpMethod.Put, "/api/slots/1/integrations", new { artnet = new[] { new { signal = "volume_level", universe = 1, channel = 600 } } }).Status, "bad channel");
        var udp = IntegrationServiceTests.FreePort(udp: true);
        var o = s.Call(HttpMethod.Put, "/api/integrations/osc", new { listenPort = udp });
        Assert.Equal(200, o.Status, "OSC saved"); Assert.True(o.Json.GetProperty("status").GetProperty("listening").GetBoolean(), "and listening: " + o.Text);
        s.Call(HttpMethod.Put, "/api/integrations/osc", new { listenPort = 0 });
        var a = s.Call(HttpMethod.Put, "/api/integrations/artnet", new { enabled = true, target = "127.0.0.1", rateHz = 10 });
        Assert.Equal(200, a.Status, "Art-Net saved"); s.Call(HttpMethod.Put, "/api/integrations/artnet", new { enabled = false });
        var overview = s.Call(HttpMethod.Get, "/api/integrations").Json;
        foreach (var k in new[] { "mqtt", "webhooks", "hooks", "osc", "artnet", "prometheus" }) Assert.True(overview.TryGetProperty(k, out _), $"overview has {k}");
    }
}
