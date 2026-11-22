using System.Buffers;
using Microsoft.AspNetCore.ResponseCompression;
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

        var processor = new Processor(cfg["DataDirectory"] ?? "data", cfg.GetValue("Slots", 10), cfg.GetValue("SlotSpinMicroseconds", 0));
        processor.AddLoader(new SimplSharpLoader());
        processor.AddLoader(new SimplLoader(Path.Combine(processor.DataDirectory, "symbols.json")));
        processor.AddLoader(new LpzLoader(Path.Combine(processor.DataDirectory, "primitives.json")));
        var unsupportedPath = UnsupportedLogPath(cfg["UnsupportedLog"], processor.DataDirectory);
        if (unsupportedPath != null)
        {
            processor.Unsupported.UseFile(unsupportedPath);
            Console.WriteLine($"Unsupported symbols are listed in {unsupportedPath}");
        }
        processor.LogWritten += e => Console.WriteLine($"{e.Time:HH:mm:ss} [slot {e.Slot:00}] {e.Level,-7} {e.Message}");
        builder.Services.AddSingleton(processor);
        builder.Services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true; // off by default because of BREACH; these responses are program data, not pages that echo a secret
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
        });
        builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
        builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

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
        // gzip/brotli for JSON, html, js and the like (the default MIME list; downloads of programs and other
        // binary files are not touched). Compression only applies to a response whose request asks for it, so
        // WebSocket upgrades (101, no body) pass straight through.
        app.UseResponseCompression();
        app.UseDefaultFiles();
        // Pages are served with an ETag and no-cache: the browser keeps them but asks every time, and an unchanged
        // file costs a 304, so an edited page still shows at once in dev mode.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c => c.Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] = "no-cache"
        });

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var uploads = Path.Combine(processor.DataDirectory, "uploads");
        Directory.CreateDirectory(uploads);
        // Slots loaded from a file on this computer with "reload when it changes" on: the watch on that file.
        var watches = new SourceWatch?[processor.Slots.Count];

        var recordings = new SlotRecordings(processor, new RecordingStore(processor.DataDirectory));

        object SlotDto(ProgramSlot s) => new
        {
            number = s.Number,
            roomId = s.RoomId,
            state = s.State.ToString(),
            kind = s.Runtime?.Kind,
            program = s.Runtime?.ProgramName,
            file = s.ProgramPath is null ? null : Path.GetFileName(s.ProgramPath),
            error = s.LastError,
            watching = watches[s.Number - 1]?.Program,
            recording = recordings.Busy(s.Number),
            devices = s.Devices.OrderBy(d => d.IpId).Select(DeviceDto)
        };

        object DeviceDto(JoinDevice d) => new
        {
            ipId = $"{d.IpId:X2}",
            slot = d.Slot,
            type = d.Type,
            description = d.Description,
            online = d.Online,
            connections = d.ConnectionCount,
            registeredAt = d.RegisteredAt,
            lastOnline = d.LastOnline,
            lastOffline = d.LastOffline,
            totalConnects = d.TotalConnects,
            inputsReceived = d.InputsReceived,
            feedbackSent = d.FeedbackSent,
            feedbackJoins = d.FeedbackJoinCount,
            inputJoins = d.InputJoinCount,
            clients = d.Connections.Select(ConnectionDto)
        };

        object ConnectionDto(DeviceConnection c) => new
        {
            id = c.Id,
            transport = c.Transport,
            address = c.RemoteAddress,
            port = c.RemotePort,
            endpoint = c.Endpoint,
            hostname = c.Hostname,
            kind = c.ClientKind,
            model = c.Model,
            firmware = c.Firmware,
            mac = c.MacAddress,
            peer = c.Peer,
            userAgent = c.UserAgent,
            probeStatus = c.ProbeStatus,
            lastProbe = c.LastProbe,
            connectedAt = c.ConnectedAt,
            lastActivity = c.LastActivity,
            messagesIn = Interlocked.Read(ref c.MessagesIn),
            messagesOut = Interlocked.Read(ref c.MessagesOut),
            bytesIn = Interlocked.Read(ref c.BytesIn),
            bytesOut = Interlocked.Read(ref c.BytesOut),
            joinsIn = Interlocked.Read(ref c.JoinsIn),
            joinsOut = Interlocked.Read(ref c.JoinsOut),
            heartbeats = Interlocked.Read(ref c.Heartbeats),
            errors = Interlocked.Read(ref c.Errors),
            latencyMs = c.LatencyMs < 0 ? (double?)null : c.LatencyMs,
            queued = c.QueueDepth?.Invoke(),
            details = c.Details
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
                paused = p is null ? null : new { step = p.Step, time = p.Time, signal = p.Signal, kind = p.Kind.ToString(), value = p.Value, source = p.Source, symbol = p.Symbol, cause = p.Cause, depth = p.Depth, chain = p.Chain }
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

        // First-run Welcome screen: shown until someone finishes or skips it; remembered for everyone.
        var welcome = new WelcomeState(processor.DataDirectory);
        app.MapGet("/api/welcome", () => new { show = welcome.Show });
        app.MapPost("/api/welcome/dismiss", () => { welcome.Dismiss(); return Results.Ok(new { show = false }); });

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

        var snapshots = new SlotSnapshots(processor.DataDirectory);

        static string FormatSize(long b) => b switch
        {
            < 1024 => $"{b} bytes",
            < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
            _ => $"{b / 1048576.0:0.#} MB"
        };

        // Save the slot's program, NVRAM and User files. Never throws: a failed snapshot is a warning, not a reason to
        // stop what the caller is doing. Returns false if it failed. Call with the slot locked.
        bool TrySnapshot(ProgramSlot slot, string reason, string doing)
        {
            try
            {
                var info = snapshots.Take(slot.Number, slot.Runtime?.ProgramName, slot.ProgramPath is null ? null : Path.GetFileName(slot.ProgramPath), reason);
                if (info != null)
                {
                    slot.Log("Notice", $"Saved a snapshot of {info.Program ?? "the slot"} (program, NVRAM and User files, {FormatSize(info.Size)}) {doing}");
                    if (info.Skipped is { Length: > 0 })
                        slot.Log("Warning", $"{info.Skipped.Length} file(s) couldn't be copied into the snapshot: {string.Join("; ", info.Skipped.Take(3))}{(info.Skipped.Length > 3 ? "; …" : "")}");
                }
                return true;
            }
            catch (Exception e)
            {
                slot.Log("Warning", $"Couldn't save a snapshot {doing.TrimEnd('.')}: {e.Message}");
                return false;
            }
        }

        object SnapshotDto(SnapshotInfo i) => new { id = i.Id, time = i.Time, program = i.Program, file = i.File, reason = i.Reason, size = i.Size, skipped = i.Skipped?.Length ?? 0 };

        // Replace the slot's program files and load the program, which is files[0]. Call with the slot's upload lock held.
        IResult Install(int n, IReadOnlyList<(string Name, byte[] Data)> files, bool start)
        {
            var dir = Path.Combine(uploads, $"slot{n:00}");
            var path = Path.Combine(dir, Path.GetFileName(files[0].Name));
            // Unload first so the old program lets go of its files, then replace them and load the new one.
            // The slot stays locked throughout, so a start/stop/unload request waits instead of interleaving.
            return processor.WithSlotLocked(n, slot =>
            {
                if (slot.Runtime != null) TrySnapshot(slot, SlotSnapshots.BeforeLoad, "before loading.");
                slot.Unload();
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                foreach (var (name, data) in files) File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(name)), data);
                try { processor.LoadProgram(n, path, start); }
                catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message, slot = SlotDto(slot) }); }
                return Results.Ok(SlotDto(slot));
            });
        }

        static byte[] ReadAll(IFormFile f)
        {
            using var ms = new MemoryStream();
            f.CopyTo(ms);
            return ms.ToArray();
        }

        void StopWatching(int n)
        {
            lock (watches) { watches[n - 1]?.Dispose(); watches[n - 1] = null; }
        }

        // Load a program straight from disk with the files that belong with it (ProgramSource.Companions). They are
        // read before the slot is touched, so a file that can't be read leaves the running program alone.
        IResult LoadFromDisk(int n, string program, string[] chosen, bool start)
        {
            var files = ProgramSource.Companions(program, chosen);
            var data = files.Select(f => (f.Name, File.ReadAllBytes(f.Source))).ToList();
            var result = Install(n, data, start);
            var slot = processor.Slot(n);
            if (files.Count > 1)
                slot.Log("Notice", $"Loaded from {Path.GetDirectoryName(program)} with " + string.Join(", ", files.Skip(1).Select(f =>
                    Path.GetFileName(f.Source) == f.Name ? f.Name : $"{Path.GetFileName(f.Source)} (as {f.Name})")));
            return result;
        }

        // Reload a watched slot when its files change. The watch is replaced or stopped by anything else that loads or
        // unloads the slot, so a stale one does nothing.
        async Task ReloadAsync(int n, SourceWatch w, string[] chosen)
        {
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                lock (watches) if (watches[n - 1] != w) return;
                var slot = processor.Slot(n);
                slot.Log("Notice", $"{Path.GetFileName(w.Program)} changed on disk; reloading.");
                LoadFromDisk(n, w.Program, chosen, slot.State != SlotState.Stopped);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                processor.Slot(n).Log("Warning", $"Couldn't reload {Path.GetFileName(w.Program)}: {e.Message} It will be tried again when the file changes.");
            }
            catch (Exception e) { processor.Slot(n).Log("Error", $"Reloading {Path.GetFileName(w.Program)} failed: {e.Message}"); }
            finally { uploadLocks[n - 1].Release(); }
        }

        slotApi.MapPost("/program", async (int n, HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest("Send the program as multipart/form-data field 'file'.");
            var form = await req.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest("No file.");
            var start = !string.Equals(form["start"], "false", StringComparison.OrdinalIgnoreCase);
            // Sidecar files (MyProgram.openvp.json, MyProgram.symbols.json, MyProgram.primitives.json) can ride along.
            // A compiled program's signal names are in its .sig, which the loader looks for under the
            // program's own name. If exactly one .sig came with it, it is that program's, whatever it's
            // called (copies get renamed, e.g. with a date added), so save it under the program's name.
            var name = Path.GetFileName(file.FileName);
            var sigs = form.Files.Where(f => f != file && f.FileName.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)).ToList();
            var sigFor = name.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase) && sigs.Count == 1 ? sigs[0] : null;
            var files = new List<(string, byte[])> { (name, ReadAll(file)) };
            foreach (var extra in form.Files.Where(f => f != file))
                files.Add((extra == sigFor ? Path.ChangeExtension(name, ".sig") : Path.GetFileName(extra.FileName), ReadAll(extra)));

            await uploadLocks[n - 1].WaitAsync();
            try { StopWatching(n); return Install(n, files, start); }
            finally { uploadLocks[n - 1].Release(); }
        });

        // Load a program from a file on this computer, bringing its .sig and sidecars along, and optionally reload it
        // whenever it changes (say, every time SIMPL Windows compiles). Used by the desktop app, which knows where a
        // picked file is; a browser only ever gets the file's contents. Only for requests from this computer: a remote
        // user's paths mean nothing here, and this way nobody elsewhere can make OpenVP read files on this machine.
        slotApi.MapPost("/program/path", async (int n, HttpContext ctx, JsonElement body) =>
        {
            if (!ApiAuth.IsDirectLocal(ctx))
                return Results.Json(new { error = "Loading from a path only works on the computer OpenVP runs on. Upload the files instead." }, statusCode: StatusCodes.Status403Forbidden);
            string? Str(string k) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool Flag(string k, bool dflt) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : dflt;
            var path = Str("path")?.Trim().Trim('"');
            if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
                return Results.BadRequest(new { error = "Send {\"path\": \"C:\\full\\path\\to\\Program.lpz\"}." });
            if (!File.Exists(path)) return Results.BadRequest(new { error = $"There's no file at {path}." });
            var chosen = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("files", out var fl) && fl.ValueKind == JsonValueKind.Array
                ? fl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)
                    .Where(p => Path.IsPathFullyQualified(p) && File.Exists(p)).ToArray()
                : Array.Empty<string>();
            var start = Flag("start", true);
            var watch = Flag("watch", true);

            await uploadLocks[n - 1].WaitAsync();
            try
            {
                StopWatching(n);
                // Made before the files are read, so a change while they're copied still triggers a reload.
                var w = watch ? new SourceWatch(path, () => ProgramSource.Companions(path, chosen).Select(f => f.Source)) : null;
                IResult result;
                try { result = LoadFromDisk(n, path, chosen, start); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    w?.Dispose();
                    return Results.UnprocessableEntity(new { error = $"Couldn't read the program: {e.Message}" });
                }
                if (w != null)
                {
                    w.Changed += () => _ = ReloadAsync(n, w, chosen);
                    lock (watches) watches[n - 1] = w;
                    processor.Slot(n).Log("Notice", $"Watching {path}: the slot reloads when it changes.");
                }
                return result is IStatusCodeHttpResult { StatusCode: 200 } ? Results.Ok(SlotDto(processor.Slot(n))) : result;
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        // Stop reloading the slot when its file changes; the program keeps running.
        slotApi.MapDelete("/watch", (int n) =>
        {
            var was = watches[n - 1]?.Program;
            StopWatching(n);
            if (was != null) processor.Slot(n).Log("Notice", $"Stopped watching {was}.");
            return SlotDto(processor.Slot(n));
        });

        // Snapshots: a copy of the slot's program files, NVRAM and User folders, taken before every load.
        slotApi.MapGet("/snapshots", (int n) => snapshots.List(n).Select(SnapshotDto));

        slotApi.MapPost("/snapshots", async (int n) =>
        {
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                return processor.WithSlotLocked(n, slot =>
                {
                    try
                    {
                        var info = snapshots.Take(n, slot.Runtime?.ProgramName, slot.ProgramPath is null ? null : Path.GetFileName(slot.ProgramPath), SlotSnapshots.Manual);
                        if (info == null) return Results.BadRequest(new { error = "There's nothing in this slot to save: no program and no files." });
                        slot.Log("Notice", $"Saved a snapshot of {info.Program ?? "the slot"} (program, NVRAM and User files, {FormatSize(info.Size)}).");
                        return Results.Ok(SnapshotDto(info));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        return Results.Json(new { error = $"Couldn't save the snapshot: {e.Message}" }, statusCode: 500);
                    }
                });
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        slotApi.MapPost("/snapshots/{id}/restore", async (int n, string id) =>
        {
            if (snapshots.Get(n, id) == null) return Results.NotFound(new { error = "There's no such snapshot." });
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                return processor.WithSlotLocked(n, slot =>
                {
                    if (snapshots.Get(n, id) == null) return Results.NotFound(new { error = "There's no such snapshot." });
                    // The current state goes into a snapshot first, so a roll back can be undone. If that can't be done, stop here.
                    if (!TrySnapshot(slot, SlotSnapshots.BeforeRollBack, "before rolling back."))
                        return Results.Json(new { error = "Couldn't save a snapshot of the current state, so nothing was rolled back. See the slot log." }, statusCode: 500);
                    StopWatching(n);
                    var wasRunning = slot.State == SlotState.Running;
                    slot.Unload(); // the program lets go of its files before they are replaced
                    SnapshotInfo? info;
                    try { info = snapshots.Restore(n, id); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        slot.Log("Error", $"Rolling back failed: {e.Message}");
                        return Results.Json(new { error = $"Couldn't restore the snapshot: {e.Message}", slot = SlotDto(slot) }, statusCode: 500);
                    }
                    if (info == null) return Results.NotFound(new { error = "There's no such snapshot." });
                    slot.Log("Notice", $"Rolled back to the snapshot of {info.Program ?? "the slot"} from {info.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({info.Reason}).");
                    if (snapshots.RestoredProgramPath(n, info) is { } path && File.Exists(path))
                    {
                        try { processor.LoadProgram(n, path, wasRunning); }
                        catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message, slot = SlotDto(slot) }); }
                    }
                    return Results.Ok(SlotDto(slot));
                });
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        slotApi.MapDelete("/snapshots/{id}", async (int n, string id) =>
        {
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                try { return snapshots.Delete(n, id) ? Results.Ok(new { deleted = id }) : Results.NotFound(new { error = "There's no such snapshot." }); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { return Results.Json(new { error = $"Couldn't delete the snapshot: {e.Message}" }, statusCode: 500); }
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        // Virtual Control Room ID: panels can be routed to the slot by it (/ws/panel?room=).
        slotApi.MapPut("/room", (int n, JsonElement body) =>
        {
            try
            {
                var id = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("roomId", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                processor.Slot(n).SetRoomId(id);
                return Results.Ok(SlotDto(processor.Slot(n)));
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        slotApi.MapPost("/start", (int n) =>
        {
            try { processor.Slot(n).Start(); return Results.Ok(SlotDto(processor.Slot(n))); }
            catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message }); }
        });
        slotApi.MapPost("/stop", (int n) => { processor.Slot(n).Stop(); return SlotDto(processor.Slot(n)); });
        slotApi.MapPost("/unload", async (int n) =>
        {
            // Under the upload lock, so a reload of a watched slot that's already under way can't load it again afterwards.
            await uploadLocks[n - 1].WaitAsync();
            try { StopWatching(n); processor.Slot(n).Unload(); return SlotDto(processor.Slot(n)); }
            finally { uploadLocks[n - 1].Release(); }
        });

        // IP table: every IP ID in the slot with who is connected to it and what is known about them.
        slotApi.MapGet("/iptable", (int n) => processor.Slot(n).Devices.OrderBy(d => d.IpId).Select(DeviceDto));

        JoinDevice? FindIpId(int n, string ipid) =>
            uint.TryParse(ipid, System.Globalization.NumberStyles.HexNumber, null, out var id) ? processor.Slot(n).FindDevice(id) : null;

        // Run the identification again (reverse DNS, Crestron autodiscovery) for everything on one IP ID.
        slotApi.MapPost("/iptable/{ipid}/identify", async (int n, string ipid) =>
        {
            var dev = FindIpId(n, ipid);
            if (dev == null) return Results.NotFound(new { error = $"Slot {n} has no IP ID {ipid}." });
            await Task.WhenAll(dev.Connections.Where(c => c.Transport != "EISC").Select(c => ClientIdentifier.IdentifyAsync(c)));
            return Results.Ok(DeviceDto(dev));
        });

        // Log in to a connected device over SSH with credentials typed in for this request (never stored),
        // run read-only console commands, and add what they return to the IP table.
        slotApi.MapPost("/iptable/{ipid}/clients/{cid}/login", async (int n, string ipid, string cid, JsonElement body, CancellationToken ct) =>
        {
            var conn = FindIpId(n, ipid)?.FindConnection(cid);
            if (conn == null) return Results.NotFound(new { error = "That connection is gone; refresh the IP table." });
            if (conn.Transport == "EISC") return Results.BadRequest(new { error = "An EISC link to another slot has nothing to log in to." });
            string Str(string k) => body.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var port = body.TryGetProperty("port", out var p) && p.TryGetInt32(out var pn) && pn is > 0 and < 65536 ? pn : 22;
            if (Str("username").Length == 0) return Results.BadRequest(new { error = "Enter the device's username." });
            var result = await DeviceConsole.ReadAsync(conn, Str("username"), Str("password"), port, ct);
            processor.Slot(n).Log(result.Ok ? "Notice" : "Warning", $"[IP table] IPID {ipid.ToUpperInvariant()} {conn.Endpoint}: {result.Message}");
            return result.Ok
                ? Results.Ok(new { message = result.Message, client = ConnectionDto(conn) })
                : Results.UnprocessableEntity(new { error = result.Message, client = ConnectionDto(conn) });
        });

        slotApi.MapGet("/signals", (int n) =>
            new RawJson(GraphJson.WriteSignals(processor.Slot(n).Runtime?.GetSignals() ?? Array.Empty<SignalInfo>())));

        // The program's wiring, for the debugger's path view (docs/debugger-paths.md section 1). Serialized once per
        // loaded program and kept; ?compact=1 leaves out unwired and constant pins. ETag + If-None-Match give a 304.
        slotApi.MapGet("/graph", (int n, HttpRequest req) =>
        {
            var rt = processor.Slot(n).Runtime;
            if (rt == null) return Results.NotFound(new { error = "No program in that slot." });
            if (rt is not IProgramGraphSource src) return Results.NotFound(new { error = $"{rt.Kind} programs can't describe their wiring." });
            var compact = req.Query.TryGetValue("compact", out var c) && c.ToString() is "1" or "true";
            return new ValidatedJson(GraphJson.For(src, compact));
        });

        slotApi.MapPost("/signals", (int n, JsonElement body) =>
        {
            var slot = processor.Slot(n);
            if (slot.Runtime == null) return Results.NotFound("No program in that slot.");
            if (!body.TryGetProperty("name", out var nameEl))
                return Results.BadRequest("Send {\"name\": \"...\", \"value\": ...} or {\"name\": \"...\", \"pulse\": true}.");
            var name = nameEl.GetString() ?? "";
            // Pulse: high then low with nothing in between, like a processor's debugger.
            if (body.TryGetProperty("pulse", out var pulseEl) && pulseEl.ValueKind == JsonValueKind.True)
                return slot.ForcePulse(name) ? Results.Ok() : Results.NotFound($"Unknown or non-digital signal '{name}'.");
            if (!body.TryGetProperty("value", out var valueEl))
                return Results.BadRequest("Send {\"name\": \"...\", \"value\": ...} or {\"name\": \"...\", \"pulse\": true}.");
            return slot.ForceSignal(name, ValueOf(valueEl)) ? Results.Ok() : Results.NotFound($"Unknown signal '{name}'.");
        });

        // ---- Record and replay: a slot's inputs and the feedback they caused, replayed as a regression test.
        // Forces from the debugger go through ProgramSlot.ForceSignal / ForcePulse (above), which is how they get recorded.
        IResult Recordings(Func<IResult> action)
        {
            try { return action(); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (RecordingFormatException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 409); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return Results.Json(new { error = $"Couldn't read or write the recording: {e.Message}" }, statusCode: 500); }
        }

        string? NameFrom(JsonElement body) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty("name", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        bool Flag(JsonElement body, string key, bool dflt) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : dflt;

        slotApi.MapGet("/recording/status", (int n) => recordings.Status(n));

        slotApi.MapPost("/recording/start", (int n) => Recordings(() =>
        {
            recordings.StartRecording(n);
            processor.Slot(n).Log("Notice", "Recording started.");
            return Results.Ok(recordings.Status(n));
        }));

        slotApi.MapPost("/recording/stop", (int n, JsonElement? body) => Recordings(() =>
            Results.Ok(new { recording = recordings.StopRecording(n, body is { } b ? NameFrom(b) : null), status = recordings.Status(n) })));

        slotApi.MapPost("/replay/cancel", (int n) =>
            recordings.CancelReplay(n) ? Results.Ok(recordings.Status(n)) : Results.Json(new { error = "Nothing is being replayed." }, statusCode: 409));

        slotApi.MapGet("/recordings", (int n) => recordings.Store.List(n));

        slotApi.MapPost("/recordings", async (int n, HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "Send the recording as multipart/form-data field 'file'." });
            var form = await req.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest(new { error = "No file." });
            if (file.Length > RecordingStore.MaxFileBytes) return Results.BadRequest(new { error = "That file is too large to be a recording." });
            var bytes = ReadAll(file);
            var name = form["name"].ToString();
            return Recordings(() => Results.Ok(recordings.Store.Import(n, bytes, string.IsNullOrWhiteSpace(name) ? null : name)));
        });

        slotApi.MapGet("/recordings/{id}", (int n, string id) =>
            recordings.Store.ReadFile(n, id) is { } bytes ? Results.Bytes(bytes, "application/json") : Results.NotFound(new { error = "There's no such recording." }));

        slotApi.MapGet("/recordings/{id}/download", (int n, string id) =>
        {
            var info = recordings.Store.Get(n, id);
            var bytes = info == null ? null : recordings.Store.ReadFile(n, id);
            if (info == null || bytes == null) return Results.NotFound(new { error = "There's no such recording." });
            var safe = new string(info.Name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_').ToArray()).Trim();
            return Results.File(bytes, "application/json", (safe.Length == 0 ? "recording" : safe) + ".openvp-recording.json");
        });

        slotApi.MapPut("/recordings/{id}", (int n, string id, JsonElement body) => Recordings(() =>
            recordings.Store.Rename(n, id, NameFrom(body)) is { } info ? Results.Ok(info) : Results.NotFound(new { error = "There's no such recording." })));

        slotApi.MapDelete("/recordings/{id}", (int n, string id) => Recordings(() =>
            recordings.Store.Delete(n, id) ? Results.Ok(new { deleted = id }) : Results.NotFound(new { error = "There's no such recording." })));

        slotApi.MapPost("/recordings/{id}/replay", (int n, string id, JsonElement? body) => Recordings(() =>
        {
            var b = body ?? default;
            recordings.StartReplay(n, id, new ReplayOptions { Fast = Flag(b, "fast", false), IncludeEisc = Flag(b, "includeEisc", true) });
            return Results.Ok(recordings.Status(n));
        }));

        slotApi.MapGet("/recordings/{id}/report", (int n, string id) =>
            recordings.Store.Get(n, id) == null ? Results.NotFound(new { error = "There's no such recording." })
            : recordings.Store.LatestReport(n, id) is { } r ? Results.Ok(r) : Results.NotFound(new { error = "This recording hasn't been replayed yet." }));

        slotApi.MapGet("/recordings/{id}/reports", (int n, string id) =>
            recordings.Store.Get(n, id) == null ? Results.NotFound(new { error = "There's no such recording." }) : Results.Ok(recordings.Store.Reports(n, id)));

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

        // ------------------------------------------------------------------ slot files (\NVRAM and \USER)

        // A slot's NVRAM and user folders, for config files the program reads. root = nvram | user;
        // path = a folder or file inside it, with '/' between parts. The program sees /user/a/b.json as \USER\a\b.json.
        IResult Files(Func<IResult> body)
        {
            try { return body(); }
            catch (Exception e) when (e is ArgumentException or UnauthorizedAccessException)
            { return Results.BadRequest(new { error = e.Message }); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            { return Results.NotFound(new { error = "No such file or folder." }); }
            catch (IOException e) { return Results.Conflict(new { error = e.Message }); }
        }

        string ProgramPathOf(string root, string rel) =>
            "\\" + root.ToUpperInvariant() + (rel.Length == 0 ? "\\" : "\\" + rel.Replace('/', '\\'));

        string Rel(string? path) => (path ?? "").Replace('\\', '/').Trim('/');

        slotApi.MapGet("/files", (int n, string? root, string? path) => Files(() =>
        {
            var dir = SlotFiles.Resolve(processor.Slot(n).Directory, root, path);
            if (!Directory.Exists(dir)) return Results.NotFound(new { error = "No such folder." });
            var r = root!.Trim().ToLowerInvariant();
            var info = new DirectoryInfo(dir);
            var entries = info.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new { name = d.Name, folder = true, size = (long?)null, modified = d.LastWriteTimeUtc })
                .Concat(info.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new { name = f.Name, folder = false, size = (long?)f.Length, modified = f.LastWriteTimeUtc }));
            // The folder SIMPL+ MakeProgramFilename uses: \NVRAM\{Program ID Tag}\.
            var programTag = processor.Slot(n).Runtime?.ProgramIdTag;
            var programFolder = programTag is null ? null : SlotFiles.FolderName(programTag);
            return Results.Ok(new { root = r, path = Rel(path), programPath = ProgramPathOf(r, Rel(path)), programFolder, entries, missing = Missing(n) });
        }));

        // Files and folders the program looked for and didn't find (config files not put in place yet),
        // with the folder each one belongs in, so the console can offer to create it.
        object[] Missing(int n) => processor.Slot(n).MissingFiles.Items.Select(m => (object)new
        {
            root = m.Root, folder = m.Folder, name = m.Name, isFolder = m.IsFolder,
            programPath = m.ProgramPath, folderPath = m.FolderPath, diskFolder = m.DiskFolder, folderExists = m.FolderExists,
        }).ToArray();

        slotApi.MapGet("/files/missing", (int n) => Results.Ok(Missing(n)));

        slotApi.MapPost("/files/folder", (int n, JsonElement body) => Files(() =>
        {
            string? Str(string k) => body.TryGetProperty(k, out var v) ? v.GetString() : null;
            var name = Str("name");
            if (!SlotFiles.IsValidName(name)) return Results.BadRequest(new { error = "Give the folder a name without / \\ : * ? \" < > |." });
            var dir = Path.Combine(SlotFiles.Resolve(processor.Slot(n).Directory, Str("root"), Str("path")), name!);
            if (File.Exists(dir)) return Results.Conflict(new { error = $"A file called '{name}' is already there." });
            Directory.CreateDirectory(dir);
            return Results.Ok();
        }));

        // Upload into a folder: any number of multipart files. A file name may carry sub-folders
        // (a whole folder dragged in from the browser), which are created as needed.
        slotApi.MapPost("/files", async (int n, string? root, string? path, HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "Send the files as multipart/form-data." });
            var form = await req.ReadFormAsync();
            if (form.Files.Count == 0) return Results.BadRequest(new { error = "No files." });
            return Files(() =>
            {
                var dir = SlotFiles.Resolve(processor.Slot(n).Directory, root, path);
                var saved = new List<string>();
                foreach (var f in form.Files)
                {
                    var parts = f.FileName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0 || !parts.All(SlotFiles.IsValidName))
                        return Results.BadRequest(new { error = $"Bad file name '{f.FileName}'." });
                    var target = SlotFiles.Resolve(processor.Slot(n).Directory, root, Rel(path) + "/" + string.Join('/', parts));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (Directory.Exists(target)) return Results.Conflict(new { error = $"A folder called '{parts[^1]}' is already there." });
                    using (var fs = File.Create(target)) f.CopyTo(fs);
                    saved.Add(Path.GetRelativePath(dir, target).Replace('\\', '/'));
                }
                return Results.Ok(new { saved });
            });
        });

        slotApi.MapGet("/files/download", (int n, string? root, string? path) => Files(() =>
        {
            var file = SlotFiles.Resolve(processor.Slot(n).Directory, root, path);
            if (!File.Exists(file)) return Results.NotFound(new { error = "No such file." });
            return Results.File(File.OpenRead(file), "application/octet-stream", Path.GetFileName(file));
        }));

        slotApi.MapPost("/files/rename", (int n, JsonElement body) => Files(() =>
        {
            string? Str(string k) => body.TryGetProperty(k, out var v) ? v.GetString() : null;
            var name = Str("name");
            if (!SlotFiles.IsValidName(name)) return Results.BadRequest(new { error = "Give it a name without / \\ : * ? \" < > |." });
            var from = SlotFiles.Resolve(processor.Slot(n).Directory, Str("root"), Str("path"));
            if (from == SlotFiles.Resolve(processor.Slot(n).Directory, Str("root"), "")) return Results.BadRequest(new { error = "Can't rename the top folder." });
            var to = Path.Combine(Path.GetDirectoryName(from)!, name!);
            if (Directory.Exists(from)) Directory.Move(from, to);
            else if (File.Exists(from)) File.Move(from, to);
            else return Results.NotFound(new { error = "No such file or folder." });
            return Results.Ok();
        }));

        // Deletes a file, or a folder and everything in it. The NVRAM and user folders themselves stay.
        slotApi.MapDelete("/files", (int n, string? root, string? path) => Files(() =>
        {
            var target = SlotFiles.Resolve(processor.Slot(n).Directory, root, path);
            if (target == SlotFiles.Resolve(processor.Slot(n).Directory, root, "")) return Results.BadRequest(new { error = "Can't delete the top folder." });
            if (Directory.Exists(target)) Directory.Delete(target, true);
            else if (File.Exists(target)) File.Delete(target);
            else return Results.NotFound(new { error = "No such file or folder." });
            return Results.Ok();
        }));

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

        // Live metrics: CPU, memory, threads, and per-slot activity (stuck handlers, orphaned threads).
        app.MapGet("/api/metrics", (bool? history) => processor.Metrics.Snapshot(history ?? true));

        // The list of unsupported symbols/APIs (docs/unsupported.md in a source checkout), to download and commit.
        app.MapGet("/api/unsupported", (string? format) => format == "json"
            ? Results.Ok(new { file = processor.Unsupported.FilePath, items = processor.Unsupported.Items })
            : Results.File(System.Text.Encoding.UTF8.GetBytes(processor.Unsupported.Render()), "text/markdown; charset=utf-8", "unsupported.md"));

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
            // Raw events go in; the send loop below turns them into JSON, so the slot thread only pays for an enqueue.
            var outbox = Channel.CreateBounded<object>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            void OnTrace(TraceEvent e)
            {
                if (slot == null || e.Slot == slot) outbox.Writer.TryWrite(e);
            }
            void OnLog(LogEntry e)
            {
                if (slot == null || e.Slot == slot) outbox.Writer.TryWrite(e);
            }
            void OnStep(ProgramSlot st)
            {
                // Snapshot now: the stepper's state may have moved on by the time the send loop runs.
                if (slot == null || st.Number == slot) outbox.Writer.TryWrite(StepDto(st));
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
            var frame = new ArrayBufferWriter<byte>(16 * 1024);
            using var jw = new Utf8JsonWriter(frame);
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var read = outbox.Reader.WaitToReadAsync(ctx.RequestAborted).AsTask();
                    if (await Task.WhenAny(read, receive) == receive) break;
                    // One JSON array per frame: whatever is queued now, up to MaxTraceBatch events.
                    while (outbox.Reader.TryPeek(out _))
                    {
                        frame.Clear();
                        jw.Reset(frame);
                        jw.WriteStartArray();
                        for (var n = 0; n < MaxTraceBatch && outbox.Reader.TryRead(out var item); n++) WriteWsEvent(jw, item, json);
                        jw.WriteEndArray();
                        jw.Flush();
                        await ws.SendAsync(frame.WrittenMemory, WebSocketMessageType.Text, true, ctx.RequestAborted);
                    }
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

        // Metrics once a second (the first message includes the five-minute history): /ws/metrics
        app.Map("/ws/metrics", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[256];
            var receive = ws.ReceiveAsync(buffer, ctx.RequestAborted);
            var first = true;
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var msg = JsonSerializer.SerializeToUtf8Bytes(processor.Metrics.Snapshot(includeHistory: first), json);
                    first = false;
                    await ws.SendAsync(msg, WebSocketMessageType.Text, true, ctx.RequestAborted);
                    if (await Task.WhenAny(Task.Delay(1000, ctx.RequestAborted), receive) == receive) break;
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        });

        // Browser panels: /ws/panel?ipid=03[&slot=1 | &room=ROOMID]
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
            string? room = ctx.Request.Query["room"].ToString();
            if (string.IsNullOrWhiteSpace(room)) room = null;
            if (room != null)
            {
                var bySlot = processor.FindSlotByRoomId(room);
                if (bySlot == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ctx.Response.WriteAsync($"No slot has Room ID '{room}'");
                    return;
                }
                if (slot != null && slot != bySlot.Number)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync($"Room ID '{room}' belongs to slot {bySlot.Number}, not slot {slot}");
                    return;
                }
                slot = bySlot.Number;
            }
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote is { IsIPv4MappedToIPv6: true }) remote = remote.MapToIPv4();
            var client = new DeviceConnection("Web panel", remote?.ToString(), ctx.Connection.RemotePort);
            var ua = ctx.Request.Headers.UserAgent.ToString();
            if (ua.Length > 0) ClientIdentifier.ApplyUserAgent(client, ua.Length > 500 ? ua[..500] : ua);
            client.SetDetail("Forwarded for", ctx.Request.Headers["X-Forwarded-For"].ToString());
            client.SetDetail("Origin", ctx.Request.Headers.Origin.ToString());
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await new WebPanelSession(ws, processor, ipid, slot, client).RunAsync(ctx.RequestAborted);
        });

        // ------------------------------------------------------------------ autoload

        foreach (var entry in cfg.GetSection("Autoload").GetChildren())
        {
            if (!int.TryParse(entry.Key.Replace("slot", "", StringComparison.OrdinalIgnoreCase), out var n) || string.IsNullOrEmpty(entry.Value)) continue;
            try { processor.LoadProgram(n, entry.Value); }
            catch (Exception e) { Console.WriteLine($"Autoload slot {n} failed: {e.Message}"); }
        }

        app.Lifetime.ApplicationStopping.Register(() => { for (var i = 1; i <= watches.Length; i++) StopWatching(i); cip?.Dispose(); recordings.Dispose(); processor.Dispose(); });
        return app;
    }

    /// <summary>Most events in one /ws/trace frame, so one burst can't build an enormous message.</summary>
    private const int MaxTraceBatch = 200;

    /// <summary>Writes one /ws/trace event with the same fields the page has always seen.</summary>
    private static void WriteWsEvent(Utf8JsonWriter w, object item, JsonSerializerOptions json)
    {
        switch (item)
        {
            case TraceEvent e:
                w.WriteStartObject();
                w.WriteString("t", "trace");
                w.WriteString("time", e.Time);
                w.WriteNumber("slot", e.Slot);
                w.WriteString("signal", e.Signal);
                w.WriteString("kind", e.Kind switch { SigKind.Digital => "Digital", SigKind.Analog => "Analog", _ => "Serial" });
                w.WritePropertyName("value");
                switch (e.Value)
                {
                    case bool b: w.WriteBooleanValue(b); break;
                    case ushort u: w.WriteNumberValue(u); break;
                    case string str: w.WriteStringValue(str); break;
                    default: JsonSerializer.Serialize(w, e.Value, json); break;
                }
                w.WriteString("source", e.Source);
                if (e.Symbol != null) w.WriteString("symbol", e.Symbol); else w.WriteNull("symbol");
                if (e.Cause != null) w.WriteString("cause", e.Cause); else w.WriteNull("cause");
                w.WriteNumber("depth", e.Depth);
                w.WriteNumber("chain", e.Chain);
                w.WriteEndObject();
                break;
            case LogEntry l:
                w.WriteStartObject();
                w.WriteString("t", "log");
                w.WriteString("time", l.Time);
                w.WriteNumber("slot", l.Slot);
                w.WriteString("level", l.Level);
                w.WriteString("message", l.Message);
                w.WriteEndObject();
                break;
            default:
                JsonSerializer.Serialize(w, item, json);
                break;
        }
    }

    /// <summary>
    /// Where to keep the list of unsupported symbols: <c>OpenVP:UnsupportedLog</c> if set ("off" to keep it in
    /// memory only); else <c>docs/unsupported.md</c> in the source checkout the server runs from, so it gets
    /// committed and shared; else <c>unsupported.md</c> in the data folder.
    /// </summary>
    internal static string? UnsupportedLogPath(string? configured, string dataDirectory)
    {
        if (string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "OpenVP.sln")) && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                    return Path.Combine(dir.FullName, "docs", "unsupported.md");
        return Path.Combine(dataDirectory, "unsupported.md");
    }
}
