using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenVP.Core;
using OpenVP.Core.Alerts;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>The alert, uptime, history and report endpoints on a real server in this process, with a real SMTP exchange (MailKit to a tiny listener) and a real webhook listener.</summary>
public static class AlertApiTests
{
    private sealed class Srv : IDisposable
    {
        public WebApplication App; public int Main, Tunnel; public string Dir, Key; public Processor P; public HttpClient Http;
        public Srv()
        {
            Dir = Directory.CreateTempSubdirectory("openvp-alert-e2e").FullName;
            Main = IntegrationServiceTests.FreePort(); Tunnel = IntegrationServiceTests.FreePort();
            var args = new List<string> { $"--urls=http://127.0.0.1:{Main}", $"--OpenVP:TunnelPort={Tunnel}", $"--OpenVP:DataDirectory={Dir}", "--OpenVP:CipEnabled=false", "--OpenVP:UnsupportedLog=off", "--OpenVP:Slots=2", "--OpenVP:Mdns=false" };
            App = OpenVpServer.Create(args.ToArray());
            App.StartAsync().GetAwaiter().GetResult();
            Key = File.ReadAllText(Path.Combine(Dir, "apikey.txt")).Trim();
            P = App.Services.GetRequiredService<Processor>();
            P.LoadProgram(1, Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw"));
            Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{Main}"), Timeout = TimeSpan.FromSeconds(30) };
        }
        public (int Status, JsonElement Json, string Text) Call(HttpMethod m, string path, object? body = null, int? port = null, string? host = null, bool key = false)
        {
            var req = new HttpRequestMessage(m, $"http://127.0.0.1:{port ?? Main}{path}");
            if (host != null) req.Headers.Host = host;
            if (key) req.Headers.Authorization = new("Bearer", Key);
            if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var r = Http.Send(req);
            var text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            JsonElement j = default; try { if (text.Length > 0) j = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
            return ((int)r.StatusCode, j, text);
        }
        public void Dispose() { try { App.StopAsync().GetAwaiter().GetResult(); } catch { } Http.Dispose(); try { Directory.Delete(Dir, true); } catch { } }
    }

    /// <summary>A listener that records the requests it gets and answers 200.</summary>
    private sealed class Hook : IDisposable
    {
        public readonly HttpListener L = new(); public readonly List<(string Path, string Body, Dictionary<string, string> Headers)> Got = new(); public readonly int Port;
        public Hook()
        {
            Port = IntegrationServiceTests.FreePort(); L.Prefixes.Add($"http://127.0.0.1:{Port}/"); L.Start();
            _ = Task.Run(async () =>
            {
                while (L.IsListening)
                {
                    try
                    {
                        var c = await L.GetContextAsync();
                        var body = await new StreamReader(c.Request.InputStream).ReadToEndAsync();
                        lock (Got) Got.Add((c.Request.Url!.AbsolutePath, body, c.Request.Headers.AllKeys.ToDictionary(k => k!, k => c.Request.Headers[k]!)));
                        c.Response.StatusCode = 200; c.Response.Close();
                    }
                    catch { }
                }
            });
        }
        public void Dispose() { try { L.Stop(); L.Close(); } catch { } }
        public List<JsonElement> Bodies(string path) { lock (Got) return Got.Where(g => g.Path == path).Select(g => JsonDocument.Parse(g.Body).RootElement.Clone()).ToList(); }
    }

    /// <summary>A minimal SMTP listener (no TLS) that keeps what it receives.</summary>
    private sealed class Smtp : IDisposable
    {
        public readonly TcpListener L = new(IPAddress.Loopback, 0); public readonly List<(string From, List<string> To, string Data)> Mail = new(); public int Port => ((IPEndPoint)L.LocalEndpoint).Port;
        public Smtp()
        {
            L.Start();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await L.AcceptTcpClientAsync(); } catch { return; }
                    _ = Task.Run(async () =>
                    {
                        using var cl = c; var s = cl.GetStream(); using var rd = new StreamReader(s, Encoding.Latin1); using var wr = new StreamWriter(s, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };
                        await wr.WriteLineAsync("220 test ESMTP");
                        string from = "", data = ""; var to = new List<string>();
                        while (await rd.ReadLineAsync() is { } line)
                        {
                            var u = line.ToUpperInvariant();
                            if (u.StartsWith("EHLO")) { await wr.WriteAsync("250-test\r\n250 8BITMIME\r\n"); }
                            else if (u.StartsWith("HELO")) await wr.WriteLineAsync("250 test");
                            else if (u.StartsWith("MAIL FROM")) { from = line[10..]; to = new(); await wr.WriteLineAsync("250 OK"); }
                            else if (u.StartsWith("RCPT TO")) { to.Add(line[8..]); await wr.WriteLineAsync("250 OK"); }
                            else if (u == "DATA")
                            {
                                await wr.WriteLineAsync("354 go");
                                var sb = new StringBuilder();
                                while (await rd.ReadLineAsync() is { } l && l != ".") sb.AppendLine(l);
                                data = sb.ToString();
                                lock (Mail) Mail.Add((from, to, data));
                                await wr.WriteLineAsync("250 queued");
                            }
                            else if (u == "QUIT") { await wr.WriteLineAsync("221 bye"); break; }
                            else await wr.WriteLineAsync("250 OK");
                        }
                    });
                }
            });
        }
        public void Dispose() => L.Stop();
    }

    private static string Id(JsonElement c) => c.GetProperty("channel").GetProperty("id").GetString()!;

    [Test]
    public static void Channels_are_saved_without_leaking_secrets_and_a_fault_reaches_a_webhook_and_an_email_and_resolves()
    {
        using var hook = new Hook(); using var smtp = new Smtp(); using var s = new Srv();
        Assert.Equal(401, s.Call(HttpMethod.Get, "/api/alerts", host: "openvp.example").Status, "not without the key from another host");
        Assert.Equal(404, s.Call(HttpMethod.Get, "/api/alerts", port: s.Tunnel, key: true).Status, "never on the tunnel port");
        Assert.Equal(404, s.Call(HttpMethod.Get, "/api/alerts/stream", port: s.Tunnel, key: true).Status, "nor the stream");

        var wh = s.Call(HttpMethod.Post, "/api/alerts/channels", new { type = "webhook", name = "Hook", url = $"http://127.0.0.1:{hook.Port}/alerts", secret = "topsecret" });
        Assert.Equal(200, wh.Status, "webhook channel saved: " + wh.Text);
        Assert.True(wh.Json.GetProperty("channel").GetProperty("hasSecret").GetBoolean(), "says a secret is saved");
        Assert.True(!wh.Text.Contains("topsecret"), "never returns it");
        var mail = s.Call(HttpMethod.Post, "/api/alerts/channels", new { type = "email", name = "Mail", host = "127.0.0.1", port = smtp.Port, security = "none", from = "openvp@test.local", to = "ops@test.local", password = "pw" });
        Assert.Equal(200, mail.Status, "email channel saved: " + mail.Text);
        Assert.Equal(400, s.Call(HttpMethod.Post, "/api/alerts/channels", new { type = "email", name = "Bad", host = "", from = "a@b.c", to = "d@e.f" }).Status, "an email channel without a server is refused");
        Assert.Equal(400, s.Call(HttpMethod.Post, "/api/alerts/channels", new { type = "teams", name = "Teams" }).Status, "Teams needs its address");
        var cfg = s.Call(HttpMethod.Get, "/api/alerts/config");
        Assert.True(!cfg.Text.Contains("topsecret") && !cfg.Text.Contains("\"pw\""), "no secret in the configuration");
        var file = File.ReadAllText(Path.Combine(s.Dir, "alerts", "config.json"));
        Assert.True(!file.Contains("topsecret") && !file.Contains("\"pw\""), "no secret in config.json either");

        var whId = Id(wh.Json); var mailId = Id(mail.Json);
        var rule = s.Call(HttpMethod.Put, "/api/alerts/config", new { rules = new[] { new { id = "r", name = "all", minSeverity = "info", channels = new[] { whId, mailId } } } });
        Assert.Equal(200, rule.Status, "rule saved: " + rule.Text);

        var t = s.Call(HttpMethod.Post, $"/api/alerts/channels/{whId}/test");
        Assert.Equal("sent", t.Json.GetProperty("status").GetString()!, "test message sent: " + t.Text);
        var testBody = hook.Bodies("/alerts").Single();
        Assert.Equal("alert.test", testBody.GetProperty("event").GetString()!, "the test is marked as a test");
        Assert.Equal("sent", s.Call(HttpMethod.Post, $"/api/alerts/channels/{mailId}/test").Json.GetProperty("status").GetString()!, "email test sent");
        Assert.Equal(1, smtp.Mail.Count, "one email reached the SMTP listener");
        Assert.True(smtp.Mail[0].Data.Contains("Subject: [OpenVP] Test:") && smtp.Mail[0].Data.Contains("multipart/alternative"), "with a subject and both parts");
        Assert.True(smtp.Mail[0].To[0].Contains("ops@test.local"), "to the right person");

        // A real fault in slot 1.
        s.P.Slot(1).Fault("unhandled exception in OnPoll");
        Assert.Eventually(() => hook.Bodies("/alerts").Any(b => b.GetProperty("event").GetString() == "alert.raised"), "the fault was sent to the webhook", 8000);
        var raised = hook.Bodies("/alerts").First(b => b.GetProperty("event").GetString() == "alert.raised");
        Assert.Equal("slot.fault", raised.GetProperty("kind").GetString()!, "kind"); Assert.Equal("critical", raised.GetProperty("severity").GetString()!, "severity"); Assert.Equal(1, raised.GetProperty("slot").GetInt32(), "slot");
        Assert.Eventually(() => { lock (smtp.Mail) return smtp.Mail.Any(m => m.Data.Contains("Subject: [OpenVP] CRITICAL: Slot 1 is in Error")); }, "and emailed (the crash policy's own alert comes too)", 8000);
        var active = s.Call(HttpMethod.Get, "/api/alerts").Json.GetProperty("active");
        Assert.True(active.GetArrayLength() >= 1, "an active alert"); Assert.Equal("critical", active[0].GetProperty("severity").GetString()!, "severities are words in the API");
        active = s.Call(HttpMethod.Get, "/api/alerts").Json.GetProperty("active");
        Assert.Equal("red", s.Call(HttpMethod.Get, "/api/alerts/status").Json.GetProperty("status").GetProperty("color").GetString()!, "the tray colour is red");

        // Acknowledge, then clear it by restarting the slot.
        var id = active.EnumerateArray().First(a => a.GetProperty("kind").GetString() == "slot.fault").GetProperty("id").GetString()!;
        Assert.Equal(200, s.Call(HttpMethod.Post, $"/api/alerts/{id}/ack").Status, "acknowledged");
        Assert.Equal(404, s.Call(HttpMethod.Post, "/api/alerts/nope/ack").Status, "unknown alert");
        s.P.Restart(1);
        Assert.Eventually(() => hook.Bodies("/alerts").Any(b => b.GetProperty("event").GetString() == "alert.resolved"), "a resolved message was sent", 8000);
        Assert.Eventually(() => { lock (smtp.Mail) return smtp.Mail.Any(m => m.Data.Contains("Subject: [OpenVP] RESOLVED: Slot 1 is in Error")); }, "and emailed", 8000);
        Assert.Eventually(() => s.Call(HttpMethod.Get, "/api/alerts").Json.GetProperty("active").GetArrayLength() == 0, "nothing active", 5000);
        // The same fault again inside the repeat window is held back and says why.
        s.P.Slot(1).Fault("again");
        Assert.Eventually(() => s.Call(HttpMethod.Get, "/api/alerts").Json.GetProperty("active").EnumerateArray().Any(a => a.GetProperty("kind").GetString() == "slot.fault"), "active again", 5000);
        Thread.Sleep(300);
        Assert.Equal(1, hook.Bodies("/alerts").Count(b => b.GetProperty("event").GetString() == "alert.raised" && b.GetProperty("kind").GetString() == "slot.fault"), "no second raised message for the same fault");
        var again = s.Call(HttpMethod.Get, "/api/alerts").Json.GetProperty("active").EnumerateArray().First(a => a.GetProperty("kind").GetString() == "slot.fault");
        Assert.True(again.GetProperty("deliveries").EnumerateArray().Any(d => d.GetProperty("status").GetString() == "suppressed"), "the history says suppressed");
        // Removing a channel removes it from the rules.
        Assert.Equal(200, s.Call(HttpMethod.Delete, $"/api/alerts/channels/{whId}").Status, "channel removed");
        var rules = s.Call(HttpMethod.Get, "/api/alerts/config").Json.GetProperty("rules")[0].GetProperty("channels");
        Assert.Equal(1, rules.GetArrayLength(), "and dropped from the rule");
    }

    [Test]
    public static void The_alert_stream_sends_status_then_alerts_as_server_sent_events()
    {
        using var s = new Srv();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/alerts/stream");
        using var resp = s.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType!.MediaType!, "content type");
        using var rd = new StreamReader(resp.Content.ReadAsStream());
        var first = ReadEventAsync(rd);
        Assert.True(first.StartsWith("event: status"), "the first event is the status: " + first);
        s.Call(HttpMethod.Post, "/api/alerts/channels/desktop/test");
        string ev = "";
        for (var i = 0; i < 4 && !ev.StartsWith("event: alert"); i++) ev = ReadEventAsync(rd);
        Assert.True(ev.StartsWith("event: alert") && ev.Contains("\"event\":\"test\""), "a test alert arrives as an alert event: " + ev);
        Assert.Equal(1, s.Call(HttpMethod.Get, "/api/alerts/status").Json.GetProperty("listeners").GetInt32(), "one listener is counted");
    }

    private static string ReadEventAsync(StreamReader rd)
    {
        var sb = new StringBuilder();
        var end = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < end)
        {
            var t = rd.ReadLineAsync();
            if (!t.Wait(8000)) break;
            var l = t.Result;
            if (l == null) break;
            if (l.StartsWith(":")) continue;
            if (l.Length == 0) { if (sb.Length > 0) break; continue; }
            sb.AppendLine(l);
        }
        return sb.ToString();
    }

    [Test]
    public static void Uptime_history_and_reports_are_served_and_validated()
    {
        using var s = new Srv();
        Assert.Eventually(() => s.Call(HttpMethod.Get, "/api/metrics/history?range=5m").Json.GetProperty("points").GetArrayLength() >= 2, "second-by-second history fills in", 6000);
        foreach (var r in new[] { "1h", "24h", "7d", "30d", "90d" }) Assert.Equal(200, s.Call(HttpMethod.Get, "/api/metrics/history?range=" + r).Status, "history " + r);
        Assert.Equal(400, s.Call(HttpMethod.Get, "/api/metrics/history?range=2y").Status, "unknown range");
        var up = s.Call(HttpMethod.Get, "/api/uptime?range=24h");
        Assert.Equal(200, up.Status, "uptime: " + up.Text);
        var slot1 = up.Json.GetProperty("slots").EnumerateArray().First(x => x.GetProperty("slot").GetInt32() == 1);
        Assert.True(slot1.GetProperty("availability").GetDouble() > 90, "slot 1 has been running");
        Assert.Equal(96, slot1.GetProperty("buckets").GetArrayLength(), "timeline buckets");
        Assert.Equal(400, s.Call(HttpMethod.Get, "/api/uptime?range=5m").Status, "too short a range");
        Assert.Equal(200, s.Call(HttpMethod.Get, "/api/uptime/heatmap?range=7d&mode=hour&offset=60").Status, "heat map");
        var html = s.Http.GetStringAsync("/api/reports/preview?period=daily").GetAwaiter().GetResult();
        Assert.True(html.Contains("health report") && html.Contains("SampleProgram"), "the report page names the program");
        var dl = s.Http.GetAsync("/api/reports/preview?period=weekly&download=1").GetAwaiter().GetResult();
        Assert.True(dl.Content.Headers.ContentDisposition?.FileName?.Contains("openvp-weekly-report") == true, "download has a file name");
        Assert.Equal(400, s.Call(HttpMethod.Post, "/api/reports/send", new { period = "daily", channels = Array.Empty<string>() }).Status, "sending with no channel is refused");
        var rep = s.Call(HttpMethod.Put, "/api/alerts/config", new { report = new { enabled = true, frequency = "weekly", day = 1, time = "08:30", channels = new[] { "desktop" } } });
        Assert.Equal(200, rep.Status, "schedule saved: " + rep.Text);
        Assert.Equal("weekly", rep.Json.GetProperty("report").GetProperty("frequency").GetString()!, "weekly");
        Assert.Equal(400, s.Call(HttpMethod.Put, "/api/alerts/config", new { thresholds = new[] { new { metric = "cpu", warn = 90, crit = 50 } } }).Status, "warning above critical is refused");
        Assert.Equal(400, s.Call(HttpMethod.Put, "/api/alerts/config", new { rules = new[] { new { id = "x", name = "x", quiet = new { enabled = true, start = "99:00", end = "07:00" } } } }).Status, "bad quiet hours are refused");
        // Config changes show in the activity feed.
        var act = s.Call(HttpMethod.Get, "/api/activity?limit=20").Json;
        Assert.True(act.EnumerateArray().Any(a => a.GetProperty("text").GetString()!.StartsWith("Changed the alert report schedule")), "the activity feed records the change");
    }
}
