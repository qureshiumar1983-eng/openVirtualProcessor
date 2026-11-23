using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OpenVP.Core;
using OpenVP.Core.Integrations;

namespace OpenVP.Server;

/// <summary>
/// REST endpoints for the integrations (MQTT, webhooks, OSC, Art-Net, Prometheus) and for single signals. They are thin: the
/// work is in <c>OpenVP.Core.Integrations</c>. Everything under <c>/api</c> needs the access key. Two paths are outside it on
/// purpose: <c>POST /hooks/{token}</c> (the token is the credential) and <c>GET /metrics</c> (the key, unless
/// <c>OpenVP:MetricsPublic=true</c>). Neither is ever served on the internet tunnel's port (see <see cref="TunnelGate"/>).
/// </summary>
public static class IntegrationEndpoints
{
    private static IResult Bad(string m) => Results.BadRequest(new { error = m });

    private static string? Str(JsonElement b, string key) =>
        b.ValueKind == JsonValueKind.Object && b.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool? Bool(JsonElement b, string key) =>
        b.ValueKind == JsonValueKind.Object && b.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static int? Int(JsonElement b, string key) =>
        b.ValueKind == JsonValueKind.Object && b.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static object MqttDto(IntegrationsHost h)
    {
        var s = h.Store.Global.Mqtt;
        return new
        {
            enabled = s.Enabled, host = s.Host, port = s.Port, tls = s.Tls, username = s.Username, clientId = s.ClientId, baseTopic = s.BaseTopic,
            homeAssistant = s.HomeAssistant, discoveryPrefix = s.DiscoveryPrefix,
            hasPassword = h.Store.Secrets.Has("mqtt.password"),
            effectiveBase = s.EffectiveBase, effectiveClientId = s.EffectiveClientId,
            status = h.Mqtt.Status()
        };
    }

    private static object HookDto(IncomingHook k) => new
    {
        id = k.Id, token = k.Token, path = "/hooks/" + k.Token, label = k.Label, slot = k.Slot, allow = k.Allow, enabled = k.Enabled,
        created = k.Created, lastUsed = k.LastUsed, count = k.Count
    };

    private static object WebhookDto(WebhookRule r) => new
    {
        id = r.Id, name = r.Name, url = r.Url, headers = r.Headers, hasSecret = !string.IsNullOrEmpty(r.Secret), signal = r.Signal, expression = r.Expression, enabled = r.Enabled
    };

    private static object SlotDto(SlotIntegrations s) => new { mqtt = s.Mqtt, osc = s.Osc, artnet = s.ArtNet, webhooks = s.Webhooks.Select(WebhookDto) };

    private static object ForJson(SignalInfo i) => SignalValues.ForJson(i.Kind, i.Value);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static IResult Status(SignalDirectory.SetOutcome o, string? err) => o switch
    {
        SignalDirectory.SetOutcome.NoProgram => Results.Json(new { error = err }, statusCode: 409),
        SignalDirectory.SetOutcome.UnknownSignal => Results.NotFound(new { error = err }),
        _ => Bad(err ?? "Bad value.")
    };

    public static void Map(WebApplication app, RouteGroupBuilder slotApi, Processor processor, IntegrationsHost host, ApiAuth auth, CrashSupervisor crash,
        Action<HttpContext, string, string, int?> note)
    {
        // ------------------------------------------------------------------ overview
        app.MapGet("/api/integrations", () => new
        {
            mqtt = MqttDto(host),
            webhooks = host.Webhooks.Status(),
            hooks = host.Hooks.List().Count,
            osc = new { settings = host.Store.Global.Osc, status = host.Osc.Status() },
            artnet = new { settings = host.Store.Global.ArtNet, status = host.ArtNet.Status() },
            prometheus = new { path = "/metrics", @public = auth.MetricsPublic, keyRequired = auth.Enabled && !auth.MetricsPublic, scrapes = host.Scrapes, lastScrape = host.LastScrape }
        });

        // ------------------------------------------------------------------ MQTT
        app.MapGet("/api/integrations/mqtt", () => MqttDto(host));
        app.MapPut("/api/integrations/mqtt", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var cur = host.Store.Global.Mqtt;
                var next = cur with
                {
                    Enabled = Bool(body, "enabled") ?? cur.Enabled, Host = Str(body, "host") ?? cur.Host, Port = Int(body, "port") ?? cur.Port,
                    Tls = Bool(body, "tls") ?? cur.Tls,
                    Username = body.TryGetProperty("username", out var u) ? (u.ValueKind == JsonValueKind.String ? u.GetString() : null) : cur.Username,
                    ClientId = body.TryGetProperty("clientId", out var c) ? (c.ValueKind == JsonValueKind.String ? c.GetString() : null) : cur.ClientId,
                    BaseTopic = body.TryGetProperty("baseTopic", out var bt) ? (bt.ValueKind == JsonValueKind.String ? bt.GetString() : null) : cur.BaseTopic,
                    HomeAssistant = Bool(body, "homeAssistant") ?? cur.HomeAssistant,
                    DiscoveryPrefix = Str(body, "discoveryPrefix") ?? cur.DiscoveryPrefix
                };
                var saved = host.Store.SetMqtt(next, Str(body, "password"), Bool(body, "clearPassword") ?? false);
                note(ctx, "integration.mqtt", saved.Enabled ? $"MQTT bridge on: broker {saved.Host}:{saved.Port}{(saved.Tls ? " (TLS)" : "")}, base topic {saved.EffectiveBase}" : "MQTT bridge turned off", null);
                return Results.Ok(MqttDto(host));
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        // ------------------------------------------------------------------ OSC and Art-Net
        app.MapGet("/api/integrations/osc", () => new { settings = host.Store.Global.Osc, status = host.Osc.Status() });
        app.MapPut("/api/integrations/osc", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var cur = host.Store.Global.Osc;
                var next = host.Store.SetOsc(cur with { ListenPort = Int(body, "listenPort") ?? cur.ListenPort, TargetHost = Str(body, "targetHost") ?? cur.TargetHost, TargetPort = Int(body, "targetPort") ?? cur.TargetPort });
                note(ctx, "integration.osc", $"OSC: {(next.ListenPort > 0 ? $"listening on UDP {next.ListenPort}" : "not listening")}, {(next.TargetHost.Length > 0 ? $"sending to {next.TargetHost}:{next.TargetPort}" : "not sending")}", null);
                return Results.Ok(new { settings = next, status = host.Osc.Status() });
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        app.MapGet("/api/integrations/artnet", () => new { settings = host.Store.Global.ArtNet, status = host.ArtNet.Status() });
        app.MapPut("/api/integrations/artnet", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var cur = host.Store.Global.ArtNet;
                var next = host.Store.SetArtNet(cur with { Enabled = Bool(body, "enabled") ?? cur.Enabled, Target = Str(body, "target") ?? cur.Target, Port = Int(body, "port") ?? cur.Port, RateHz = Int(body, "rateHz") ?? cur.RateHz, Listen = Bool(body, "listen") ?? cur.Listen });
                note(ctx, "integration.artnet", next.Enabled ? $"Art-Net on: {next.Target}:{next.Port} at {next.RateHz} fps{(next.Listen ? ", listening" : "")}" : "Art-Net turned off", null);
                return Results.Ok(new { settings = next, status = host.ArtNet.Status() });
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        // ------------------------------------------------------------------ per-slot choices and outgoing webhooks
        slotApi.MapGet("/integrations", (int n) => SlotDto(host.Store.Slot(n)));
        slotApi.MapPut("/integrations", (int n, HttpContext ctx, JsonElement body) =>
        {
            if (body.ValueKind != JsonValueKind.Object) return Bad("Send a JSON object with any of mqtt, osc, artnet, webhooks.");
            try
            {
                var changed = new List<string>();
                var next = host.Store.UpdateSlot(n, cur =>
                {
                    var s = cur;
                    if (body.TryGetProperty("mqtt", out var m)) { s = s with { Mqtt = SlotIntegrations.CheckMqtt(Read<MqttSignal>(m)) }; changed.Add("MQTT"); }
                    if (body.TryGetProperty("osc", out var o)) { s = s with { Osc = SlotIntegrations.CheckOsc(Read<OscSignal>(o)) }; changed.Add("OSC"); }
                    if (body.TryGetProperty("artnet", out var a)) { s = s with { ArtNet = SlotIntegrations.CheckArtNet(Read<ArtNetChannel>(a)) }; changed.Add("Art-Net"); }
                    if (body.TryGetProperty("webhooks", out var w)) { s = s with { Webhooks = SlotIntegrations.CheckWebhooks(ReadWebhooks(w), cur.Webhooks) }; changed.Add("webhooks"); }
                    return s;
                });
                if (changed.Count > 0) note(ctx, "integration.slot", $"Changed the {string.Join(", ", changed)} signals of slot {n}", n);
                return Results.Ok(SlotDto(next));
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (JsonException e) { return Bad("That isn't valid: " + e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        slotApi.MapPost("/webhooks/{id}/test", async (int n, string id, HttpContext ctx) =>
        {
            try
            {
                var d = await host.Webhooks.TestAsync(n, id, ctx.RequestAborted);
                note(ctx, "integration.webhook.test", $"Sent a test webhook “{d.RuleName}” from slot {n}: {(d.Ok ? "delivered" : "failed")}", n);
                return Results.Ok(d);
            }
            catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
        });
        app.MapGet("/api/webhooks/deliveries", (int? slot, int? max) => host.Webhooks.Deliveries(slot, max ?? 100));

        // ------------------------------------------------------------------ incoming hooks
        app.MapGet("/api/hooks", () => host.Hooks.List().Select(HookDto));
        app.MapPost("/api/hooks", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var allow = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("allow", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : null;
                var h = host.Hooks.Create(Int(body, "slot") ?? 0, Str(body, "label"), allow);
                note(ctx, "integration.hook.create", $"Made an incoming webhook “{h.Label}” for slot {h.Slot}{(h.Allow.Count > 0 ? $" ({h.Allow.Count} signals allowed)" : " (any signal)")}", h.Slot);
                return Results.Ok(HookDto(h));
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });
        app.MapPut("/api/hooks/{id}", (string id, HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var allow = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("allow", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : null;
                var h = host.Hooks.Update(id, Str(body, "label"), allow, Bool(body, "enabled"));
                if (h == null) return Results.NotFound(new { error = "There's no such hook." });
                note(ctx, "integration.hook.update", $"Changed the incoming webhook “{h.Label}”{(h.Enabled ? "" : " (switched off)")}", h.Slot);
                return Results.Ok(HookDto(h));
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });
        app.MapDelete("/api/hooks/{id}", (string id, HttpContext ctx) =>
        {
            var h = host.Hooks.Find(id);
            if (h == null || !host.Hooks.Revoke(id)) return Results.NotFound(new { error = "There's no such hook." });
            note(ctx, "integration.hook.revoke", $"Revoked the incoming webhook “{h.Label}” for slot {h.Slot}", h.Slot);
            return Results.Ok(new { revoked = id });
        });

        // The one open endpoint: the token is the credential. Never on the tunnel port (the gate answers 404 first; this checks again).
        app.MapMethods("/hooks/{token}", new[] { "POST" }, async (string token, HttpContext ctx) =>
        {
            if (ctx.Items.ContainsKey(TunnelGate.ItemKey)) return Results.NotFound();
            var remote = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var buf = new byte[IncomingHooks.MaxBody + 1];
            var len = 0;
            while (len < buf.Length)
            {
                var r = await ctx.Request.Body.ReadAsync(buf.AsMemory(len), ctx.RequestAborted);
                if (r == 0) break;
                len += r;
            }
            var res = host.Hooks.Handle(token, buf.AsSpan(0, len), remote);
            if (res.RetryAfterSeconds > 0) ctx.Response.Headers.RetryAfter = res.RetryAfterSeconds.ToString();
            return res.Ok ? Results.Ok(new { ok = true }) : Results.Json(new { error = res.Message }, statusCode: res.Status);
        });
        app.MapFallback("/hooks/{token}", (HttpContext ctx) => { ctx.Response.Headers.Allow = "POST"; return Results.StatusCode(405); });

        // ------------------------------------------------------------------ Prometheus
        app.MapGet("/api/integrations/prometheus", () => new
        {
            path = "/metrics", @public = auth.MetricsPublic, keyRequired = auth.Enabled && !auth.MetricsPublic, scrapes = host.Scrapes, lastScrape = host.LastScrape
        });
        app.MapGet("/metrics", () =>
        {
            host.NoteScrape();
            return Results.Text(PrometheusText.Render(processor, host, s => crash.Totals(s)), PrometheusText.ContentType);
        });

        // ------------------------------------------------------------------ REST per signal
        slotApi.MapGet("/signals/{name}", (int n, string name) =>
        {
            var slot = processor.Slot(n);
            if (slot.Runtime == null) return Results.Json(new { error = "There is no program in that slot." }, statusCode: 409);
            var info = host.Directory.Current(n, name);
            if (info == null) return Results.NotFound(new { error = $"The program has no signal '{name}'." });
            return Results.Ok(new { slot = n, name = info.Name, kind = SignalValues.KindName(info.Kind), value = ForJson(info) });
        });

        slotApi.MapPut("/signals/{name}", async (int n, string name, HttpContext ctx) =>
        {
            var text = await ReadValue(ctx);
            if (text == null) return Bad("Send a value: 1, 0, true, false, pulse, a number, 50%, or text; as JSON (1, \"hello\", {\"value\": 1}) or plain text.");
            var o = host.Directory.TrySetFromText(n, name, text, out var err);
            if (o != SignalDirectory.SetOutcome.Ok) return Status(o, err);
            var kind = host.Directory.TryGet(n, name, out var info) ? SignalValues.KindName(info.Kind) : null;
            return Results.Ok(new { slot = n, name, kind, set = text });
        });

        slotApi.MapPost("/signals/{name}/pulse", (int n, string name) =>
        {
            if (processor.Slot(n).Runtime == null) return Results.Json(new { error = "There is no program in that slot." }, statusCode: 409);
            if (!host.Directory.TryGet(n, name, out var info)) return Results.NotFound(new { error = $"The program has no signal '{name}'." });
            if (info.Kind != SigKind.Digital) return Bad($"'{name}' is {SignalValues.KindName(info.Kind)}; only digital signals can be pulsed.");
            return processor.Slot(n).ForcePulse(name) ? Results.Ok(new { slot = n, name, pulsed = true }) : Results.NotFound(new { error = $"The program has no signal '{name}'." });
        });

        var streams = 0;
        slotApi.MapGet("/signals/{name}/stream", async (int n, string name, HttpContext ctx) =>
        {
            var slot = processor.Slot(n);
            if (slot.Runtime == null) { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "There is no program in that slot." }); return; }
            var first = host.Directory.Current(n, name);
            if (first == null) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = $"The program has no signal '{name}'." }); return; }
            if (Interlocked.Increment(ref streams) > 100)
            {
                Interlocked.Decrement(ref streams);
                ctx.Response.StatusCode = 429; await ctx.Response.WriteAsJsonAsync(new { error = "Too many streams are open." }); return;
            }
            // Raw changes go into a bounded queue (the slot thread only enqueues); this loop writes them out.
            var q = Channel.CreateBounded<TraceEvent>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            using var sub = slot.Hub.Subscribe([name], e => q.Writer.TryWrite(e));
            try
            {
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.ContentEncoding = "identity"; // keeps response compression from buffering the stream
                ctx.Response.Headers["X-Accel-Buffering"] = "no";
                async Task Send(string evt, SigKind kind, object? value, DateTimeOffset time)
                {
                    var data = JsonSerializer.Serialize(new { slot = n, name, kind = SignalValues.KindName(kind), value = SignalValues.ForJson(kind, value), time }, Web);
                    await ctx.Response.WriteAsync($"event: {evt}\ndata: {data}\n\n", Encoding.UTF8, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
                await Send("value", first.Kind, first.Value, DateTimeOffset.Now);
                while (!ctx.RequestAborted.IsCancellationRequested)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                    wait.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        if (!await q.Reader.WaitToReadAsync(wait.Token)) break;
                        while (q.Reader.TryRead(out var e)) await Send("change", e.Kind, e.Value, e.Time);
                    }
                    catch (OperationCanceledException) when (!ctx.RequestAborted.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": keepalive\n\n", ctx.RequestAborted);
                        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally { Interlocked.Decrement(ref streams); }
        });
    }

    // A value in the body of PUT /signals/{name}: JSON (1, true, "text", {"value": 1}) or plain text (50%, pulse, hello).
    private static async Task<string?> ReadValue(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ctx.RequestAborted);
        if (raw.Length > 100_000) return null;
        var t = raw.Trim();
        if (t.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(t);
            var v = doc.RootElement;
            if (v.ValueKind == JsonValueKind.Object) { if (!v.TryGetProperty("value", out v)) return null; }
            return v.ValueKind switch
            {
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.String => v.GetString(),
                _ => null
            };
        }
        catch (JsonException) { return t; }   // not JSON: the text itself is the value (50%, pulse, hello)
    }

    private static List<T> Read<T>(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.Deserialize<List<T>>(Web) ?? new() : throw new ArgumentException("Expected a list.");

    private static List<WebhookRule> ReadWebhooks(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected a list of webhooks.");
        var list = new List<WebhookRule>();
        foreach (var x in e.EnumerateArray())
        {
            var r = x.Deserialize<WebhookRule>(Web) ?? new WebhookRule();
            // "secret" absent or null keeps the saved one; "" removes it. (The API never sends a secret back.)
            if (!x.TryGetProperty("secret", out var sec) || sec.ValueKind == JsonValueKind.Null) r = r with { Secret = null };
            list.Add(r);
        }
        return list;
    }
}
