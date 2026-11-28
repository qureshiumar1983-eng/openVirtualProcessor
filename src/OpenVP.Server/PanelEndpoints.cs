using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using OpenVP.Core;
using OpenVP.Panels;

namespace OpenVP.Server;

/// <summary>
/// Everything behind the panel pages: the generated panel and its saved layouts, the emulator that opens an uploaded CH5 project against a
/// slot, smart-graphics projects, and the rack view. Every route here has a line in <see cref="PolicyTable"/>. None of it is served on the
/// tunnel port, which only ever serves <c>/s/{token}</c> (see <see cref="TunnelGate"/>).
/// </summary>
internal static class PanelEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly FileExtensionContentTypeProvider Mime = new();

    /// <summary>The generated panel for the slot's program (every button, slider and text box it has), or null with no program loaded.
    /// The console's generated panel and the MCP tools both use this, so they show the same controls.</summary>
    public static AutoPanelModel? AutoPanelFor(ProgramSlot slot)
    {
        var rt = slot.Runtime;
        if (rt == null) return null;
        var signals = rt.GetSignals();
        var types = slot.Devices.ToDictionary(d => d.IpId, d => d.Type);
        Func<string, string?>? deviceOf = null;
        if (rt is IProgramGraphSource g)
        {
            try
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var sym in g.GetGraph().Symbols.Where(s => s.Device))
                    foreach (var pin in sym.Inputs.Concat(sym.Outputs))
                        if (pin.Length > 0 && pin is not ("0" or "1")) map.TryAdd(pin, sym.Name);
                deviceOf = map.GetValueOrDefault;
            }
            catch (Exception) { /* a runtime that can't describe its wiring just gets prefix groups */ }
        }
        return AutoPanel.Build(slot.Number, rt.ProgramName, signals, types, deviceOf);
    }

    public static void Map(WebApplication app, RouteGroupBuilder slotApi, Processor processor, Ch5Panels ch5, SmartGraphicsStore sg, PanelLayouts layouts,
        Func<CipServer?> cip, ActivityLog activity, Func<HttpContext, string> actor, Func<int, bool> visible, Func<object?>? remoteEisc = null)
    {
        var web = app.Environment.WebRootFileProvider;
        string Template(string name)
        {
            var f = web.GetFileInfo(name);
            if (!f.Exists) return "";
            using var r = new StreamReader(f.CreateReadStream());
            return r.ReadToEnd();
        }

        // ------------------------------------------------------------------ generated panel

        slotApi.MapGet("/autopanel", (int n) =>
        {
            var slot = processor.Slot(n);
            if (AutoPanelFor(slot) is not { } model) return Results.NotFound(new { error = "No program in that slot." });
            return Results.Ok(new { model.Slot, model.Program, model.Groups, model.ControlCount, model.Truncated, state = slot.State.ToString() });
        });

        // ------------------------------------------------------------------ saved layouts

        slotApi.MapGet("/layouts", (int n) => layouts.List(n));
        slotApi.MapGet("/layouts/{name}", (int n, string name) =>
        {
            try { return layouts.Get(n, name) is { } l ? Results.Content(l.ToJsonString(Json), "application/json") : Results.NotFound(new { error = "No such layout." }); }
            catch (LayoutException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        slotApi.MapPut("/layouts/{name}", (int n, string name, JsonElement body, HttpContext ctx) =>
        {
            try
            {
                var info = layouts.Save(n, name, body, processor.Slot(n).Runtime?.ProgramName);
                activity.Add("layout.save", $"Saved the panel layout “{info.Name}” for slot {n}", n, actor(ctx));
                return Results.Ok(info);
            }
            catch (LayoutException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        slotApi.MapDelete("/layouts/{name}", (int n, string name, HttpContext ctx) =>
        {
            try
            {
                if (!layouts.Delete(n, name)) return Results.NotFound(new { error = "No such layout." });
                activity.Add("layout.delete", $"Deleted the panel layout “{name}” of slot {n}", n, actor(ctx));
                return Results.Ok();
            }
            catch (LayoutException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        // ------------------------------------------------------------------ smart graphics projects

        static object SgDto(SgInfo i) => new { slot = i.Slot, ipId = $"{i.IpId:X2}", name = i.Name, format = i.Format, pages = i.Pages, objects = i.Objects, uploadedAt = i.UploadedAt };
        app.MapGet("/api/panels/sg", () => sg.List().Where(i => visible(i.Slot)).Select(SgDto));

        static bool ParseIp(string ipid, out uint ip) => uint.TryParse(ipid, System.Globalization.NumberStyles.HexNumber, null, out ip) && ip is not (0 or > 0xFE);

        slotApi.MapGet("/panels/{ipid}/sg", (int n, string ipid) =>
        {
            if (!ParseIp(ipid, out var ip)) return Results.BadRequest(new { error = "IP ID must be 01-FE (hex)." });
            return sg.Get(n, ip) is { } p ? Results.Json(p, SmartGraphics.JsonOptions) : Results.NotFound(new { error = "No smart-graphics project for that IP ID." });
        });
        slotApi.MapGet("/panels/{ipid}/sg/assets/{name}", (int n, string ipid, string name, HttpContext ctx) =>
        {
            if (!ParseIp(ipid, out var ip)) return Results.BadRequest(new { error = "IP ID must be 01-FE (hex)." });
            var f = sg.AssetPath(n, ip, name);
            if (f == null) return Results.NotFound();
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            return Mime.TryGetContentType(f, out var ct) ? Results.File(f, ct) : Results.NotFound();
        });
        slotApi.MapPost("/panels/{ipid}/sg", async (int n, string ipid, HttpContext ctx) =>
        {
            if (!ParseIp(ipid, out var ip)) return Results.BadRequest(new { error = "IP ID must be 01-FE (hex)." });
            if (!ctx.Request.HasFormContentType) return Results.BadRequest(new { error = "Send the project as multipart/form-data field 'file' (.vtz, .c3p, .xml or .json)." });
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest(new { error = "No file." });
            if (file.Length > Ch5Panels.MaxArchiveBytes) return Results.BadRequest(new { error = $"The file is bigger than {Ch5Panels.MaxArchiveBytes / 1048576} MB." });
            try
            {
                await using var s = file.OpenReadStream();
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                ms.Position = 0;
                var p = sg.Install(n, ip, ms, Path.GetFileName(file.FileName));
                activity.Add("panel.upload", $"Uploaded the smart-graphics project {p.Name} for slot {n}, IP ID {ip:X2}", n, actor(ctx));
                return Results.Json(p, SmartGraphics.JsonOptions);
            }
            catch (Ch5Exception e) { return Results.BadRequest(new { error = e.Message }); }
        });
        slotApi.MapDelete("/panels/{ipid}/sg", (int n, string ipid, HttpContext ctx) =>
        {
            if (!ParseIp(ipid, out var ip)) return Results.BadRequest(new { error = "IP ID must be 01-FE (hex)." });
            if (!sg.Remove(n, ip)) return Results.NotFound(new { error = "No smart-graphics project for that IP ID." });
            activity.Add("panel.remove", $"Removed the smart-graphics project of slot {n}, IP ID {ip:X2}", n, actor(ctx));
            return Results.Ok();
        });

        // ------------------------------------------------------------------ emulator: a stored CH5 project, run against any slot and IP ID

        string ConfigScript(int slot, uint ip) =>
            "<script>window.OPENVP_CONFIG=" + JsonSerializer.Serialize(new { ipid = $"{ip:X2}", slot = slot.ToString(), emulate = true }, Json) + ";</script>";

        async Task<IResult> EmulateFile(HttpContext ctx, int ps, string pi, int ts, string ti, string? path)
        {
            if (!ParseIp(pi, out var projectIp) || !ParseIp(ti, out var targetIp) || !processor.IsValidSlot(ps) || !processor.IsValidSlot(ts)) return Results.NotFound();
            if (!visible(ps) || !visible(ts)) return Results.NotFound();
            if (!ch5.Exists(ps, projectIp)) return Results.NotFound(new { error = "No CH5 project for that slot and IP ID." });
            if (string.IsNullOrEmpty(path) && !ctx.Request.Path.Value!.EndsWith('/')) return Results.Redirect(ctx.Request.Path.Value + "/");
            if ((path ?? "").Split('/', '\\').Any(p => p.StartsWith('.'))) return Results.NotFound();
            var file = ch5.ResolveFile(ps, projectIp, path);
            if (file == null) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".html" or ".htm")
            {
                var html = await File.ReadAllTextAsync(file);
                // The shim goes first so it is there before the project's own scripts.
                var snippet = ConfigScript(ts, targetIp) + "<script src=\"/openvp-crcomlib.js\"></script>";
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(Inject(html, snippet));
                return Results.Empty;
            }
            if (!Mime.TryGetContentType(file, out var mime)) mime = "application/octet-stream";
            return Results.File(file, mime, enableRangeProcessing: true);
        }
        app.MapGet("/emulate/{ps:int}/{pi}/as/{ts:int}/{ti}", (HttpContext ctx, int ps, string pi, int ts, string ti) => EmulateFile(ctx, ps, pi, ts, ti, null));
        app.MapGet("/emulate/{ps:int}/{pi}/as/{ts:int}/{ti}/{**path}", (HttpContext ctx, int ps, string pi, int ts, string ti, string? path) => EmulateFile(ctx, ps, pi, ts, ti, path));

        // ------------------------------------------------------------------ rack view

        app.MapGet("/api/rack", () => Rack(processor, cip(), visible, remoteEisc));
    }

    private static string Inject(string html, string snippet)
    {
        foreach (var pattern in new[] { @"<head(\s[^>]*)?>", @"<html(\s[^>]*)?>", @"<!doctype[^>]*>" })
        {
            var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (m.Success) return html.Insert(m.Index + m.Length, snippet);
        }
        return snippet + html;
    }

    /// <summary>The processor as a rack: slots, the devices in each one's IP table with their clients and latency, and the EISC links between slots.</summary>
    internal static object Rack(Processor processor, CipServer? cip, Func<int, bool> visible, Func<object?>? remoteEisc = null)
    {
        var links = new List<object>();
        var seen = new HashSet<string>();
        var slots = new List<object>();
        foreach (var s in processor.Slots.Where(s => visible(s.Number)))
        {
            var devices = new List<object>();
            foreach (var d in s.Devices.OrderBy(d => d.IpId))
            {
                var clients = d.Connections;
                var cls = clients.Any(c => c.Transport is "EISC" or "Remote EISC") ? DeviceClass.Eisc : DeviceKinds.Classify(d.Type);
                var peers = clients.Where(c => c.Transport == "EISC").ToList();
                foreach (var c in peers)
                {
                    if (!int.TryParse(c.GetDetail("Peer slot"), out var ps) || !uint.TryParse(c.GetDetail("Peer IP ID"), System.Globalization.NumberStyles.HexNumber, null, out var pip)) continue;
                    var key = s.Number < ps || (s.Number == ps && d.IpId <= pip) ? $"{s.Number}:{d.IpId:X2}-{ps}:{pip:X2}" : $"{ps}:{pip:X2}-{s.Number}:{d.IpId:X2}";
                    if (seen.Add(key)) links.Add(new { a = new { slot = s.Number, ipId = $"{d.IpId:X2}" }, b = new { slot = ps, ipId = $"{pip:X2}" } });
                }
                devices.Add(new
                {
                    ipId = $"{d.IpId:X2}",
                    publicIpId = $"{s.PublicIpId(d.IpId):X2}",
                    type = d.Type,
                    description = d.Description,
                    kind = cls.ToString().ToLowerInvariant(),
                    online = d.Online,
                    lastOnline = d.LastOnline,
                    lastOffline = d.LastOffline,
                    clients = clients.Where(c => c.Transport is not ("EISC" or "Remote EISC")).Select(c =>
                    {
                        var h = c.LatencyHistory();
                        return new
                        {
                            id = c.Id,
                            transport = c.Transport,
                            address = c.RemoteAddress,
                            name = c.Hostname ?? c.Model ?? c.ClientKind ?? c.Transport,
                            kind = c.ClientKind,
                            connectedAt = c.ConnectedAt,
                            lastActivity = c.LastActivity,
                            latencyMs = c.LatencyMs < 0 ? (double?)null : c.LatencyMs,
                            latencySource = c.LatencySource,
                            latencyHistory = h,
                            latencyMin = h.Length > 0 ? h.Min() : (double?)null,
                            latencyAvg = h.Length > 0 ? h.Average() : (double?)null,
                            latencyMax = h.Length > 0 ? h.Max() : (double?)null,
                            joinsIn = Interlocked.Read(ref c.JoinsIn),
                            joinsOut = Interlocked.Read(ref c.JoinsOut)
                        };
                    }),
                    eisc = peers.Select(c => new { peer = c.Peer, slot = c.GetDetail("Peer slot"), ipId = c.GetDetail("Peer IP ID") }),
                    remote = clients.Where(c => c.Transport == "Remote EISC").Select(c => new
                    {
                        peer = c.Peer, link = c.GetDetail("Link"), remoteSlot = c.GetDetail("Remote slot"), remoteIpId = c.GetDetail("Remote IP ID"), connectedAt = c.ConnectedAt,
                        latencyMs = c.LatencyMs < 0 ? (double?)null : c.LatencyMs, joinsIn = Interlocked.Read(ref c.JoinsIn), joinsOut = Interlocked.Read(ref c.JoinsOut)
                    })
                });
            }
            slots.Add(new
            {
                number = s.Number,
                name = string.IsNullOrWhiteSpace(s.Settings.Name) ? null : s.Settings.Name,
                state = s.State.ToString(),
                program = s.Runtime?.ProgramName,
                kind = s.Runtime?.Kind,
                roomId = s.RoomId,
                color = s.Settings.Color,
                devices
            });
        }
        return new
        {
            now = DateTimeOffset.Now,
            cip = cip == null ? null : new { port = cip.Port, sessions = cip.SessionCount, heartbeatProbe = cip.ProbeHeartbeats },
            slots,
            links,
            remoteEisc = remoteEisc?.Invoke()
        };
    }
}
