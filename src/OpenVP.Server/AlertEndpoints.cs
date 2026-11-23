using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Alerts;

namespace OpenVP.Server;

/// <summary>
/// REST endpoints for alerts, channels and rules, thresholds, the alert stream, uptime and metrics history, the offline heat map and health
/// reports. The work is in <c>OpenVP.Core.Alerts</c>. Everything is under <c>/api</c>, so it needs the access key and is never served on the
/// internet tunnel's port. Secrets (SMTP password, ntfy token, signing secret, Teams and Slack addresses) are written but never returned.
/// </summary>
public static class AlertEndpoints
{
    private static IResult Bad(string m) => Results.BadRequest(new { error = m });
    /// <summary>JSON with enums as words (<c>"warning"</c>), the way the alert pages read them.</summary>
    private static IResult Ok(object? o) => Results.Json(o, AlertJson.Options);
    private static readonly JsonSerializerOptions J = AlertJson.Options;

    private static string? Str(JsonElement b, string key) =>
        b.ValueKind == JsonValueKind.Object && b.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Has(JsonElement b, string key) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(key, out _);

    public static TimeSpan? Range(string? r) => r switch
    {
        "5m" => TimeSpan.FromMinutes(5), "1h" => TimeSpan.FromHours(1), "24h" => TimeSpan.FromHours(24), "7d" => TimeSpan.FromDays(7),
        "30d" => TimeSpan.FromDays(30), "90d" => TimeSpan.FromDays(90), _ => null
    };

    /// <summary>A Teams or Slack address, shortened so a page can show which one is saved without giving it away.</summary>
    public static string? Hint(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Host}/…{url[^Math.Min(4, url.Length)..]}" : "saved";
    }

    public static object ChannelDto(AlertChannel c, AlertConfigStore store)
    {
        var s = store.Secrets;
        return new
        {
            id = c.Id, name = c.Name, type = c.Type, enabled = c.Enabled, url = c.Type is "webhook" or "ntfy" ? c.Url : null, topic = c.Topic, host = c.Host, port = c.Port, security = c.Security,
            username = c.Username, from = c.From, to = c.To,
            hasPassword = s.Has(c.SecretName("password")), hasToken = s.Has(c.SecretName("token")), hasSecret = s.Has(c.SecretName("secret")),
            hasUrl = c.Type is "teams" or "slack" ? s.Has(c.SecretName("url")) : (bool?)null,
            urlHint = c.Type is "teams" or "slack" ? Hint(s.Get(c.SecretName("url"))) : null
        };
    }

    private static object ConfigDto(AlertsHost host)
    {
        var c = host.Config.Current;
        return new
        {
            settings = c.Settings, channels = c.Channels.Select(ch => ChannelDto(ch, host.Config)), rules = c.Rules, thresholds = c.Thresholds, report = c.Report,
            kinds = AlertKinds.All.Select(k => new { id = k.Id, label = k.Label }),
            metrics = ThresholdMetrics.All.Select(m => new { id = m.Id, label = m.Label, unit = m.Unit, each = m.Each, max = m.Max }),
            types = AlertChannel.Types, reportLastSent = host.Reports.LastSent
        };
    }

    public static void Map(WebApplication app, Processor processor, AlertsHost host, Action<HttpContext, string, string, int?> note)
    {
        // ------------------------------------------------------------------ alerts and history
        app.MapGet("/api/alerts", (int? limit) => Ok(new
        {
            status = host.Engine.Status(), active = host.Engine.Active(), history = host.Engine.History(limit ?? 200),
            muted = host.Engine.Mutes().Select(m => new { key = m.Key, until = m.Value }), listeners = host.Stream.Listeners
        }));
        app.MapGet("/api/alerts/status", () => Ok(host.StatusDto()));

        app.MapPost("/api/alerts/{id}/ack", (HttpContext ctx, string id) =>
        {
            var who = ApiAuth.IsDirectLocal(ctx) ? Actors.ThisComputer : ctx.Connection.RemoteIpAddress?.ToString() ?? Actors.Unknown;
            if (!host.Engine.Acknowledge(id, who)) return Results.NotFound(new { error = "There's no such alert." });
            note(ctx, "alert.ack", $"Acknowledged the alert “{host.Engine.Get(id)?.Title}”", host.Engine.Get(id)?.Slot);
            return Ok(host.Engine.Get(id));
        });
        app.MapPost("/api/alerts/{id}/mute", (HttpContext ctx, string id, JsonElement body) =>
        {
            TimeSpan? span = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number ? TimeSpan.FromMinutes(Math.Clamp(m.GetDouble(), 1, 60 * 24 * 365)) : null;
            if (!host.Engine.Mute(id, span)) return Results.NotFound(new { error = "There's no such alert." });
            var a = host.Engine.Get(id);
            note(ctx, "alert.mute", $"Muted the alert “{a?.Title}” {(span == null ? "until it is unmuted" : "for " + (span.Value.TotalMinutes >= 120 ? $"{span.Value.TotalHours:0.#} h" : $"{span.Value.TotalMinutes:0} min"))}", a?.Slot);
            return Ok(a);
        });
        app.MapPost("/api/alerts/{id}/unmute", (HttpContext ctx, string id) =>
        {
            if (!host.Engine.Unmute(id)) return Results.NotFound(new { error = "That alert isn't muted." });
            note(ctx, "alert.mute", $"Unmuted the alert “{host.Engine.Get(id)?.Title}”", host.Engine.Get(id)?.Slot);
            return Ok(host.Engine.Get(id));
        });

        // ------------------------------------------------------------------ configuration
        app.MapGet("/api/alerts/config", () => Ok(ConfigDto(host)));
        app.MapGet("/api/alerts/thresholds", () => Ok(new
        {
            thresholds = host.Config.Current.Thresholds.Where(t => t.Enabled),
            metrics = ThresholdMetrics.All.Select(m => new { id = m.Id, label = m.Label, unit = m.Unit, each = m.Each })
        }));

        app.MapPut("/api/alerts/config", (HttpContext ctx, JsonElement body) =>
        {
            if (body.ValueKind != JsonValueKind.Object) return Bad("Send a JSON object.");
            try
            {
                var cur = host.Config.Current;
                var next = cur;
                var changed = new List<string>();
                if (Has(body, "settings")) { next = next with { Settings = body.GetProperty("settings").Deserialize<AlertSettings>(J) ?? cur.Settings }; changed.Add("settings"); }
                if (Has(body, "rules")) { next = next with { Rules = body.GetProperty("rules").Deserialize<List<AlertRule>>(J) ?? [] }; changed.Add("rules"); }
                if (Has(body, "thresholds")) { next = next with { Thresholds = body.GetProperty("thresholds").Deserialize<List<ThresholdDef>>(J) ?? [] }; changed.Add("thresholds"); }
                if (Has(body, "report")) { next = next with { Report = body.GetProperty("report").Deserialize<ReportSchedule>(J) ?? cur.Report }; changed.Add("report schedule"); }
                if (changed.Count == 0) return Bad("Send settings, rules, thresholds or report.");
                host.Config.Save(next);
                note(ctx, "settings", $"Changed the alert {string.Join(", ", changed)}", null);
                return Ok(ConfigDto(host));
            }
            catch (Exception e) when (e is ArgumentException or JsonException) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        // ------------------------------------------------------------------ channels
        app.MapPost("/api/alerts/channels", (HttpContext ctx, JsonElement body) => SaveChannel(ctx, null, body));
        app.MapPut("/api/alerts/channels/{id}", (HttpContext ctx, string id, JsonElement body) => SaveChannel(ctx, id, body));
        app.MapDelete("/api/alerts/channels/{id}", (HttpContext ctx, string id) =>
        {
            var cur = host.Config.Current;
            if (cur.Channel(id) is not { } ch) return Results.NotFound(new { error = "There's no such channel." });
            var next = cur with
            {
                Channels = cur.Channels.Where(c => c.Id != id).ToList(),
                Rules = cur.Rules.Select(r => r with { Channels = r.Channels.Where(c => c != id).ToArray() }).ToList(),
                Report = cur.Report with { Channels = cur.Report.Channels.Where(c => c != id).ToArray() }
            };
            host.Config.Save(next);
            note(ctx, "settings", $"Removed the alert channel “{ch.Name}”", null);
            return Ok(ConfigDto(host));
        });
        app.MapPost("/api/alerts/channels/{id}/test", async (HttpContext ctx, string id) =>
        {
            try
            {
                var d = await host.Engine.TestAsync(id);
                note(ctx, "alert.test", $"Sent a test message to “{d.ChannelName}”: {(d.Status == "sent" ? "delivered" : "failed" + (d.Note != null ? " (" + d.Note + ")" : ""))}", null);
                return Ok(d);
            }
            catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
        });

        IResult SaveChannel(HttpContext ctx, string? id, JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object) return Bad("Send a JSON object.");
            try
            {
                var cur = host.Config.Current;
                var existing = id == null ? null : cur.Channel(id);
                if (id != null && existing == null) return Results.NotFound(new { error = "There's no such channel." });
                var type = (Str(body, "type") ?? existing?.Type ?? "").ToLowerInvariant();
                if (existing != null && Has(body, "type") && type != existing.Type) return Bad("A channel's type can't be changed. Make a new channel.");
                var baseCh = existing ?? new AlertChannel { Id = AlertConfigValidator.NewId(), Type = type };
                string? S(string k, string? d) => Has(body, k) ? Str(body, k)?.Trim() : d;
                var ch = baseCh with
                {
                    Name = S("name", baseCh.Name) ?? "",
                    Enabled = body.TryGetProperty("enabled", out var en) && en.ValueKind is JsonValueKind.True or JsonValueKind.False ? en.GetBoolean() : baseCh.Enabled,
                    Url = type is "webhook" or "ntfy" ? S("url", baseCh.Url) : null, Topic = S("topic", baseCh.Topic), Host = S("host", baseCh.Host),
                    Port = body.TryGetProperty("port", out var pt) && pt.ValueKind == JsonValueKind.Number && pt.TryGetInt32(out var pi) ? pi : baseCh.Port,
                    Security = S("security", baseCh.Security) ?? "starttls", Username = S("username", baseCh.Username), From = S("from", baseCh.From), To = S("to", baseCh.To)
                };
                // Secrets: left out keeps the saved one; "" removes it.
                var secrets = new Dictionary<string, string?>();
                void Secret(string field, string what) { if (Has(body, field)) secrets[ch.SecretName(what)] = Str(body, field); }
                if (type == "email") Secret("password", "password");
                if (type == "ntfy") Secret("token", "token");
                if (type == "webhook") Secret("secret", "secret");
                if (type is "teams" or "slack")
                {
                    Secret("url", "url");
                    var addr = secrets.TryGetValue(ch.SecretName("url"), out var nv) ? nv : host.Config.Secrets.Get(ch.SecretName("url"));
                    if (string.IsNullOrWhiteSpace(addr)) return Bad($"{(type == "teams" ? "Microsoft Teams" : "Slack")} needs the incoming webhook address.");
                    if (!(Uri.TryCreate(addr.Trim(), UriKind.Absolute, out var au) && au.Scheme is "http" or "https")) return Bad("The webhook address must start with http:// or https://.");
                    secrets[ch.SecretName("url")] = addr.Trim();
                }
                var channels = cur.Channels.ToList();
                var ix = channels.FindIndex(c => c.Id == ch.Id);
                if (ix >= 0) channels[ix] = ch; else channels.Add(ch);
                var saved = host.Config.Save(cur with { Channels = channels }, secrets);
                var final = saved.Channel(ch.Id)!;
                note(ctx, "settings", $"{(existing == null ? "Added" : "Changed")} the alert channel “{final.Name}” ({final.Type})", null);
                return Ok(new { channel = ChannelDto(final, host.Config), config = ConfigDto(host) });
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        }

        // ------------------------------------------------------------------ the stream for the desktop app and browser notifications
        app.MapGet("/api/alerts/stream", async (HttpContext ctx) =>
        {
            var sub = host.Stream.Subscribe();
            if (sub == null) { ctx.Response.StatusCode = 503; await ctx.Response.WriteAsJsonAsync(new { error = "Too many windows are listening." }); return; }
            using var reg = sub.Value.Registration;
            try
            {
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.ContentEncoding = "identity";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";
                async Task Write(string s) { await ctx.Response.WriteAsync(s, Encoding.UTF8, ctx.RequestAborted); await ctx.Response.Body.FlushAsync(ctx.RequestAborted); }
                await Write($"event: status\ndata: {JsonSerializer.Serialize(host.StatusDto(), J)}\n\n");
                var reader = sub.Value.Reader;
                while (!ctx.RequestAborted.IsCancellationRequested)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                    wait.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        if (!await reader.WaitToReadAsync(wait.Token)) break;
                        while (reader.TryRead(out var frame)) await Write(frame);
                    }
                    catch (OperationCanceledException) when (!ctx.RequestAborted.IsCancellationRequested) { await Write(": keepalive\n\n"); }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });

        // ------------------------------------------------------------------ uptime
        app.MapGet("/api/uptime", (string? range, int? buckets) =>
        {
            var span = Range(range ?? "24h");
            if (span == null || span < TimeSpan.FromHours(1)) return Bad("range must be 1h, 24h, 7d, 30d or 90d.");
            var to = DateTimeOffset.UtcNow; var from = to - span.Value;
            var r = host.Uptime.Report(from, to, Math.Clamp(buckets ?? 96, 12, 240));
            Dictionary<string, object?> Row(EntityUptime e, string? name, string? label, string? state) => new()
            {
                ["slot"] = e.Key.Slot, ["ipId"] = e.Key.Kind == 'd' ? e.Key.IpId.ToString("X2") : null, ["name"] = name, ["label"] = label, ["state"] = state,
                ["availability"] = e.Availability, ["upSeconds"] = e.UpSeconds, ["downSeconds"] = e.DownSeconds, ["drops"] = e.Drops,
                ["buckets"] = e.Buckets, ["outages"] = e.Outages.Select(o => new { from = o.From, to = o.To }).ToList()
            };
            return Ok(new
            {
                range = range ?? "24h", from = r.From, to = r.To, since = r.Since, first = host.Uptime.FirstRecord(),
                slots = r.Entities.Where(e => e.Key.Kind == 's').Select(e =>
                {
                    var s = processor.IsValidSlot(e.Key.Slot) ? processor.Slot(e.Key.Slot) : null;
                    return Row(e, s?.Name, s?.Runtime?.ProgramName, s?.State.ToString());
                }).ToList(),
                devices = r.Entities.Where(e => e.Key.Kind == 'd').Select(e =>
                {
                    var s = processor.IsValidSlot(e.Key.Slot) ? processor.Slot(e.Key.Slot) : null;
                    var d = s?.FindDevice((uint)e.Key.IpId);
                    return Row(e, s?.Name, d == null ? null : (string.IsNullOrWhiteSpace(d.Description) ? d.Type : d.Description), d == null ? "gone" : d.Online ? "online" : "offline");
                }).ToList()
            });
        });

        app.MapGet("/api/uptime/heatmap", (string? range, string? mode, int? offset) =>
        {
            var span = Range(range ?? "7d");
            if (span == null || span < TimeSpan.FromHours(24)) return Bad("range must be 24h, 7d, 30d or 90d.");
            var to = DateTimeOffset.UtcNow;
            var off = offset ?? (int)TimeZoneInfo.Local.GetUtcOffset(to).TotalMinutes;
            var h = host.Uptime.Heat(to - span.Value, to, mode == "day" ? "day" : "hour", Math.Clamp(off, -14 * 60, 14 * 60));
            return Ok(new
            {
                mode = h.Mode, from = h.From, to = h.To, columns = h.Columns,
                rows = h.Rows.Select(r =>
                {
                    var s = processor.IsValidSlot(r.Slot) ? processor.Slot(r.Slot) : null;
                    var d = s?.FindDevice((uint)r.IpId);
                    return new { slot = r.Slot, ipId = r.IpId.ToString("X2"), label = d == null ? null : (string.IsNullOrWhiteSpace(d.Description) ? d.Type : d.Description), slotName = s?.Name, minutes = r.Minutes, drops = r.Drops, total = r.TotalMinutes };
                })
            });
        });

        // ------------------------------------------------------------------ metrics history
        app.MapGet("/api/metrics/history", (string? range) =>
        {
            var span = Range(range ?? "1h");
            if (span == null) return Bad("range must be 5m, 1h, 24h, 7d, 30d or 90d.");
            var s = host.History.Query(range ?? "1h", span.Value);
            return Ok(new
            {
                range = s.Range, stepSeconds = s.StepSeconds, from = s.From, to = s.To,
                points = s.Points.Select(p => new
                {
                    t = p.Time, n = p.Seconds, cpu = p.Cpu, cpuMax = p.CpuMax, mem = p.Mem, memMax = p.MemMax, threads = p.Threads, offline = p.Offline, offlineMax = p.OfflineMax,
                    slots = p.Slots.Select(x => new { slot = x.Slot, busy = x.Busy, busyMax = x.BusyMax, eps = x.Eps, epsMax = x.EpsMax, queue = x.Queue, queueMax = x.QueueMax, eventMs = x.EventMsMax })
                })
            });
        });

        // ------------------------------------------------------------------ health reports
        app.MapGet("/api/reports/preview", (string? period, string? download) =>
        {
            var p = AlertsHost.PeriodName(period ?? "daily");
            var html = HealthReport.Html(host.BuildReport(p));
            return download is "1" or "true"
                ? Results.File(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", $"openvp-{p}-report-{DateTime.Now:yyyyMMdd-HHmm}.html")
                : Results.Content(html, "text/html; charset=utf-8");
        });
        app.MapGet("/api/reports/data", (string? period) => Ok(host.BuildReport(AlertsHost.PeriodName(period ?? "daily"))));
        app.MapPost("/api/reports/send", async (HttpContext ctx, JsonElement body) =>
        {
            var period = AlertsHost.PeriodName(Str(body, "period") ?? host.Config.Current.Report.Frequency);
            var chans = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("channels", out var cs) && cs.ValueKind == JsonValueKind.Array
                ? cs.EnumerateArray().Select(c => c.GetString() ?? "").ToList() : host.Config.Current.Report.Channels.ToList();
            try
            {
                var v = await host.SendReportAsync(period, chans);
                return Ok(v);
            }
            catch (InvalidOperationException e) { return Bad(e.Message); }
        });
    }
}
