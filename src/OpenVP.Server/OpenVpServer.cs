using System.Buffers;
using Microsoft.AspNetCore.ResponseCompression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using OpenVP.Core;
using OpenVP.Core.Alerts;
using OpenVP.Core.Community;
using OpenVP.Core.Eisc;
using OpenVP.Core.Labs;
using OpenVP.Core.Plugins;
using OpenVP.Core.Ops;
using OpenVP.Core.Security;
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
        // Uploads (programs, whole-processor exports) can be large; the default form limit is 128 MB.
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 512L * 1024 * 1024);
        var cfg = builder.Configuration.GetSection("OpenVP");

        // Users, tokens, the audit log and HTTPS settings live under data/security. HTTPS is decided now because it changes what Kestrel listens on.
        var dataDir = cfg["DataDirectory"] ?? "data";
        var secSettings = new SecuritySettings(dataDir);
        var httpsMgr = new HttpsManager(dataDir, secSettings);
        OpenVP.Core.Integrations.IntegrationsHost? integrationsHolder = null; // set below; single sign-on reads its secret store lazily
        var oidc = new OidcSupport(secSettings, () => integrationsHolder!.Store.Secrets) { AllowHttp = cfg.GetValue("OidcAllowHttp", false) };
        oidc.AddServices(builder.Services);
        var extraUrls = new List<string>();
        var baseUrls = builder.Configuration["urls"];
        if (string.IsNullOrWhiteSpace(baseUrls)) baseUrls = "http://localhost:5000";
        var httpsSettings = secSettings.Get().Https;
        if (httpsSettings.Enabled)
        {
            httpsMgr.Load(out var httpsProblem);
            if (!httpsMgr.HasCertificate) Console.WriteLine("HTTPS is off: " + httpsProblem);
            else if (CheckHttpsPort(baseUrls, httpsSettings.Port) is { } portProblem) Console.WriteLine("HTTPS is off: " + portProblem);
            else
            {
                if (httpsProblem != null) Console.WriteLine("HTTPS warning: " + httpsProblem);
                extraUrls.Add($"https://0.0.0.0:{httpsSettings.Port}");
                httpsMgr.MarkListening(httpsSettings.Port);
                builder.WebHost.ConfigureKestrel(k => k.ConfigureHttpsDefaults(o => o.ServerCertificateSelector = (_, _) => httpsMgr.Holder.Current));
            }
        }

        // The internet tunnel gets its own listener on 127.0.0.1, never the console's port (see TunnelGate).
        var tunnelPort = cfg.GetValue("TunnelPort", 8085);
        string? tunnelUnavailable = null;
        if (tunnelPort > 0)
        {
            tunnelUnavailable = CheckTunnelPort(baseUrls, tunnelPort);
            if (tunnelUnavailable == null) extraUrls.Add("http://127.0.0.1:" + tunnelPort);
            else Console.WriteLine("Internet access (tunnel) is off: " + tunnelUnavailable);
        }
        else tunnelUnavailable = "Internet access is switched off (OpenVP:TunnelPort is 0).";
        if (extraUrls.Count > 0) builder.WebHost.UseUrls(baseUrls + ";" + string.Join(";", extraUrls));

        var processor = new Processor(dataDir, cfg.GetValue("Slots", 10), cfg.GetValue("SlotSpinMicroseconds", 0));
        processor.AddLoader(new SimplSharpLoader());
        processor.AddLoader(new SimplLoader(Path.Combine(processor.DataDirectory, "symbols.json")));
        processor.AddLoader(new LpzLoader(Path.Combine(processor.DataDirectory, "primitives.json")));
        var unsupportedPath = UnsupportedLogPath(cfg["UnsupportedLog"], processor.DataDirectory);
        processor.Unsupported.SetSupportCheck((kind, name) => OpenVP.Lpz.SupportedSymbols.IsSupported(kind, name) || OpenVP.SimplSharpHost.ShimSupport.IsSupported(kind, name)); // rows for symbols, types and members added since are dropped
        if (unsupportedPath != null)
        {
            processor.Unsupported.UseFile(unsupportedPath);
            Console.WriteLine($"Unsupported symbols are listed in {unsupportedPath}");
        }
        processor.LogWritten += e => Console.WriteLine($"{e.Time:HH:mm:ss} [slot {e.Slot:00}] {e.Level,-7} {e.Message}");
        foreach (var slot in processor.Slots) slot.Io.Start(); // simulators that were left on
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
                cip = new CipServer(processor, cfg.GetValue("CipPort", 41794)) { LogPackets = cfg.GetValue("CipLogPackets", false), ProbeHeartbeats = cfg.GetValue("CipHeartbeatProbe", false) };
                cip.Start();
                Console.WriteLine($"CIP listener (experimental) on port {cip.Port}");
            }
            catch (Exception e) { Console.WriteLine($"CIP listener not started: {e.Message}"); }
        }

        // CH5 clients (Crestron Virtual Panel, the HTML5 Web XPanel) speak the same CIP inside a secure WebSocket on 49200,
        // and may first ask for a token over HTTPS on 443. Both are best effort: a port that can't be opened is only reported.
        var webXPanels = new List<WebXPanelServer>();
        if (cip != null && cfg.GetValue("WebXPanelEnabled", true))
        {
            var cipServer = cip;
            var xpCert = new Lazy<System.Security.Cryptography.X509Certificates.X509Certificate2>(() => WebXPanelCertificate(httpsMgr, dataDir));
            var consolePorts = (baseUrls + ";" + string.Join(";", extraUrls)).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(u => Uri.TryCreate(u.Replace("://*", "://localhost").Replace("://+", "://localhost"), UriKind.Absolute, out var uri) ? uri.Port : 0).ToHashSet();
            void Listen(string setting, int fallback, string what)
            {
                var port = cfg.GetValue(setting, fallback);
                if (port <= 0 || webXPanels.Any(w => w.Port == port)) return;
                if (consolePorts.Contains(port)) { Console.WriteLine($"{what} not started: port {port} is used by the console (OpenVP:{setting})."); return; }
                try
                {
                    var w = new WebXPanelServer(cipServer, port, () => xpCert.Value);
                    w.Start();
                    webXPanels.Add(w);
                    Console.WriteLine($"{what} (experimental) on port {w.Port}");
                }
                catch (Exception e) { Console.WriteLine($"{what} not started on port {port}: {e.Message}"); }
            }
            Listen("WebXPanelPort", 49200, "CH5 panel listener (CIP over WebSocket)");
            Listen("WebXPanelTokenPort", 443, "CH5 panel token listener");
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

        // Who is calling and what they may do: users, sessions, API tokens, the audit log and slot locks (see Authz).
        var users = new UserStore(dataDir);
        var sessions = new SessionStore(dataDir);
        var tokens = new TokenStore(dataDir);
        var audit = new AuditLog(dataDir);
        var slotLocks = new SlotLocks(dataDir);
        var presence = new PresenceTracker();
        var acmeSvc = new AcmeService(dataDir, httpsMgr, secSettings);
        var acmeTimer = new Timer(_ => { try { _ = acmeSvc.RenewIfDueAsync((a, t) => audit.Add(a, t, "auto", "")); } catch { } }, null, TimeSpan.FromMinutes(5), TimeSpan.FromHours(12));
        var authz = new Authz(auth, users, sessions, tokens, secSettings, slotLocks, audit);
        builder.Services.AddSingleton(authz);
        builder.Services.AddSingleton(audit);
        builder.Services.AddSingleton(httpsMgr);
        builder.Services.AddSingleton(acmeSvc);
        if (users.Any) Console.WriteLine($"{users.Count} user{(users.Count == 1 ? "" : "s")}: sign in at /login.html. The access key still works for scripts and Admins." + (authz.LocalExempt ? " This computer (localhost) needs no login." : ""));

        var app = builder.Build();
        oidc.Attach(app.Services);
        var gate = new TunnelGate(tunnelUnavailable == null ? tunnelPort : 0);
        app.Use(gate.Handle);   // first: anything on the tunnel port is marked remote and limited to /s/...
        app.Use(HttpsRedirect(secSettings, httpsMgr));
        app.Use(authz.Handle);
        app.UseWhen(c => c.Request.Path.StartsWithSegments("/signin-oidc") || c.Request.Path.StartsWithSegments("/signout-callback-oidc"), b => b.UseAuthentication());
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        // gzip/brotli for JSON, html, js and the like (the default MIME list; downloads of programs and other
        // binary files are not touched). Compression only applies to a response whose request asks for it, so
        // WebSocket upgrades (101, no body) pass straight through.
        app.UseResponseCompression();
        // The console's own pages are never served on the tunnel port.
        app.UseWhen(c => !c.Items.ContainsKey(TunnelGate.ItemKey), b =>
        {
            b.UseDefaultFiles();
            // Pages are served with an ETag and no-cache: the browser keeps them but asks every time, and an unchanged
            // file costs a 304, so an edited page still shows at once in dev mode.
            b.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = c => c.Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] = "no-cache"
            });
        });

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var uploads = Path.Combine(processor.DataDirectory, "uploads");
        Directory.CreateDirectory(uploads);
        // Slots loaded from a file on this computer with "reload when it changes" on: the watch on that file.
        var watches = new SourceWatch?[processor.Slots.Count];

        var recordings = new SlotRecordings(processor, new RecordingStore(processor.DataDirectory));
        var trash = new Trash(processor.DataDirectory);
        trash.Purge();
        var recent = new RecentPrograms(processor.DataDirectory);
        var crash = new CrashSupervisor(processor, stuckCheck: TimeSpan.FromSeconds(1));
        // Who did what and when, kept in data/activity.jsonl (see ActivityLog). "who" is as good as OpenVP can tell today; user accounts will add a name.
        var activity = new ActivityLog(processor.DataDirectory);
        // Everything in the activity feed is also written to the tamper-evident audit log, with the caller's address (except what OpenVP does on its own at start-up).
        activity.Added += e =>
        {
            if (e.Action is "crash" or "report" or "state" || (e.Action == "restore" && e.Who == Actors.Auto)) return;
            var cp = Principal.Current;
            audit.Add(e.Action, e.Text, e.User ?? (e.Who == Actors.Auto ? "auto" : null), cp is { Ip.Length: > 0 } ? cp.Ip : e.Who, e.Slot, e.Detail);
        };
        crash.Acted += (n, what, why) => activity.Add("crash", $"Slot {n} crashed ({why}): {what}", n, Actors.Auto);
        var checker = new ReadinessChecker(processor.DataDirectory);
        var templates = Templates.Default();
        var samplesDir = Path.Combine(AppContext.BaseDirectory, "samples");
        string Who(HttpContext ctx)
        {
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip is { IsIPv4MappedToIPv6: true }) ip = ip.MapToIPv4();
            return Actors.FromAddress(ip?.ToString(), ApiAuth.IsDirectLocal(ctx));
        }
        void Note(HttpContext ctx, string action, string text, int? slot = null, string? detail = null) => activity.Add(action, text, slot, Who(ctx), null, detail);
        // A caller limited to some slots only sees those in lists.
        static bool Visible(int n) => Principal.Current?.CanSlot(n) ?? true;
        string Prog(int n) => processor.Slot(n).Runtime?.ProgramName ?? "the program";
        // Program summaries don't change once a program is loaded, so keep them; a SIMPL# program's devices appear when it starts, so ask again.
        var summaries = new System.Runtime.CompilerServices.ConditionalWeakTable<IProgramRuntime, ProgramSummary>();
        ProgramSummary? SummaryOf(ProgramSlot s)
        {
            if (s.Runtime is not IProgramAnalysis a) return null;
            try
            {
                if (s.Runtime.Kind == "SIMPL#") return a.Summarize();
                if (!summaries.TryGetValue(s.Runtime, out var cached)) summaries.Add(s.Runtime, cached = a.Summarize());
                return cached;
            }
            catch (Exception) { return null; }
        }
        object? SummaryDto(ProgramSummary? m) => m == null ? null : new
        {
            text = m.Text, kind = m.Kind, panels = m.PanelIpIds, eiscs = m.EiscIpIds, otherDevices = m.OtherDevices, comPorts = m.ComPorts, irPorts = m.IrPorts,
            splusModules = m.SplusModules, userMacros = m.UserMacros, signals = m.Signals, devicesKnown = m.DevicesKnown, uses = m.Uses
        };
        // Deleted files, snapshots and recordings stay in data/trash for 24 hours so Undo works; purge what has aged out.
        var trashTimer = new Timer(_ => { try { trash.Purge(); } catch { } }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        object SlotDto(ProgramSlot s) => new
        {
            number = s.Number,
            roomId = s.RoomId,
            name = s.Settings.Name,
            color = s.Settings.Color,
            autostart = s.Settings.Autostart,
            startOrder = s.Settings.StartOrder ?? s.Number,
            startDelaySeconds = s.Settings.StartDelaySeconds,
            cwsPublic = s.Settings.CwsPublic,
            cws = s.Io.Cws.Paths,
            crash = CrashDto(s),
            remembered = s.Settings.Program is { } m ? new { source = m.Source, file = m.File, path = m.FilePath, running = m.Running, watch = m.Watch } : null,
            state = s.State.ToString(),
            kind = s.Runtime?.Kind,
            program = s.Runtime?.ProgramName,
            file = s.ProgramPath is null ? null : Path.GetFileName(s.ProgramPath),
            error = s.LastError,
            summary = SummaryDto(SummaryOf(s)),
            locked = slotLocks.Get(s.Number) is { } lk ? Authz.LockDto(lk) : null,
            presence = presence.ForSlot(s.Number).Select(e => new { name = e.Name, initials = e.Initials, kind = e.Kind, since = e.Since, me = Principal.Current is { } pc && e.Key == Authz.PresenceKey(pc) }),
            alerts = s.Monitor.Oscillation.Recent(DateTimeOffset.Now),
            alertText = s.Monitor.Oscillation.Alerts.FirstOrDefault()?.Message,
            exceptions = s.Exceptions.Count,
            watching = watches[s.Number - 1]?.Program,
            recording = recordings.Busy(s.Number),
            devices = s.Devices.OrderBy(d => d.IpId).Select(DeviceDto)
        };

        object CrashDto(ProgramSlot s)
        {
            var c = s.Settings.Crash;
            var st = crash.Status(s.Number);
            return new
            {
                mode = c.Mode, maxRestarts = c.MaxRestarts, windowMinutes = c.WindowMinutes, stuckSeconds = c.StuckSeconds, faultOnUnhandled = c.FaultOnUnhandled,
                restarts = st.RestartsInWindow,
                last = st.LastTime is null ? null : new { time = st.LastTime, action = st.LastAction, reason = st.LastReason }
            };
        }

        object DeviceDto(JoinDevice d) => new
        {
            ipId = $"{d.IpId:X2}",
            publicIpId = $"{processor.Slot(d.Slot).PublicIpId(d.IpId):X2}", // what panels connect to (differs from ipId when the IP ID is remapped)
            remapped = processor.Slot(d.Slot).Io.Remap.IsRemapped(d.IpId),
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
            latencySource = c.LatencySource,
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
                conditions = st.Conditions ?? Array.Empty<string>(),
                paused = p is null ? null : new { step = p.Step, time = p.Time, signal = p.Signal, kind = p.Kind.ToString(), value = p.Value, source = p.Source, symbol = p.Symbol, cause = p.Cause, depth = p.Depth, chain = p.Chain, reason = p.Reason }
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

        // First-run Welcome screen: shown until someone finishes or skips it; remembered for everyone.
        var welcome = new WelcomeState(processor.DataDirectory);
        app.MapGet("/api/welcome", () => new { show = welcome.Show });
        app.MapPost("/api/welcome/dismiss", () => { welcome.Dismiss(); return Results.Ok(new { show = false }); });

        app.MapGet("/api/slots", () => processor.Slots.Where(s => Visible(s.Number)).Select(SlotDto));

        // Everything under /api/slots/{n}: an unknown slot is a 404, not a server error.
        var slotApi = app.MapGroup("/api/slots/{n:int}").AddEndpointFilter(async (ctx, next) =>
        {
            var n = Convert.ToInt32(ctx.HttpContext.Request.RouteValues["n"]);
            return processor.IsValidSlot(n) ? await next(ctx) : Results.NotFound(new { error = $"Slot must be 1–{processor.Slots.Count}." });
        });

        slotApi.MapGet("", (int n) => SlotDto(processor.Slot(n)));
        DeviceEndpoints.MapSlot(slotApi, processor, (c, a, t, sl) => Note(c, a, t, sl));

        // MQTT, outgoing and incoming webhooks, OSC, Art-Net, Prometheus and REST per signal (OpenVP.Core.Integrations).
        var integrations = new OpenVP.Core.Integrations.IntegrationsHost(processor, () => new MqttNetTransport());
        integrationsHolder = integrations;
        SecurityEndpoints.Map(app, authz, processor, presence, httpsMgr, acmeSvc, oidc, integrations.Store.Secrets, activity, () => cip?.Port, () => app.Urls.Select(u => Uri.TryCreate(u.Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost").Replace("://+", "://localhost").Replace("://*", "://localhost"), UriKind.Absolute, out var uri) ? uri.Port : 0).Append(tunnelPort));
        authz.CwsPublic = n => processor.IsValidSlot(n) && processor.Slot(n).Settings.CwsPublic;
        CwsEndpoints.Map(app, processor);
        IntegrationEndpoints.Map(app, slotApi, processor, integrations, auth, crash, (c, a, t, sl) => Note(c, a, t, sl));

        // Alerts, uptime and metrics history, thresholds and health reports (OpenVP.Core.Alerts). OpenVP:Alerts=false turns the lot off.
        AlertsHost? alerts = null;
        if (cfg.GetValue("Alerts", true))
        {
            alerts = new AlertsHost(processor, activity, integrations.Store.Secrets, new MailKitEmailSender());
            crash.Acted += alerts.Monitor.OnCrash;
            AlertEndpoints.Map(app, processor, alerts, (c, a, t, sl) => Note(c, a, t, sl));
        }

        // One upload per slot at a time; a second one waits instead of racing on the same folder.
        var uploadLocks = processor.Slots.Select(_ => new SemaphoreSlim(1, 1)).ToArray();

        var snapshots = new SlotSnapshots(processor.DataDirectory);
        // Every program that goes into a slot is kept as a version (data/versions), so it can be loaded again, downloaded and compared.
        var versions = new ProgramVersions(processor.DataDirectory);

        static string FormatSize(long b) => b switch
        {
            < 1024 => $"{b} bytes",
            < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
            _ => $"{b / 1048576.0:0.#} MB"
        };

        // Save the slot's program, NVRAM and User files. Never throws: a failed snapshot is a warning, not a reason to
        // stop what the caller is doing. Returns false if it failed. Call with the slot locked.
        bool TrySnapshot(ProgramSlot slot, string reason, string doing, out SnapshotInfo? taken)
        {
            taken = null;
            try
            {
                var info = snapshots.Take(slot.Number, slot.Runtime?.ProgramName, slot.ProgramPath is null ? null : Path.GetFileName(slot.ProgramPath), reason);
                taken = info;
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
        IResult Install(int n, IReadOnlyList<(string Name, byte[] Data)> files, bool start, ProgramMemory? memory = null, string? fromPath = null, string[]? extra = null, string who = Actors.ThisComputer, string? logAs = "load", string? how = null)
        {
            how ??= logAs == "auto-reload" ? "auto-reload" : fromPath != null ? "path" : "upload";
            var dir = Path.Combine(uploads, $"slot{n:00}");
            var path = Path.Combine(dir, Path.GetFileName(files[0].Name));
            // Unload first so the old program lets go of its files, then replace them and load the new one.
            // The slot stays locked throughout, so a start/stop/unload request waits instead of interleaving.
            return processor.WithSlotLocked(n, slot =>
            {
                if (slot.Runtime != null) TrySnapshot(slot, SlotSnapshots.BeforeLoad, "before loading.", out _);
                slot.Unload();
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                foreach (var (name, data) in files) File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(name)), data);
                // The slot remembers what it was given, so it comes back after OpenVP restarts (even a program that failed to load: it then comes back in Error).
                slot.RememberProgram(memory ?? new ProgramMemory { Source = ProgramMemory.Upload, File = Path.GetFileName(files[0].Name), Running = start });
                crash.ClearHistory(n);
                try { processor.LoadProgram(n, path, start); }
                catch (Exception e)
                {
                    try { versions.Record(n, files, how, who, null); } catch (Exception ve) { slot.Log("Warning", $"Couldn't keep this program as a version: {ve.Message}"); }
                    if (logAs != null) activity.Add(logAs, $"Couldn't load {Path.GetFileName(files[0].Name)} into slot {n}: {Readiness.Explain(e)}", n, who);
                    return Results.UnprocessableEntity(new { error = e.Message, slot = SlotDto(slot) });
                }
                try { versions.Record(n, files, how, who, slot.Runtime?.Kind); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { slot.Log("Warning", $"Couldn't keep this program as a version: {e.Message}"); }
                if (logAs != null)
                    activity.Add(logAs, logAs == "auto-reload" ? $"Reloaded {Path.GetFileName(files[0].Name)} in slot {n} because the file changed" : $"Loaded {Path.GetFileName(files[0].Name)} into slot {n}{(start ? "" : " (not started)")}",
                        n, who, null, slot.Runtime?.Kind);
                try { recent.Add(Path.GetFileName(files[0].Name), slot.Runtime?.Kind ?? "", n, fromPath, fromPath == null ? files : null, extra); }
                catch (Exception e) { slot.Log("Warning", $"Couldn't add the program to Recent: {e.Message}"); }
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
        IResult LoadFromDisk(int n, string program, string[] chosen, bool start, bool watch = false, string who = Actors.ThisComputer, string? logAs = "load")
        {
            var files = ProgramSource.Companions(program, chosen);
            var data = files.Select(f => (f.Name, File.ReadAllBytes(f.Source))).ToList();
            var memory = new ProgramMemory { Source = ProgramMemory.Path, File = Path.GetFileName(data[0].Item1), FilePath = program, Files = chosen.Length == 0 ? null : chosen, Running = start, Watch = watch };
            var result = Install(n, data, start, memory, program, chosen, who, logAs);
            var slot = processor.Slot(n);
            if (files.Count > 1)
                slot.Log("Notice", $"Loaded from {Path.GetDirectoryName(program)} with " + string.Join(", ", files.Skip(1).Select(f =>
                    Path.GetFileName(f.Source) == f.Name ? f.Name : $"{Path.GetFileName(f.Source)} (as {f.Name})")));
            return result;
        }

        // Reload a watched slot when its files change. The watch is replaced or stopped by anything else that loads or
        // unloads the slot, so a stale one does nothing.
        var pendingReloads = new Dictionary<int, (SourceWatch Watch, string[] Chosen)>();
        async Task ReloadAsync(int n, SourceWatch w, string[] chosen)
        {
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                lock (watches) if (watches[n - 1] != w) return;
                var slot = processor.Slot(n);
                // A locked slot is left alone: the change waits and is loaded when the lock ends.
                if (slotLocks.Get(n) is { } held)
                {
                    lock (pendingReloads) pendingReloads[n] = (w, chosen);
                    slot.Log("Warning", $"{Path.GetFileName(w.Program)} changed on disk, but auto-reload is paused because the slot is locked by {held.By}{(string.IsNullOrEmpty(held.Reason) ? "" : " (" + held.Reason + ")")}. It will reload when the lock ends.");
                    activity.Add("auto-reload.paused", $"Auto-reload of slot {n} paused: the slot is locked by {held.By}", n, Actors.Auto);
                    return;
                }
                lock (pendingReloads) pendingReloads.Remove(n);
                slot.Log("Notice", $"{Path.GetFileName(w.Program)} changed on disk; reloading.");
                LoadFromDisk(n, w.Program, chosen, slot.State != SlotState.Stopped, watch: true, who: Actors.Auto, logAs: "auto-reload");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                processor.Slot(n).Log("Warning", $"Couldn't reload {Path.GetFileName(w.Program)}: {e.Message} It will be tried again when the file changes.");
            }
            catch (Exception e) { processor.Slot(n).Log("Error", $"Reloading {Path.GetFileName(w.Program)} failed: {e.Message}"); }
            finally { uploadLocks[n - 1].Release(); }
        }

        // When a lock ends, load what changed while it was on.
        slotLocks.Changed += (n, locked) =>
        {
            if (locked) return;
            (SourceWatch Watch, string[] Chosen) pending;
            lock (pendingReloads) { if (!pendingReloads.Remove(n, out pending)) return; }
            _ = ReloadAsync(n, pending.Watch, pending.Chosen);
        };
        var lockTimer = new Timer(_ => { try { slotLocks.Tick(); } catch { } }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        slotApi.MapPost("/program", async (int n, HttpContext ctx) =>
        {
            var req = ctx.Request;
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

            var how = string.Equals(form["how"], "push", StringComparison.OrdinalIgnoreCase) ? "push" : "upload";
            await uploadLocks[n - 1].WaitAsync();
            try { StopWatching(n); return Install(n, files, start, who: Who(ctx), how: how); }
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
                try { result = LoadFromDisk(n, path, chosen, start, watch, Who(ctx)); }
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
        slotApi.MapDelete("/watch", (int n, HttpContext ctx) =>
        {
            var was = watches[n - 1]?.Program;
            StopWatching(n);
            processor.Slot(n).RememberWatch(false);
            if (was != null) processor.Slot(n).Log("Notice", $"Stopped watching {was}.");
            if (was != null) Note(ctx, "settings", $"Stopped reloading slot {n} when its file changes", n, was);
            return SlotDto(processor.Slot(n));
        });

        // Snapshots: a copy of the slot's program files, NVRAM and User folders, taken before every load.
        slotApi.MapGet("/snapshots", (int n) => snapshots.List(n).Select(SnapshotDto));

        slotApi.MapPost("/snapshots", async (int n, HttpContext ctx) =>
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
                        Note(ctx, "snapshot", $"Saved a snapshot of slot {n} ({info.Program ?? "files only"})", n);
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

        slotApi.MapPost("/snapshots/{id}/restore", async (int n, string id, HttpContext ctx, JsonElement? body) =>
        {
            bool? startOverride = body is { ValueKind: JsonValueKind.Object } b0 && b0.TryGetProperty("start", out var st0) && st0.ValueKind is JsonValueKind.True or JsonValueKind.False ? st0.GetBoolean() : null;
            if (snapshots.Get(n, id) == null) return Results.NotFound(new { error = "There's no such snapshot." });
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                return processor.WithSlotLocked(n, slot =>
                {
                    if (snapshots.Get(n, id) == null) return Results.NotFound(new { error = "There's no such snapshot." });
                    // The current state goes into a snapshot first, so a roll back can be undone. If that can't be done, stop here.
                    if (!TrySnapshot(slot, SlotSnapshots.BeforeRollBack, "before rolling back.", out _))
                        return Results.Json(new { error = "Couldn't save a snapshot of the current state, so nothing was rolled back. See the slot log." }, statusCode: 500);
                    StopWatching(n);
                    var wasRunning = startOverride ?? slot.State == SlotState.Running;
                    slot.Unload(); // the program lets go of its files before they are replaced
                    SnapshotInfo? info;
                    try { info = snapshots.Restore(n, id); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        slot.Log("Error", $"Rolling back failed: {e.Message}");
                        return Results.Json(new { error = $"Couldn't restore the snapshot: {e.Message}", slot = SlotDto(slot) }, statusCode: 500);
                    }
                    if (info == null) return Results.NotFound(new { error = "There's no such snapshot." });
                    slot.Io.Reload(); // simulators, redirects and the IP ID remap come back with the snapshot
                    integrations.Store.ReloadSlot(n); // so do the slot's integration choices
                    slot.Log("Notice", $"Rolled back to the snapshot of {info.Program ?? "the slot"} from {info.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({info.Reason}).");
                    Note(ctx, "rollback", $"Rolled slot {n} back to the snapshot of {info.Program ?? "the slot"} from {info.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({info.Reason})", n);
                    if (snapshots.RestoredProgramPath(n, info) is { } path && File.Exists(path))
                    {
                        try { versions.RecordFolder(n, Path.GetDirectoryName(path)!, Path.GetFileName(path), "roll back", Who(ctx)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                        slot.RememberProgram(new ProgramMemory { Source = ProgramMemory.Upload, File = Path.GetFileName(path), Running = wasRunning });
                        try { processor.LoadProgram(n, path, wasRunning); }
                        catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message, slot = SlotDto(slot) }); }
                    }
                    return Results.Ok(SlotDto(slot));
                });
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        slotApi.MapDelete("/snapshots/{id}", async (int n, string id, HttpContext ctx) =>
        {
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                try
                {
                    if (snapshots.Delete(n, id, trash) is not { } t) return Results.NotFound(new { error = "There's no such snapshot." });
                    Note(ctx, "snapshot.delete", $"Deleted a snapshot of slot {n}", n);
                    return Results.Ok(new { deleted = id, undo = t.Id });
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { return Results.Json(new { error = $"Couldn't delete the snapshot: {e.Message}" }, statusCode: 500); }
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        // Name, colour, autostart, start order and delay, crash policy. Send only the fields to change; the console saves on every change.
        slotApi.MapPut("/settings", (int n, HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var slot = processor.Slot(n);
                slot.UpdateSettings(cur => cur.WithPatch(body));
                var changed = body.ValueKind == JsonValueKind.Object ? body.EnumerateObject().Select(p => p.Name == "crash" && p.Value.ValueKind == JsonValueKind.Object ? "crash policy" : p.Name).ToList() : new List<string>();
                if (changed.Count > 0) Note(ctx, "settings", $"Changed slot {n} settings: {string.Join(", ", changed)}", n);
                return Results.Ok(SlotDto(slot));
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        // Load a program from the Recent list into this slot: from its path when it was loaded from a file on this computer,
        // else from the copy OpenVP kept of the upload.
        slotApi.MapPost("/program/recent/{id}", async (int n, string id, HttpContext ctx) =>
        {
            var e = recent.Get(id);
            if (e == null) return Results.NotFound(new { error = "That program is no longer in the Recent list." });
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                StopWatching(n);
                if (e.Path != null)
                {
                    if (!ApiAuth.IsDirectLocal(ctx))
                        return Results.Json(new { error = "Loading from a path only works on the computer OpenVP runs on." }, statusCode: StatusCodes.Status403Forbidden);
                    if (!File.Exists(e.Path)) return Results.NotFound(new { error = $"{e.Path} isn't there any more." });
                    var chosen = (e.Files ?? Array.Empty<string>()).Where(File.Exists).ToArray();
                    var w = new SourceWatch(e.Path, () => ProgramSource.Companions(e.Path, chosen).Select(f => f.Source));
                    IResult result;
                    try { result = LoadFromDisk(n, e.Path, chosen, true, watch: true, who: Who(ctx)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { w.Dispose(); return Results.UnprocessableEntity(new { error = $"Couldn't read the program: {ex.Message}" }); }
                    w.Changed += () => _ = ReloadAsync(n, w, chosen);
                    lock (watches) watches[n - 1] = w;
                    return result is IStatusCodeHttpResult { StatusCode: 200 } ? Results.Ok(SlotDto(processor.Slot(n))) : result;
                }
                var files = recent.ReadCopy(id);
                if (files == null) return Results.NotFound(new { error = "OpenVP didn't keep a copy of this upload (it was too big, or has been cleared). Choose the file again." });
                return Install(n, files, true, who: Who(ctx), how: "recent");
            }
            finally { uploadLocks[n - 1].Release(); }
        });

        // Virtual Control Room ID: panels can be routed to the slot by it (/ws/panel?room=).
        slotApi.MapPut("/room", (int n, HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var id = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("roomId", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                processor.Slot(n).SetRoomId(id);
                Note(ctx, "settings", $"Changed the Room ID of slot {n} to {processor.Slot(n).RoomId}", n);
                return Results.Ok(SlotDto(processor.Slot(n)));
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        // The slot as JSON plus "undo": {snapshot} when an unload left a snapshot that brings the program back.
        JsonObject WithUndo(object dto, (string? Id, bool Running) undo)
        {
            var node = JsonSerializer.SerializeToNode(dto, json)!.AsObject();
            if (undo.Id != null) node["undo"] = new JsonObject { ["snapshot"] = undo.Id, ["start"] = undo.Running };
            return node;
        }

        // The same actions are used by the buttons, the keyboard, the command palette and the bulk endpoint.
        void StartSlot(int n)
        {
            crash.ClearHistory(n);
            var slot = processor.Slot(n);
            // A slot in Error (a crash, or a failed start) is started afresh from its file; any other state just starts.
            if (slot.State == SlotState.Error && slot.ProgramPath != null) processor.Restart(n); else slot.Start();
            slot.RememberRunning(true);
        }
        void StopSlot(int n) { processor.Slot(n).Stop(); processor.Slot(n).RememberRunning(false); }
        void RestartSlot(int n) { crash.ClearHistory(n); processor.Restart(n); processor.Slot(n).RememberRunning(true); }
        // Unload keeps a snapshot first, so Undo can bring the program back. Returns that snapshot's id (null if there was nothing to keep).
        (string? Id, bool Running) UnloadSlot(int n)
        {
            var slot = processor.Slot(n);
            string? undo = null;
            var running = slot.State == SlotState.Running;
            StopWatching(n);
            if (slot.Runtime != null && TrySnapshot(slot, SlotSnapshots.BeforeUnload, "before unloading.", out var snap)) undo = snap?.Id;
            slot.Unload();
            slot.RememberProgram(null);
            return (undo, running);
        }

        slotApi.MapPost("/start", async (int n, HttpContext ctx) =>
        {
            await uploadLocks[n - 1].WaitAsync();
            try { StartSlot(n); Note(ctx, "start", $"Started {Prog(n)} in slot {n}", n); return Results.Ok(SlotDto(processor.Slot(n))); }
            catch (Exception e) { Note(ctx, "start", $"Couldn't start slot {n}: {e.Message}", n); return Results.UnprocessableEntity(new { error = e.Message }); }
            finally { uploadLocks[n - 1].Release(); }
        });
        slotApi.MapPost("/stop", (int n, HttpContext ctx) => { StopSlot(n); Note(ctx, "stop", $"Stopped {Prog(n)} in slot {n}", n); return SlotDto(processor.Slot(n)); });
        slotApi.MapPost("/restart", async (int n, HttpContext ctx) =>
        {
            await uploadLocks[n - 1].WaitAsync();
            try { RestartSlot(n); Note(ctx, "restart", $"Restarted {Prog(n)} in slot {n}", n); return Results.Ok(SlotDto(processor.Slot(n))); }
            catch (Exception e) { Note(ctx, "restart", $"Couldn't restart slot {n}: {e.Message}", n); return Results.UnprocessableEntity(new { error = e.Message, slot = SlotDto(processor.Slot(n)) }); }
            finally { uploadLocks[n - 1].Release(); }
        });
        slotApi.MapPost("/unload", async (int n, HttpContext ctx) =>
        {
            // Under the upload lock, so a reload of a watched slot that's already under way can't load it again afterwards.
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                var was = processor.Slot(n).Runtime?.ProgramName;
                var undo = UnloadSlot(n);
                Note(ctx, "unload", $"Unloaded {was ?? "the program"} from slot {n}", n);
                return Results.Ok(WithUndo(SlotDto(processor.Slot(n)), undo));
            }
            finally { uploadLocks[n - 1].Release(); }
        });


        // ------------------------------------------------------------------ export and import, backups, versions, schedules, push, git (OpenVP.Core.Ops)

        var secretStore = integrations.Store.Secrets;
        var previews = new ImportPreviews(processor.DataDirectory);
        var profiler = new ProgramProfiler(processor.DataDirectory);
        var pushTargets = new TargetStore(processor.DataDirectory, secretStore);
        var pushClient = new PushClient(pushTargets);
        var pushManager = new PushManager(pushClient);
        var reloaders = new Dictionary<string, Action>(); // settings that live in a store; each is read again after an import replaced its file
        var exporter = new ProcessorExporter(processor.DataDirectory, processor.Slots.Count, secretStore, n => processor.Slot(n).Runtime?.ProgramName,
            () => new
            {
                telnetEnabled = cfg.GetValue("TelnetEnabled", false), telnetPort = cfg.GetValue("TelnetPort", 41795), sshEnabled = cfg.GetValue("SshEnabled", false), sshPort = cfg.GetValue("SshPort", 22),
                mdns = cfg.GetValue<bool?>("Mdns"), mdnsName = cfg["MdnsName"], consoleMaxConnections = cfg.GetValue("ConsoleMaxConnections", 5)
            }, recent);
        var backups = new BackupService(processor.DataDirectory, (path, opts) =>
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            return exporter.Export(fs, opts);
        }, secretStore);
        backups.Result += (ok, message, reason) => { if (!ok || reason != "manual") activity.Add("backup", ok ? "Backed up the processor: " + message : "Backup failed: " + message, null, Actors.Auto); };
        if (alerts != null) backups.Result += (ok, message, _) => alerts.Monitor.OnBackup(ok, message);

        async Task<string> ExecuteSchedule(ScheduleDef d, string trigger)
        {
            var a = d.Action;
            if (a.Type == ScheduleAction.Backup)
            {
                var info = await Task.Run(() => backups.RunNow("scheduled"));
                if (info == null) throw new InvalidOperationException(backups.Settings.LastOk == false ? backups.Settings.LastMessage ?? "The backup failed." : "A backup is already running.");
                return $"Backed up to {info.File}";
            }
            var n = a.Slot ?? 0;
            if (!processor.IsValidSlot(n)) throw new InvalidOperationException($"Slot {n} doesn't exist.");
            // A locked slot isn't changed by a schedule (a manual "Run now" by an Admin or the one who locked it still goes through).
            if (slotLocks.Blocking(n, trigger == "manual" ? Principal.Current : null) is { } held) throw new ScheduleSkippedException(SlotLocks.Message(held));
            await uploadLocks[n - 1].WaitAsync();
            try
            {
                switch (a.Type)
                {
                    case ScheduleAction.Restart:
                        if (processor.Slot(n).ProgramPath == null) throw new InvalidOperationException($"Slot {n} has no program to restart.");
                        RestartSlot(n);
                        return $"Restarted {Prog(n)} in slot {n}";
                    case ScheduleAction.Start:
                        if (processor.Slot(n).Runtime == null && processor.Slot(n).State != SlotState.Error) throw new InvalidOperationException($"Slot {n} has no program to start.");
                        StartSlot(n);
                        return $"Started {Prog(n)} in slot {n}";
                    case ScheduleAction.Stop:
                        StopSlot(n);
                        return $"Stopped {Prog(n)} in slot {n}";
                    case ScheduleAction.LoadVersion:
                    {
                        var files = versions.ReadFiles(n, a.VersionId) ?? throw new InvalidOperationException("That program version isn't kept any more.");
                        StopWatching(n);
                        if (Install(n, files, a.StartAfter, who: Actors.Auto, how: "schedule") is IStatusCodeHttpResult { StatusCode: not 200 })
                            throw new InvalidOperationException(processor.Slot(n).LastError ?? "The program didn't load. See the slot log.");
                        return $"Loaded {files[0].Name} into slot {n}{(a.StartAfter ? "" : " (not started)")}";
                    }
                    case ScheduleAction.LoadPath:
                    {
                        if (string.IsNullOrEmpty(a.Path) || !File.Exists(a.Path)) throw new InvalidOperationException($"There's no file at {a.Path}.");
                        StopWatching(n);
                        if (LoadFromDisk(n, a.Path, Array.Empty<string>(), a.StartAfter, false, Actors.Auto, "load") is IStatusCodeHttpResult { StatusCode: not 200 })
                            throw new InvalidOperationException(processor.Slot(n).LastError ?? "The program didn't load. See the slot log.");
                        return $"Loaded {Path.GetFileName(a.Path)} into slot {n}{(a.StartAfter ? "" : " (not started)")}";
                    }
                    case ScheduleAction.SetSignal:
                    case ScheduleAction.Pulse:
                    {
                        var text = a.Type == ScheduleAction.Pulse ? "pulse" : a.Value ?? "";
                        integrations.Directory.TrySetFromText(n, a.Signal!, text, out var error);
                        if (error != null) throw new InvalidOperationException(error);
                        return a.Type == ScheduleAction.Pulse ? $"Pulsed {a.Signal} in slot {n}" : $"Set {a.Signal} to {a.Value} in slot {n}";
                    }
                    default: throw new InvalidOperationException("Unknown action.");
                }
            }
            finally { uploadLocks[n - 1].Release(); }
        }
        var scheduler = new Scheduler(processor.DataDirectory, ExecuteSchedule);
        scheduler.Ran += run => activity.Add("schedule",
            run.Status == "ok" ? $"Schedule “{run.Name}” ran: {run.Message}" : run.Status == "failed" ? $"Schedule “{run.Name}” failed: {run.Message}" : $"Schedule “{run.Name}” skipped: {run.Message}",
            run.Slot, Actors.Auto, null, run.Trigger == "schedule" ? null : run.Trigger);
        if (alerts != null) scheduler.Ran += alerts.Monitor.OnScheduleRun;

        var git = new GitConfigSync(processor.DataDirectory, processor.Slots.Count, () => secretStore.Snapshot().Values.Append(auth.Key ?? ""));
        var gitQuiet = new HashSet<string> { "git", "backup", "backup.delete", "report", "start", "stop", "restart", "crash", "export", "push", "internet.on", "internet.off", "undo" };
        activity.Added += e => { if (!gitQuiet.Contains(e.Action) && !e.Action.StartsWith("share") && !e.Action.StartsWith("target")) git.RequestCommit(e.Text); };
        git.Committed += r => { if (r.Committed) activity.Add("git", "Committed the config to git: " + r.Message, null, Actors.Auto); else if (r.Hash == null && !r.Message.StartsWith("Nothing")) activity.Add("git", "Couldn't commit the config to git: " + r.Message, null, Actors.Auto); };

        var importer = new ProcessorImporter(processor, snapshots, secretStore, versions)
        {
            BeforeSlot = StopWatching,
            AfterSlotFiles = n => { processor.Slot(n).Io.Reload(); integrations.Store.ReloadSlot(n); crash.ClearHistory(n); },
            Loader = (n, path, start) => { try { processor.LoadProgram(n, path, start); return null; } catch (Exception e) { return Readiness.Explain(e); } },
            RecentRestored = list => recent.MergePaths(list),
            SettingsReplaced = what =>
            {
                foreach (var w in what)
                    switch (w)
                    {
                        case "integrations": integrations.Store.ReloadGlobal(); break;
                        case "alerts": alerts?.Config.Reload(); break;
                        case "schedules": scheduler.Reload(); break;
                        case "targets": pushTargets.Reload(); break;
                        case "backups": backups.Reload(); break;
                        case "versions": versions.ReloadSettings(); break;
                        case "hooks": integrations.Hooks.Reload(); break;
                        case "secrets": secretStore.Reload(); break;
                        case "shares": if (reloaders.TryGetValue("shares", out var rs)) rs(); break;
                    }
            }
        };
        var ops = new OpsServices
        {
            Processor = processor, Activity = activity, Trash = trash, Snapshots = snapshots, Versions = versions, Profiler = profiler, Backups = backups, Scheduler = scheduler,
            Locks = slotLocks, Targets = pushTargets, PushClient = pushClient, Push = pushManager, Git = git, Exporter = exporter, Importer = importer, Secrets = secretStore, SlotLocks = uploadLocks,
            Who = Who, Previews = previews, StopWatching = StopWatching,
            Install = (n, files, start, who, how) => Install(n, files, start, who: who, how: how)
        };
        OpsEndpoints.Map(app, slotApi, ops);

        // Community and Labs: experimental switches, remote EISC links, plugins, the compatibility dashboard with its issue report, and the example library.
        var labs = new LabsStore(processor.DataDirectory, key => cfg.GetValue<bool?>(key));
        var eiscLinks = new RemoteEiscStore(processor.DataDirectory);
        var appVersion = typeof(OpenVpServer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var eisc = new RemoteEiscManager(processor, eiscLinks, pushTargets, () => labs.IsOn(LabsCatalog.RemoteEisc), RemoteEiscManager.LoadOrCreateInstanceId(processor.DataDirectory), appVersion);
        var plugins = new PluginHost(processor, processor.DataDirectory, () => labs.IsOn(LabsCatalog.Plugins), (a, t) => activity.Add(a, t, null, Actors.Auto));
        Redactor.Names = new[] { Environment.UserName, Environment.MachineName }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        void ApplyLabs()
        {
            if (cip != null) cip.ProbeHeartbeats = labs.IsOn(LabsCatalog.CipHeartbeatProbe);
        }
        ApplyLabs();
        labs.Changed += (id, on) =>
        {
            ApplyLabs();
            if (id == LabsCatalog.RemoteEisc) { if (!on) eisc.StopInbound("Remote EISC was switched off in Labs."); eisc.Reconcile(); }
            if (id == LabsCatalog.Plugins) plugins.Apply();
        };
        ops.TargetRemoved = eiscLinks.RemoveForTarget;
        var community = new CommunityServices
        {
            Processor = processor, Labs = labs, Links = eiscLinks, Eisc = eisc, Plugins = plugins, Targets = pushTargets, Activity = activity, Ops = ops, Who = Who, Visible = Visible,
            ExamplesDirectory = cfg["ExamplesDirectory"] is { Length: > 0 } exd ? exd : Path.Combine(AppContext.BaseDirectory, "examples")
        };
        CommunityEndpoints.Map(app, community);
        eisc.Start();
        plugins.Start();
        app.MapGet("/healthz", () => Results.Text("ok"));

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

        slotApi.MapPost("/signals", (int n, HttpContext ctx, JsonElement body) =>
        {
            var slot = processor.Slot(n);
            if (slot.Runtime == null) return Results.NotFound("No program in that slot.");
            if (!body.TryGetProperty("name", out var nameEl))
                return Results.BadRequest("Send {\"name\": \"...\", \"value\": ...} or {\"name\": \"...\", \"pulse\": true}.");
            var name = nameEl.GetString() ?? "";
            // Pulse: high then low with nothing in between, like a processor's debugger.
            if (body.TryGetProperty("pulse", out var pulseEl) && pulseEl.ValueKind == JsonValueKind.True)
            {
                if (!slot.ForcePulse(name)) return Results.NotFound($"Unknown or non-digital signal '{name}'.");
                Note(ctx, "force", $"Pulsed {name} in slot {n}", n);
                return Results.Ok();
            }
            if (!body.TryGetProperty("value", out var valueEl))
                return Results.BadRequest("Send {\"name\": \"...\", \"value\": ...} or {\"name\": \"...\", \"pulse\": true}.");
            var forced = ValueOf(valueEl);
            if (!slot.ForceSignal(name, forced)) return Results.NotFound($"Unknown signal '{name}'.");
            Note(ctx, "force", $"Forced {name} to {forced} in slot {n}", n);
            return Results.Ok();
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

        slotApi.MapPost("/recording/start", (int n, HttpContext ctx) => Recordings(() =>
        {
            recordings.StartRecording(n);
            processor.Slot(n).Log("Notice", "Recording started.");
            Note(ctx, "recording.start", $"Started recording slot {n}", n);
            return Results.Ok(recordings.Status(n));
        }));

        slotApi.MapPost("/recording/stop", (int n, HttpContext ctx, JsonElement? body) => Recordings(() =>
        {
            var saved = recordings.StopRecording(n, body is { } b ? NameFrom(b) : null);
            Note(ctx, "recording.stop", $"Stopped recording slot {n} and saved “{saved.Name}”", n);
            return Results.Ok(new { recording = saved, status = recordings.Status(n) });
        }));

        slotApi.MapPost("/replay/cancel", (int n, HttpContext ctx) =>
        {
            if (!recordings.CancelReplay(n)) return Results.Json(new { error = "Nothing is being replayed." }, statusCode: 409);
            Note(ctx, "recording.cancel", $"Cancelled the replay in slot {n}", n);
            return Results.Ok(recordings.Status(n));
        });

        slotApi.MapGet("/recordings", (int n) => recordings.Store.List(n));

        slotApi.MapPost("/recordings", async (int n, HttpContext ctx) =>
        {
            var req = ctx.Request;
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "Send the recording as multipart/form-data field 'file'." });
            var form = await req.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest(new { error = "No file." });
            if (file.Length > RecordingStore.MaxFileBytes) return Results.BadRequest(new { error = "That file is too large to be a recording." });
            var bytes = ReadAll(file);
            var name = form["name"].ToString();
            return Recordings(() =>
            {
                var info = recordings.Store.Import(n, bytes, string.IsNullOrWhiteSpace(name) ? null : name);
                Note(ctx, "recording.upload", $"Uploaded the recording “{info.Name}” into slot {n}", n);
                return Results.Ok(info);
            });
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

        slotApi.MapDelete("/recordings/{id}", (int n, string id, HttpContext ctx) => Recordings(() =>
        {
            var name = recordings.Store.Get(n, id)?.Name;
            if (recordings.Store.Delete(n, id, trash) is not { } t) return Results.NotFound(new { error = "There's no such recording." });
            Note(ctx, "recording.delete", $"Deleted the recording “{name}” of slot {n}", n);
            return Results.Ok(new { deleted = id, undo = t.Id });
        }));

        slotApi.MapPost("/recordings/{id}/replay", (int n, string id, HttpContext ctx, JsonElement? body) => Recordings(() =>
        {
            var b = body ?? default;
            recordings.StartReplay(n, id, new ReplayOptions { Fast = Flag(b, "fast", false), IncludeEisc = Flag(b, "includeEisc", true) });
            Note(ctx, "recording.replay", $"Started replaying the recording “{recordings.Store.Get(n, id)?.Name}” in slot {n}{(Flag(b, "fast", false) ? " (fast)" : "")}", n);
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
            IEnumerable<string>? conds = body.TryGetProperty("conditions", out var cd) && cd.ValueKind == JsonValueKind.Array
                ? cd.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null;
            try { slot.Stepper.Configure(Bool("enabled"), Bool("onlyBreakSignals"), names, conds); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            return Results.Ok(StepDto(slot));
        });
        slotApi.MapPost("/step/next", (int n) => { processor.Slot(n).Stepper.Next(); return StepDto(processor.Slot(n)); });
        slotApi.MapPost("/step/run", (int n) => { processor.Slot(n).Stepper.Run(); return StepDto(processor.Slot(n)); });

        slotApi.MapPost("/console", (int n, JsonElement body) =>
        {
            try { return Results.Text(processor.Slot(n).RunConsoleCommand(body.GetProperty("command").GetString() ?? "")); }
            catch (Exception e) { return Results.Text("Error: " + e.Message); }
        });

        string CompatText(ProgramSlot slot) => slot.Runtime switch
        {
            SimplSharpRuntime s => s.Compat.ToString(),
            LpzRuntime l => LpzReport.Build(l.Package, l.Package.Signals),
            SimplRuntime r => r.Unsupported.Count == 0
                ? "Every wired symbol has a behavior."
                : "Symbols without a behavior yet:\n" + string.Join('\n', r.Unsupported.Select(u => $"  {u} pins in={u.Inputs.Count} out={u.Outputs.Count}")),
            _ => "No program loaded."
        };
        slotApi.MapGet("/compat", (int n) => Results.Text(CompatText(processor.Slot(n))));

        // The wiring check: pin order OpenVP assumes against what the program's own .smw, .lpz and .sig show. Local console only.
        slotApi.MapGet("/wiring", (int n) =>
        {
            var slot = processor.Slot(n);
            if (slot.ProgramPath is not { } path) return Results.Json(new { available = false, message = "No program loaded." });
            try
            {
                var found = WiringCheck.FindFiles(path);
                if (found.Missing != null)
                    return Results.Json(new { available = false, message = $"The wiring check needs the .smw, .lpz and .sig together (missing: {found.Missing})." });
                return Results.Json(new { available = true, report = WiringCheck.AnalyzeFiles(path) });
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return Results.Json(new { available = false, message = e.Message });
            }
        });

        DebugEndpoints.Map(app, slotApi, processor, activity, sl => SummaryDto(SummaryOf(sl)), CompatText, Who);

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
                .Select(d => new { name = d.Name, folder = true, size = (long?)null, modified = d.LastWriteTimeUtc, editable = false })
                .Concat(info.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new { name = f.Name, folder = false, size = (long?)f.Length, modified = f.LastWriteTimeUtc, editable = TextFileEditor.IsEditableName(f.Name) && f.Length <= TextFileEditor.MaxBytes }));
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

        slotApi.MapPost("/files/folder", (int n, HttpContext ctx, JsonElement body) => Files(() =>
        {
            string? Str(string k) => body.TryGetProperty(k, out var v) ? v.GetString() : null;
            var name = Str("name");
            if (!SlotFiles.IsValidName(name)) return Results.BadRequest(new { error = "Give the folder a name without / \\ : * ? \" < > |." });
            var dir = Path.Combine(SlotFiles.Resolve(processor.Slot(n).Directory, Str("root"), Str("path")), name!);
            if (File.Exists(dir)) return Results.Conflict(new { error = $"A file called '{name}' is already there." });
            Directory.CreateDirectory(dir);
            Note(ctx, "file.folder", $"Created the folder {name} in the {Str("root")?.ToUpperInvariant()} files of slot {n}", n);
            return Results.Ok();
        }));

        // Upload into a folder: any number of multipart files. A file name may carry sub-folders
        // (a whole folder dragged in from the browser), which are created as needed.
        slotApi.MapPost("/files", async (int n, string? root, string? path, HttpContext ctx) =>
        {
            var req = ctx.Request;
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
                Note(ctx, "file.upload", $"Uploaded {(saved.Count == 1 ? saved[0] : saved.Count + " files")} to the {root?.ToUpperInvariant()} files of slot {n}", n);
                return Results.Ok(new { saved });
            });
        });

        slotApi.MapGet("/files/download", (int n, string? root, string? path) => Files(() =>
        {
            var file = SlotFiles.Resolve(processor.Slot(n).Directory, root, path);
            if (!File.Exists(file)) return Results.NotFound(new { error = "No such file." });
            return Results.File(File.OpenRead(file), "application/octet-stream", Path.GetFileName(file));
        }));

        slotApi.MapPost("/files/rename", (int n, HttpContext ctx, JsonElement body) => Files(() =>
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
            Note(ctx, "file.rename", $"Renamed {Path.GetFileName(from)} to {name} in the {Str("root")?.ToUpperInvariant()} files of slot {n}", n);
            return Results.Ok();
        }));

        // Deletes a file, or a folder and everything in it. The NVRAM and user folders themselves stay.
        slotApi.MapDelete("/files", (int n, string? root, string? path, HttpContext ctx) => Files(() =>
        {
            var target = SlotFiles.Resolve(processor.Slot(n).Directory, root, path);
            if (target == SlotFiles.Resolve(processor.Slot(n).Directory, root, "")) return Results.BadRequest(new { error = "Can't delete the top folder." });
            // Moved to the trash (24 hours), so the console can offer Undo.
            var isDir = Directory.Exists(target);
            if (!isDir && !File.Exists(target)) return Results.NotFound(new { error = "No such file or folder." });
            var t = trash.Move(isDir ? "folder" : "file", $"{(isDir ? "folder" : "file")} {Path.GetFileName(target)}", n, target);
            Note(ctx, "file.delete", $"Deleted the {(isDir ? "folder" : "file")} {Path.GetFileName(target)} from the {root?.ToUpperInvariant()} files of slot {n}", n);
            return Results.Ok(new { undo = t?.Id });
        }));

        // ------------------------------------------------------------------ bulk actions, recent programs, trash

        // {"slots":[1,2,3],"action":"start|stop|restart|unload"}: one result per slot, so one slot failing doesn't stop the rest.
        // An unload returns each slot's snapshot id as "undo", which Undo restores.
        app.MapPost("/api/slots/bulk", async (HttpContext ctx, JsonElement body) =>
        {
            var action = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            if (action is not ("start" or "stop" or "restart" or "unload")) return Results.BadRequest(new { error = "action must be start, stop, restart or unload." });
            if (!(body.TryGetProperty("slots", out var arr) && arr.ValueKind == JsonValueKind.Array))
                return Results.BadRequest(new { error = "Send the slot numbers as {\"slots\":[1,2]}." });
            var numbers = arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _)).Select(e => e.GetInt32()).Distinct().ToList();
            if (numbers.Count == 0 || numbers.Any(x => !processor.IsValidSlot(x))) return Results.BadRequest(new { error = $"Slots must be numbers 1–{processor.Slots.Count}." });
            if (action == "unload" && Principal.Current is { } bp && !bp.Has(Perm.LoadProgram)) return Results.Json(new { error = "Your account isn't allowed to unload programs." }, statusCode: 403);
            var results = new List<object>();
            foreach (var n in numbers)
            {
                if (!Visible(n)) { results.Add(new { slot = n, ok = false, error = $"Your account isn't allowed to use slot {n}." }); continue; }
                if (action != "start" && slotLocks.Blocking(n, Principal.Current) is { } held) { results.Add(new { slot = n, ok = false, locked = true, error = SlotLocks.Message(held) }); continue; }
                await uploadLocks[n - 1].WaitAsync();
                try
                {
                    (string? Id, bool Running) undo = default;
                    var was = processor.Slot(n).Runtime?.ProgramName;
                    switch (action)
                    {
                        case "start": if (processor.Slot(n).Runtime == null && processor.Slot(n).State != SlotState.Error) { results.Add(new { slot = n, ok = true, skipped = "no program" }); continue; } StartSlot(n); break;
                        case "stop": StopSlot(n); break;
                        case "restart": if (processor.Slot(n).ProgramPath == null) { results.Add(new { slot = n, ok = true, skipped = "no program" }); continue; } RestartSlot(n); break;
                        case "unload": undo = UnloadSlot(n); break;
                    }
                    Note(ctx, action, $"{(action switch { "start" => "Started", "stop" => "Stopped", "restart" => "Restarted", _ => "Unloaded" })} {was ?? Prog(n)} {(action == "unload" ? "from" : "in")} slot {n} (with other slots)", n);
                    results.Add(new { slot = n, ok = true, undo = undo.Id == null ? null : new { snapshot = undo.Id, start = undo.Running } });
                }
                catch (Exception e) { results.Add(new { slot = n, ok = false, error = e.Message }); }
                finally { uploadLocks[n - 1].Release(); }
            }
            return Results.Ok(new { action, results });
        });

        object RecentDto(RecentEntry e) => new
        {
            id = e.Id, name = e.Name, kind = e.Kind, slot = e.Slot, time = e.Time, path = e.Path, hasCopy = e.HasCopy, size = e.Size,
            available = e.Path != null ? File.Exists(e.Path) : e.HasCopy
        };
        app.MapGet("/api/recent", () => recent.List().Select(RecentDto));
        app.MapDelete("/api/recent/{id}", (string id) => recent.Remove(id) ? Results.Ok() : Results.NotFound());

        app.MapGet("/api/trash", () => trash.List().Select(t => new { id = t.Id, kind = t.Kind, label = t.Label, slot = t.Slot, time = t.Time }));
        app.MapPost("/api/trash/{id}/restore", async (HttpContext ctx, string id) =>
        {
            var t = trash.Get(id);
            if (t == null) return Results.NotFound(new { error = "That can't be undone any more (it was restored already, or has been in the trash for over 24 hours)." });
            if (t.Slot is int ts && !Visible(ts)) return Results.Json(new { error = $"Your account isn't allowed to use slot {ts}." }, statusCode: 403);
            if (t.Slot is int tl && slotLocks.Blocking(tl, Principal.Current) is { } heldBy) return Results.Json(new { error = SlotLocks.Message(heldBy), locked = Authz.LockDto(heldBy) }, statusCode: 423);
            // Files go back into a slot's folders; hold the slot lock so a load in progress can't be mixed up with it.
            var lockSlot = t.Slot is int sn && processor.IsValidSlot(sn) ? uploadLocks[sn - 1] : null;
            if (lockSlot != null) await lockSlot.WaitAsync();
            try
            {
                var back = trash.Restore(id);
                if (back == null) return Results.NotFound(new { error = "That can't be undone any more." });
                Note(ctx, "undo", back.Kind == "edit" ? $"Undid a file edit: restored the {back.Label}" : $"Undid a delete: restored {back.Label}", back.Slot);
                return Results.Ok(new { restored = back.Label, kind = back.Kind, slot = back.Slot });
            }
            catch (IOException e) { return Results.Conflict(new { error = e.Message }); }
            catch (UnauthorizedAccessException e) { return Results.BadRequest(new { error = e.Message }); }
            finally { lockSlot?.Release(); }
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

        // ---- Readiness: will this program run? Answered before loading, from a throwaway load that is never started.
        object ReportDto(ReadinessReport r) => new
        {
            file = r.File, kind = r.Kind, verdict = r.Verdict.Code, headline = r.Verdict.Headline, reason = r.Verdict.Reason,
            problems = r.Verdict.Problems.Select(p => new { kind = p.Kind, name = p.Name, detail = p.Detail }),
            summary = SummaryDto(r.Summary)
        };

        // The same choice of main file and .sig as loading: the form's "file" is the program, the rest ride along.
        static (string Name, List<(string, byte[])> Files)? ReadUpload(IFormCollection form)
        {
            var main = form.Files["file"] ?? form.Files.FirstOrDefault(f => Regex.IsMatch(f.FileName, @"\.(lpz|cpz|dll|zip|smw)$", RegexOptions.IgnoreCase)) ?? form.Files.FirstOrDefault();
            if (main == null) return null;
            var name = Path.GetFileName(main.FileName);
            var sigs = form.Files.Where(f => f != main && f.FileName.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)).ToList();
            var sigFor = name.EndsWith(".lpz", StringComparison.OrdinalIgnoreCase) && sigs.Count == 1 ? sigs[0] : null;
            var files = new List<(string, byte[])> { (name, ReadAll(main)) };
            foreach (var extra in form.Files.Where(f => f != main))
                files.Add((extra == sigFor ? Path.ChangeExtension(name, ".sig") : Path.GetFileName(extra.FileName), ReadAll(extra)));
            return (name, files);
        }

        async Task<IResult> CheckFiles(IReadOnlyList<(string Name, byte[] Data)> files)
        {
            var dir = Directory.CreateTempSubdirectory("openvp-upload").FullName;
            try
            {
                foreach (var (name, data) in files) await File.WriteAllBytesAsync(Path.Combine(dir, Path.GetFileName(name)), data);
                var report = await Task.Run(() => checker.Check(Path.Combine(dir, Path.GetFileName(files[0].Name))));
                return Results.Ok(ReportDto(report));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // POST multipart: "file" (the program) plus its sidecars, as for loading. Returns the verdict and the summary; loads nothing.
        app.MapPost("/api/check", async (HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "Send the program as multipart/form-data field 'file' (and its .sig and sidecar files)." });
            var up = ReadUpload(await req.ReadFormAsync());
            return up == null ? Results.BadRequest(new { error = "No file." }) : await CheckFiles(up.Value.Files);
        });

        // The same for a file on the OpenVP computer (the desktop app, or a browser on that computer), with the files that belong with it.
        app.MapPost("/api/check/path", async (HttpContext ctx, JsonElement body) =>
        {
            if (!ApiAuth.IsDirectLocal(ctx))
                return Results.Json(new { error = "Checking a path only works on the computer OpenVP runs on. Upload the files instead." }, statusCode: StatusCodes.Status403Forbidden);
            var path = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("path", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString()?.Trim().Trim('"') : null;
            if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return Results.BadRequest(new { error = "Send {\"path\": \"C:\\full\\path\\to\\Program.lpz\"}." });
            if (!File.Exists(path)) return Results.BadRequest(new { error = $"There's no file at {path}." });
            var chosen = body.TryGetProperty("files", out var fl) && fl.ValueKind == JsonValueKind.Array
                ? fl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(f => Path.IsPathFullyQualified(f) && File.Exists(f)).ToArray()
                : Array.Empty<string>();
            try
            {
                var data = ProgramSource.Companions(path, chosen).Select(f => (f.Name, File.ReadAllBytes(f.Source))).ToList();
                return await CheckFiles(data);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Results.UnprocessableEntity(new { error = $"Couldn't read the program: {e.Message}" }); }
        });

        slotApi.MapGet("/summary", (int n) =>
        {
            var slot = processor.Slot(n);
            var m = SummaryOf(slot);
            return Results.Ok(new { loaded = slot.Runtime != null, program = slot.Runtime?.ProgramName, kind = slot.Runtime?.Kind, summary = SummaryDto(m) });
        });

        // ---- Activity: who did what and when.
        app.MapGet("/api/activity", (int? slot, int? limit) =>
        {
            if (slot is int sn && !processor.IsValidSlot(sn)) return Results.BadRequest(new { error = $"Slot must be 1–{processor.Slots.Count}." });
            return Results.Ok(activity.Query(slot, limit ?? 200).Where(e => e.Slot == null || Visible(e.Slot.Value)).Select(e => new { time = e.Time, action = e.Action, text = e.Text, slot = e.Slot, who = e.Who, user = e.User, detail = e.Detail }));
        });

        // ---- Try the demo: the SIMPL# sample in slot 1 and the SIMPL Windows sample in slot 2, linked over EISC.
        var demoSharp = Path.Combine(samplesDir, "simpl-sharp", "SampleProgram.dll");
        var demoSmw = Path.Combine(samplesDir, "simpl-windows", "SampleProgram.smw");
        var demoJson = Path.Combine(samplesDir, "simpl-windows", "SampleProgram.openvp.json");
        bool DemoAvailable() => processor.Slots.Count >= 2 && File.Exists(demoSharp) && File.Exists(demoSmw);
        object[] DemoOccupied() => new[] { 1, 2 }.Where(x => processor.IsValidSlot(x) && (processor.Slot(x).Runtime != null || processor.Slot(x).State == SlotState.Error))
            .Select(x => (object)new { slot = x, program = processor.Slot(x).Runtime?.ProgramName ?? processor.Slot(x).Settings.Program?.File ?? "a program that failed to load" }).ToArray();

        app.MapGet("/api/demo", () => new { available = DemoAvailable(), occupied = DemoOccupied() });

        app.MapPost("/api/demo", async (HttpContext ctx, JsonElement? body) =>
        {
            if (!DemoAvailable()) return Results.Json(new { error = "The demo programs aren't installed with this copy of OpenVP, or fewer than 2 slots are configured." }, statusCode: 409);
            var replace = body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("replace", out var rv) && rv.ValueKind == JsonValueKind.True;
            var occupied = DemoOccupied();
            if (occupied.Length > 0 && !replace)
                return Results.Json(new { error = "Slot 1 or 2 already has a program. Send {\"replace\": true} to replace it.", occupied }, statusCode: 409);
            var who = Who(ctx);
            foreach (var dn in new[] { 1, 2 })
                if (slotLocks.Blocking(dn, Principal.Current) is { } dHeld) return Results.Json(new { error = SlotLocks.Message(dHeld), locked = Authz.LockDto(dHeld) }, statusCode: 423);
            var sets = new[]
            {
                (Slot: 1, Files: new List<(string, byte[])> { ("SampleProgram.dll", File.ReadAllBytes(demoSharp)) }),
                (Slot: 2, Files: new List<(string, byte[])> { ("SampleProgram.smw", File.ReadAllBytes(demoSmw)), ("SampleProgram.openvp.json", File.Exists(demoJson) ? File.ReadAllBytes(demoJson) : "{}"u8.ToArray()) })
            };
            foreach (var set in sets)
            {
                await uploadLocks[set.Slot - 1].WaitAsync();
                try
                {
                    StopWatching(set.Slot);
                    var r = Install(set.Slot, set.Files, true, who: who, how: "demo");
                    if (r is not IStatusCodeHttpResult { StatusCode: 200 })
                        return Results.Json(new { error = $"Couldn't load the demo into slot {set.Slot}: {processor.Slot(set.Slot).LastError ?? "see the slot log"}" }, statusCode: 422);
                }
                finally { uploadLocks[set.Slot - 1].Release(); }
            }
            Note(ctx, "demo", "Loaded the demo: the SIMPL# sample in slot 1 and the SIMPL Windows sample in slot 2, linked over EISC");
            return Results.Ok(new { ok = true, slots = new[] { 1, 2 }, panel = new { slot = 1, ipId = "03" }, press = "digital 10" });
        });

        // ---- Starter projects
        app.MapGet("/api/templates", () => templates.List().Select(t => new { id = t.Id, name = t.Name, description = t.Description, file = t.FileName, url = "/api/templates/" + t.Id, available = templates.Available(t.Id) }));
        app.MapGet("/api/templates/{id}", (HttpContext ctx, string id) =>
        {
            var info = templates.List().FirstOrDefault(t => t.Id == id);
            if (info == null) return Results.NotFound(new { error = "There's no such template." });
            if (!templates.Available(id)) return Results.NotFound(new { error = "This template isn't installed with this copy of OpenVP." });
            Note(ctx, "template", $"Downloaded the template “{info.Name}”");
            return Results.File(templates.Build(id)!, info.ContentType, info.FileName);
        });

        // Live metrics: CPU, memory, threads, and per-slot activity (stuck handlers, orphaned threads).
        app.MapGet("/api/metrics", (bool? history) => processor.Metrics.Snapshot(history ?? true));

        // The list of unsupported symbols/APIs (docs/unsupported.md in a source checkout), to download and commit.
        app.MapGet("/api/unsupported", (string? format) => format == "json"
            ? Results.Ok(new { file = processor.Unsupported.FilePath, items = processor.Unsupported.Items })
            : Results.File(System.Text.Encoding.UTF8.GetBytes(processor.Unsupported.Render()), "text/markdown; charset=utf-8", "unsupported.md"));

        app.MapGet("/api/log", (int? slot, int? max) => processor.RecentLog(slot, max ?? 500).Where(l => Visible(l.Slot)));

        // Panel connections and CIP packets on their own, as plain text to copy into a report (see CipServer.RecentTrace). Admins only (PolicyTable): it shows join traffic of every slot.
        app.MapGet("/api/cip/trace", () =>
        {
            return Results.Text(cip == null ? "The CIP listener is off (OpenVP:CipEnabled)." : string.Join("\n", cip.RecentTrace()) + "\n", "text/plain; charset=utf-8");
        });

        app.MapGet("/api/devices", () => processor.AllDevices.Where(d => Visible(d.Slot)).OrderBy(d => d.Slot).ThenBy(d => d.IpId).Select(DeviceDto));

        // ------------------------------------------------------------------ WebSockets

        // Live debugger for one slot: trace events and log lines. Any number of these can be open, each
        // on its own slot, without mixing signals.
        app.Map("/ws/trace", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            int? slot = int.TryParse(ctx.Request.Query["slot"], out var s) ? s : null;
            var wantTrace = ctx.Request.Query["trace"] != "0";   // trace=0: log lines only (the console page)
            var me = authz.Resolve(ctx);
            if (slot is int reqSlot && !me.CanSlot(reqSlot)) { ctx.Response.StatusCode = 403; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            // Raw events go in; the send loop below turns them into JSON, so the slot thread only pays for an enqueue.
            var outbox = Channel.CreateBounded<object>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            bool Mine(int n) => slot == null ? me.CanSlot(n) : n == slot;   // a slot-limited account only gets its own slots' events
            void OnTrace(TraceEvent e)
            {
                if (Mine(e.Slot)) outbox.Writer.TryWrite(e);
            }
            void OnLog(LogEntry e)
            {
                if (Mine(e.Slot)) outbox.Writer.TryWrite(e);
            }
            void OnStep(ProgramSlot st)
            {
                // Snapshot now: the stepper's state may have moved on by the time the send loop runs.
                if (Mine(st.Number)) outbox.Writer.TryWrite(StepDto(st));
            }
            // Who else has this slot open, and whether it is locked: pushed on connect and on every change, so the debugger can warn.
            var myKey = Authz.PresenceKey(me);
            object PresenceFrame(int sn) => new { t = "presence", slot = sn, people = presence.ForSlot(sn).Select(e => new { name = e.Name, initials = e.Initials, kind = e.Kind, since = e.Since, me = e.Key == myKey }) };
            object LockFrame(int sn) => new { t = "lock", slot = sn, locked = slotLocks.Get(sn) is { } lf ? Authz.LockDto(lf) : null };
            void OnPresence(int sn) { if (sn == slot) outbox.Writer.TryWrite(PresenceFrame(sn)); }
            void OnLock(int sn, bool _) { if (sn == slot) outbox.Writer.TryWrite(LockFrame(sn)); }
            IDisposable? presenceLease = null;
            if (slot is int ps && processor.IsValidSlot(ps))
            {
                presenceLease = presence.Enter(ps, myKey, Authz.PresenceName(me), wantTrace ? "debugging" : "viewing");
                presence.Changed += OnPresence; slotLocks.Changed += OnLock;
                outbox.Writer.TryWrite(PresenceFrame(ps)); outbox.Writer.TryWrite(LockFrame(ps));
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
            // While a debugger is open on a slot, the slot keeps its recent changes, which is what "why is this signal this value?" reads.
            var lease = wantTrace && slot is int ls && processor.IsValidSlot(ls) ? processor.Slot(ls).Monitor.AcquireTrace() : null;
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
                presence.Changed -= OnPresence; slotLocks.Changed -= OnLock;
                presenceLease?.Dispose();
                lease?.Dispose();
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

        // ------------------------------------------------------------------ shared panels and the internet tunnel

        var shares = new ShareStore(processor.DataDirectory);
        reloaders["shares"] = shares.Reload;
        var ch5Panels = new Ch5Panels(processor.DataDirectory);
        TunnelManager? tunnel = null;
        if (tunnelUnavailable == null)
        {
            tunnel = new TunnelManager(processor.DataDirectory, tunnelPort, cfg["CloudflaredPath"]);
            if (cfg.GetValue("TunnelAutoStart", false))
            {
                // Only starts what is already installed; it never downloads without a click.
                _ = tunnel.StartAsync(allowDownload: false);
            }
        }
        ShareEndpoints.Map(app, processor, shares, ch5Panels, tunnel, gate, tunnelUnavailable, activity, Who);
        // Generated panel and layouts, the emulator, smart graphics and the rack view. None of it is served on the tunnel port (TunnelGate).
        PanelEndpoints.Map(app, slotApi, processor, ch5Panels, new SmartGraphicsStore(processor.DataDirectory), new PanelLayouts(processor.DataDirectory), () => cip, activity, Who, Visible, () => CommunityEndpoints.RackLinks(community, Visible));
        if (tunnel != null && alerts != null) tunnel.Changed += () => { var st = tunnel.Status(); alerts.Monitor.OnTunnel(st.State, st.State == TunnelState.Error ? st.Message : null); };
        if (tunnel != null && cfg.GetValue("TunnelAutoStart", false)) activity.Add("internet.on", "Internet access started at start-up (OpenVP:TunnelAutoStart)", null, Actors.Auto);

        // ------------------------------------------------------------------ autoload

        // At start-up: bring each slot's program back, in start order with each slot's delay, then log what happened. The
        // Autoload setting wins for the slots it names. It runs in the background so the console is up straight away.
        var autoload = new Dictionary<int, string>();
        foreach (var entry in cfg.GetSection("Autoload").GetChildren())
            if (int.TryParse(entry.Key.Replace("slot", "", StringComparison.OrdinalIgnoreCase), out var an) && processor.IsValidSlot(an) && !string.IsNullOrEmpty(entry.Value))
                autoload[an] = entry.Value;
        var restorer = new StartupRestorer(processor);
        if (alerts != null) restorer.Restored += alerts.Monitor.OnRestored;
        restorer.Restored += (e, error) => activity.Add("restore",
            error == null ? $"Restored slot {e.Slot} at start-up ({(e.Source == "config" ? "from the Autoload setting" : e.Memory?.File ?? "its remembered program")})"
                          : $"Couldn't restore slot {e.Slot} at start-up: {error}", e.Slot, Actors.Auto);
        restorer.Guard = (n, action) =>
        {
            uploadLocks[n - 1].Wait();
            try { return action(); } finally { uploadLocks[n - 1].Release(); }
        };
        // A program that was loaded from a file on this computer comes back from there, watched again if it was.
        restorer.PathLoader = e =>
        {
            var m = e.Memory!;
            var chosen = (m.Files ?? Array.Empty<string>()).Where(File.Exists).ToArray();
            SourceWatch? w = m.Watch ? new SourceWatch(m.FilePath!, () => ProgramSource.Companions(m.FilePath!, chosen).Select(f => f.Source)) : null;
            LoadFromDisk(e.Slot, m.FilePath!, chosen, m.Running, m.Watch, Actors.Auto, logAs: null);
            if (w != null)
            {
                w.Changed += () => _ = ReloadAsync(e.Slot, w, chosen);
                lock (watches) { watches[e.Slot - 1]?.Dispose(); watches[e.Slot - 1] = w; }
                processor.Slot(e.Slot).Log("Notice", $"Watching {m.FilePath}: the slot reloads when it changes.");
            }
            processor.Slot(e.Slot).Log("Notice", $"Restored {Path.GetFileName(m.FilePath)} from {m.FilePath} ({(m.Running ? "started" : "left stopped, as it was")}).");
            return true;
        };
        var plan = StartupRestorer.Plan(processor.Slots, autoload);
        if (plan.Count > 0)
        {
            Console.WriteLine($"Restoring {plan.Count} slot{(plan.Count == 1 ? "" : "s")} in start order: {string.Join(", ", plan.Select(p => p.Slot))}");
            _ = Task.Run(() =>
            {
                try
                {
                    var n = restorer.Run(plan);
                    Console.WriteLine($"Restored {n} of {plan.Count} slot{(plan.Count == 1 ? "" : "s")}.");
                }
                catch (Exception e) { Console.WriteLine($"Restoring slots failed: {e.Message}"); }
            });
        }

        // ------------------------------------------------------------------ processor-style console (telnet / SSH), off by default

        // Both listeners are off unless enabled. When on they listen on every interface; a login needs the access key as the password.
        var consoleShell = new ConsoleShell(processor);
        var consoleGate = new ConsoleGate { MaxTotal = Math.Clamp(cfg.GetValue("ConsoleMaxConnections", 5), 1, 50), MaxPerAddress = 3 };
        var consoleAuth = new UserConsoleAuth(() => auth.Enabled ? auth.Key : null, users, slotLocks, audit);
        TelnetServer? telnet = null;
        SshConsoleServer? ssh = null;
        string? telnetMessage = null, sshMessage = null;
        var telnetOn = cfg.GetValue("TelnetEnabled", false);
        var sshOn = cfg.GetValue("SshEnabled", false);
        var telnetPort = cfg.GetValue("TelnetPort", 41795);
        var sshPort = cfg.GetValue("SshPort", 22);
        if ((telnetOn || sshOn) && !auth.Enabled && !users.Any)
        {
            telnetMessage = sshMessage = "Not started: the console needs the access key, and it is switched off (OpenVP:ApiKey=off).";
            Console.WriteLine("Telnet/SSH console not started: " + telnetMessage);
            telnetOn = sshOn = false;
        }
        if (telnetOn)
        {
            try { telnet = new TelnetServer(consoleShell, consoleAuth, consoleGate, Console.WriteLine); telnet.Start(System.Net.IPAddress.Any, telnetPort); }
            catch (Exception e) { telnet = null; telnetMessage = $"Couldn't listen on port {telnetPort}: {e.Message}"; Console.WriteLine("Telnet console not started: " + telnetMessage); }
        }
        if (sshOn)
        {
            try { ssh = new SshConsoleServer(consoleShell, consoleAuth, consoleGate, Path.Combine(processor.DataDirectory, "ssh_host_key.pem"), Console.WriteLine); ssh.Start(System.Net.IPAddress.Any, sshPort); }
            catch (Exception e) { ssh = null; sshMessage = $"Couldn't listen on port {sshPort}: {e.Message}"; Console.WriteLine("SSH console not started: " + sshMessage); }
        }
        // ------------------------------------------------------------------ openvp.local (mDNS)

        var inContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" || File.Exists("/.dockerenv");
        var hostNetworking = inContainer && System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name == "docker0");
        var mdnsOn = cfg.GetValue<bool?>("Mdns") ?? (!inContainer || hostNetworking);
        MdnsResponder? mdns = null;
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (!mdnsOn)
            {
                if (inContainer && cfg.GetValue<bool?>("Mdns") == null) Console.WriteLine("mDNS (openvp.local) is off inside a container. Run with host networking and set OpenVP__Mdns=true to turn it on.");
                return;
            }
            try
            {
                var port = app.Urls.Select(u => Uri.TryCreate(u.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost"), UriKind.Absolute, out var uri) ? uri.Port : 0)
                    .FirstOrDefault(p => p > 0 && p != gate.Port) is var pp && pp > 0 ? pp : 8080;
                mdns = new MdnsResponder(port, cfg["MdnsName"] ?? "openvp", log: Console.WriteLine);
                mdns.Start();
            }
            catch (Exception e) { Console.WriteLine($"mDNS is off: {e.Message}"); }
        });

        var discovery = new NetworkDiscovery();
        DeviceEndpoints.MapGlobal(app, discovery, () => new
        {
            maxConnections = consoleGate.MaxTotal,
            open = consoleGate.Open,
            telnet = new { enabled = telnetOn, listening = telnet != null, port = telnetPort, message = telnetMessage },
            ssh = new { enabled = sshOn, listening = ssh != null, port = sshPort, message = sshMessage },
            mdns = new { enabled = mdnsOn, running = mdns?.Running ?? false, names = mdns?.HostNames, service = mdns?.InstanceName },
        }, (c, a, t, sl) => Note(c, a, t, sl));

        app.Lifetime.ApplicationStarted.Register(() => { try { integrations.Start(); } catch (Exception e) { Console.WriteLine("Integrations not started: " + e.Message); } });
        app.Lifetime.ApplicationStopping.Register(() => { scheduler.Dispose(); backups.Dispose(); git.Dispose(); alerts?.Dispose(); integrations.Dispose(); telnet?.Dispose(); ssh?.Dispose(); mdns?.Dispose(); for (var i = 1; i <= watches.Length; i++) StopWatching(i); tunnel?.Dispose(); foreach (var w in webXPanels) w.Dispose(); cip?.Dispose(); crash.Dispose(); trashTimer.Dispose(); lockTimer.Dispose(); acmeTimer.Dispose(); tokens.Flush(); recordings.Dispose(); eisc.Dispose(); plugins.Dispose(); processor.Dispose(); });
        // If the process ends some other way (not a clean stop), still take cloudflared down with it.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => tunnel?.Stop();
        return app;
    }

    /// <summary>
    /// With "redirect HTTP to HTTPS" on, a browser opening a page over plain http from another machine is sent to https. Not for this computer (the desktop app
    /// keeps using http://localhost), and never for the API, WebSockets, webhooks, /healthz, /metrics, shared panels or ACME challenges, whose clients may not follow
    /// a redirect or would lose their Authorization header. CIP is not HTTP at all and is never touched.
    /// </summary>
    public static Func<HttpContext, RequestDelegate, Task> HttpsRedirect(SecuritySettings settings, HttpsManager https) => (ctx, next) =>
    {
        if (!https.Listening || ctx.Request.IsHttps || ctx.Items.ContainsKey(TunnelGate.ItemKey)) return next(ctx);
        if (!(HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)) || !settings.Get().Https.Redirect) return next(ctx);
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/ws") || path.StartsWithSegments("/hooks") || path.StartsWithSegments("/cws") || path.StartsWithSegments("/healthz") || path.StartsWithSegments("/metrics")
            || path.StartsWithSegments("/s") || path.StartsWithSegments("/.well-known") || path.StartsWithSegments("/signin-oidc")) return next(ctx);
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip != null && System.Net.IPAddress.IsLoopback(ip)) return next(ctx);
        var port = https.BoundPort == 443 ? "" : ":" + https.BoundPort;
        ctx.Response.Redirect($"https://{ctx.Request.Host.Host}{port}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}");
        return Task.CompletedTask;
    };

    /// <summary>Null if the HTTPS port can be opened (all addresses), else why not.</summary>
    public static string? CheckHttpsPort(string urls, int port)
    {
        foreach (var u in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var probe = u.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://[::]", "://localhost");
            if (Uri.TryCreate(probe, UriKind.Absolute, out var uri) && uri.Port == port) return $"the HTTPS port ({port}) is the console's own port. Choose another.";
        }
        try { var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port); l.Start(); l.Stop(); }
        catch (System.Net.Sockets.SocketException) { return $"port {port} is already in use. Choose another HTTPS port."; }
        return null;
    }

    /// <summary>
    /// The certificate CH5 panels are shown on the WebSocket port: the console's own when it has one, otherwise a
    /// self-signed one kept in <c>data/security/certs/webxpanel.pfx</c> so it stays the same from one start to the next.
    /// </summary>
    private static System.Security.Cryptography.X509Certificates.X509Certificate2 WebXPanelCertificate(HttpsManager https, string dataDir)
    {
        if (https.Holder.Current is { } own) return own;
        var path = Path.Combine(dataDir, "security", "certs", "webxpanel.pfx");
        try
        {
            if (File.Exists(path) && CertificateTools.FromPfx(File.ReadAllBytes(path), "") is { } kept && kept.NotAfter > DateTime.Now.AddDays(7)) return kept;
        }
        catch { /* unreadable or damaged: make a new one */ }
        var (dns, ips) = CertificateTools.LocalNames();
        var cert = CertificateTools.CreateSelfSigned(dns, ips);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, CertificateTools.ToPfx(cert, ""));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, path, true);
        }
        catch { /* not saved: a new one is made at the next start */ }
        return cert;
    }

    /// <summary>Null if 127.0.0.1:<paramref name="port"/> can be used for the tunnel listener, else why not.</summary>
    public static string? CheckTunnelPort(string urls, int port)
    {
        foreach (var u in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var probe = u.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://[::]", "://localhost");
            if (Uri.TryCreate(probe, UriKind.Absolute, out var uri) && uri.Port == port)
                return $"OpenVP:TunnelPort ({port}) is the console's own port. Choose a different port.";
        }
        try
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            l.Start();
            l.Stop();
        }
        catch (System.Net.Sockets.SocketException)
        {
            return $"port {port} on 127.0.0.1 is already in use. Set OpenVP:TunnelPort to a free port.";
        }
        return null;
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
                if (l.Detail != null) w.WriteString("detail", l.Detail);
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
