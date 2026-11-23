using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>MQTT bridge (with a fake broker connection), webhooks, incoming hooks, OSC and Art-Net services, and the signal hub, on real slots.</summary>
public static class IntegrationServiceTests
{
    internal sealed class Rig : IDisposable
    {
        public Processor P; public IntegrationsHost H; public ProgramSlot Slot => P.Slot(1); public string Dir;
        public Rig(Func<IMqttTransport>? mqtt = null, HttpMessageHandler? http = null, bool load = true)
        {
            Dir = Paths.TempData();
            P = new Processor(Dir, 2);
            P.AddLoader(new SimplLoader());
            if (load) P.LoadProgram(1, Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw"));
            H = new IntegrationsHost(P, mqtt ?? (() => throw new InvalidOperationException("no MQTT")), http);
        }
        public object Value(string name) => Slot.Runtime!.GetSignals().First(s => s.Name == name).Value;
        public void Dispose() { H.Dispose(); P.Dispose(); }
    }

    internal static int FreePort(bool udp = false)
    {
        if (udp) { using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); return ((IPEndPoint)u.Client.LocalEndPoint!).Port; }
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port;
    }

    // ---------------------------------------------------------------- the hub

    [Test]
    public static void Signal_hub_listens_to_the_slot_only_while_someone_subscribes_and_filters_by_name()
    {
        using var r = new Rig();
        Assert.True(!r.Slot.TraceWanted && !r.Slot.Hub.Attached, "nobody subscribed: the slot isn't traced");
        var got = new ConcurrentQueue<string>(); var all = new ConcurrentQueue<string>();
        var a = r.Slot.Hub.Subscribe(["toggle_fb"], e => got.Enqueue(e.Signal + "=" + SignalValues.Format(e.Value)));
        Assert.True(r.Slot.TraceWanted && r.Slot.Hub.Attached, "a subscription turns tracing on");
        r.Slot.ForcePulse("toggle_press");
        Assert.Eventually(() => got.Count >= 1, "the chosen signal's change arrives");
        Assert.True(got.All(g => g.StartsWith("toggle_fb=")), "and only that signal");
        var b = r.Slot.Hub.Subscribe(null, e => all.Enqueue(e.Signal));
        r.Slot.ForcePulse("toggle_press");
        Assert.Eventually(() => all.Count >= 2, "a catch-all subscriber hears every signal");
        Assert.True(all.Distinct().Count() >= 2, "more than one signal name");
        a.Dispose(); b.Dispose();
        Assert.True(!r.Slot.TraceWanted && !r.Slot.Hub.Attached && r.Slot.Hub.SubscriberCount == 0, "all gone: tracing off again");
        using var bad = r.Slot.Hub.Subscribe(["toggle_fb"], _ => throw new InvalidOperationException("boom"));
        r.Slot.ForcePulse("toggle_press");
        Assert.Eventually(() => r.Slot.Hub.HandlerErrors > 0, "a handler that throws is contained");
        Assert.Equal(true, r.Slot.Runtime != null, "the program is still loaded");
    }

    [Test]
    public static void Sample_program_has_the_signals_the_integration_tests_rely_on()
    {
        using var r = new Rig();
        var s = r.Slot.Runtime!.GetSignals().ToDictionary(x => x.Name, x => x.Kind);
        Assert.Equal(SigKind.Digital, s["toggle_press"], "toggle_press"); Assert.Equal(SigKind.Digital, s["toggle_fb"], "toggle_fb");
        Assert.Equal(SigKind.Analog, s["volume_level"], "volume_level"); Assert.Equal(SigKind.Serial, s["hello_text"], "hello_text");
        r.H.Directory.SetFromText(1, "volume_level", "50%");
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) is 32767 or 32768, "analog set with a percentage");
        r.H.Directory.SetFromText(1, "hello_text", "hi there");
        Assert.Eventually(() => (string)r.Value("hello_text") == "hi there", "serial set");
        Assert.Equal(SignalDirectory.SetOutcome.UnknownSignal, r.H.Directory.TrySetFromText(1, "nope", "1", out _), "unknown");
        Assert.Equal(SignalDirectory.SetOutcome.BadValue, r.H.Directory.TrySetFromText(1, "toggle_press", "maybe", out _), "bad digital");
        Assert.Equal(SignalDirectory.SetOutcome.NoProgram, r.H.Directory.TrySetFromText(2, "x", "1", out _), "empty slot");
    }

    // ---------------------------------------------------------------- MQTT bridge, with a fake connection

    internal sealed class FakeMqtt : IMqttTransport
    {
        public static readonly ConcurrentQueue<FakeMqtt> All = new();
        public readonly ConcurrentQueue<(string Topic, string Payload, bool Retain, int Qos)> Published = new();
        public readonly HashSet<string> Subscribed = new();
        public MqttConnectOptions? Options;
        public bool IsConnected { get; private set; }
        public bool BlockPublish; public bool FailConnect;
        public event Action<string, byte[], bool>? MessageReceived;
        public event Action<string>? Disconnected;
        public FakeMqtt() { All.Enqueue(this); }
        public Task ConnectAsync(MqttConnectOptions o, CancellationToken ct) { if (FailConnect) throw new IOException("connection refused"); Options = o; IsConnected = true; return Task.CompletedTask; }
        public async Task PublishAsync(string topic, byte[] payload, bool retain, int qos, CancellationToken ct)
        {
            if (BlockPublish) await Task.Delay(Timeout.Infinite, ct);
            Published.Enqueue((topic, Encoding.UTF8.GetString(payload), retain, qos));
        }
        public Task SubscribeAsync(IEnumerable<string> t, CancellationToken ct) { lock (Subscribed) foreach (var x in t) Subscribed.Add(x); return Task.CompletedTask; }
        public Task UnsubscribeAsync(IEnumerable<string> t, CancellationToken ct) { lock (Subscribed) foreach (var x in t) Subscribed.Remove(x); return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken ct) { IsConnected = false; return Task.CompletedTask; }
        public void Inject(string topic, string payload, bool retained = false) => MessageReceived?.Invoke(topic, Encoding.UTF8.GetBytes(payload), retained);
        public void Drop() { IsConnected = false; Disconnected?.Invoke("test: connection lost"); }
        public string? Last(string topic) => Published.Where(p => p.Topic == topic).Select(p => p.Payload).LastOrDefault();
        public void Dispose() { IsConnected = false; }
    }

    private static Rig MqttRig(out Func<FakeMqtt> current)
    {
        while (FakeMqtt.All.TryDequeue(out _)) { }
        var r = new Rig(() => new FakeMqtt());
        current = () => FakeMqtt.All.Last();
        return r;
    }

    private static void StartMqtt(Rig r, bool ha = false)
    {
        r.H.Store.UpdateSlot(1, s => s with { Mqtt = [new MqttSignal { Signal = "toggle_fb" }, new MqttSignal { Signal = "toggle_press", Set = true, Discovery = true },
            new MqttSignal { Signal = "volume_level", Set = true, Discovery = true }, new MqttSignal { Signal = "hello_text", Set = true, Discovery = true }] });
        r.H.Store.SetMqtt(new MqttSettings { Enabled = true, Host = "broker.test", Port = 1883, BaseTopic = "t/room", HomeAssistant = ha, Username = "u" }, "s3cret");
    }

    [Test]
    public static void Mqtt_publishes_availability_state_and_current_values_retained_and_subscribes_to_set_topics()
    {
        using var r = MqttRig(out var cur);
        StartMqtt(r);
        Assert.Eventually(() => FakeMqtt.All.Any(f => f.IsConnected) && cur().Last("t/room/slot01/hello_text") != null, "connected and synced");
        var m = cur();
        Assert.Equal("broker.test", m.Options!.Host, "host"); Assert.Equal("s3cret", m.Options.Password!, "the password reaches the connection"); Assert.Equal("u", m.Options.Username!, "user");
        Assert.Equal("t/room/status", m.Options.WillTopic, "the will is on the availability topic"); Assert.Equal("offline", m.Options.WillPayload, "will text");
        Assert.Equal("online", m.Last("t/room/status")!, "availability");
        Assert.Equal("running", m.Last("t/room/slot01/state")!, "slot state");
        Assert.Equal("0", m.Last("t/room/slot01/toggle_fb")!, "digital current value");
        Assert.True(m.Published.Where(p => p.Topic.StartsWith("t/room/")).All(p => p.Retain), "everything retained");
        lock (m.Subscribed)
        {
            Assert.True(m.Subscribed.SetEquals(["t/room/slot01/toggle_press/set", "t/room/slot01/volume_level/set", "t/room/slot01/hello_text/set"]), "only signals that allow set are subscribed: " + string.Join(",", m.Subscribed));
        }
        Assert.True(!m.Published.Any(p => p.Topic.StartsWith("homeassistant")), "no discovery unless it is switched on");

        // A change of a chosen signal is published; a change of one that isn't chosen is not.
        r.Slot.ForcePulse("toggle_press");
        Assert.Eventually(() => m.Last("t/room/slot01/toggle_fb") == "1", "feedback change published");
        var before = m.Published.Count;
        r.Slot.ForceSignal("source1_press", true);
        Thread.Sleep(150);
        Assert.True(!m.Published.Skip(before).Any(p => p.Topic.Contains("source1")), "an unchosen signal isn't published");
        Assert.True(r.H.Mqtt.Status().Published > 0 && r.H.Mqtt.Status().State == "connected", "status counts it");
    }

    [Test]
    public static void Mqtt_set_topics_set_digital_analog_and_serial_signals_and_ignore_bad_or_retained_messages()
    {
        using var r = MqttRig(out var cur);
        StartMqtt(r);
        Assert.Eventually(() => FakeMqtt.All.Any(f => f.IsConnected) && cur().Last("t/room/slot01/hello_text") != null, "synced");
        var m = cur();
        m.Inject("t/room/slot01/toggle_press/set", "1");
        Assert.Eventually(() => m.Last("t/room/slot01/toggle_fb") == "1", "'1' presses and the feedback follows");
        m.Inject("t/room/slot01/toggle_press/set", "0");
        m.Inject("t/room/slot01/toggle_press/set", "pulse");
        Assert.Eventually(() => m.Last("t/room/slot01/toggle_fb") == "0", "pulse toggles it back");
        m.Inject("t/room/slot01/volume_level/set", "50%");
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) is 32767 or 32768, "50% is half scale");
        Assert.Eventually(() => m.Last("t/room/slot01/volume_level") is "32767" or "32768", "and is published back");
        m.Inject("t/room/slot01/volume_level/set", "1234");
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) == 1234, "plain number");
        m.Inject("t/room/slot01/hello_text/set", "Hello MQTT");
        Assert.Eventually(() => (string)r.Value("hello_text") == "Hello MQTT", "serial text");

        m.Inject("t/room/slot01/volume_level/set", "loud");
        m.Inject("t/room/slot01/nothing/set", "1");
        m.Inject("t/room/slot01/toggle_fb/set", "1", false);   // not allowed to set
        m.Inject("t/room/slot01/volume_level/set", "9", retained: true);
        Thread.Sleep(200);
        Assert.Equal(1234, Convert.ToInt32(r.Value("volume_level")), "bad value and retained set messages change nothing");
        Assert.True(r.H.Mqtt.Status().Received >= 8, "all counted");
    }

    [Test]
    public static void Mqtt_home_assistant_discovery_has_the_right_entity_for_each_kind()
    {
        using var r = MqttRig(out var cur);
        StartMqtt(r, ha: true);
        Assert.Eventually(() => FakeMqtt.All.Any(f => f.IsConnected) && cur().Published.Count(p => p.Topic.StartsWith("homeassistant/")) >= 3, "discovery published");
        var m = cur();
        var node = MqttTopics.NodeId(r.H.Store.Global.Mqtt);
        JsonElement Cfg(string comp, string obj) => JsonDocument.Parse(m.Last($"homeassistant/{comp}/{node}/{obj}/config")!).RootElement;
        var sw = Cfg("switch", "slot01_toggle_press");
        Assert.Equal("t/room/slot01/toggle_press", sw.GetProperty("state_topic").GetString()!, "switch state topic");
        Assert.Equal("t/room/slot01/toggle_press/set", sw.GetProperty("command_topic").GetString()!, "switch command topic");
        Assert.Equal("1", sw.GetProperty("payload_on").GetString()!, "payload on"); Assert.Equal("t/room/status", sw.GetProperty("availability_topic").GetString()!, "availability");
        Assert.Equal("OpenVP", sw.GetProperty("device").GetProperty("manufacturer").GetString()!, "device block");
        var num = Cfg("number", "slot01_volume_level");
        Assert.Equal(65535, num.GetProperty("max").GetInt32(), "number max"); Assert.True(num.TryGetProperty("command_topic", out _), "number can be set");
        var txt = Cfg("sensor", "slot01_hello_text");
        Assert.True(txt.TryGetProperty("state_topic", out _), "serial becomes a text sensor");
        Assert.True(!txt.TryGetProperty("command_topic", out _), "a Home Assistant sensor has no command topic (it would be rejected)");
        Assert.True(m.Last($"homeassistant/binary_sensor/{node}/slot01_toggle_fb/config") == null, "toggle_fb didn't ask for discovery");
        Assert.Equal(sw.GetProperty("unique_id").GetString()!, $"{node}_slot01_toggle_press", "unique id");

        // Turning discovery off for a signal removes its entity (an empty retained message).
        r.H.Store.UpdateSlot(1, s => s with { Mqtt = s.Mqtt.Select(x => x.Signal == "toggle_press" ? x with { Discovery = false } : x).ToList() });
        Assert.Eventually(() => m.Last($"homeassistant/switch/{node}/slot01_toggle_press/config") == "", "the switch entity is removed");
    }

    [Test]
    public static void Mqtt_reconnects_after_a_lost_connection_and_publishes_current_values_again()
    {
        using var r = MqttRig(out var cur);
        ((MqttBridge)r.H.Mqtt).MaxBackoff = TimeSpan.FromMilliseconds(50);
        StartMqtt(r);
        Assert.Eventually(() => FakeMqtt.All.Any(f => f.IsConnected) && cur().Last("t/room/slot01/toggle_fb") != null, "first connection");
        var first = cur();
        first.Drop();
        Assert.Eventually(() => FakeMqtt.All.Count >= 2 && cur() != first && cur().IsConnected && cur().Last("t/room/slot01/toggle_fb") != null, "a new connection syncs everything again", 8000);
        Assert.Equal("online", cur().Last("t/room/status")!, "available again");
        lock (cur().Subscribed) Assert.Equal(3, cur().Subscribed.Count, "set topics subscribed again");

        // A broker that is down is retried and reported, not fatal.
        r.H.Store.SetMqtt(r.H.Store.Global.Mqtt with { Enabled = false });
        Assert.Eventually(() => r.H.Mqtt.Status().State == "off", "switched off");
        Assert.Equal("offline", FakeMqtt.All.Last().Last("t/room/status")!, "a clean stop says offline itself (the will only runs on a lost connection)");
    }

    [Test]
    public static void A_slow_or_dead_broker_never_stalls_the_program_and_the_queue_is_bounded()
    {
        using var r = MqttRig(out var cur);
        StartMqtt(r);
        Assert.Eventually(() => FakeMqtt.All.Any(f => f.IsConnected) && cur().Last("t/room/slot01/toggle_fb") != null, "connected");
        cur().BlockPublish = true;                        // the broker stops answering
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 30_000; i++) r.Slot.ForceSignal("volume_level", i % 60000);
        // A marker event on the slot thread: it only runs after everything queued before it.
        var done = new ManualResetEventSlim();
        r.Slot.Dispatcher.Post(() => done.Set());
        Assert.True(done.Wait(20_000), "the slot thread got through all 30000 changes");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"30000 changes took {sw.Elapsed.TotalSeconds:0.0} s with the broker stuck");
        Assert.Eventually(() => r.H.Mqtt.Status().Dropped > 0, "the bounded queue dropped the oldest changes instead of growing", 20_000);
    }

    // ---------------------------------------------------------------- the password

    [Test]
    public static void Mqtt_password_is_kept_apart_owner_only_and_never_returned()
    {
        using var r = new Rig();
        r.H.Store.SetMqtt(new MqttSettings { Enabled = false, Host = "b.test", Username = "u" }, "hunter2");
        var global = File.ReadAllText(Path.Combine(r.Dir, "integrations", "global.json"));
        Assert.True(!global.Contains("hunter2"), "global.json has no password");
        Assert.True(r.H.Store.Secrets.Has("mqtt.password") && r.H.Store.Secrets.Get("mqtt.password") == "hunter2", "the secret store has it");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(r.H.Store.Secrets.FilePath);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode, "secrets file is readable only by its owner");
        }
        // A password change that leaves the field out keeps it; clear removes it.
        r.H.Store.SetMqtt(r.H.Store.Global.Mqtt with { Port = 8883 });
        Assert.Equal("hunter2", r.H.Store.Secrets.Get("mqtt.password")!, "kept when not sent");
        r.H.Store.SetMqtt(r.H.Store.Global.Mqtt, clearPassword: true);
        Assert.True(!r.H.Store.Secrets.Has("mqtt.password"), "cleared");

        // With a protector (DPAPI on Windows) the file holds ciphertext.
        var dir = Paths.TempData();
        var s = new SecretStore(dir, v => Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(v.Reverse().ToArray()))), v => new string(Encoding.UTF8.GetString(Convert.FromBase64String(v)).Reverse().ToArray()));
        s.Set("mqtt.password", "p@ss");
        var raw = File.ReadAllText(s.FilePath);
        Assert.True(raw.Contains("dpapi:") && !raw.Contains("p@ss"), "stored protected: " + raw);
        Assert.Equal("p@ss", new SecretStore(dir, v => v, v => new string(Encoding.UTF8.GetString(Convert.FromBase64String(v)).Reverse().ToArray())).Get("mqtt.password")!, "and read back");
    }

    // ---------------------------------------------------------------- webhooks

    internal sealed class LocalHttp : IDisposable
    {
        private readonly HttpListener _l = new();
        public readonly int Port = FreePort();
        public readonly ConcurrentQueue<(string Path, Dictionary<string, string> Headers, byte[] Body)> Requests = new();
        public Func<int, int> Status = _ => 200;
        private int _n;
        public LocalHttp()
        {
            _l.Prefixes.Add($"http://127.0.0.1:{Port}/"); _l.Start();
            _ = Task.Run(async () =>
            {
                while (_l.IsListening)
                {
                    HttpListenerContext c;
                    try { c = await _l.GetContextAsync(); } catch { return; }
                    var ms = new MemoryStream(); await c.Request.InputStream.CopyToAsync(ms);
                    Requests.Enqueue((c.Request.Url!.AbsolutePath, c.Request.Headers.AllKeys.ToDictionary(k => k!, k => c.Request.Headers[k]!, StringComparer.OrdinalIgnoreCase), ms.ToArray()));
                    c.Response.StatusCode = Status(Interlocked.Increment(ref _n));
                    c.Response.Close();
                }
            });
        }
        public void Dispose() { try { _l.Close(); } catch { } }
    }

    [Test]
    public static void Webhook_signature_is_hmac_sha256_over_timestamp_dot_body()
    {
        // Known value: HMAC-SHA256(key "key", "1700000000.{\"a\":1}") computed independently.
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var sig = WebhookSigner.Sign("key", "1700000000", body);
        Assert.Equal("sha256=a438e398bfafc57e4396bb7fc2304422f0f768e965d073ca313cb52e22e6ad03", sig, "known HMAC");
        Assert.True(WebhookSigner.Verify("key", "1700000000", body, sig), "verifies");
        Assert.True(!WebhookSigner.Verify("key", "1700000001", body, sig), "a different timestamp fails");
        Assert.True(!WebhookSigner.Verify("other", "1700000000", body, sig), "a different key fails");
        Assert.True(!WebhookSigner.Verify("key", "1700000000", Encoding.UTF8.GetBytes("{\"a\":2}"), sig), "a changed body fails");
        Assert.True(!WebhookSigner.Verify("key", "1700000000", body, "sha256=00"), "garbage fails");
    }

    [Test]
    public static void Outgoing_webhook_posts_json_signs_it_retries_and_logs_every_delivery()
    {
        using var srv = new LocalHttp();
        srv.Status = n => n <= 2 ? 503 : 200;          // fails twice, then works
        using var r = new Rig();
        r.H.Webhooks.Backoff = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)];
        r.H.Store.UpdateSlot(1, s => s with { Webhooks = SlotIntegrations.CheckWebhooks(
            [new WebhookRule { Name = "fb", Url = $"http://127.0.0.1:{srv.Port}/in", Signal = "toggle_fb", Secret = "topsecret", Headers = new() { ["X-Token"] = "abc" } }], []) });
        r.Slot.ForcePulse("toggle_press");
        Assert.Eventually(() => r.H.Webhooks.Deliveries(1).Any(d => d.Ok), "delivered after retries", 8000);
        var d = r.H.Webhooks.Deliveries(1).First(x => x.Ok);
        Assert.Equal(3, d.Attempts, "two 503s then a 200 is three attempts"); Assert.Equal(200, d.Status!.Value, "final status");
        var reqs = srv.Requests.ToArray();
        Assert.True(reqs.Length >= 3, "the server saw each attempt");
        var req = reqs[^1];
        Assert.Equal("abc", req.Headers["X-Token"], "custom header"); Assert.Equal("signal.change", req.Headers["X-OpenVP-Event"], "event header");
        Assert.True(req.Headers["Content-Type"].StartsWith("application/json"), "json");
        Assert.True(WebhookSigner.Verify("topsecret", req.Headers["X-OpenVP-Timestamp"], req.Body, req.Headers["X-OpenVP-Signature"]), "the signature verifies with the shared secret");
        var j = JsonDocument.Parse(req.Body).RootElement;
        Assert.Equal("toggle_fb", j.GetProperty("signal").GetString()!, "payload signal"); Assert.Equal("digital", j.GetProperty("kind").GetString()!, "kind");
        Assert.Equal(1, j.GetProperty("slot").GetInt32(), "slot"); Assert.True(j.GetProperty("value").ValueKind is JsonValueKind.True or JsonValueKind.False, "digital value is a JSON bool");
        Assert.True(r.H.Webhooks.Status().Sent >= 1, "counted");

        // A 4xx is the receiver saying no: it is not retried.
        srv.Status = _ => 400;
        var before = srv.Requests.Count;
        r.Slot.ForcePulse("toggle_press");
        Assert.Eventually(() => r.H.Webhooks.Deliveries(1).Any(x => x.Status == 400), "a 400 is logged");
        Assert.Equal(1, r.H.Webhooks.Deliveries(1).First(x => x.Status == 400).Attempts, "and tried once");
        Assert.True(r.H.Webhooks.Status().Failed >= 1, "failure counted");
    }

    [Test]
    public static void Webhook_test_send_condition_rules_and_secrets_are_kept()
    {
        using var srv = new LocalHttp();
        using var r = new Rig();
        r.H.Store.UpdateSlot(1, s => s with { Webhooks = SlotIntegrations.CheckWebhooks(
            [new WebhookRule { Id = "t1", Name = "test me", Url = $"http://127.0.0.1:{srv.Port}/a", Signal = "toggle_fb", Secret = "k1" },
             new WebhookRule { Id = "c1", Name = "cond", Url = $"http://127.0.0.1:{srv.Port}/b", Expression = "toggle_fb=1 && volume_level>100" }], []) });
        var res = r.H.Webhooks.TestAsync(1, "t1").GetAwaiter().GetResult();
        Assert.True(res.Ok && res.Test && res.Event == "test", "test send works and is marked as a test");
        Assert.Equal("test", srv.Requests.First(x => x.Path == "/a").Headers["X-OpenVP-Event"], "event is test");
        Assert.Throws<KeyNotFoundException>(() => r.H.Webhooks.TestAsync(1, "nope").GetAwaiter().GetResult());
        Assert.Equal(0L, r.H.Webhooks.Status().Sent, "a test isn't counted as a real delivery");

        // The condition fires once, when it becomes true.
        r.Slot.ForceSignal("volume_level", 500);
        r.Slot.ForcePulse("toggle_press");                                   // fb = 1 now, volume 500: true
        Assert.Eventually(() => srv.Requests.Any(x => x.Path == "/b"), "the condition webhook fired");
        var body = JsonDocument.Parse(srv.Requests.First(x => x.Path == "/b").Body).RootElement;
        Assert.Equal("expression.true", body.GetProperty("event").GetString()!, "event"); Assert.Equal(500, body.GetProperty("values").GetProperty("volume_level").GetInt32(), "values of the signals it reads");
        var n = srv.Requests.Count(x => x.Path == "/b");
        r.Slot.ForceSignal("volume_level", 600);                             // still true: no second delivery
        Thread.Sleep(200);
        Assert.Equal(n, srv.Requests.Count(x => x.Path == "/b"), "no repeat while it stays true");

        // Editing the rule without sending the secret keeps it; an empty string removes it; a bad rule is refused.
        var cur = r.H.Store.Slot(1).Webhooks;
        var kept = SlotIntegrations.CheckWebhooks([cur[0] with { Secret = null, Name = "renamed" }], cur);
        Assert.Equal("k1", kept[0].Secret!, "secret kept"); Assert.True(SlotIntegrations.CheckWebhooks([cur[0] with { Secret = "" }], cur)[0].Secret == null, "secret cleared");
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckWebhooks([new WebhookRule { Url = "ftp://x", Signal = "a" }], []));
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckWebhooks([new WebhookRule { Url = "http://x", Signal = "a", Expression = "b=1" }], []));
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckWebhooks([new WebhookRule { Url = "http://x", Expression = "((" }], []));
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckWebhooks([new WebhookRule { Url = "http://x", Signal = "a", Headers = new() { ["Bad\r\nName"] = "x" } }], []));
    }

    [Test]
    public static void A_dead_webhook_endpoint_never_stalls_the_program_and_its_queue_is_bounded()
    {
        using var r = new Rig();
        // An address nothing listens on: every attempt fails at once and is retried after a pause.
        r.H.Webhooks.Backoff = [TimeSpan.FromSeconds(30)];
        r.H.Store.UpdateSlot(1, s => s with { Webhooks = SlotIntegrations.CheckWebhooks([new WebhookRule { Url = $"http://127.0.0.1:{FreePort()}/x", Signal = "volume_level" }], []) });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 1; i <= 20_000; i++) r.Slot.ForceSignal("volume_level", i);
        var done = new ManualResetEventSlim(); r.Slot.Dispatcher.Post(() => done.Set());
        Assert.True(done.Wait(20_000), "the slot thread finished all its work");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "and quickly");
        Assert.Eventually(() => r.H.Webhooks.Status().Dropped > 0, "the per-rule queue is bounded and drops the oldest", 10_000);
        Assert.True(r.H.Webhooks.Status().Pending <= WebhookService.QueueSize + 1, "pending never exceeds the queue");
    }

    // ---------------------------------------------------------------- incoming hooks

    [Test]
    public static void Incoming_hook_token_is_scoped_to_one_slot_and_its_allowed_signals_and_can_be_revoked()
    {
        using var r = new Rig();
        var all = r.H.Hooks.Create(1, "any", null);
        var scoped = r.H.Hooks.Create(1, "scoped", ["toggle_press", "volume_level"]);
        Assert.Equal(32, all.Token.Length, "192 random bits in base64url"); Assert.True(all.Token != scoped.Token, "tokens differ");
        HookResult Post(string token, string body, string ip = "10.0.0.1") => r.H.Hooks.Handle(token, Encoding.UTF8.GetBytes(body), ip);

        Assert.True(Post(scoped.Token, "{\"signal\":\"volume_level\",\"value\":777}").Ok, "analog set");
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) == 777, "applied");
        Assert.True(Post(scoped.Token, "{\"signal\":\"volume_level\",\"value\":\"25%\"}").Ok, "percentage as text");
        Assert.True(Post(scoped.Token, "{\"pulse\":\"toggle_press\"}").Ok, "pulse");
        Assert.Eventually(() => (bool)r.Value("toggle_fb"), "the pulse toggled the feedback");
        Assert.True(Post(scoped.Token, "{\"signal\":\"toggle_press\",\"value\":true}").Ok, "true is high");
        Assert.Equal(403, Post(scoped.Token, "{\"signal\":\"hello_text\",\"value\":\"x\"}").Status, "a signal outside the allow-list is refused");
        Assert.Equal(403, Post(scoped.Token, "{\"pulse\":\"toggle_fb\"}").Status, "so is a pulse of one");
        Assert.True(Post(all.Token, "{\"signal\":\"hello_text\",\"value\":\"from hook\"}").Ok, "a hook with no list may set any signal of its slot");
        Assert.Eventually(() => (string)r.Value("hello_text") == "from hook", "serial applied");

        Assert.Equal(404, Post(all.Token, "{\"signal\":\"nope\",\"value\":1}").Status, "unknown signal"); Assert.Equal(400, Post(all.Token, "{\"signal\":\"toggle_press\",\"value\":\"maybe\"}").Status, "bad value");
        Assert.Equal(400, Post(all.Token, "not json").Status, "bad JSON"); Assert.Equal(400, Post(all.Token, "{\"value\":1}").Status, "no signal"); Assert.Equal(400, Post(all.Token, "[1]").Status, "not an object");
        Assert.Equal(413, r.H.Hooks.Handle(all.Token, new byte[IncomingHooks.MaxBody + 1], "10.0.0.1").Status, "too large");

        // The hook is for its slot only: slot 2 is empty, and a hook can't name another slot in its body.
        var s2 = r.H.Hooks.Create(2, "empty slot", null);
        Assert.Equal(409, Post(s2.Token, "{\"signal\":\"toggle_press\",\"value\":1}").Status, "no program in that slot");
        Assert.True(Post(all.Token, "{\"slot\":2,\"signal\":\"toggle_press\",\"value\":1}").Ok, "a 'slot' field in the body is ignored: the hook's own slot is used");
        Assert.True(r.P.Slot(2).Runtime == null, "and slot 2 is untouched");

        // Switch off, then revoke.
        r.H.Hooks.Update(scoped.Id, null, null, false);
        Assert.Equal(404, Post(scoped.Token, "{\"pulse\":\"toggle_press\"}").Status, "a disabled hook looks like no hook");
        r.H.Hooks.Update(scoped.Id, null, null, true);
        Assert.True(Post(scoped.Token, "{\"pulse\":\"toggle_press\"}").Ok, "and works again");
        Assert.True(r.H.Hooks.Revoke(scoped.Id), "revoked");
        Assert.Equal(404, Post(scoped.Token, "{\"pulse\":\"toggle_press\"}").Status, "the token stops working at once");
        Assert.True(!r.H.Hooks.Revoke(scoped.Id), "revoking twice says no");

        // Saved owner-only, tokens survive a restart (a new instance reads the file).
        var file = Path.Combine(r.Dir, "integrations", "hooks.json");
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file), "hooks.json is owner-only");
        using var again = new IncomingHooks(r.P, r.H.Directory);
        Assert.True(again.List().Any(h => h.Token == all.Token), "tokens persist");
        Assert.True(r.H.Hooks.List().First(h => h.Id == all.Id).Count >= 3, "use is counted");
    }

    [Test]
    public static void Incoming_hook_is_rate_limited_per_token_and_wrong_tokens_per_address()
    {
        using var r = new Rig();
        long now = 0;
        using var hooks = new IncomingHooks(r.P, r.H.Directory, () => now) { RatePerSecond = 5, Burst = 10, BadTokensPerMinute = 4 };
        var h = hooks.Create(1, "rl", null);
        var ok = Encoding.UTF8.GetBytes("{\"signal\":\"volume_level\",\"value\":1}");
        var results = Enumerable.Range(0, 14).Select(_ => hooks.Handle(h.Token, ok, "10.0.0.2")).ToList();
        Assert.Equal(10, results.Count(x => x.Ok), "a burst of 10 goes through");
        Assert.Equal(4, results.Count(x => x.Status == 429), "then 429");
        Assert.True(results.Last().RetryAfterSeconds >= 1, "with a Retry-After");
        Assert.Equal(404, hooks.Handle("not-the-token", ok, "10.0.0.2").Status, "a wrong token is a 404, not a 429");
        now += 1000;                                                                // a second later, 5 more are allowed
        Assert.Equal(5, Enumerable.Range(0, 8).Count(_ => hooks.Handle(h.Token, ok, "10.0.0.2").Ok), "tokens refill at the set rate");
        // Another hook isn't affected by this one's limit.
        var h2 = hooks.Create(1, "other", null);
        Assert.True(hooks.Handle(h2.Token, ok, "10.0.0.2").Ok, "each token has its own bucket");

        // Guessing: after 4 wrong tokens the address is locked out, even for the right token.
        for (var i = 0; i < 4; i++) Assert.Equal(404, hooks.Handle("guess" + i, ok, "6.6.6.6").Status, "wrong token");
        var blocked = hooks.Handle(h2.Token, ok, "6.6.6.6");
        Assert.Equal(429, blocked.Status, "the guesser is refused"); Assert.True(blocked.RetryAfterSeconds > 0, "with a wait time");
        Assert.True(hooks.Handle(h2.Token, ok, "10.0.0.3").Ok, "other addresses still work");
        now += 61_000;
        Assert.True(hooks.Handle(h2.Token, ok, "6.6.6.6").Ok, "the lock-out ends after a minute");
    }

    // ---------------------------------------------------------------- OSC

    [Test]
    public static void Osc_listener_sets_chosen_signals_from_osc_messages()
    {
        using var r = new Rig();
        var port = FreePort(udp: true);
        r.H.Store.UpdateSlot(1, s => s with { Osc = [new OscSignal { Signal = "toggle_press", Send = false, Receive = true }, new OscSignal { Signal = "volume_level", Send = false, Receive = true },
            new OscSignal { Signal = "hello_text", Send = false, Receive = true }, new OscSignal { Signal = "toggle_fb", Send = false, Receive = false }] });
        r.H.Store.SetOsc(new OscSettings { ListenPort = port });
        Assert.True(r.H.Osc.Status().Listening && r.H.Osc.Status().Error == null, "listening: " + r.H.Osc.Status().Error);
        using var udp = new UdpClient();
        void Send(string addr, params object?[] args) { var b = OscCodec.Encode(new OscMessage(addr, args)); udp.Send(b, b.Length, new IPEndPoint(IPAddress.Loopback, port)); }
        Send("/openvp/slot01/volume_level", 1000);
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) == 1000, "int sets an analog signal");
        Send("/openvp/slot01/volume_level", 0.5f);
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) is 32767 or 32768, "a float 0..1 is a fraction of full scale");
        Send("/openvp/slot01/hello_text", "osc says hi");
        Assert.Eventually(() => (string)r.Value("hello_text") == "osc says hi", "string sets serial");
        Send("/openvp/slot01/toggle_press", 1); Send("/openvp/slot01/toggle_press", 0);
        Assert.Eventually(() => (bool)r.Value("toggle_fb"), "digital press");
        Send("/openvp/slot01/toggle_press/pulse");
        Assert.Eventually(() => !(bool)r.Value("toggle_fb"), "/pulse toggles it back");
        Send("/openvp/slot01/toggle_fb", 1);                       // not marked for receiving
        Send("/openvp/slot02/volume_level", 5);                    // other slot
        Send("/other/thing", 5);
        Thread.Sleep(200);
        Assert.True(!(bool)r.Value("toggle_fb"), "a signal not chosen for receiving is left alone");
        Assert.True(r.H.Osc.Status().Rejected >= 3, "refusals are counted");

        // Not listening when the port is 0, and the port is released.
        r.H.Store.SetOsc(new OscSettings { ListenPort = 0 });
        Assert.True(!r.H.Osc.Status().Listening, "off");
        using var again = new UdpClient(new IPEndPoint(IPAddress.Any, port));
    }

    [Test]
    public static void Osc_sender_sends_changes_of_chosen_signals_to_the_target()
    {
        using var r = new Rig();
        using var target = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var tport = ((IPEndPoint)target.Client.LocalEndPoint!).Port;
        r.H.Store.UpdateSlot(1, s => s with { Osc = [new OscSignal { Signal = "toggle_fb" }, new OscSignal { Signal = "volume_level" }, new OscSignal { Signal = "hello_text" }] });
        r.H.Store.SetOsc(new OscSettings { TargetHost = "127.0.0.1", TargetPort = tport });
        var got = new ConcurrentQueue<OscMessage>();
        _ = Task.Run(async () => { while (true) { try { var res = await target.ReceiveAsync(); foreach (var m in OscCodec.DecodePacket(res.Buffer)) got.Enqueue(m); } catch { return; } } });
        r.Slot.ForcePulse("toggle_press");
        r.Slot.ForceSignal("volume_level", 4321);
        r.H.Directory.SetFromText(1, "hello_text", "to osc");
        r.Slot.ForceSignal("source1_press", true);
        Assert.Eventually(() => got.Any(m => m.Address == "/openvp/slot01/hello_text") && got.Any(m => m.Address == "/openvp/slot01/volume_level"), "messages arrive");
        Assert.Equal(4321, (int)got.First(m => m.Address == "/openvp/slot01/volume_level").Args[0]!, "analog goes as an int");
        Assert.Equal("to osc", (string)got.First(m => m.Address == "/openvp/slot01/hello_text").Args[0]!, "serial goes as a string");
        Assert.True(got.Any(m => m.Address == "/openvp/slot01/toggle_fb" && (int)m.Args[0]! == 1), "digital goes as int 1");
        Assert.True(got.All(m => !m.Address.Contains("source1")), "signals that weren't chosen are not sent");
        Assert.True(r.H.Osc.Status().Sent >= 3, "counted");
    }

    // ---------------------------------------------------------------- Art-Net

    [Test]
    public static void ArtNet_sender_maps_analog_signals_to_dmx_channels_8_and_16_bit_at_a_steady_rate()
    {
        using var r = new Rig();
        using var node = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var nport = ((IPEndPoint)node.Client.LocalEndPoint!).Port;
        r.Slot.ForceSignal("volume_level", 65535);
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) == 65535, "volume at full");
        r.H.Store.UpdateSlot(1, s => s with { ArtNet = [new ArtNetChannel { Signal = "volume_level", Universe = 3, Channel = 1, Mode = "8" }, new ArtNetChannel { Signal = "toggle_fb", Universe = 3, Channel = 10, Mode = "8" }] });
        r.H.Store.SetArtNet(new ArtNetSettings { Enabled = true, Target = "127.0.0.1", Port = nport, RateHz = 40 });
        var packets = new ConcurrentQueue<ArtDmx>();
        _ = Task.Run(async () => { while (true) { try { var res = await node.ReceiveAsync(); if (ArtNetCodec.TryDecodeDmx(res.Buffer, out var d)) packets.Enqueue(d!); } catch { return; } } });
        Assert.Eventually(() => packets.Count >= 5, "a steady stream arrives even though nothing changes", 5000);
        var p = packets.Last();
        Assert.Equal(3, p.Universe, "universe"); Assert.Equal(255, (int)p.Data[0], "65535 is DMX 255 on channel 1"); Assert.Equal(0, (int)p.Data[9], "a low digital is 0 on channel 10");
        Assert.True(p.Data.Length >= 10 && p.Data.Length % 2 == 0, "data covers the highest channel, even length");
        var seqs = packets.Select(x => (int)x.Sequence).ToList();
        Assert.True(seqs.All(s => s != 0), "sequence is never 0"); Assert.True(seqs.Zip(seqs.Skip(1)).All(z => z.Second == z.First + 1 || (z.First == 255 && z.Second == 1)), "and counts up");
        r.Slot.ForceSignal("volume_level", 32768);
        Assert.Eventually(() => packets.Last().Data[0] == 128, "a change shows in the next packets");
        r.Slot.ForcePulse("toggle_press");
        Assert.Eventually(() => packets.Last().Data[9] == 255, "a digital is 255 when high");

        // 16-bit: coarse then fine byte.
        r.H.Store.UpdateSlot(1, s => s with { ArtNet = [new ArtNetChannel { Signal = "volume_level", Universe = 0x1234, Channel = 5, Mode = "16" }] });
        r.Slot.ForceSignal("volume_level", 0xABCD);
        Assert.Eventually(() => packets.Any(x => x.Universe == 0x1234 && x.Data[4] == 0xAB && x.Data[5] == 0xCD), "16-bit: coarse 0xAB, fine 0xCD");
        Assert.True(r.H.ArtNet.Status().PacketsSent > 5, "counted");

        // Turn it off: the packets stop.
        r.H.Store.SetArtNet(r.H.Store.Global.ArtNet with { Enabled = false });
        Thread.Sleep(150); var n = packets.Count; Thread.Sleep(250);
        Assert.Equal(n, packets.Count, "no packets once it is off");
    }

    [Test]
    public static void ArtNet_receiver_sets_signals_only_when_the_value_changes_and_mappings_are_checked()
    {
        using var r = new Rig();
        r.H.Store.UpdateSlot(1, s => s with { ArtNet = [new ArtNetChannel { Signal = "volume_level", Universe = 7, Channel = 2, Mode = "8", Send = false, Receive = true },
            new ArtNetChannel { Signal = "hello_text", Universe = 7, Channel = 9, Mode = "16", Send = false, Receive = true }] });
        // (hello_text is serial; only analog makes sense for DMX, so a wide mapping onto it sets its text to the number: we test the analog one)
        var data = new byte[16]; data[1] = 200;
        r.H.Store.SetArtNet(new ArtNetSettings { Enabled = true, Target = "127.0.0.1", Listen = false });
        r.H.ArtNet.HandlePacket(ArtNetCodec.EncodeDmx(7, 1, data));
        Assert.Eventually(() => Convert.ToInt32(r.Value("volume_level")) == 200 * 257, "channel 2 = 200 sets the analog to 200*257");
        r.H.ArtNet.HandlePacket(ArtNetCodec.EncodeDmx(8, 1, data));               // another universe
        data[1] = 10;
        r.H.ArtNet.HandlePacket(ArtNetCodec.EncodeDmx(8, 2, data));
        Thread.Sleep(100);
        Assert.Equal(200 * 257, Convert.ToInt32(r.Value("volume_level")), "another universe is ignored");
        r.H.ArtNet.HandlePacket(new byte[] { 1, 2, 3 });
        Assert.True(r.H.ArtNet.Status().Rejected >= 1 && r.H.ArtNet.Status().PacketsReceived >= 3, "bad packets rejected, good ones counted");

        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckArtNet([new ArtNetChannel { Signal = "a", Channel = 513 }]));
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckArtNet([new ArtNetChannel { Signal = "a", Channel = 512, Mode = "16" }]));
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckArtNet([new ArtNetChannel { Signal = "a", Universe = 40000 }]));
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckArtNet([new ArtNetChannel { Signal = "a", Channel = 5 }, new ArtNetChannel { Signal = "b", Channel = 5 }]));
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckArtNet([new ArtNetChannel { Signal = "a", Channel = 5, Mode = "16" }, new ArtNetChannel { Signal = "b", Channel = 6 }]));
        Assert.Throws<ArgumentException>(() => new ArtNetSettings { RateHz = 100 }.Validated());
    }

    // ---------------------------------------------------------------- files and snapshots

    [Test]
    public static void Slot_choices_are_saved_with_the_slot_and_travel_with_its_snapshots()
    {
        using var r = new Rig();
        r.H.Store.UpdateSlot(1, s => s with { Mqtt = [new MqttSignal { Signal = "toggle_fb" }], Osc = [new OscSignal { Signal = "volume_level", Receive = true }] });
        var file = Path.Combine(r.Slot.Directory, "devices", "integrations.json");
        Assert.True(File.Exists(file), "saved in the slot's devices folder");
        var snaps = new SlotSnapshots(r.Dir);
        var info = snaps.Take(1, "SampleProgram", null, "manual")!;
        r.H.Store.UpdateSlot(1, s => s with { Mqtt = [] });
        Assert.Equal(0, r.H.Store.Slot(1).Mqtt.Count, "changed after the snapshot");
        snaps.Restore(1, info.Id);
        r.H.Store.ReloadSlot(1);
        Assert.Equal("toggle_fb", r.H.Store.Slot(1).Mqtt.Single().Signal, "rolling back brings the choices back");
        Assert.True(r.H.Store.Slot(1).Osc.Single().Receive, "osc too");
        using var p2 = new Processor(r.Dir, 2);
        Assert.Equal(1, new IntegrationStore(p2).Slot(1).Mqtt.Count, "and they survive a restart");
        // The broker password, hooks and global settings are not in the slot's folder.
        r.H.Store.SetMqtt(new MqttSettings { Host = "h.test" }, "pw");
        Assert.True(!Directory.EnumerateFiles(r.Slot.Directory, "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("\"pw\"")), "no password under the slot folder");
        Assert.Throws<ArgumentException>(() => SlotIntegrations.CheckMqtt([new MqttSignal { Signal = " " }]));
        Assert.Throws<ArgumentException>(() => new MqttSettings { Enabled = true, Host = "" }.Validated());
        Assert.Throws<ArgumentException>(() => new MqttSettings { Host = "h", Port = 0 }.Validated());
        Assert.Throws<ArgumentException>(() => new MqttSettings { Host = "h", BaseTopic = "a/+/b" }.Validated());
        Assert.Throws<ArgumentException>(() => new OscSettings { ListenPort = 70000 }.Validated());
    }
}
