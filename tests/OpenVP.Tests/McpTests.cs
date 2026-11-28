using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>The MCP server at /mcp, driven by the SDK's own client against a real OpenVP server, as an AI assistant would use it.</summary>
public static class McpTests
{
    /// <summary>An MCP client that a test can dispose with <c>using</c> (the SDK's client is only async-disposable).</summary>
    private sealed class Conn(McpClient client) : IDisposable
    {
        public McpClient Client { get; } = client;
        public void Dispose() { try { Client.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { } }
    }

    private static Conn Connect(OpsTestServer s, string? bearer)
    {
        var headers = new Dictionary<string, string>();
        if (bearer != null) headers["Authorization"] = "Bearer " + bearer;
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(s.Url + "/mcp"), AdditionalHeaders = headers });
        return new Conn(McpClient.CreateAsync(transport).GetAwaiter().GetResult());
    }

    private static (bool IsError, string Text, JsonElement Json) Call(Conn c, string tool, Dictionary<string, object?> args)
    {
        var r = c.Client.CallToolAsync(tool, args).GetAwaiter().GetResult();
        var text = string.Concat(r.Content.OfType<TextContentBlock>().Select(b => b.Text));
        JsonElement j = default; try { j = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
        return (r.IsError == true, text, j);
    }

    [Test]
    public static void Mcp_lists_controls_presses_buttons_and_reports_what_changed()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        using var c = Connect(s, s.Key);

        var tools = c.Client.ListToolsAsync().GetAwaiter().GetResult().Select(t => t.Name).ToHashSet();
        foreach (var t in new[] { "list_slots", "load_program", "control_slot", "list_controls", "press_button", "set_value", "read_signals", "wait_for_signal", "test_all_buttons", "read_log", "replay_recording" })
            Assert.True(tools.Contains(t), "tool " + t);

        var slots = Call(c, "list_slots", new());
        Assert.True(!slots.IsError && slots.Json[0].GetProperty("state").GetString() == "Running", "slot 1 runs: " + slots.Text);

        var controls = Call(c, "list_controls", new() { ["slot"] = 1, ["search"] = "toggle" });
        Assert.True(!controls.IsError && controls.Json.GetProperty("controls").EnumerateArray().Any(x => x.GetProperty("drives").GetString() == "toggle_press"), "the toggle button is listed: " + controls.Text);

        var press = Call(c, "press_button", new() { ["slot"] = 1, ["control"] = "toggle_press" });
        Assert.True(!press.IsError, "press: " + press.Text);
        var changed = press.Json.GetProperty("changes").EnumerateArray().ToDictionary(x => x.GetProperty("signal").GetString()!, x => x.GetProperty("after"));
        Assert.True(changed.TryGetValue("toggle_fb", out var fb) && fb.ValueKind == JsonValueKind.True, "pressing toggle turns toggle_fb on: " + press.Text);
        Assert.True(press.Json.GetProperty("timeline").EnumerateArray().Any(x => x.GetProperty("signal").GetString() == "toggle_press"), "the timeline shows the press itself");

        Assert.True(Call(c, "wait_for_signal", new() { ["slot"] = 1, ["signal"] = "toggle_fb", ["expected"] = "true", ["timeoutMs"] = 500 }).Json.GetProperty("passed").GetBoolean(), "assert passes");
        var miss = Call(c, "wait_for_signal", new() { ["slot"] = 1, ["signal"] = "toggle_fb", ["expected"] = "0", ["timeoutMs"] = 100 });
        Assert.True(!miss.Json.GetProperty("passed").GetBoolean() && miss.Json.GetProperty("actual").GetBoolean(), "and fails with the actual value: " + miss.Text);

        var again = Call(c, "press_button", new() { ["slot"] = 1, ["control"] = "toggle_press" });
        Assert.True(again.Json.GetProperty("changes").EnumerateArray().Any(x => x.GetProperty("signal").GetString() == "toggle_fb" && x.GetProperty("after").ValueKind == JsonValueKind.False), "a second press turns it off: " + again.Text);

        var unknown = Call(c, "press_button", new() { ["slot"] = 1, ["control"] = "no_such_button" });
        Assert.True(unknown.IsError && unknown.Text.Contains("no control or signal"), "an unknown control is a clear error: " + unknown.Text);

        var sweep = Call(c, "test_all_buttons", new() { ["slot"] = 1, ["search"] = "_press", ["holdMs"] = 20, ["settleMs"] = 50 });
        Assert.True(!sweep.IsError && sweep.Json.GetProperty("pressed").GetInt32() >= 4, "every *_press button is pressed: " + sweep.Text);
        Assert.Equal("Running", sweep.Json.GetProperty("state").GetString(), "the program still runs after the sweep");

        Assert.True(Call(c, "read_log", new() { ["slot"] = 1 }).Json.ValueKind == JsonValueKind.Array, "the log reads");
        Assert.True(s.Get("/api/activity").Text.Contains("(MCP"), "presses show in the activity feed");
    }

    [Test]
    public static void Mcp_loads_a_program_from_a_path_and_restarts_it()
    {
        using var s = new OpsTestServer();
        using var c = Connect(s, s.Key);
        var smw = Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw");
        var load = Call(c, "load_program", new() { ["slot"] = 2, ["path"] = smw });
        Assert.True(!load.IsError && load.Json.GetProperty("state").GetString() == "Running", "loaded and running: " + load.Text);
        Assert.True(Call(c, "load_program", new() { ["slot"] = 2, ["path"] = "relative.smw" }).IsError, "a relative path is refused");
        var restart = Call(c, "control_slot", new() { ["slot"] = 2, ["action"] = "restart" });
        Assert.True(!restart.IsError && restart.Json.GetProperty("state").GetString() == "Running", "restarted: " + restart.Text);
        var stop = Call(c, "control_slot", new() { ["slot"] = 2, ["action"] = "stop" });
        Assert.Equal("Stopped", stop.Json.GetProperty("state").GetString(), "stopped: " + stop.Text);
        var press = Call(c, "press_button", new() { ["slot"] = 2, ["control"] = "toggle_press" });
        Assert.True(press.IsError && press.Text.Contains("control_slot"), "pressing a stopped program says how to start it: " + press.Text);
    }

    [Test]
    public static void Mcp_needs_sign_in_and_each_tool_checks_its_permission_and_slot()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        Assert.True(Throws(() => Connect(s, null)), "no key, no MCP");
        Assert.True(Throws(() => Connect(s, "wrong")), "a wrong key, no MCP");

        // A page on another site can't drive it, whatever its cookies (and even though this machine may skip the key).
        var req = new HttpRequestMessage(HttpMethod.Post, s.Url + "/mcp") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        req.Headers.Add("Origin", "http://evil.example");
        Assert.Equal(403, (int)s.Http.Send(req).StatusCode, "cross-origin is refused");

        string Token(object body) => s.Post("/api/tokens", body).Json.GetProperty("token").GetString()!;
        using var ro = Connect(s, Token(new { name = "ro", scope = "read-only" }));
        Assert.True(!Call(ro, "list_controls", new() { ["slot"] = 1 }).IsError, "read-only may look");
        var denied = Call(ro, "press_button", new() { ["slot"] = 1, ["control"] = "toggle_press" });
        Assert.True(denied.IsError && denied.Text.Contains("Signal"), "but not press: " + denied.Text);
        Assert.True(Call(ro, "control_slot", new() { ["slot"] = 1, ["action"] = "stop" }).IsError, "or stop");

        using var op = Connect(s, Token(new { name = "op", scope = "operator", slots = new[] { 2 } }));
        Assert.True(Call(op, "press_button", new() { ["slot"] = 1, ["control"] = "toggle_press" }).Text.Contains("slot 1"), "a token for slot 2 can't press in slot 1");
        Assert.Equal(0, Call(op, "list_slots", new()).Json.EnumerateArray().Count(x => x.GetProperty("slot").GetInt32() == 1), "and doesn't see it");
        Assert.True(Call(op, "load_program", new() { ["slot"] = 2, ["path"] = "/x.smw" }).IsError, "an operator doesn't load programs");

        Assert.Equal(200, s.Post("/api/slots/1/lock", new { reason = "live event" }).Status, "lock slot 1");
        using var tech = Connect(s, Token(new { name = "op2", scope = "operator" }));
        var locked = Call(tech, "press_button", new() { ["slot"] = 1, ["control"] = "toggle_press" });
        Assert.True(locked.IsError && locked.Text.Contains("locked", StringComparison.OrdinalIgnoreCase), "a locked slot refuses presses: " + locked.Text);
        Assert.True(!Call(tech, "read_signals", new() { ["slot"] = 1 }).IsError, "but can still be read");

        Assert.Equal(OpenVP.Core.Security.Perm.View, EndpointPolicy.For("POST", "/mcp/")!.Perm, "policy line");
    }

    [Test]
    public static void Mcp_can_be_switched_off()
    {
        using var s = new OpsTestServer(more: "--OpenVP:McpEnabled=false");
        Assert.True(Throws(() => Connect(s, s.Key)), "no MCP endpoint");
        Assert.Equal(200, s.Get("/api/slots").Status, "the rest of the API still works");
    }

    private static bool Throws(Func<Conn> connect)
    {
        try { connect().Dispose(); return false; }
        catch (Exception) { return true; }
    }
}
