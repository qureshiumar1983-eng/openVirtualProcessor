using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Eisc;

namespace OpenVP.Tests;

/// <summary>Two servers, each with an EISC program, linked over a real WebSocket; and the link's own rules (resync, loops, a link to oneself).</summary>
public static class RemoteEiscTests
{
    private static JoinKey D(uint j) => new(SigKind.Digital, j);
    private static JoinKey A(uint j) => new(SigKind.Analog, j);
    private static JoinKey S(uint j) => new(SigKind.Serial, j);
    private static string Examples => Path.Combine(Paths.RepoRoot, "examples", "programs");

    private sealed class Pair : IDisposable
    {
        public OpsTestServer A, B;
        public string TargetId = "", LinkId = "", Token = "";
        public Pair()
        {
            A = new OpsTestServer(true, more: ["--OpenVP:ExamplesDirectory=" + Examples]);
            B = new OpsTestServer(true, more: ["--OpenVP:ExamplesDirectory=" + Examples]);
            // Remote EISC is a Labs feature and off by default: switch it on at both ends, as an Admin would.
            Assert.Equal(200, A.Call(HttpMethod.Put, "/api/labs/remote-eisc", new { enabled = true }).Status, "Labs on A");
            Assert.Equal(200, B.Call(HttpMethod.Put, "/api/labs/remote-eisc", new { enabled = true }).Status, "Labs on B");
            Assert.Equal(200, A.Post("/api/examples/eisc-a/load", new { slot = 1 }).Status, "load A");
            Assert.Equal(200, B.Post("/api/examples/eisc-b/load", new { slot = 2 }).Status, "load B");
            var t = B.Post("/api/tokens", new { name = "link-from-a", scope = "eisc-link" });
            Assert.Equal(200, t.Status, "token: " + t.Text);
            Token = t.Json.GetProperty("token").GetString()!;
        }
        public void Link(bool enabled = true)
        {
            TargetId = A.Post("/api/targets", new { name = "B", url = B.Url, key = Token }).Json.GetProperty("id").GetString()!;
            var r = A.Post("/api/eisc-links", new { name = "a-b", targetId = TargetId, localSlot = 1, localIpId = "10", remoteSlot = 2, remoteIpId = "10", enabled });
            Assert.Equal(200, r.Status, "link: " + r.Text);
            LinkId = r.Json.GetProperty("id").GetString()!;
        }
        public string State() => A.Get("/api/eisc-links").Json.GetProperty("links")[0].GetProperty("status") is { ValueKind: JsonValueKind.Object } st ? st.GetProperty("state").GetString()! : "none";
        public JoinDevice PanelA => A.P.Slot(1).FindDevice(3)!;
        public JoinDevice PanelB => B.P.Slot(2).FindDevice(3)!;
        public void Dispose() { A.Dispose(); B.Dispose(); }
    }

    [Test]
    public static void Joins_flow_both_ways_between_two_real_servers()
    {
        using var p = new Pair();
        p.Link();
        Assert.Eventually(() => p.State() == "connected", "the link connects", 10000);

        // A's toggle (digital 1 press) goes to B's panel feedback digital 1; the slider to analog 1; the text to serial 1.
        p.PanelA.ReceiveInput(D(1), true); p.PanelA.ReceiveInput(D(1), false);
        Assert.Eventually(() => (bool)p.PanelB.GetFeedback(D(1)), "A's toggle shows on B", 5000);
        p.PanelA.ReceiveInput(A(1), (ushort)40000);
        Assert.Eventually(() => (ushort)p.PanelB.GetFeedback(A(1)) == 40000, "A's slider shows on B");
        p.PanelA.ReceiveInput(D(2), true);
        Assert.Eventually(() => (string)p.PanelB.GetFeedback(S(1)) == "Hello from A", "A's text arrives on B");

        // And the other way: B's digital 1 press lights A's digital 2, B's slider moves A's analog 1.
        p.PanelB.ReceiveInput(D(1), true);
        Assert.Eventually(() => (bool)p.PanelA.GetFeedback(D(2)), "B's button reaches A", 5000);
        p.PanelB.ReceiveInput(A(1), (ushort)1234);
        Assert.Eventually(() => (ushort)p.PanelA.GetFeedback(A(1)) == 1234, "B's slider reaches A");

        // The other end sees an incoming link, and both sides show it as a connection on the EISC.
        var inc = p.B.Get("/api/eisc-links").Json.GetProperty("inbound");
        Assert.Equal(1, inc.GetArrayLength(), "B lists the incoming link");
        Assert.True(p.A.P.Slot(1).FindDevice(0x10)!.Connections.Any(c => c.Transport == "Remote EISC" && c.LatencyMs >= 0 || c.Transport == "Remote EISC"), "A's EISC has a remote link connection");
        var rack = p.A.Get("/api/rack").Json;
        Assert.Equal("connected", rack.GetProperty("remoteEisc").GetProperty("outbound")[0].GetProperty("state").GetString(), "the rack shows the link state");
        var st = p.A.Get("/api/eisc-links").Json.GetProperty("links")[0].GetProperty("status");
        Assert.True(st.GetProperty("joinsOut").GetInt64() > 0 && st.GetProperty("joinsIn").GetInt64() > 0, "joins counted both ways");
        Assert.Eventually(() => p.A.Get("/api/eisc-links").Json.GetProperty("links")[0].GetProperty("status").TryGetProperty("latencyMs", out var l) && l.ValueKind == JsonValueKind.Number, "latency is measured", 8000);
    }

    [Test]
    public static void A_dropped_link_reconnects_and_resyncs_every_join()
    {
        var was = RemoteEiscManager.InitialBackoff; RemoteEiscManager.InitialBackoff = TimeSpan.FromMilliseconds(200);
        try
        {
            using var p = new Pair();
            p.Link();
            Assert.Eventually(() => p.State() == "connected", "connected", 10000);
            p.PanelA.ReceiveInput(D(1), true);
            Assert.Eventually(() => (bool)p.PanelB.GetFeedback(D(1)), "value crossed");

            // B switches Remote EISC off in Labs: its incoming links close. A keeps trying.
            Assert.Equal(200, p.B.Call(HttpMethod.Put, "/api/labs/remote-eisc", new { enabled = false }).Status, "switch off on B");
            Assert.Eventually(() => p.State() is "reconnecting" or "error", "A notices the drop", 8000);
            // While it is down, A changes joins; B's program changes too.
            p.PanelA.ReceiveInput(A(1), (ushort)777);
            p.PanelA.ReceiveInput(D(1), false); p.PanelA.ReceiveInput(D(1), true); p.PanelA.ReceiveInput(D(1), false); // toggle off, on, off: net off
            p.PanelA.ReceiveInput(D(1), true); p.PanelA.ReceiveInput(D(1), false);                                      // the toggle program flips on and off
            Assert.True((ushort)p.PanelB.GetFeedback(A(1)) != 777, "nothing crossed while it was down");
            var before = p.A.P.Slot(1).FindDevice(0x10)!.FeedbackSnapshot();
            Assert.Equal(200, p.B.Call(HttpMethod.Put, "/api/labs/remote-eisc", new { enabled = true }).Status, "switch on on B");
            Assert.Eventually(() => p.State() == "connected", "A reconnects by itself", 15000);
            Assert.Eventually(() => (ushort)p.PanelB.GetFeedback(A(1)) == 777, "the join changed while it was down is there after the reconnect", 5000);
            // Every feedback join A has equals what B sees as its input (a full resync, not only what changed).
            var bEisc = p.B.P.Slot(2).FindDevice(0x10)!;
            foreach (var (k, v) in p.A.P.Slot(1).FindDevice(0x10)!.FeedbackSnapshot())
                Assert.True(Equals(bEisc.GetInput(k), v), $"{k} equals on both sides after the resync");
            Assert.True(p.A.Get("/api/eisc-links").Json.GetProperty("links")[0].GetProperty("status").GetProperty("reconnects").GetInt32() >= 1, "a reconnect was counted");
        }
        finally { RemoteEiscManager.InitialBackoff = was; }
    }

    [Test]
    public static void The_link_follows_a_reloaded_program_and_waits_for_a_missing_one()
    {
        var was = RemoteEiscManager.InitialBackoff; RemoteEiscManager.InitialBackoff = TimeSpan.FromMilliseconds(200);
        try
        {
            using var p = new Pair();
            p.A.Post("/api/slots/1/unload");
            p.Link();
            Assert.Eventually(() => p.State() == "waiting", "no EISC in the slot yet: waiting", 5000);
            Assert.Equal(200, p.A.Post("/api/examples/eisc-a/load", new { slot = 1 }).Status, "now load it");
            Assert.Eventually(() => p.State() == "connected", "it connects once the EISC exists", 10000);
            // Reload the program: the EISC device is replaced, the link ends and comes back to the new one.
            Assert.Equal(200, p.A.Post("/api/examples/eisc-a/load", new { slot = 1, replace = true }).Status, "reload");
            Assert.Eventually(() => p.State() == "connected" && p.A.P.Slot(1).FindDevice(0x10)!.Connections.Any(c => c.Transport == "Remote EISC"), "linked to the new EISC", 10000);
            p.PanelA.ReceiveInput(D(1), true);
            Assert.Eventually(() => (bool)p.PanelB.GetFeedback(D(1)), "and joins flow again");
        }
        finally { RemoteEiscManager.InitialBackoff = was; }
    }

    // ------------------------------------------------------------------ the session on its own

    private sealed class Duplex(Stream r, Stream w) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true; public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => w.Flush();
        public override int Read(byte[] b, int o, int c) => r.Read(b, o, c);
        public override ValueTask<int> ReadAsync(Memory<byte> m, CancellationToken ct = default) => r.ReadAsync(m, ct);
        public override void Write(byte[] b, int o, int c) { w.Write(b, o, c); w.Flush(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> m, CancellationToken ct = default) { await w.WriteAsync(m, ct); await w.FlushAsync(ct); }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    }

    private sealed class Session : IDisposable
    {
        public JoinDevice DevA = new(1, 0x10, "EISC", ""), DevB = new(2, 0x10, "EISC", "");
        public RemoteEiscSession SA, SB;
        public Task RunA, RunB;
        private readonly CancellationTokenSource _cts = new();
        public Session(string instA = "inst-a", string instB = "inst-b", Action<JoinDevice>? beforeStart = null)
        {
            var p1 = new Pipe(); var p2 = new Pipe();
            var wa = WebSocket.CreateFromStream(new Duplex(p1.Reader.AsStream(), p2.Writer.AsStream()), new WebSocketCreationOptions { IsServer = false });
            var wb = WebSocket.CreateFromStream(new Duplex(p2.Reader.AsStream(), p1.Writer.AsStream()), new WebSocketCreationOptions { IsServer = true });
            beforeStart?.Invoke(DevA);
            SA = new RemoteEiscSession(wa, DevA, instA, "1.0", "B", "", 0, (l, m) => Log.Add(m));
            SB = new RemoteEiscSession(wb, DevB, instB, "1.0", "A", "", 0, (l, m) => Log.Add(m));
            RunA = Task.Run(() => SA.RunAsync(_cts.Token)); RunB = Task.Run(() => SB.RunAsync(_cts.Token));
        }
        public List<string> Log = new();
        public void Stop() { _cts.Cancel(); try { Task.WaitAll([RunA, RunB], 3000); } catch { } }
        public void Dispose() { Stop(); SA.Dispose(); SB.Dispose(); }
    }

    [Test]
    public static void A_connection_starts_with_a_full_resync_in_both_directions()
    {
        using var s = new Session(beforeStart: a => { a.SetFeedback(D(5), true); a.SetFeedback(A(3), (ushort)321); a.SetFeedback(S(2), "text"); });
        s.DevB.SetFeedback(D(9), true);
        Assert.Eventually(() => s.SA.HelloReceived.IsCompletedSuccessfully && s.SB.HelloReceived.IsCompletedSuccessfully, "hello both ways", 10000);
        Assert.Eventually(() => (bool)s.DevB.GetInput(D(5)) && (ushort)s.DevB.GetInput(A(3)) == 321 && (string)s.DevB.GetInput(S(2)) == "text", "A's joins arrived at B", 10000);
        Assert.Eventually(() => (bool)s.DevA.GetInput(D(9)), "B's join arrived at A", 10000);
        Assert.True(s.DevA.Online && s.DevB.Online, "both EISCs count the link as a connection");
        Assert.Equal("Remote EISC", s.DevA.Connections.Single().Transport, "as a remote EISC connection");
        // A resync does not replay serials the other side already holds: reconnect, count what fires.
        var fired = 0;
        s.DevB.InputReceived += (_, k, _) => { if (k.Kind == SigKind.Serial) fired++; };
        s.Stop();
        Assert.Eventually(() => !s.DevA.Online && !s.DevB.Online, "the link going away takes both offline");
        using var again = new Session(beforeStart: null);
        again.DevA.SetFeedback(S(2), "text");
        again.DevB.ReceiveInput(S(2), "text");
        var n = 0; again.DevB.InputReceived += (_, k, _) => { if (k.Kind == SigKind.Serial) n++; };
        Assert.Eventually(() => again.SB.HelloReceived.IsCompletedSuccessfully, "hello", 10000);
        Thread.Sleep(300);
        Assert.Equal(0, n, "an unchanged serial isn't delivered again by a resync");
    }

    [Test]
    public static void Two_programs_that_echo_each_other_do_not_loop()
    {
        using var s = new Session();
        Assert.Eventually(() => s.SA.HelloReceived.IsCompletedSuccessfully && s.SB.HelloReceived.IsCompletedSuccessfully, "hello", 10000);
        // Both programs mirror whatever serial arrives straight back out.
        s.DevA.InputReceived += (d, k, v) => { if (k.Kind == SigKind.Serial) d.SetFeedback(k, v); };
        s.DevB.InputReceived += (d, k, v) => { if (k.Kind == SigKind.Serial) d.SetFeedback(k, v); };
        s.DevA.SetFeedback(S(1), "ping");
        Assert.Eventually(() => (string)s.DevB.GetInput(S(1)) == "ping", "it arrives");
        Thread.Sleep(700);
        Assert.Equal(1L, s.DevB.InputsReceived, "B got the text once");
        Assert.Equal(0L, s.DevA.InputsReceived, "and it was not bounced back to A");
        // A digital mirrored the same way settles too (no change, no event).
        s.DevA.InputReceived += (d, k, v) => { if (k.Kind == SigKind.Digital) d.SetFeedback(k, v); };
        s.DevB.InputReceived += (d, k, v) => { if (k.Kind == SigKind.Digital) d.SetFeedback(k, v); };
        s.DevA.SetFeedback(D(1), true);
        Assert.Eventually(() => (bool)s.DevB.GetInput(D(1)), "digital arrives");
        Thread.Sleep(500);
        Assert.True(Interlocked.Read(ref s.SA.Connection.JoinsOut) < 10 && Interlocked.Read(ref s.SB.Connection.JoinsOut) < 10, "a handful of messages, not a storm");
    }

    [Test]
    public static void A_join_that_two_programs_keep_changing_is_paused_not_flooded()
    {
        var was = RemoteEiscSession.PauseFor; RemoteEiscSession.PauseFor = TimeSpan.FromSeconds(30);
        try
        {
            using var s = new Session();
            Assert.Eventually(() => s.SA.HelloReceived.IsCompletedSuccessfully && s.SB.HelloReceived.IsCompletedSuccessfully, "hello", 10000);
            // Each program sets analog 1 to one more than it just received: a loop that never settles.
            s.DevA.InputReceived += (d, k, v) => { if (k.Kind == SigKind.Analog) d.SetFeedback(k, (ushort)((ushort)v + 1)); };
            s.DevB.InputReceived += (d, k, v) => { if (k.Kind == SigKind.Analog) d.SetFeedback(k, (ushort)((ushort)v + 1)); };
            s.DevA.SetFeedback(A(1), (ushort)1);
            Assert.Eventually(() => Interlocked.Read(ref s.SA.Connection.Errors) + Interlocked.Read(ref s.SB.Connection.Errors) > 0, "the limiter trips", 5000);
            Thread.Sleep(500);
            var a = Interlocked.Read(ref s.SA.Connection.JoinsOut) + Interlocked.Read(ref s.SB.Connection.JoinsOut);
            Thread.Sleep(700);
            var b = Interlocked.Read(ref s.SA.Connection.JoinsOut) + Interlocked.Read(ref s.SB.Connection.JoinsOut);
            Assert.Equal(a, b, "and the traffic stops");
            Assert.True(a <= 2 * RemoteEiscSession.MaxJoinsPerSecond + 10, "after about the allowed number of messages: " + a);
            Assert.True(s.Log.Any(m => m.Contains("changing more than")), "a warning explains why");
        }
        finally { RemoteEiscSession.PauseFor = was; }
    }

    [Test]
    public static void A_link_to_the_same_machine_is_refused()
    {
        using var s = new Session("same", "same");
        Assert.Eventually(() => s.RunA.IsCompleted && s.RunB.IsCompleted, "both ends end", 10000);
        Assert.True(s.SA.EndReason?.Contains("same OpenVP") == true, "the reason is a link to itself: " + s.SA.EndReason);
        Assert.True(!s.SA.HelloReceived.Result, "never counted as connected");
    }

    [Test]
    public static void A_bad_message_does_not_end_the_link()
    {
        using var s = new Session();
        Assert.Eventually(() => s.SA.HelloReceived.IsCompletedSuccessfully, "hello", 10000);
        // Garbage in the stream of messages is counted and ignored; the next real join still arrives.
        var field = typeof(RemoteEiscSession).GetMethod("Handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Throws<Exception>(() => { try { field.Invoke(s.SA, ["{not json"]); } catch (System.Reflection.TargetInvocationException e) { throw new Exception(e.InnerException!.Message); } });
        s.DevA.SetFeedback(D(7), true);
        Assert.Eventually(() => (bool)s.DevB.GetInput(D(7)), "still linked");
    }

    // ------------------------------------------------------------------ who may open a link

    [Test]
    public static void Only_an_eisc_link_token_or_an_admin_can_open_the_link_socket_and_the_token_can_do_nothing_else()
    {
        using var p = new Pair();
        string Token(string name, string scope, int[]? slots = null) => p.B.Post("/api/tokens", slots == null ? (object)new { name, scope } : new { name, scope, slots }).Json.GetProperty("token").GetString()!;
        var path = "/ws/eisc?slot=2&ipid=10";
        Assert.Equal(101, new SecClient(p.B, p.Token).WebSocket(path), "the eisc-link token opens it");
        Assert.Equal(101, new SecClient(p.B, p.B.Key).WebSocket(path), "so does the access key (an Admin)");
        Assert.Equal(403, new SecClient(p.B, Token("ro", "read-only")).WebSocket(path), "a read-only token can't");
        Assert.Equal(403, new SecClient(p.B, Token("op", "operator")).WebSocket(path), "an operator token can't");
        Assert.Equal(403, new SecClient(p.B, Token("pg", "programmer")).WebSocket(path), "a programmer token can't");
        Assert.Equal(401, new SecClient(p.B).WebSocket(path), "nobody can't");
        Assert.Equal(403, new SecClient(p.B, Token("one", "eisc-link", [1])).WebSocket(path), "a token limited to slot 1 can't open slot 2's EISC");
        Assert.Equal(101, new SecClient(p.B, Token("two", "eisc-link", [2])).WebSocket(path), "but can open its own slot's");
        Assert.Equal(409, new SecClient(p.B, p.Token).WebSocket("/ws/eisc?slot=2&ipid=03"), "a panel isn't an EISC");
        Assert.Equal(404, new SecClient(p.B, p.Token).WebSocket("/ws/eisc?slot=3&ipid=10"), "an empty slot has none");
        Assert.Equal(403, new SecClient(p.B, p.Token).WebSocket(path, origin: "https://evil.example"), "a web page on another site can't even with the token");

        // The link token is good for the link and nothing else.
        var cl = new SecClient(p.B, p.Token);
        foreach (var (m, u) in new[] { (HttpMethod.Get, "/api/slots"), (HttpMethod.Get, "/api/slots/2/signals"), (HttpMethod.Post, "/api/slots/2/start"), (HttpMethod.Get, "/api/tokens"), (HttpMethod.Get, "/api/eisc-links"), (HttpMethod.Get, "/api/labs"), (HttpMethod.Get, "/api/rack") })
            Assert.Equal(403, cl.Call(m, u, m == HttpMethod.Post ? new { } : null).Status, $"{m} {u} is refused for the link token");
        Assert.Equal(200, cl.Get("/api/info").Status, "only the public info answers");

        // Remote EISC off in Labs: the socket is a plain 404 even for the key.
        p.B.Call(HttpMethod.Put, "/api/labs/remote-eisc", new { enabled = false });
        Assert.Equal(404, new SecClient(p.B, p.B.Key).WebSocket(path), "switched off in Labs");
    }

    [Test]
    public static void Link_settings_are_for_admins_and_validated()
    {
        using var s = new OpsTestServer();
        foreach (var r in new[] { "viewer", "technician", "programmer" })
            Assert.Equal(200, s.Post("/api/users", new { name = r, password = r + "-password-1", role = r }).Status, "user " + r);
        foreach (var r in new[] { "viewer", "technician", "programmer" })
        {
            var c = new SecClient(s); Assert.Equal(200, c.Login(r, r + "-password-1"), "login " + r);
            Assert.Equal(403, c.Get("/api/eisc-links").Status, r + " can't list links");
            Assert.Equal(403, c.Post("/api/eisc-links", new { name = "x" }).Status, r + " can't add one");
            Assert.Equal(200, c.Get("/api/rack").Status, r + " can see the rack");
        }
        var t = s.Post("/api/targets", new { name = "Other", url = "http://127.0.0.1:1", key = "k" }).Json.GetProperty("id").GetString()!;
        Assert.Equal(400, s.Post("/api/eisc-links", new { name = "", targetId = t, localSlot = 1, localIpId = "10", remoteSlot = 1, remoteIpId = "10" }).Status, "a name is required");
        Assert.Equal(400, s.Post("/api/eisc-links", new { name = "x", targetId = "nope", localSlot = 1, localIpId = "10", remoteSlot = 1, remoteIpId = "10" }).Status, "the target must exist");
        Assert.Equal(400, s.Post("/api/eisc-links", new { name = "x", targetId = t, localSlot = 9, localIpId = "10", remoteSlot = 1, remoteIpId = "10" }).Status, "the slot must exist");
        Assert.Equal(400, s.Post("/api/eisc-links", new { name = "x", targetId = t, localSlot = 1, localIpId = "zz", remoteSlot = 1, remoteIpId = "10" }).Status, "the IP ID is hex");
        var ok = s.Post("/api/eisc-links", new { name = "x", targetId = t, localSlot = 1, localIpId = "10", remoteSlot = 1, remoteIpId = "10" });
        Assert.Equal(200, ok.Status, "valid: " + ok.Text);
        Assert.Equal(400, s.Post("/api/eisc-links", new { name = "y", targetId = t, localSlot = 1, localIpId = "10", remoteSlot = 2, remoteIpId = "10" }).Status, "one EISC, one link");
        Assert.Equal(200, s.Delete("/api/targets/" + t).Status, "delete the target");
        Assert.Equal(0, s.Get("/api/eisc-links").Json.GetProperty("links").GetArrayLength(), "its links go with it");
    }
}
