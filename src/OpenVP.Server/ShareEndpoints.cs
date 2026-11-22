using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using OpenVP.Core;
using OpenVP.Panels;

namespace OpenVP.Server;

/// <summary>
/// Shared panels. Two groups of endpoints:
///   - <c>/api/shares</c>, <c>/api/panels</c>, <c>/api/tunnel</c>, <c>/api/network</c>: for the console, protected by the
///     access key like the rest of /api.
///   - <c>/s/{token}/...</c>: what a link holder gets. The token is the credential; the slot and IP ID come from the
///     share, never from the request. These are the only paths the tunnel port serves (see <see cref="TunnelGate"/>).
/// </summary>
internal static class ShareEndpoints
{
    public const string PinCookie = "openvp_share_pin";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly FileExtensionContentTypeProvider Mime = MakeMime();

    private static FileExtensionContentTypeProvider MakeMime()
    {
        var p = new FileExtensionContentTypeProvider();
        p.Mappings[".mjs"] = "text/javascript";
        p.Mappings[".js"] = "text/javascript";
        p.Mappings[".wasm"] = "application/wasm";
        p.Mappings[".webmanifest"] = "application/manifest+json";
        p.Mappings[".woff2"] = "font/woff2";
        p.Mappings[".map"] = "application/json";
        return p;
    }

    public static void Map(WebApplication app, Processor processor, ShareStore shares, Ch5Panels ch5, TunnelManager? tunnel, TunnelGate gate, string? tunnelUnavailable)
    {
        var web = app.Environment.WebRootFileProvider;

        string Template(string name)
        {
            var f = web.GetFileInfo(name);
            if (!f.Exists) return "";
            using var r = new StreamReader(f.CreateReadStream());
            return r.ReadToEnd();
        }

        static string Html(string s) => System.Net.WebUtility.HtmlEncode(s);

        // ------------------------------------------------------------------ console API

        object ShareDto(ShareLink l) => new
        {
            id = l.Id,
            token = l.Token,
            path = "/s/" + l.Token,
            label = l.Label,
            slot = l.Slot,
            ipId = $"{l.IpId:X2}",
            kind = ch5.Exists(l.Slot, l.IpId) ? "ch5" : "test",
            hasPin = l.HasPin,
            createdAt = l.CreatedAt,
            expiresAt = l.ExpiresAt,
            lastUsed = l.LastUsed,
            connected = shares.Connections(l.Id)
        };

        app.MapGet("/api/shares", () => shares.Active().Select(ShareDto));

        app.MapPost("/api/shares", (JsonElement body) =>
        {
            try
            {
                var slot = body.TryGetProperty("slot", out var s) && s.TryGetInt32(out var si) ? si : 0;
                if (!processor.IsValidSlot(slot)) return Results.BadRequest(new { error = $"Slot must be 1-{processor.Slots.Count}." });
                var ipText = body.TryGetProperty("ipId", out var ip) ? ip.ToString() : "";
                if (!uint.TryParse(ipText, System.Globalization.NumberStyles.HexNumber, null, out var ipId) || ipId is 0 or > 0xFE)
                    return Results.BadRequest(new { error = "IP ID must be 01-FE (hex)." });
                var label = body.TryGetProperty("label", out var lb) ? lb.GetString() : null;
                var pin = body.TryGetProperty("pin", out var pn) && pn.ValueKind != JsonValueKind.Null ? pn.ToString() : null;
                var expires = body.TryGetProperty("expires", out var ex) ? ex.GetString() : "never";
                var link = shares.Create(slot, ipId, label, string.IsNullOrEmpty(pin) ? null : pin, ShareStore.ParseExpiry(expires));
                return Results.Ok(ShareDto(link));
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        app.MapDelete("/api/shares/{id}", (string id) => shares.Revoke(id) ? Results.Ok() : Results.NotFound(new { error = "No such link (maybe it already expired)." }));

        // Undo a revoke (the console's "Undo" toast): the same link, token and PIN come back, if it was revoked in the last few minutes.
        app.MapPost("/api/shares/{id}/undo", (string id) => shares.Undo(id) is { } l ? Results.Ok(ShareDto(l)) : Results.NotFound(new { error = "That link can't be brought back (it was revoked too long ago, or has expired)." }));

        object Ch5Dto(Ch5Info i) => new { slot = i.Slot, ipId = $"{i.IpId:X2}", name = i.Name, uploadedAt = i.UploadedAt, files = i.Files, bytes = i.Bytes };
        app.MapGet("/api/panels", () => ch5.List().Select(Ch5Dto));

        app.MapPost("/api/slots/{n:int}/panels/{ipid}/ch5", async (int n, string ipid, HttpRequest req) =>
        {
            if (!processor.IsValidSlot(n)) return Results.NotFound(new { error = $"Slot must be 1-{processor.Slots.Count}." });
            if (!uint.TryParse(ipid, System.Globalization.NumberStyles.HexNumber, null, out var ipId) || ipId is 0 or > 0xFE)
                return Results.BadRequest(new { error = "IP ID must be 01-FE (hex)." });
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "Send the project as multipart/form-data field 'file' (a .ch5z)." });
            var form = await req.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest(new { error = "No file." });
            if (file.Length > Ch5Panels.MaxArchiveBytes) return Results.BadRequest(new { error = $"The archive is bigger than {Ch5Panels.MaxArchiveBytes / 1048576} MB." });
            try
            {
                await using var s = file.OpenReadStream();
                // ZipArchive needs a seekable stream.
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                ms.Position = 0;
                return Results.Ok(Ch5Dto(ch5.Install(n, ipId, ms, Path.GetFileName(file.FileName))));
            }
            catch (Ch5Exception e) { return Results.BadRequest(new { error = e.Message }); }
        });

        app.MapDelete("/api/slots/{n:int}/panels/{ipid}/ch5", (int n, string ipid) =>
        {
            if (!uint.TryParse(ipid, System.Globalization.NumberStyles.HexNumber, null, out var ipId) || ipId is 0 or > 0xFE)
                return Results.BadRequest(new { error = "IP ID must be 01-FE (hex)." });
            return ch5.Remove(n, ipId) ? Results.Ok() : Results.NotFound(new { error = "No CH5 project for that IP ID." });
        });

        // What address would work for someone else: this machine's LAN addresses (the console may have been opened as localhost).
        app.MapGet("/api/network", () => new
        {
            host = Dns.GetHostName(),
            lan = LanAddresses(),
            tunnelPort = tunnel?.Port
        });

        object TunnelDto(TunnelStatus s) => new
        {
            available = tunnel != null,
            unavailableReason = tunnelUnavailable,
            state = s.State.ToString().ToLowerInvariant(),
            url = s.Url,
            message = s.Message,
            needsDownload = s.NeedsDownload,
            downloadUrl = s.DownloadUrl,
            binary = s.Binary,
            port = tunnel?.Port,
            log = s.Log
        };
        var idle = new TunnelStatus(TunnelState.Off, null, tunnelUnavailable, false, null, null, Array.Empty<string>());
        app.MapGet("/api/tunnel", () => TunnelDto(tunnel?.Status() ?? idle));
        app.MapPost("/api/tunnel/start", async (HttpContext ctx) =>
        {
            if (tunnel == null) return Results.Json(new { error = tunnelUnavailable ?? "Internet access is not available." }, statusCode: 409);
            var download = false;
            try
            {
                if (ctx.Request.ContentLength > 0 && await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body) is { ValueKind: JsonValueKind.Object } b
                    && b.TryGetProperty("download", out var d)) download = d.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { }
            // Keep going in the background (the download or cloudflared's start-up can take a while); the console polls.
            var run = tunnel.StartAsync(download);
            await Task.WhenAny(run, Task.Delay(2000));
            return Results.Ok(TunnelDto(tunnel.Status()));
        });
        app.MapPost("/api/tunnel/stop", () =>
        {
            tunnel?.Stop();
            return Results.Ok(TunnelDto(tunnel?.Status() ?? idle));
        });

        // ------------------------------------------------------------------ what a link holder gets

        string ClientKey(HttpContext ctx)
        {
            // cloudflared connects from 127.0.0.1 and passes the visitor's address on; only trust that on the tunnel port.
            if (gate.IsTunnelRequest(ctx))
            {
                var cf = ctx.Request.Headers["Cf-Connecting-IP"].ToString();
                if (cf.Length is > 0 and <= 64) return cf;
            }
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip is { IsIPv4MappedToIPv6: true }) ip = ip.MapToIPv4();
            return ip?.ToString() ?? "?";
        }

        static void Harden(HttpContext ctx)
        {
            var h = ctx.Response.Headers;
            h["Referrer-Policy"] = "no-referrer";           // the address holds the secret; never leak it to other sites
            h["X-Content-Type-Options"] = "nosniff";
            h["Cache-Control"] = "no-store";
        }

        static bool IsSecure(HttpContext ctx) =>
            ctx.Request.IsHttps
            || string.Equals(ctx.Request.Headers["X-Forwarded-Proto"].ToString(), "https", StringComparison.OrdinalIgnoreCase)
            || ctx.Request.Headers["CF-Visitor"].ToString().Contains("\"https\"");

        async Task Gone(HttpContext ctx)
        {
            Harden(ctx);
            ctx.Response.StatusCode = StatusCodes.Status410Gone;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(Template("share/gone.html"));
        }

        bool IsAuthorized(HttpContext ctx, ShareLink link) => shares.Authorized(link, ctx.Request.Cookies[PinCookie]);

        string Inject(string html, string snippet)
        {
            foreach (var pattern in new[] { @"<head(\s[^>]*)?>", @"<html(\s[^>]*)?>", @"<!doctype[^>]*>" })
            {
                var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (m.Success) return html.Insert(m.Index + m.Length, snippet);
            }
            return snippet + html;
        }

        // The settings the share page needs before openvp-crcomlib.js runs.
        string ConfigScript(ShareLink l) =>
            "<script>window.OPENVP_CONFIG=" + JsonSerializer.Serialize(new
            {
                share = true,
                wsPath = $"/s/{l.Token}/ws",
                statusUrl = $"/s/{l.Token}/status",
                label = l.Label
            }, Json) + ";</script>";

        async Task SharePage(HttpContext ctx, string token)
        {
            var link = shares.FindByToken(token);
            if (link == null) { await Gone(ctx); return; }
            Harden(ctx);
            if (!IsAuthorized(ctx, link))
            {
                var note = shares.IsLocked(link, ClientKey(ctx), out var wait)
                    ? $"Too many wrong PINs. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minute(s)."
                    : "";
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(Template("share/pin.html")
                    .Replace("{{label}}", Html(string.IsNullOrEmpty(link.Label) ? "Panel" : link.Label))
                    .Replace("{{note}}", Html(note))
                    .Replace("{{token}}", link.Token));
                return;
            }
            shares.Touch(link.Id);
            if (ch5.Exists(link.Slot, link.IpId)) { ctx.Response.Redirect($"/s/{link.Token}/app/"); return; }
            // No CH5 project: OpenVP's own test panel, locked to this slot and IP ID.
            var panel = Template("panel.html");
            if (panel.Length == 0) { ctx.Response.StatusCode = 500; return; }
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(Inject(panel, $"<base href=\"/s/{link.Token}/\">" + ConfigScript(link)));
        }

        app.MapGet("/s/{token}", (HttpContext ctx, string token) => SharePage(ctx, token));

        app.MapGet("/s/{token}/status", async (HttpContext ctx, string token) =>
        {
            Harden(ctx);
            if (shares.FindByToken(token) == null) { ctx.Response.StatusCode = StatusCodes.Status410Gone; return; }
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPost("/s/{token}/pin", async (HttpContext ctx, string token) =>
        {
            Harden(ctx);
            if (ApiAuth.IsCrossOrigin(ctx)) { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
            var link = shares.FindByToken(token);
            if (link == null) { ctx.Response.StatusCode = StatusCodes.Status410Gone; return; }
            string? pin = null;
            try
            {
                if (ctx.Request.ContentLength is > 0 and < 1024)
                {
                    var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
                    if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("pin", out var p)) pin = p.ToString();
                }
            }
            catch (JsonException) { }
            var res = shares.CheckPin(link, pin, ClientKey(ctx));
            if (res.Outcome == PinOutcome.Ok)
            {
                var remaining = link.ExpiresAt is { } e ? e - DateTimeOffset.UtcNow : TimeSpan.FromDays(30);
                ctx.Response.Cookies.Append(PinCookie, shares.ProofFor(link), new CookieOptions
                {
                    Path = $"/s/{link.Token}",   // only this link ever sees the cookie
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = IsSecure(ctx),
                    MaxAge = remaining < TimeSpan.FromDays(30) ? remaining : TimeSpan.FromDays(30)
                });
                await ctx.Response.WriteAsJsonAsync(new { ok = true });
                return;
            }
            if (res.Outcome == PinOutcome.Locked)
            {
                ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(res.RetryAfter.TotalSeconds)).ToString();
                await ctx.Response.WriteAsJsonAsync(new { error = $"Too many wrong PINs. Try again in {Math.Max(1, (int)Math.Ceiling(res.RetryAfter.TotalMinutes))} minute(s)." });
                return;
            }
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { error = $"Wrong PIN. {res.AttemptsLeft} {(res.AttemptsLeft == 1 ? "try" : "tries")} left." });
        });

        // The shim every shared page uses, from the link's own address so nothing outside /s/ is needed.
        app.MapGet("/s/{token}/openvp-crcomlib.js", async (HttpContext ctx, string token) =>
        {
            if (shares.FindByToken(token) == null) { await Gone(ctx); return; }
            var f = web.GetFileInfo("openvp-crcomlib.js");
            if (!f.Exists) { ctx.Response.StatusCode = 404; return; }
            Harden(ctx);
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/javascript; charset=utf-8";
            await using var s = f.CreateReadStream();
            await s.CopyToAsync(ctx.Response.Body);
        });

        // The CH5 project for this link's slot and IP ID.
        async Task<IResult> AppFile(HttpContext ctx, string token, string? path)
        {
            var link = shares.FindByToken(token);
            if (link == null) { await Gone(ctx); return Results.Empty; }
            if (!IsAuthorized(ctx, link)) return Results.Redirect($"/s/{link.Token}");
            if (!ch5.Exists(link.Slot, link.IpId)) return Results.Redirect($"/s/{link.Token}");
            if (string.IsNullOrEmpty(path) && !ctx.Request.Path.Value!.EndsWith('/')) return Results.Redirect($"/s/{link.Token}/app/");
            if ((path ?? "").Split('/', '\\').Any(p => p.StartsWith('.'))) return Results.NotFound();   // no dotfiles
            var file = ch5.ResolveFile(link.Slot, link.IpId, path);
            if (file == null) return Results.NotFound();
            Harden(ctx);
            ctx.Response.Headers.CacheControl = "no-cache";
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".html" or ".htm")
            {
                shares.Touch(link.Id);
                var html = await File.ReadAllTextAsync(file);
                // The shim goes first so it is there before the project's own scripts.
                var snippet = ConfigScript(link) + $"<script src=\"/s/{link.Token}/openvp-crcomlib.js\"></script>";
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(Inject(html, snippet));
                return Results.Empty;
            }
            if (!Mime.TryGetContentType(file, out var ct)) ct = "application/octet-stream";
            return Results.File(file, ct, enableRangeProcessing: true);
        }
        app.MapGet("/s/{token}/app", (HttpContext ctx, string token) => AppFile(ctx, token, null));
        app.MapGet("/s/{token}/app/{**path}", (HttpContext ctx, string token, string? path) => AppFile(ctx, token, path));

        // The WebSocket. Slot and IP ID come from the share, nothing else from the request.
        app.Map("/s/{token}/ws", async (HttpContext ctx, string token) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            var link = shares.FindByToken(token);
            if (link == null || !processor.IsValidSlot(link.Slot)) { ctx.Response.StatusCode = StatusCodes.Status410Gone; return; }
            if (!IsAuthorized(ctx, link)) { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            if (ApiAuth.IsCrossOrigin(ctx)) { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return; }

            var remote = ctx.Connection.RemoteIpAddress;
            if (remote is { IsIPv4MappedToIPv6: true }) remote = remote.MapToIPv4();
            var who = gate.IsTunnelRequest(ctx) ? ClientKey(ctx) : remote?.ToString();
            var client = new DeviceConnection("Web panel", who, gate.IsTunnelRequest(ctx) ? 0 : ctx.Connection.RemotePort);
            var ua = ctx.Request.Headers.UserAgent.ToString();
            if (ua.Length > 0) ClientIdentifier.ApplyUserAgent(client, ua.Length > 500 ? ua[..500] : ua);
            client.SetDetail("Shared link", string.IsNullOrEmpty(link.Label) ? link.Id : $"{link.Label} ({link.Id})");
            client.SetDetail("Via", gate.IsTunnelRequest(ctx) ? "internet tunnel" : "direct");

            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            using var open = shares.OpenConnection(link.Id);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            // Revoked or expired: close within about a second.
            var watcher = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cts.Token);
                        if (!shares.IsActive(link.Id))
                        {
                            cts.Cancel();
                            await Task.Delay(2000);
                            if (ws.State != WebSocketState.Closed) ws.Abort();   // don't wait on a client that won't answer the close
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) { }
            });
            try { await new WebPanelSession(ws, processor, link.IpId, link.Slot, client).RunAsync(cts.Token); }
            finally { cts.Cancel(); try { await watcher; } catch { } }
        });
    }

    public static IReadOnlyList<object> LanAddresses()
    {
        var res = new List<object>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    var a = ua.Address;
                    if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a)) continue;
                    var s = a.ToString();
                    if (s.StartsWith("169.254.")) continue;   // link-local, not reachable by anyone else
                    res.Add(new { address = s, name = nic.Name });
                }
            }
        }
        catch (NetworkInformationException) { }
        return res;
    }
}
