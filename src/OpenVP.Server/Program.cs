using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using OpenVP.Core;
using OpenVP.Panels;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

// Content root = install folder, so wwwroot and appsettings.json are found from any working directory.
// The data folder stays relative to the working directory.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 512L * 1024 * 1024);
var cfg = builder.Configuration.GetSection("OpenVP");

var processor = new Processor(cfg["DataDirectory"] ?? "data", cfg.GetValue("Slots", 10));
processor.AddLoader(new SimplSharpLoader());
processor.AddLoader(new SimplLoader(Path.Combine(processor.DataDirectory, "symbols.json")));
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

var app = builder.Build();
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

object ValueOf(JsonElement v) => v.ValueKind switch
{
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Number => v.GetInt32(),
    _ => v.ToString()
};

// ------------------------------------------------------------------ REST

app.MapGet("/api/info", () => new { name = "OpenVP", version = "0.1.0", slots = processor.Slots.Count, cipPort = cip?.Port });

app.MapGet("/api/slots", () => processor.Slots.Select(SlotDto));

app.MapGet("/api/slots/{n:int}", (int n) => SlotDto(processor.Slot(n)));

app.MapPost("/api/slots/{n:int}/program", async (int n, HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.BadRequest("Send the program as multipart/form-data field 'file'.");
    var form = await req.ReadFormAsync();
    var file = form.Files["file"] ?? form.Files.FirstOrDefault();
    if (file == null) return Results.BadRequest("No file.");
    var dir = Path.Combine(uploads, $"slot{n:00}");
    if (Directory.Exists(dir)) Directory.Delete(dir, true);
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, Path.GetFileName(file.FileName));
    await using (var fs = File.Create(path)) await file.CopyToAsync(fs);
    // Sidecar files (MyProgram.openvp.json, MyProgram.symbols.json) can ride along.
    foreach (var extra in form.Files.Where(f => f != file))
    {
        await using var fs = File.Create(Path.Combine(dir, Path.GetFileName(extra.FileName)));
        await extra.CopyToAsync(fs);
    }
    var start = !string.Equals(form["start"], "false", StringComparison.OrdinalIgnoreCase);
    try { processor.LoadProgram(n, path, start); }
    catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message, slot = SlotDto(processor.Slot(n)) }); }
    return Results.Ok(SlotDto(processor.Slot(n)));
});

app.MapPost("/api/slots/{n:int}/start", (int n) =>
{
    try { processor.Slot(n).Start(); return Results.Ok(SlotDto(processor.Slot(n))); }
    catch (Exception e) { return Results.UnprocessableEntity(new { error = e.Message }); }
});
app.MapPost("/api/slots/{n:int}/stop", (int n) => { processor.Slot(n).Stop(); return SlotDto(processor.Slot(n)); });
app.MapPost("/api/slots/{n:int}/unload", (int n) => { processor.Slot(n).Unload(); return SlotDto(processor.Slot(n)); });

app.MapGet("/api/slots/{n:int}/signals", (int n) =>
    (processor.Slot(n).Runtime?.GetSignals() ?? Array.Empty<SignalInfo>())
        .Select(s => new { name = s.Name, kind = s.Kind.ToString(), value = s.Value }));

app.MapPost("/api/slots/{n:int}/signals", (int n, JsonElement body) =>
{
    var rt = processor.Slot(n).Runtime;
    if (rt == null) return Results.NotFound("No program in that slot.");
    var name = body.GetProperty("name").GetString()!;
    return rt.TrySetSignal(name, ValueOf(body.GetProperty("value"))) ? Results.Ok() : Results.NotFound($"Unknown signal '{name}'.");
});

app.MapPost("/api/slots/{n:int}/console", (int n, JsonElement body) =>
{
    try { return Results.Text(processor.Slot(n).RunConsoleCommand(body.GetProperty("command").GetString() ?? "")); }
    catch (Exception e) { return Results.Text("Error: " + e.Message); }
});

app.MapGet("/api/slots/{n:int}/compat", (int n) => processor.Slot(n).Runtime switch
{
    SimplSharpRuntime s => Results.Text(s.Compat.ToString()),
    SimplRuntime r => Results.Text(r.Unsupported.Count == 0
        ? "Every wired symbol has a behavior."
        : "Symbols without a behavior yet:\n" + string.Join('\n', r.Unsupported.Select(u => $"  {u} pins in={u.Inputs.Count} out={u.Outputs.Count}"))),
    _ => Results.Text("No program loaded.")
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
    processor.Traced += OnTrace;
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
        processor.Traced -= OnTrace;
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
app.Run();

public partial class Program { }
