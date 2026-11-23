using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenVP.Core.Alerts;
using OpenVP.Core.Integrations;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>What each channel sends: Teams card, Slack blocks, ntfy, generic webhook (signed) and email; and the sender's retries and secrets.</summary>
public static class AlertChannelTests
{
    private static Notification N(string evt = "raised", AlertSeverity sev = AlertSeverity.Critical, string? url = "http://openvp.local:8080") =>
        new(evt, "id1", "slot.stuck:3", AlertKinds.SlotStuck, sev, "Slot 3 (Lobby) is stuck", "Slot thread has been inside CTimer OnPoll for 7 s <b>& more</b>", 3, "SLOT03", "Lobby.cpz",
            new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero), 2, [new("Running now", "CTimer OnPoll")], "testbox", null, url);

    private sealed class Capture : HttpMessageHandler
    {
        public readonly List<(Uri Url, string Body, Dictionary<string, string> Headers)> Requests = new();
        public Queue<HttpStatusCode> Answers = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
            var h = req.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value));
            lock (Requests) Requests.Add((req.RequestUri!, body, h));
            return new HttpResponseMessage(Answers.Count > 0 ? Answers.Dequeue() : HttpStatusCode.OK);
        }
    }

    private sealed class FakeEmail : IEmailSender
    {
        public readonly List<(SmtpEndpoint Server, EmailMessage Msg)> Sent = new();
        public Exception? Fail;
        public Task SendAsync(SmtpEndpoint server, EmailMessage message, CancellationToken ct)
        {
            if (Fail != null) throw Fail;
            Sent.Add((server, message));
            return Task.CompletedTask;
        }
    }

    private static ChannelDispatcher Make(Capture cap, Dictionary<string, string> secrets, IEmailSender? email = null) =>
        new(n => secrets.GetValueOrDefault(n), email, new AlertStream(), cap) { Backoff = [TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5)] };

    // ------------------------------------------------------------------ payload formats

    [Test]
    public static void Teams_gets_an_adaptive_card_in_a_message_envelope()
    {
        var j = AlertFormats.TeamsCard(N());
        Assert.Equal("message", j["type"]!.GetValue<string>(), "envelope type");
        var att = j["attachments"]![0]!;
        Assert.Equal("application/vnd.microsoft.card.adaptive", att["contentType"]!.GetValue<string>(), "adaptive card content type");
        var card = att["content"]!;
        Assert.Equal("AdaptiveCard", card["type"]!.GetValue<string>(), "card");
        Assert.Equal("attention", card["body"]![0]!["style"]!.GetValue<string>(), "critical is the red container");
        Assert.True(card["body"]![0]!["items"]![0]!["text"]!.GetValue<string>().Contains("Slot 3 (Lobby) is stuck"), "title in the header");
        var facts = card["body"]![2]!["facts"]!.AsArray().ToDictionary(f => f!["title"]!.GetValue<string>(), f => f!["value"]!.GetValue<string>());
        Assert.Equal("3", facts["Slot"], "slot fact"); Assert.Equal("Lobby.cpz", facts["Program"], "program fact"); Assert.Equal("CTimer OnPoll", facts["Running now"], "extra fact"); Assert.Equal("2", facts["Times"], "count fact");
        Assert.Equal("Action.OpenUrl", card["actions"]![0]!["type"]!.GetValue<string>(), "open console button");
        Assert.True(AlertFormats.TeamsCard(N(url: null))["attachments"]![0]!["content"]!["actions"] == null, "no button without a console address");
        Assert.Equal("warning", AlertFormats.TeamsCard(N(sev: AlertSeverity.Warning))["attachments"]![0]!["content"]!["body"]![0]!["style"]!.GetValue<string>(), "warning is yellow");
        Assert.Equal("good", AlertFormats.TeamsCard(N("resolved"))["attachments"]![0]!["content"]!["body"]![0]!["style"]!.GetValue<string>(), "resolved is green");
    }

    [Test]
    public static void Slack_gets_block_kit_blocks_in_a_coloured_attachment()
    {
        var j = AlertFormats.SlackBlocks(N());
        Assert.True(j["text"]!.GetValue<string>().Length > 0, "fallback text");
        var att = j["attachments"]![0]!;
        Assert.Equal("#e05252", att["color"]!.GetValue<string>(), "critical colour bar");
        var blocks = att["blocks"]!.AsArray();
        Assert.Equal("header", blocks[0]!["type"]!.GetValue<string>(), "header block");
        Assert.Equal("plain_text", blocks[0]!["text"]!["type"]!.GetValue<string>(), "header text is plain_text");
        Assert.Equal("section", blocks[1]!["type"]!.GetValue<string>(), "message section");
        Assert.True(blocks[1]!["text"]!["text"]!.GetValue<string>().Contains("&lt;b&gt;&amp; more&lt;/b&gt;"), "mrkdwn special characters are escaped");
        Assert.True(blocks[2]!["fields"]!.AsArray().Count >= 3, "facts become fields");
        Assert.Equal("context", blocks[3]!["type"]!.GetValue<string>(), "context line");
        Assert.Equal("actions", blocks[4]!["type"]!.GetValue<string>(), "button");
        Assert.Equal("#2f9e6e", AlertFormats.SlackBlocks(N("resolved"))["attachments"]![0]!["color"]!.GetValue<string>(), "resolved is green");
        Assert.True(AlertFormats.SlackBlocks(N() with { Title = new string('x', 400) })["attachments"]![0]!["blocks"]![0]!["text"]!["text"]!.GetValue<string>().Length <= 150, "header text is capped at 150");
    }

    [Test]
    public static void Ntfy_gets_json_with_priority_tags_and_a_click_link()
    {
        var j = AlertFormats.Ntfy(N(), "openvp");
        Assert.Equal("openvp", j["topic"]!.GetValue<string>(), "topic");
        Assert.Equal(5, j["priority"]!.GetValue<int>(), "critical is max priority");
        Assert.Equal("rotating_light", j["tags"]![0]!.GetValue<string>(), "emoji tag");
        Assert.Equal("Slot 3 (Lobby) is stuck", j["title"]!.GetValue<string>(), "title");
        Assert.Equal("http://openvp.local:8080", j["click"]!.GetValue<string>(), "click");
        Assert.Equal(4, AlertFormats.Ntfy(N(sev: AlertSeverity.Warning), "t")["priority"]!.GetValue<int>(), "warning is high");
        var r = AlertFormats.Ntfy(N("resolved"), "t");
        Assert.Equal(3, r["priority"]!.GetValue<int>(), "resolved is default"); Assert.Equal("white_check_mark", r["tags"]![0]!.GetValue<string>(), "tick tag"); Assert.True(r["title"]!.GetValue<string>().StartsWith("Resolved: "), "title says resolved");
    }

    [Test]
    public static void The_generic_webhook_body_has_the_event_the_alert_and_its_facts()
    {
        var j = AlertFormats.Webhook(N());
        Assert.Equal("alert.raised", j["event"]!.GetValue<string>(), "event");
        Assert.Equal("critical", j["severity"]!.GetValue<string>(), "severity");
        Assert.Equal("slot.stuck", j["kind"]!.GetValue<string>(), "kind"); Assert.Equal("slot.stuck:3", j["key"]!.GetValue<string>(), "key");
        Assert.Equal(3, j["slot"]!.GetValue<int>(), "slot"); Assert.Equal("SLOT03", j["room"]!.GetValue<string>(), "room"); Assert.Equal(2, j["count"]!.GetValue<int>(), "count");
        Assert.Equal("CTimer OnPoll", j["facts"]!["Running now"]!.GetValue<string>(), "facts");
        Assert.Equal("alert.resolved", AlertFormats.Webhook(N("resolved"))["event"]!.GetValue<string>(), "resolved event");
        Assert.Equal("alert.test", AlertFormats.Webhook(N("test"))["event"]!.GetValue<string>(), "test event");
    }

    [Test]
    public static void The_email_has_a_clear_subject_a_text_part_and_an_escaped_html_part()
    {
        var m = AlertFormats.Email(N(), "openvp@example.com", ["a@example.com", "b@example.com"]);
        Assert.Equal("[OpenVP] CRITICAL: Slot 3 (Lobby) is stuck", m.Subject, "subject");
        Assert.Equal(2, m.To.Count, "recipients");
        Assert.True(m.Text.Contains("Slot: 3") && m.Text.Contains("Open the console: http://openvp.local:8080"), "text part has the facts and link");
        Assert.True(m.Html.Contains("&lt;b&gt;&amp; more&lt;/b&gt;") && !m.Html.Contains("<b>&"), "html is escaped");
        Assert.True(m.Html.Contains("#e05252") && m.Html.Contains("Open console"), "html has the colour and the button");
        Assert.Equal("[OpenVP] RESOLVED: Slot 3 (Lobby) is stuck", AlertFormats.EmailSubject(N("resolved")), "resolved subject");
        Assert.Equal("[OpenVP] Test: Slot 3 (Lobby) is stuck", AlertFormats.EmailSubject(N("test")), "test subject");
        Assert.Equal("a@b.c|d@e.f|g@h.i", string.Join("|", AlertConfigValidator.Addresses("a@b.c, d@e.f; g@h.i")), "address list");
    }

    [Test]
    public static void The_MailKit_message_has_both_parts_and_the_right_addresses()
    {
        var msg = MailKitEmailSender.Build(AlertFormats.Email(N(), "OpenVP <openvp@example.com>", ["ops@example.com"]));
        Assert.Equal("openvp@example.com", msg.From.Mailboxes.Single().Address, "from");
        Assert.Equal("ops@example.com", msg.To.Mailboxes.Single().Address, "to");
        Assert.Equal("[OpenVP] CRITICAL: Slot 3 (Lobby) is stuck", msg.Subject, "subject");
        Assert.True(msg.TextBody.Contains("Slot thread has been inside"), "text body");
        Assert.True(msg.HtmlBody.Contains("<table"), "html body");
        Assert.True(msg.Body is MimeKit.Multipart { ContentType.MimeType: "multipart/alternative" }, "alternative parts");
    }

    // ------------------------------------------------------------------ sending

    [Test]
    public static void Teams_and_slack_post_to_the_address_in_the_secret_store()
    {
        var cap = new Capture();
        var secrets = new Dictionary<string, string> { ["alert.t.url"] = "http://teams.test/hook/abc", ["alert.s.url"] = "http://slack.test/services/xyz" };
        using var d = Make(cap, secrets);
        var t = d.SendAsync(new AlertChannel { Id = "t", Type = "teams" }, N(), false).GetAwaiter().GetResult();
        var s = d.SendAsync(new AlertChannel { Id = "s", Type = "slack" }, N(), false).GetAwaiter().GetResult();
        Assert.True(t.Ok && s.Ok, "both delivered");
        Assert.Equal("teams.test", cap.Requests[0].Url.Host, "teams address");
        Assert.True(JsonNode.Parse(cap.Requests[0].Body)!["attachments"] != null, "teams body is the card");
        Assert.Equal("slack.test", cap.Requests[1].Url.Host, "slack address");
        Assert.True(JsonNode.Parse(cap.Requests[1].Body)!["attachments"]![0]!["blocks"] != null, "slack body has blocks");
        var none = d.SendAsync(new AlertChannel { Id = "x", Type = "slack" }, N(), false).GetAwaiter().GetResult();
        Assert.True(!none.Ok && none.Note!.Contains("No webhook address"), "a missing address is reported");
    }

    [Test]
    public static void The_generic_webhook_is_signed_with_the_same_scheme_as_outgoing_webhooks()
    {
        var cap = new Capture();
        using var d = Make(cap, new() { ["alert.w.secret"] = "s3cret" });
        var o = d.SendAsync(new AlertChannel { Id = "w", Type = "webhook", Url = "http://hook.test/in" }, N(), false).GetAwaiter().GetResult();
        Assert.True(o.Ok, "delivered");
        var r = cap.Requests.Single();
        var ts = r.Headers["X-OpenVP-Timestamp"];
        Assert.True(WebhookSigner.Verify("s3cret", ts, System.Text.Encoding.UTF8.GetBytes(r.Body), r.Headers["X-OpenVP-Signature"]), "the signature verifies");
        Assert.Equal("alert.raised", r.Headers["X-OpenVP-Event"], "event header");
    }

    [Test]
    public static void Ntfy_posts_json_to_the_server_root_with_the_bearer_token()
    {
        var cap = new Capture();
        using var d = Make(cap, new() { ["alert.n.token"] = "tk_abc" });
        d.SendAsync(new AlertChannel { Id = "n", Type = "ntfy", Url = "http://ntfy.test:8081", Topic = "openvp" }, N(), false).GetAwaiter().GetResult();
        var r = cap.Requests.Single();
        Assert.Equal("/", r.Url.AbsolutePath, "the JSON API is the server root");
        Assert.Equal("Bearer tk_abc", r.Headers["Authorization"], "token");
        Assert.Equal("openvp", JsonNode.Parse(r.Body)!["topic"]!.GetValue<string>(), "topic in the body");
    }

    [Test]
    public static void Failed_sends_are_retried_for_server_errors_and_not_for_client_errors()
    {
        var cap = new Capture();
        cap.Answers = new Queue<HttpStatusCode>([HttpStatusCode.InternalServerError, HttpStatusCode.OK]);
        using var d = Make(cap, new());
        var ch = new AlertChannel { Id = "w", Type = "webhook", Url = "http://hook.test/in" };
        var a = d.SendAsync(ch, N(), true).GetAwaiter().GetResult();
        Assert.True(a.Ok && a.Attempts == 2, "a 500 is retried and then succeeds");
        cap.Answers = new Queue<HttpStatusCode>([HttpStatusCode.BadRequest, HttpStatusCode.OK]);
        var b = d.SendAsync(ch, N(), true).GetAwaiter().GetResult();
        Assert.True(!b.Ok && b.Attempts == 1 && b.Note!.Contains("400"), "a 400 is final");
        cap.Answers = new Queue<HttpStatusCode>([HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK]);
        var c = d.SendAsync(ch, N(), false).GetAwaiter().GetResult();
        Assert.True(!c.Ok && c.Attempts == 1, "a test is tried once");
    }

    [Test]
    public static void Email_goes_through_the_sender_with_the_stored_password_and_reports_failures()
    {
        var mail = new FakeEmail();
        using var d = Make(new Capture(), new() { ["alert.m.password"] = "pw" }, mail);
        var ch = new AlertChannel { Id = "m", Type = "email", Host = "smtp.test", Port = 465, Security = "ssl", Username = "u", From = "openvp@x.test", To = "a@x.test, b@x.test" };
        var o = d.SendAsync(ch, N("resolved"), false).GetAwaiter().GetResult();
        Assert.True(o.Ok, "sent");
        var (srv, msg) = mail.Sent.Single();
        Assert.Equal("smtp.test", srv.Host, "host"); Assert.Equal(465, srv.Port, "port"); Assert.Equal("ssl", srv.Security, "security"); Assert.Equal("pw", srv.Password, "password from the secret store");
        Assert.Equal(2, msg.To.Count, "both recipients"); Assert.True(msg.Subject.StartsWith("[OpenVP] RESOLVED"), "subject");
        mail.Fail = new InvalidOperationException("535 authentication failed");
        var bad = d.SendAsync(ch, N(), false).GetAwaiter().GetResult();
        Assert.True(!bad.Ok && bad.Note!.Contains("535"), "the server's reason is kept");
    }

    [Test]
    public static void The_desktop_channel_publishes_to_whoever_is_listening()
    {
        var stream = new AlertStream();
        using var d = new ChannelDispatcher(_ => null, null, stream);
        var none = d.SendAsync(new AlertChannel { Id = "desktop", Type = "desktop" }, N(), false).GetAwaiter().GetResult();
        Assert.True(none.Ok && none.Note!.Contains("Nobody was listening"), "says so when nobody listens");
        var sub = stream.Subscribe()!.Value;
        var one = d.SendAsync(new AlertChannel { Id = "desktop", Type = "desktop" }, N(), false).GetAwaiter().GetResult();
        Assert.True(one.Ok && one.Note == "1 listener", "one listener");
        Assert.True(sub.Reader.TryRead(out var frame), "the frame arrived");
        Assert.True(frame!.StartsWith("event: alert\ndata: {") && frame.Contains("\"severity\":\"critical\"") && frame.EndsWith("\n\n"), "an SSE frame: " + frame);
        sub.Registration.Dispose();
        Assert.Equal(0, stream.Listeners, "unsubscribed");
    }

    [Test]
    public static void A_slow_channel_does_not_block_the_others_and_a_full_queue_drops_the_oldest()
    {
        var gate = new ManualResetEventSlim(false);
        var cap = new BlockingHandler(gate);
        using var d = new ChannelDispatcher(_ => null, null, new AlertStream(), cap) { Timeout = TimeSpan.FromSeconds(30), Backoff = [] };
        var slow = new AlertChannel { Id = "slow", Type = "webhook", Url = "http://slow.test/" };
        var done = 0; var dropped = 0;
        for (var i = 0; i < ChannelDispatcher.QueueSize + 10; i++)
            d.Enqueue(slow, N(), true, o => { if (o.Note != null && o.Note.StartsWith("Dropped")) Interlocked.Increment(ref dropped); else Interlocked.Increment(ref done); });
        Assert.Eventually(() => Volatile.Read(ref dropped) >= 9, "the oldest are dropped once the queue is full");
        var other = new TaskCompletionSource<bool>();
        d.Enqueue(new AlertChannel { Id = "fast", Type = "desktop" }, N(), true, o => other.TrySetResult(o.Ok));
        Assert.True(other.Task.Wait(2000) && other.Task.Result, "another channel is not held up by the slow one");
        gate.Set();
    }

    private sealed class BlockingHandler(ManualResetEventSlim gate) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            gate.Wait(ct);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
