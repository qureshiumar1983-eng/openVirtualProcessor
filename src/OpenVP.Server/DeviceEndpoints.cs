using System.Globalization;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Panels;

namespace OpenVP.Server;

/// <summary>
/// REST endpoints for device simulators, the COM terminal, the socket sniffer, network redirects, the IP ID remap and network
/// discovery. They are thin: the work is in <c>OpenVP.Core</c> (<see cref="SlotIo"/>) and <c>OpenVP.Panels</c> (<see cref="NetworkDiscovery"/>).
/// Everything is under <c>/api</c>, so it sits behind the access key.
/// </summary>
public static class DeviceEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Iso(DateTimeOffset t) => t.ToString("O", CultureInfo.InvariantCulture);

    public static object SimDto(SimHost h, bool full = true) => new
    {
        id = h.Def.Id,
        name = h.Def.Name,
        profile = h.Def.Profile,
        kind = h.Def.Kind,
        com = h.Def.Com,
        port = h.Def.Port,
        enabled = h.Def.Enabled,
        listenPort = h.ListenPort,
        listening = h.Listening,
        comActive = h.ComActive,
        error = h.Error,
        connections = h.Connections,
        bytesIn = h.BytesIn,
        bytesOut = h.BytesOut,
        matched = h.Engine.Matched,
        unmatched = h.Engine.Unmatched,
        state = h.Engine.State.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => k.Value),
        rules = full ? h.Def.Rules : null,
        unsolicited = full ? h.Def.Unsolicited : null,
        onConnect = full ? h.Def.OnConnect : null,
        initialState = full ? h.Def.State : null,
        activity = full ? h.Engine.Recent().TakeLast(30).Select(a => new { time = Iso(a.Time), kind = a.Kind, text = a.Text }) : null,
    };

    private static object PortDto(PortChannel p, SlotSimulators sims) => new
    {
        key = p.Key,
        label = p.Label,
        open = p.Open,
        openedAt = p.OpenedAt is { } t ? Iso(t) : null,
        txBytes = p.TxBytes,
        rxBytes = p.RxBytes,
        simulator = sims.All.FirstOrDefault(h => h.MatchesCom(p.Key)) is { } s ? new { id = s.Def.Id, name = s.Def.Name } : null,
    };

    private static object TrafficDto(IEnumerable<TrafficEntry> entries) =>
        entries.Select(e => new { seq = e.Seq, time = Iso(e.Time), dir = e.Dir == TrafficDir.Tx ? "tx" : "rx", text = e.Text, origin = e.Origin });

    private static object SocketDto(SocketRecord s) => new
    {
        id = s.Id,
        kind = s.Kind,
        role = s.Role,
        protocol = s.Protocol,
        local = s.Local,
        remote = s.Remote,
        redirectedTo = s.RedirectedTo,
        state = s.State,
        opened = Iso(s.Opened),
        closed = s.Closed is { } c ? Iso(c) : null,
        bytesIn = s.BytesIn,
        bytesOut = s.BytesOut,
    };

    private static IResult Bad(string message) => Results.BadRequest(new { error = message });

    private static bool TryHex(string? s, out uint id) =>
        uint.TryParse(s?.Trim(), NumberStyles.HexNumber, null, out id) && id is >= 1 and <= 0xFE;

    /// <summary>Per-slot endpoints, added to the <c>/api/slots/{n}</c> group.</summary>
    public static void MapSlot(RouteGroupBuilder slotApi, Processor processor, Action<HttpContext, string, string, int?> note)
    {
        // ---------------------------------------------------------------- simulators
        slotApi.MapGet("/simulators", (int n) =>
        {
            var io = processor.Slot(n).Io;
            return new
            {
                profiles = SimProfiles.All.Select(p => new { id = p.Id, name = p.Name, description = p.Description }),
                simulators = io.Simulators.All.Select(h => SimDto(h)),
                ports = io.Ports.All.Select(p => p.Key),
                max = SlotSimulators.MaxPerSlot,
            };
        });

        slotApi.MapPost("/simulators", (int n, JsonElement body, HttpContext ctx) =>
        {
            var io = processor.Slot(n).Io;
            try
            {
                var def = ReadDef(body, null);
                var h = io.Simulators.Add(def);
                note(ctx, "simulator.add", $"Added the simulator “{h.Def.Name}” ({h.Def.Kind}) to slot {n}", n);
                if (h.Error != null) processor.Slot(n).Log("Warning", $"Simulator {h.Def.Name}: {h.Error}");
                return Results.Ok(SimDto(h));
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (JsonException e) { return Bad("The simulator isn't valid JSON: " + e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 500); }
        });

        slotApi.MapPut("/simulators/{id}", (int n, string id, JsonElement body, HttpContext ctx) =>
        {
            var io = processor.Slot(n).Io;
            if (io.Simulators.Find(id) is not { } existing) return Results.NotFound(new { error = "There's no such simulator." });
            try
            {
                var def = ReadDef(body, existing.Def.Clone());
                var reset = body.TryGetProperty("resetState", out var rs) && rs.ValueKind == JsonValueKind.True;
                var h = io.Simulators.Update(existing.Def.Id, def, reset);
                if (h == null) return Results.NotFound(new { error = "There's no such simulator." });
                note(ctx, "simulator.edit", $"Changed the simulator “{h.Def.Name}” of slot {n}", n);
                return Results.Ok(SimDto(h));
            }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (JsonException e) { return Bad("The simulator isn't valid JSON: " + e.Message); }
        });

        slotApi.MapDelete("/simulators/{id}", (int n, string id, HttpContext ctx) =>
        {
            var io = processor.Slot(n).Io;
            var name = io.Simulators.Find(id)?.Def.Name;
            if (!io.Simulators.Remove(io.Simulators.Find(id)?.Def.Id ?? id)) return Results.NotFound(new { error = "There's no such simulator." });
            note(ctx, "simulator.remove", $"Removed the simulator “{name}” from slot {n}", n);
            return Results.Ok(new { deleted = id });
        });

        slotApi.MapPost("/simulators/{id}/state", (int n, string id, JsonElement body) =>
        {
            if (processor.Slot(n).Io.Simulators.Find(id) is not { } h) return Results.NotFound(new { error = "There's no such simulator." });
            if (body.TryGetProperty("reset", out var r) && r.ValueKind == JsonValueKind.True) h.Engine.ResetState();
            else
            {
                var key = body.TryGetProperty("key", out var k) ? k.GetString() : null;
                var value = body.TryGetProperty("value", out var v) ? v.ToString() : null;
                if (string.IsNullOrWhiteSpace(key) || value == null) return Bad("Give a key and a value.");
                h.Engine.SetState(key.Trim(), value);
            }
            return Results.Ok(SimDto(h));
        });

        // Try a message on a copy of the simulator, with its current state: what would it reply, and after how long?
        slotApi.MapPost("/simulators/{id}/try", (int n, string id, JsonElement body) =>
        {
            if (processor.Slot(n).Io.Simulators.Find(id) is not { } h) return Results.NotFound(new { error = "There's no such simulator." });
            var text = SimText.Unescape(body.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "");
            var clock = new ImmediateSimClock();
            var replies = new List<object>();
            SimEngine? copy = null;
            copy = new SimEngine(h.Def.Clone(), (_, s) => replies.Add(new { text = SimText.Escape(s), delayMs = copy!.LastReplyDelayMs }), clock);
            foreach (var kv in h.Engine.State) copy.SetState(kv.Key, kv.Value);
            copy.Receive(0, text);
            var result = new { replies, state = copy.State.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => k.Value), matched = copy.Matched > 0 };
            copy.Dispose();
            return Results.Ok(result);
        });

        // ---------------------------------------------------------------- COM terminal
        slotApi.MapGet("/ports", (int n) =>
        {
            var io = processor.Slot(n).Io;
            return io.Ports.All.Select(p => PortDto(p, io.Simulators));
        });

        slotApi.MapGet("/ports/traffic", (int n, string key, long? after) =>
        {
            if (processor.Slot(n).Io.Ports.Find(key) is not { } p) return Results.NotFound(new { error = "That port isn't open. Start the program first." });
            var entries = p.Traffic.Since(after ?? 0, 1000);
            return Results.Ok(new { key = p.Key, open = p.Open, last = p.Traffic.LastSeq, txBytes = p.TxBytes, rxBytes = p.RxBytes, entries = TrafficDto(entries) });
        });

        slotApi.MapPost("/ports/send", (int n, JsonElement body, HttpContext ctx) =>
        {
            var key = body.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
            var text = body.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var raw = body.TryGetProperty("raw", out var rw) && rw.ValueKind == JsonValueKind.True;
            if (processor.Slot(n).Io.Ports.Find(key) is not { } p) return Results.NotFound(new { error = "That port isn't open. Start the program first." });
            var data = raw ? text : SimText.Unescape(text);
            if (data.Length == 0) return Bad("Type something to send.");
            if (data.Length > 8192) return Bad("That is too much to send at once (8192 characters).");
            if (!p.ToProgramRx(data, "terminal")) return Results.Conflict(new { error = "The program isn't running, so the port is closed." });
            note(ctx, "port.send", $"Sent {data.Length} byte(s) to the program's {p.Label} (slot {n}) from the terminal", n);
            return Results.Ok(new { sent = data.Length });
        });

        slotApi.MapPost("/ports/clear", (int n, JsonElement body) =>
        {
            var key = body.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
            if (processor.Slot(n).Io.Ports.Find(key) is not { } p) return Results.NotFound(new { error = "That port isn't open." });
            p.Traffic.Clear();
            return Results.Ok(new { cleared = true });
        });

        // ---------------------------------------------------------------- sockets
        slotApi.MapGet("/sockets", (int n) =>
        {
            var t = processor.Slot(n).Io.Sockets;
            t.Watch();
            return t.All.OrderBy(s => s.Closed != null).ThenByDescending(s => s.Opened).Select(SocketDto);
        });

        slotApi.MapGet("/sockets/{id:long}/traffic", (int n, long id, long? after) =>
        {
            var t = processor.Slot(n).Io.Sockets;
            t.Watch();
            if (t.Find(id) is not { } s) return Results.NotFound(new { error = "There's no such socket." });
            return Results.Ok(new { socket = SocketDto(s), last = s.Traffic.LastSeq, entries = TrafficDto(s.Traffic.Since(after ?? 0, 1000)) });
        });

        // ---------------------------------------------------------------- redirects
        slotApi.MapGet("/redirects", (int n) =>
        {
            var io = processor.Slot(n).Io;
            return new
            {
                redirects = io.Redirects.List,
                simulators = io.Simulators.All.Where(h => h.Def.Kind != "com").Select(h => new { id = h.Def.Id, name = h.Def.Name, kind = h.Def.Kind, listenPort = h.ListenPort }),
            };
        });

        slotApi.MapPost("/redirects", (int n, RedirectEntry e, HttpContext ctx) =>
        {
            try
            {
                var added = processor.Slot(n).Io.Redirects.Add(e);
                note(ctx, "redirect.add", $"Slot {n}: the program's connections to {added.From} now go to {added.To}", n);
                return Results.Ok(added);
            }
            catch (ArgumentException ex) { return Bad(ex.Message); }
        });

        slotApi.MapPut("/redirects/{id}", (int n, string id, RedirectEntry e, HttpContext ctx) =>
        {
            try
            {
                var r = processor.Slot(n).Io.Redirects.Update(id, e);
                if (r == null) return Results.NotFound(new { error = "There's no such redirect." });
                note(ctx, "redirect.edit", $"Changed a network redirect of slot {n} ({r.From} to {r.To}{(r.Enabled ? "" : ", off")})", n);
                return Results.Ok(r);
            }
            catch (ArgumentException ex) { return Bad(ex.Message); }
        });

        slotApi.MapDelete("/redirects/{id}", (int n, string id, HttpContext ctx) =>
        {
            if (!processor.Slot(n).Io.Redirects.Remove(id)) return Results.NotFound(new { error = "There's no such redirect." });
            note(ctx, "redirect.remove", $"Removed a network redirect of slot {n}", n);
            return Results.Ok(new { deleted = id });
        });

        // ---------------------------------------------------------------- IP ID remap
        object Remap(ProgramSlot s) => new
        {
            entries = s.Io.Remap.Entries.Select(e => new { program = e.Program.ToString("X2"), @public = e.Public.ToString("X2") }),
            registered = s.Devices.OrderBy(d => d.IpId).Select(d => new { ipId = d.IpId.ToString("X2"), @public = s.PublicIpId(d.IpId).ToString("X2") }),
        };

        slotApi.MapGet("/ipremap", (int n) => Remap(processor.Slot(n)));

        slotApi.MapPut("/ipremap", (int n, JsonElement body, HttpContext ctx) =>
        {
            var s = processor.Slot(n);
            var prog = body.TryGetProperty("program", out var a) ? a.GetString() : null;
            var pub = body.TryGetProperty("public", out var b) ? b.GetString() : null;
            if (!TryHex(prog, out var p) || !TryHex(pub, out var q)) return Bad("IP IDs are two hex digits from 01 to FE, like 03 or 0A.");
            try { s.SetIpRemap(p, q); }
            catch (ArgumentException e) { return Bad(e.Message); }
            note(ctx, "ipremap", p == q ? $"Slot {n}: IP ID {p:X2} answers at its own number again" : $"Slot {n}: the program's IP ID {p:X2} now answers at {q:X2}", n);
            return Results.Ok(Remap(s));
        });

        slotApi.MapDelete("/ipremap/{ipid}", (int n, string ipid, HttpContext ctx) =>
        {
            var s = processor.Slot(n);
            if (!TryHex(ipid, out var p)) return Bad("IP IDs are two hex digits from 01 to FE.");
            if (!s.Io.Remap.IsRemapped(p)) return Results.NotFound(new { error = "That IP ID isn't remapped." });
            s.ClearIpRemap(p);
            note(ctx, "ipremap", $"Slot {n}: IP ID {p:X2} answers at its own number again", n);
            return Results.Ok(Remap(s));
        });
    }

    /// <summary>A simulator from a request body: a profile (<c>profile</c>) or a full definition, with any fields in the body laid over it.</summary>
    internal static SimDef ReadDef(JsonElement body, SimDef? start)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new ArgumentException("Send the simulator as a JSON object.");
        SimDef def;
        if (start != null)
        {
            def = start;
            Overlay(def, body);
        }
        else if (body.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } pid
                 && !body.TryGetProperty("rules", out _))
        {
            def = SimProfiles.Create(pid) ?? throw new ArgumentException($"There's no profile called {pid}.");
            Overlay(def, body);
        }
        else def = body.Deserialize<SimDef>(Json) ?? new SimDef();
        return def;
    }

    private static void Overlay(SimDef def, JsonElement body)
    {
        foreach (var prop in body.EnumerateObject())
        {
            switch (prop.Name.ToLowerInvariant())
            {
                case "name": def.Name = prop.Value.GetString() ?? def.Name; break;
                case "kind": def.Kind = prop.Value.GetString() ?? def.Kind; break;
                case "com": def.Com = prop.Value.GetString() ?? ""; break;
                case "port": def.Port = prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32() : def.Port; break;
                case "enabled": def.Enabled = prop.Value.ValueKind != JsonValueKind.False; break;
                case "rules": def.Rules = prop.Value.Deserialize<List<SimRule>>(Json) ?? new(); break;
                case "unsolicited": def.Unsolicited = prop.Value.Deserialize<List<SimTimer>>(Json) ?? new(); break;
                case "onconnect": def.OnConnect = prop.Value.Deserialize<List<string>>(Json) ?? new(); break;
                case "initialstate": def.State = prop.Value.Deserialize<Dictionary<string, string>>(Json) ?? new(); break;
                case "state" when def.State.Count == 0: def.State = prop.Value.Deserialize<Dictionary<string, string>>(Json) ?? new(); break;
            }
        }
    }

    /// <summary>Global endpoints: network discovery and the console listeners' status.</summary>
    public static void MapGlobal(WebApplication app, NetworkDiscovery discovery, Func<object> consoles, Action<HttpContext, string, string, int?> note)
    {
        app.MapGet("/api/discovery", () => discovery.Snapshot());
        app.MapPost("/api/discovery", (HttpContext ctx) =>
        {
            if (!discovery.Start()) return Results.Conflict(new { error = "A scan is already running.", report = discovery.Snapshot() });
            note(ctx, "discovery", "Started a network scan", null);
            return Results.Ok(discovery.Snapshot());
        });
        app.MapDelete("/api/discovery", () => { discovery.Cancel(); return Results.Ok(discovery.Snapshot()); });
        app.MapGet("/api/consoles", () => consoles());
    }
}
