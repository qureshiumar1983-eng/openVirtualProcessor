using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using OpenVP.Core;
using OpenVP.Core.Community;
using OpenVP.Core.Eisc;
using OpenVP.Core.Labs;
using OpenVP.Core.Ops;
using OpenVP.Core.Plugins;
using OpenVP.Core.Security;
using OpenVP.Lpz;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

namespace OpenVP.Server;

/// <summary>Everything the Community and Labs endpoints use, built once in <see cref="OpenVpServer.Create"/>.</summary>
public sealed class CommunityServices
{
    public required Processor Processor { get; init; }
    public required LabsStore Labs { get; init; }
    public required RemoteEiscStore Links { get; init; }
    public required RemoteEiscManager Eisc { get; init; }
    public required PluginHost Plugins { get; init; }
    public required TargetStore Targets { get; init; }
    public required ActivityLog Activity { get; init; }
    public required OpsServices Ops { get; init; }
    public required Func<HttpContext, string> Who { get; init; }
    public required Func<int, bool> Visible { get; init; }
    public required string ExamplesDirectory { get; init; }
}

/// <summary>
/// The Community and Labs endpoints: the Labs switches, remote EISC links (and the WebSocket their other end connects to), plugins, the compatibility
/// dashboard, the one-click issue report and the example library. Every route has a line in <see cref="PolicyTable"/>; none is served on the tunnel port.
/// </summary>
public static class CommunityEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    public static void Map(WebApplication app, CommunityServices s)
    {
        MapLabs(app, s);
        MapEisc(app, s);
        MapPlugins(app, s);
        MapCompat(app, s);
        MapExamples(app, s);
    }

    // =================================================================== Labs

    private static void MapLabs(WebApplication app, CommunityServices s)
    {
        app.MapGet("/api/labs", () => new { features = s.Labs.List() });
        app.MapPut("/api/labs/{id}", (string id, HttpContext ctx, JsonElement body) =>
        {
            var f = LabsStore.Find(id);
            if (f == null) return Error(404, "There's no such Labs feature.");
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("enabled", out var en) || en.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Error(400, "Send {\"enabled\": true} or {\"enabled\": false}.");
            try { s.Labs.Set(f.Id, en.GetBoolean()); }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
            s.Activity.Add("labs", $"{(en.GetBoolean() ? "Switched on" : "Switched off")} the Labs feature “{f.Name}”", null, s.Who(ctx));
            return Results.Ok(new { features = s.Labs.List() });
        });
    }

    // =================================================================== Remote EISC

    private static void MapEisc(WebApplication app, CommunityServices s)
    {
        object LinkDto(RemoteEiscLink l)
        {
            var t = s.Targets.Get(l.TargetId);
            var st = s.Eisc.Status(l.Id);
            return new
            {
                id = l.Id, name = l.Name, targetId = l.TargetId, targetName = t?.Name, targetUrl = t?.Url, hasKey = t != null && s.Targets.HasKey(t.Id),
                localSlot = l.LocalSlot, localIpId = l.LocalIpId, remoteSlot = l.RemoteSlot, remoteIpId = l.RemoteIpId, enabled = l.Enabled,
                status = st == null ? null : StatusDto(st)
            };
        }

        app.MapGet("/api/eisc-links", () => new
        {
            labsOn = s.Labs.IsOn(LabsCatalog.RemoteEisc), instance = s.Eisc.Instance,
            links = s.Links.List().Select(LinkDto),
            inbound = s.Eisc.Inbound().Select(i => new { slot = i.Slot, ipId = i.IpId, remote = i.Remote, latencyMs = i.LatencyMs, since = i.Since, joinsIn = i.JoinsIn, joinsOut = i.JoinsOut, peerVersion = i.PeerVersion }),
            targets = s.Targets.List().Select(t => new { id = t.Id, name = t.Name, url = t.Url, hasKey = s.Targets.HasKey(t.Id) })
        });

        RemoteEiscLink? FromBody(JsonElement b, out string? error)
        {
            error = null;
            if (b.ValueKind != JsonValueKind.Object) { error = "Send the link as a JSON object."; return null; }
            string Str(string n) => b.TryGetProperty(n, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
            int Int(string n) => b.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.Number ? v.TryGetInt32(out var i) : int.TryParse(v.GetString(), out i)) ? i : 0;
            return new RemoteEiscLink
            {
                Name = Str("name"), TargetId = Str("targetId"), LocalSlot = Int("localSlot"), LocalIpId = Str("localIpId"), RemoteSlot = Int("remoteSlot"), RemoteIpId = Str("remoteIpId"),
                Enabled = !b.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False
            };
        }

        app.MapPost("/api/eisc-links", (HttpContext ctx, JsonElement body) =>
        {
            var l = FromBody(body, out var err);
            if (l == null) return Error(400, err!);
            try
            {
                var c = s.Links.Add(l, s.Processor.Slots.Count, id => s.Targets.Get(id) != null);
                s.Activity.Add("eisc.link", $"Added the remote EISC link “{c.Name}” (slot {c.LocalSlot} EISC {c.LocalIpId} to slot {c.RemoteSlot} EISC {c.RemoteIpId} on {s.Targets.Get(c.TargetId)?.Name})", c.LocalSlot, s.Who(ctx));
                return Results.Json(LinkDto(c));
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        app.MapPut("/api/eisc-links/{id}", (string id, HttpContext ctx, JsonElement body) =>
        {
            var l = FromBody(body, out var err);
            if (l == null) return Error(400, err!);
            try
            {
                var c = s.Links.Update(id, l, s.Processor.Slots.Count, tid => s.Targets.Get(tid) != null);
                if (c == null) return Error(404, "There's no such link.");
                s.Activity.Add("eisc.link", $"Changed the remote EISC link “{c.Name}”", c.LocalSlot, s.Who(ctx));
                return Results.Json(LinkDto(c));
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        app.MapDelete("/api/eisc-links/{id}", (string id, HttpContext ctx) =>
        {
            var l = s.Links.Get(id);
            if (l == null || !s.Links.Remove(id)) return Error(404, "There's no such link.");
            s.Activity.Add("eisc.link", $"Removed the remote EISC link “{l.Name}”", l.LocalSlot, s.Who(ctx));
            return Results.Ok();
        });

        // The other end of a link: ws(s)://this-host/ws/eisc?slot=N&ipid=XX, authenticated with an API token of scope eisc-link (or an Admin).
        app.Map("/ws/eisc", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            async Task Refuse(int code, string text) { ctx.Response.StatusCode = code; await ctx.Response.WriteAsync(text); }
            if (!s.Labs.IsOn(LabsCatalog.RemoteEisc)) { await Refuse(404, "Remote EISC is switched off in Labs on this OpenVP."); return; }
            if (!int.TryParse(ctx.Request.Query["slot"], out var slot) || !s.Processor.IsValidSlot(slot)) { await Refuse(404, "No such slot."); return; }
            if (!RemoteEiscStore.TryIpId(ctx.Request.Query["ipid"], out var ipid)) { await Refuse(400, "ipid (hex, 03-FE) is required."); return; }
            if (!(Principal.Current?.CanSlot(slot) ?? true)) { await Refuse(403, $"This token isn't allowed to use slot {slot}."); return; }
            var dev = s.Processor.Slot(slot).FindDevice(ipid);
            if (dev == null) { await Refuse(404, $"Slot {slot} has no device with IP ID {ipid:X2} (is its program loaded and started?)."); return; }
            if (DeviceKinds.Classify(dev.Type) != DeviceClass.Eisc) { await Refuse(409, $"IP ID {ipid:X2} in slot {slot} isn't an EISC."); return; }
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote is { IsIPv4MappedToIPv6: true }) remote = remote.MapToIPv4();
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await s.Eisc.RunInboundAsync(ws, dev, remote?.ToString() ?? "", ctx.Connection.RemotePort, ctx.RequestAborted);
        });
    }

    internal static object StatusDto(RemoteEiscStatus st) => new
    {
        state = st.State, message = st.Message, latencyMs = st.LatencyMs, connectedSince = st.ConnectedSince, reconnects = st.Reconnects, nextRetry = st.NextRetry,
        joinsIn = st.JoinsIn, joinsOut = st.JoinsOut, peerVersion = st.PeerVersion
    };

    /// <summary>The links for the rack view: state and latency, no addresses or keys.</summary>
    internal static object RackLinks(CommunityServices s, Func<int, bool> visible) => new
    {
        enabled = s.Labs.IsOn(LabsCatalog.RemoteEisc),
        outbound = s.Links.List().Where(l => visible(l.LocalSlot)).Select(l =>
        {
            var st = s.Eisc.Status(l.Id);
            return new { id = l.Id, name = l.Name, localSlot = l.LocalSlot, localIpId = l.LocalIpId, remoteSlot = l.RemoteSlot, remoteIpId = l.RemoteIpId, remoteName = s.Targets.Get(l.TargetId)?.Name, enabled = l.Enabled,
                state = st?.State ?? "off", message = st?.Message, latencyMs = st?.LatencyMs, connectedSince = st?.ConnectedSince, reconnects = st?.Reconnects ?? 0 };
        }),
        inbound = s.Eisc.Inbound().Where(i => visible(i.Slot)).Select(i => new { slot = i.Slot, ipId = i.IpId, latencyMs = i.LatencyMs, since = i.Since })
    };

    // =================================================================== Plugins

    private static void MapPlugins(WebApplication app, CommunityServices s)
    {
        app.MapGet("/api/plugins", () => new
        {
            active = s.Plugins.Active, folder = s.Plugins.Folder,
            plugins = s.Plugins.List(),
            permissions = PluginPermissions.All.Select(p => new { name = p, description = PluginPermissions.Describe(p) })
        });

        app.MapPost("/api/plugin-admin/{name}/enable", (string name, HttpContext ctx, JsonElement body) =>
        {
            var approved = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("permissions", out var pa) && pa.ValueKind == JsonValueKind.Array
                ? pa.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
            try
            {
                var v = s.Plugins.Enable(name, approved, Principal.Current?.Display ?? s.Who(ctx));
                return v.Status == "running" || v.Status == "off" && v.Error == null ? Results.Json(v, Web) : Error(422, v.Error ?? "The plugin didn't start.");
            }
            catch (KeyNotFoundException e) { return Error(404, e.Message); }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        app.MapPost("/api/plugin-admin/{name}/disable", (string name) => s.Plugins.Disable(name) ? Results.Ok() : Error(404, "That plugin isn't enabled."));

        // A plugin's own routes and its page: /api/plugins/<name>/<route> and /api/plugins/<name>/ui/<file>.
        app.Map("/api/plugins/{name}/{**path}", async (HttpContext ctx, string name, string? path) =>
        {
            if (!s.Plugins.Active) return Error(404, "Plugins is switched off in Labs.");
            path = (path ?? "").Trim('/');
            if (path == "ui" || path.StartsWith("ui/"))
            {
                if (!HttpMethods.IsGet(ctx.Request.Method)) return Error(405, "Plugin pages are read-only.");
                var file = s.Plugins.UiFile(name, path.Length > 3 ? path[3..] : "");
                if (file == null) return Error(404, "No such plugin page.");
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                return Results.File(file.Value.Data, file.Value.ContentType);
            }
            var p = Principal.Current;
            if (!HttpMethods.IsGet(ctx.Request.Method) && !(p?.Has(Perm.Operate) ?? false)) return Error(403, "Your account isn't allowed to do that (a plugin route that changes things needs Operate).");
            string text;
            using (var rd = new StreamReader(ctx.Request.Body)) { var buf = new char[1 << 20]; var n = await rd.ReadBlockAsync(buf, 0, buf.Length); text = new string(buf, 0, n); }
            var q = ctx.Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            var req = new PluginRequest(ctx.Request.Method, path, q, text, p?.Name ?? "");
            var resp = await Task.Run(() => s.Plugins.Handle(name, req));
            if (resp == null) return Error(404, "No such plugin route (is the plugin enabled?).");
            return Results.Text(resp.Body, resp.ContentType, System.Text.Encoding.UTF8, resp.Status);
        });
    }

    // =================================================================== Compatibility dashboard and the issue report

    private sealed record Row(string Kind, string Name, string Detail, int Seen, string Category, string Status, string? First, string? Last);

    private static (object Dto, string Code) ProgramDto(ProgramSlot slot)
    {
        var rt = slot.Runtime;
        var problems = new List<ReadinessProblem>();
        string? blocker = null;
        if (rt is IProgramAnalysis a)
        {
            try { problems.AddRange(a.Problems()); blocker = a.Blocker; } catch (Exception) { }
        }
        var verdict = Readiness.Evaluate(problems, slot.State == SlotState.Error ? slot.LastError : null, blocker);
        int supported, partial = 0, unsupported;
        string unit;
        switch (rt)
        {
            case SimplSharpRuntime ss:
            {
                var c = ss.Compat;
                var partialTypes = c.MissingMembers.Select(m => m[..Math.Max(0, m.LastIndexOf('.'))]).Where(t => !c.MissingTypes.Contains(t)).Distinct().Count();
                unsupported = c.MissingTypes.Count; partial = partialTypes; supported = Math.Max(0, c.UsedTypes.Count - unsupported - partial); unit = "Crestron types used";
                break;
            }
            case LpzRuntime lz:
            {
                var types = lz.Package.Logic.Symbols.Where(x => x.Type != -48).Select(x => x.Type).Distinct().ToList();
                unsupported = types.Count(t => !(PrimitiveCatalog.Known.TryGetValue(t, out var k) && k.Implemented)); supported = types.Count - unsupported; unit = "primitive types";
                break;
            }
            case SimplRuntime sr:
                unsupported = problems.Select(p => p.Name).Distinct().Count(); supported = Math.Max(0, sr.SymbolCount - sr.Unsupported.Count); unit = "symbols";
                break;
            default:
                supported = 0; unsupported = problems.Count; unit = "items";
                break;
        }
        var byCat = problems.GroupBy(p => (Cat: CompatCatalog.CategoryOf(p.Kind), St: CompatCatalog.StatusOf(p.Kind)))
            .Select(g => new { category = g.Key.Cat, status = CompatCatalog.Code(g.Key.St), count = g.Count() });
        return (new
        {
            slot = slot.Number, name = slot.Settings.Name, program = rt?.ProgramName, kind = rt?.Kind, state = slot.State.ToString(), verdict = new { code = verdict.Code, headline = verdict.Headline, reason = verdict.Reason },
            summary = (rt as IProgramAnalysis) is { } an ? SafeSummary(an) : null,
            totals = new { supported, partial, unsupported, unit },
            categories = byCat,
            problems = problems.GroupBy(p => (p.Kind, p.Name)).Select(g => g.First()).Select(p => new { kind = p.Kind, name = p.Name, detail = p.Detail, category = CompatCatalog.CategoryOf(p.Kind), status = CompatCatalog.Code(CompatCatalog.StatusOf(p.Kind)) })
        }, verdict.Code);
    }

    private static string? SafeSummary(IProgramAnalysis a) { try { return a.Summarize().Text; } catch (Exception) { return null; } }

    private static void MapCompat(WebApplication app, CommunityServices s)
    {
        var version = typeof(OpenVpServer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

        app.MapGet("/api/compat", () =>
        {
            var built = s.Processor.Slots.Where(x => x.Runtime != null && s.Visible(x.Number)).Select(ProgramDto).ToList();
            var programs = built.Select(b => b.Dto).ToList();
            var items = s.Processor.Unsupported.Items;
            var rows = items.Select(i => new Row(i.Kind, i.Name, i.Detail, i.Seen, CompatCatalog.CategoryOf(i.Kind), CompatCatalog.Code(CompatCatalog.StatusOf(i.Kind)), i.FirstSeen.ToString("yyyy-MM-dd"), i.LastSeen.ToString("yyyy-MM-dd"))).ToList();
            var panel = CompatCatalog.PanelFeatures;
            return new
            {
                version, os = IssueReport.OsName(), repo = IssueReport.Repo,
                programs,
                logged = new
                {
                    file = s.Processor.Unsupported.FilePath == null ? null : Path.GetFileName(s.Processor.Unsupported.FilePath),
                    total = rows.Count, partial = rows.Count(r => r.Status == "partial"), unsupported = rows.Count(r => r.Status == "unsupported"), seen = rows.Sum(r => r.Seen),
                    categories = CompatCatalog.Categories.Select(c => new { category = c, items = rows.Where(r => r.Category == c).OrderByDescending(r => r.Seen).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase) })
                        .Where(c => c.items.Any())
                },
                panelFeatures = panel.Select(f => new { area = f.Area, feature = f.Feature, status = CompatCatalog.Code(f.Status), note = f.Note }),
                counts = new
                {
                    programs = new { total = built.Count, willRun = built.Count(b => b.Code == "will-run"), partly = built.Count(b => b.Code == "partly"), wontRun = built.Count(b => b.Code == "wont-run") },
                    panel = new { supported = panel.Count(f => f.Status == Support.Supported), partial = panel.Count(f => f.Status == Support.Partial), unsupported = panel.Count(f => f.Status == Support.Unsupported) }
                }
            };
        });

        // The text of a GitHub issue for the unsupported items the person picked. Nothing is sent: the page shows it, lets the person edit it, and opens the link in their own browser.
        // Body: {"slot": 2} for one slot's program, or {"items": [{"kind","name"}]} for items from the log, plus an optional "note". The server looks the items up in its own list.
        app.MapPost("/api/report/draft", (JsonElement body) =>
        {
            var items = new List<IssueItem>();
            string? programType = null;
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("slot", out var sl) && sl.TryGetInt32(out var n))
            {
                if (!s.Processor.IsValidSlot(n) || !s.Visible(n)) return Error(404, "There's no such slot.");
                var slot = s.Processor.Slot(n);
                programType = slot.Runtime?.Kind;
                if (slot.Runtime is IProgramAnalysis a)
                    foreach (var p in a.Problems().GroupBy(p => (p.Kind, p.Name)).Select(g => g.First()))
                        items.Add(new IssueItem(p.Kind, p.Name, p.Detail, s.Processor.Unsupported.Items.FirstOrDefault(i => i.Kind == p.Kind && i.Name == p.Name)?.Seen ?? 1));
            }
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var log = s.Processor.Unsupported.Items;
                foreach (var e in arr.EnumerateArray().Take(500))
                {
                    var kind = e.TryGetProperty("kind", out var k) ? k.GetString() : null;
                    var name = e.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                    var hit = log.FirstOrDefault(i => i.Kind == kind && i.Name == name);
                    if (hit != null && !items.Any(i => i.Kind == hit.Kind && i.Name == hit.Name)) items.Add(new IssueItem(hit.Kind, hit.Name, hit.Detail, hit.Seen));
                }
                if (programType == null && body.TryGetProperty("programType", out var pt) && pt.ValueKind == JsonValueKind.String)
                    programType = s.Processor.Slots.Select(x => x.Runtime?.Kind).Where(x => x != null).Distinct().FirstOrDefault(x => x == pt.GetString());
            }
            if (items.Count == 0) return Error(400, "Choose at least one unsupported item to report.");
            var note = body.TryGetProperty("note", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString() : null;
            var d = IssueReport.Draft(version, IssueReport.OsName(), programType, items, note);
            var url = IssueReport.Url(d.Title, d.Body);
            return Results.Ok(new { title = d.Title, body = d.Body, itemCount = d.ItemCount, included = d.Included, truncated = d.Truncated, url, urlLength = url.Length, repo = IssueReport.Repo });
        });

        // Turns the (possibly edited) title and text into the link, cutting the text if the link would be too long, and says what looks private in it.
        app.MapPost("/api/report/url", (JsonElement body) =>
        {
            if (body.ValueKind != JsonValueKind.Object) return Error(400, "Send {\"title\", \"body\"}.");
            var title = body.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var text = body.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            if (title.Trim().Length == 0) return Error(400, "Give the issue a title.");
            var url = IssueReport.Fit(title, text, out var fitted, out var truncated);
            return Results.Ok(new { url, urlLength = url.Length, body = fitted, truncated, warnings = Redactor.Findings(title + "\n" + text), repo = IssueReport.Repo });
        });
    }

    // =================================================================== Example library

    private sealed class ExampleDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Kind { get; set; } = "";
        public string[] Files { get; set; } = [];
        /// <summary>A folder under the install folder the files come from, instead of the example's own folder (the built SIMPL# sample lives under samples/).</summary>
        public string? BaseDir { get; set; }
        public string? Panel { get; set; }
        public string? TryIt { get; set; }
        public string[]? Tags { get; set; }
    }

    private static List<(ExampleDef Def, string Folder)> ReadExamples(string root)
    {
        var list = new List<(ExampleDef, string)>();
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var def = JsonSerializer.Deserialize<ExampleDef>(File.ReadAllText(Path.Combine(dir, "example.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (def == null || string.IsNullOrWhiteSpace(def.Id) || def.Files.Length == 0 || def.Id != Path.GetFileName(dir)) continue;
                list.Add((def, dir));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return list;
    }

    private static List<(string Name, byte[] Data)>? ExampleFiles(ExampleDef def, string folder)
    {
        var baseRoot = Path.GetFullPath(AppContext.BaseDirectory) + Path.DirectorySeparatorChar;
        var dir = def.BaseDir == null ? Path.GetFullPath(folder) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, def.BaseDir));
        if (def.BaseDir != null && !(dir + Path.DirectorySeparatorChar).StartsWith(baseRoot, StringComparison.Ordinal)) return null;
        var files = new List<(string, byte[])>();
        foreach (var f in def.Files)
        {
            if (f != Path.GetFileName(f)) return null;
            var p = Path.Combine(dir, f);
            if (!File.Exists(p)) return null;
            files.Add((f, File.ReadAllBytes(p)));
        }
        return files;
    }

    private static void MapExamples(WebApplication app, CommunityServices s)
    {
        app.MapGet("/api/examples", () => ReadExamples(s.ExamplesDirectory).Select(e => new
        {
            id = e.Def.Id, name = e.Def.Name, description = e.Def.Description, kind = e.Def.Kind, files = e.Def.Files, panel = e.Def.Panel, tryIt = e.Def.TryIt, tags = e.Def.Tags,
            available = ExampleFiles(e.Def, e.Folder) != null
        }));

        // {"slot": N, "replace": bool, "start": bool}. Needs Programmer. Refuses a locked slot and, without "replace", a slot that already has a program.
        app.MapPost("/api/examples/{id}/load", async (string id, HttpContext ctx, JsonElement body) =>
        {
            var ex = ReadExamples(s.ExamplesDirectory).FirstOrDefault(e => e.Def.Id == id);
            if (ex.Def == null) return Error(404, "There's no such example.");
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("slot", out var sv) || !sv.TryGetInt32(out var n) || !s.Processor.IsValidSlot(n)) return Error(400, $"Choose a slot, 1-{s.Processor.Slots.Count}.");
            var p = Principal.Current;
            if (p != null && !p.CanSlot(n)) return Error(403, $"Your account isn't allowed to use slot {n}.");
            if (s.Ops.Locks.Blocking(n, p) is { } held) return Results.Json(new { error = SlotLocks.Message(held), locked = Authz.LockDto(held) }, statusCode: 423);
            var slot = s.Processor.Slot(n);
            var replace = body.TryGetProperty("replace", out var rv) && rv.ValueKind == JsonValueKind.True;
            var start = !body.TryGetProperty("start", out var st) || st.ValueKind != JsonValueKind.False;
            if ((slot.Runtime != null || slot.State == SlotState.Error) && !replace)
                return Results.Json(new { error = $"Slot {n} already has a program. Send {{\"replace\": true}} to replace it.", occupied = true, program = slot.Runtime?.ProgramName }, statusCode: 409);
            var files = ExampleFiles(ex.Def, ex.Folder);
            if (files == null) return Error(409, "This example isn't installed with this copy of OpenVP.");
            await s.Ops.SlotLocks[n - 1].WaitAsync();
            try
            {
                s.Ops.StopWatching(n);
                var r = s.Ops.Install(n, files, start, s.Who(ctx), "example");
                if (r is not IStatusCodeHttpResult { StatusCode: 200 })
                    return Results.Json(new { error = $"The example didn't load into slot {n}: {slot.LastError ?? "see the slot log"}" }, statusCode: 422);
            }
            finally { s.Ops.SlotLocks[n - 1].Release(); }
            s.Activity.Add("example", $"Loaded the example “{ex.Def.Name}” into slot {n}", n, s.Who(ctx));
            return Results.Ok(new { ok = true, slot = n, name = ex.Def.Name, panel = ex.Def.Panel, tryIt = ex.Def.TryIt, state = slot.State.ToString() });
        });
    }
}
