using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Panels;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

namespace OpenVP.Server;

/// <summary>Builds the OpenVP web server (console, REST API, WebSockets, CIP). Used by the openvp server
/// executable and by the Windows desktop app, which runs the server inside its own process.</summary>
public static class OpenVpServer
{
    public static WebApplication Create(string[] args)
    {
        // Content root = install folder, so wwwroot and appsettings.json are found from any working directory.
        // The data folder stays relative to the working directory.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 512L * 1024 * 1024);
        var cfg = builder.Configuration.GetSection("OpenVP");

        var processor = new Processor(cfg["DataDirectory"] ?? "data", cfg.GetValue("Slots", 10));
        processor.AddLoader(new SimplSharpLoader());
        processor.AddLoader(new SimplLoader(Path.Combine(processor.DataDirectory, "symbols.json")));
        processor.AddLoader(new LpzLoader());
        processor.LogWritten += e => Console.WriteLine($"{e.Time:HH:mm:ss} [slot {e.Slot:00}] {e.Level,-7} {e.Message}");
        builder.Services.AddSingleton(processor);

        CipServer? cip = null;
        if (cfg.GetValue("CipEnabled", true))
        {
            try
            {
                cip = new CipServer(processor, cfg.GetValue("CipPort", 41794)) { LogPackets = cfg.GetValue("CipLogPackets", false) };
                cip.Start();
                Console.WriteLine($"CIP listener (experimental) on port {cip.Port}");
            }
            catch (Exception e) { Console.WriteLine($"CIP listener not started: {e.Message}"); }
        }

        var auth = ApiAuth.FromConfig(cfg, processor.DataDirectory);
        if (!auth.Enabled)
            Console.WriteLine("WARNING: access key is OFF (OpenVP:ApiKey=off). Anyone who can reach this port can upload and run code.");
        else
        {
            Console.WriteLine($"Access key ({auth.Source}): {auth.Key}");
            Console.WriteLine("  Log in by opening http://<this-host>:<port>/?key=<key> once, or send 'Authorization: Bearer <key>'.");
            if (!auth.LocalRequiresKey) Console.WriteLine("  Requests from this machine (localhost) don't need the key.");
        }

        var app = builder.Build();
        app.Use(auth.Handle);
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var uploads = Path.Combine(processor.DataDirectory, "uploads");
        Directory.CreateDirectory(uploads);

        object SlotDto(ProgramSlot s) => new
        {
            number = s.Number,
            state = s.State.ToString(),
            kind = s.Runtime?.Kind,
            program = s.Runtime?.ProgramName,
            file = s.ProgramPath is null ? null : Path.GetFileName(s.ProgramPath),
            error = s.LastError,
            devices = s.Devices.OrderBy(d => d.IpId).Select(DeviceDto)
        };

        object DeviceDto(JoinDevice d) => new
        {
            ipId = $"{d.IpId:X2}",
            slot = d.Slot,
            type = d.Type,
            description = d.Description,
            online = d.Online,
            connections = d.ConnectionCount
        };

        object StepDto(ProgramSlot s)
        {
            var st = s.Stepper.Snapshot();
            var p = st.Paused;
            return new
            {
                t = "step",
                slot = s.Number,
                version = st.Version,
                enabled = st.Enabled,
                onlyBreakSignals = st.OnlyBreakSignals,
                breakSignals = st.BreakSignals,
                paused = p is null ? null : new { step = p.Step, time = p.Time, signal = p.Signal, kind = p.Kind.ToString(), value = p.Value, source = p.Source }
            };
        }

        object ValueOf(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : (int)Math.Clamp(Math.Round(v.GetDouble()), int.MinValue, int.MaxValue),
            _ => v.ToString()
        };

        // ------------------------------------------------------------------ REST

        app.MapGet("/api/info", (HttpContext ctx) => new
        {
            name = "OpenVP", version = "0.1.0", slots = processor.Slots.Count, cipPort = cip?.Port,
            auth = auth.Enabled, authenticated = auth.IsAuthenticated(ctx)
        });

        app.MapPost("/api/login", (HttpContext ctx, JsonElement body) =>
        {
            var key = body.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (!auth.Enabled) return Results.Ok();
            if (!auth.Matches(key)) return Results.Json(new { error = "Wrong access key." }, statusCode: StatusCodes.Status401Unauthorized);
            auth.SetCookie(ctx, key!);
            return Results.Ok();
        });
        app.MapPost("/api/logout", (HttpContext ctx) => { auth.ClearCookie(ctx); return Results.Ok(); });

        app.MapGet("/api/slots", () => processor.Slots.Select(SlotDto));

        // Everything under /api/slots/{n}: an unknown slot is a 404, not a server error.
        var slotApi = app.MapGroup("/api/slots/{n:int}").AddEndpointFilter(async (ctx, next) =>
        {
            var n = Convert.ToInt32(ctx.HttpContext.Request.RouteValues["n"]);
            return processor.IsValidSlot(n) ? await next(ctx) : Results.NotFound(new { error = $"Slot must be 1–{processor.Slots.Count}." });
        });

        slotApi.MapGet("", (int n) => SlotDto(processor.Slot(n)));

        // One upload per slot at a time; a second one waits instead of racing on the same folder.
        var uploadLocks = processor.Slots.Select(_ => new SemaphoreSlim(1, 1)).ToArray();

        slotApi.MapPost("/program", async (int n, HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest("Send the program as multipart/form-data field 'file'.");
            var form = await req.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest("No file.");
            var start = !string.Equals(form["start"], "false", StringComparison.OrdinalIgnoreCase);
            var dir = Path.Combine(uploads, $"slot{n:00}");
            var path = Path.Combine(dir, Path.GetFileName(file.FileName));

            await uploadLocks[n - 1].WaitAsync();
            try
            {
                // Unload first so the old program lets go of its files, then replace them and load the new one.
                // The slot stays locked throughout, so a start/stop/unload request waits instead of interleaving.
                return processor.WithSlotLocked(n, slot =>
                {
                    slot.Unload();
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Directory.CreateDirectory(dir);
                    using (var fs = File.Create(path)) file.CopyTo(fs);
                    // Sidecar files (MyProgram.openvp.json, MyProgram.symbols.json) can ride along.
                    foreach (var extra in form.Files.Where(f => f != file))
                    {
                        using var fs = File.Create(Path.Combine(dir, Path.GetFileName(extra.FileName)));
                        extra.CopyTo(fs);
                    }
                    try { processor.LoadProgram(n, path, start); }
                    catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message, slot = SlotDto(slot) }); }
                    return Results.Ok(SlotDto(slot));
                });
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        slotApi.MapPost("/start", (int n) =>
        {
            try { processor.Slot(n).Start(); return Results.Ok(SlotDto(processor.Slot(n))); }
            catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message }); }
        });
        slotApi.MapPost("/stop", (int n) => { processor.Slot(n).Stop(); return SlotDto(processor.Slot(n)); });
        slotApi.MapPost("/unload", (int n) => { processor.Slot(n).Unload(); return SlotDto(processor.Slot(n)); });

        slotApi.MapGet("/signals", (int n) =>
            (processor.Slot(n).Runtime?.GetSignals() ?? Array.Empty<SignalInfo>())
                .Select(s => new { name = s.Name, kind = s.Kind.ToString(), value = s.Value }));

        slotApi.MapPost("/signals", (int n, JsonElement body) =>
        {
            var rt = processor.Slot(n).Runtime;
            if (rt == null) return Results.NotFound("No program in that slot.");
            if (!body.TryGetProperty("name", out var nameEl) || !body.TryGetProperty("value", out var valueEl))
                return Results.BadRequest("Send {\"name\": \"...\", \"value\": ...}.");
            var name = nameEl.GetString() ?? "";
            return rt.TrySetSignal(name, ValueOf(valueEl)) ? Results.Ok() : Results.NotFound($"Unknown signal '{name}'.");
        });

        // Step-through debugging. GET the state; POST { enabled?, onlyBreakSignals?, breakSignals? } to change it.
        slotApi.MapGet("/step", (int n) => StepDto(processor.Slot(n)));
        slotApi.MapPost("/step", (int n, JsonElement body) =>
        {
            var slot = processor.Slot(n);
            bool? Bool(string k) => body.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            IEnumerable<string>? names = body.TryGetProperty("breakSignals", out var b) && b.ValueKind == JsonValueKind.Array
                ? b.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null;
            slot.Stepper.Configure(Bool("enabled"), Bool("onlyBreakSignals"), names);
            return StepDto(slot);
        });
        slotApi.MapPost("/step/next", (int n) => { processor.Slot(n).Stepper.Next(); return StepDto(processor.Slot(n)); });
        slotApi.MapPost("/step/run", (int n) => { processor.Slot(n).Stepper.Run(); return StepDto(processor.Slot(n)); });

        slotApi.MapPost("/console", (int n, JsonElement body) =>
        {
            try { return Results.Text(processor.Slot(n).RunConsoleCommand(body.GetProperty("command").GetString() ?? "")); }
            catch (Exception e) { return Results.Text("Error: " + e.Message); }
        });

        slotApi.MapGet("/compat", (int n) => processor.Slot(n).Runtime switch
        {
            SimplSharpRuntime s => Results.Text(s.Compat.ToString()),
            LpzRuntime l => Results.Text(LpzReport.Build(l.Package, l.Package.Signals)),
            SimplRuntime r => Results.Text(r.Unsupported.Count == 0
                ? "Every wired symbol has a behavior."
                : "Symbols without a behavior yet:\n" + string.Join('\n', r.Unsupported.Select(u => $"  {u} pins in={u.Inputs.Count} out={u.Outputs.Count}"))),
            _ => Results.Text("No program loaded.")
        });

        // Inspect a compiled program (.lpz, optional .sig) without loading it.
        app.MapPost("/api/inspect", async (HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest("Send the .lpz (and optionally its .sig) as multipart/form-data.");
            var form = await req.ReadFormAsync();
            var dir = Directory.CreateTempSubdirectory("openvp-inspect").FullName;
            try
            {
                string? lpz = null, sig = null;
                foreach (var f in form.Files)
                {
                    var path = Path.Combine(dir, Path.GetFileName(f.FileName));
                    await using (var fs = File.Create(path)) await f.CopyToAsync(fs);
                    if (path.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase)) lpz = path;
                    if (path.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)) sig = path;
                }
                if (lpz == null) return Results.BadRequest("No .lpz file in the upload.");
                try { return Results.Text(LpzInspect.Report(lpz, sig)); }
                catch (InvalidDataException e) { return Results.UnprocessableEntity(new { error = e.Message }); }
            }
            finally { Directory.Delete(dir, true); }
        });

        app.MapGet("/api/log", (int? slot, int? max) => processor.RecentLog(slot, max ?? 500));

        app.MapGet("/api/devices", () => processor.AllDevices.OrderBy(d => d.Slot).ThenBy(d => d.IpId).Select(DeviceDto));

        // ------------------------------------------------------------------ WebSockets

        // Live debugger for one slot: trace events and log lines. Any number of these can be open, each
        // on its own slot, without mixing signals.
        app.Map("/ws/trace", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            int? slot = int.TryParse(ctx.Request.Query["slot"], out var s) ? s : null;
            var wantTrace = ctx.Request.Query["trace"] != "0";   // trace=0: log lines only (the console page)
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });
            void OnTrace(TraceEvent e)
            {
                if (slot == null || e.Slot == slot)
                    outbox.Writer.TryWrite(JsonSerializer.Serialize(new { t = "trace", time = e.Time, slot = e.Slot, signal = e.Signal, kind = e.Kind.ToString(), value = e.Value, source = e.Source }, json));
            }
            void OnLog(LogEntry e)
            {
                if (slot == null || e.Slot == slot)
                    outbox.Writer.TryWrite(JsonSerializer.Serialize(new { t = "log", time = e.Time, slot = e.Slot, level = e.Level, message = e.Message }, json));
            }
            void OnStep(ProgramSlot st)
            {
                if (slot == null || st.Number == slot)
                    outbox.Writer.TryWrite(JsonSerializer.Serialize(StepDto(st), json));
            }
            if (wantTrace)
            {
                // Subscribe on the chosen slot only, so a debugger on one slot costs the others nothing.
                foreach (var sl in processor.Slots)
                    if (slot == null || sl.Number == slot) sl.Traced += OnTrace;
                processor.StepChanged += OnStep;
                if (slot is int sn && sn >= 1 && sn <= processor.Slots.Count) OnStep(processor.Slot(sn)); // current state on connect
            }
            processor.LogWritten += OnLog;
            var buffer = new byte[1024];
            var receive = ws.ReceiveAsync(buffer, ctx.RequestAborted);
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var read = outbox.Reader.WaitToReadAsync(ctx.RequestAborted).AsTask();
                    if (await Task.WhenAny(read, receive) == receive) break;
                    while (outbox.Reader.TryRead(out var msg))
                        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ctx.RequestAborted);
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
            finally
            {
                foreach (var sl in processor.Slots) sl.Traced -= OnTrace;
                processor.StepChanged -= OnStep;
                processor.LogWritten -= OnLog;
            }
        });

        // Browser panels: /ws/panel?ipid=03[&slot=1]
        app.Map("/ws/panel", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            if (!uint.TryParse(ctx.Request.Query["ipid"], System.Globalization.NumberStyles.HexNumber, null, out var ipid) || ipid is 0 or > 0xFE)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("ipid query parameter (hex, 01-FE) is required");
                return;
            }
            int? slot = int.TryParse(ctx.Request.Query["slot"], out var s) ? s : null;
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await new WebPanelSession(ws, processor, ipid, slot).RunAsync(ctx.RequestAborted);
        });

        // ------------------------------------------------------------------ autoload

        foreach (var entry in cfg.GetSection("Autoload").GetChildren())
        {
            if (!int.TryParse(entry.Key.Replace("slot", "", StringComparison.OrdinalIgnoreCase), out var n) || string.IsNullOrEmpty(entry.Value)) continue;
            try { processor.LoadProgram(n, entry.Value); }
            catch (Exception e) { Console.WriteLine($"Autoload slot {n} failed: {e.Message}"); }
        }

        app.Lifetime.ApplicationStopping.Register(() => { cip?.Dispose(); processor.Dispose(); });
        return app;
    }
}
