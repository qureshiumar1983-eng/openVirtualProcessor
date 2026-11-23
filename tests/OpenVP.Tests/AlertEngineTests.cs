using OpenVP.Core.Alerts;

namespace OpenVP.Tests;

/// <summary>A dispatcher that remembers what it was asked to send and answers at once.</summary>
public sealed class FakeDispatcher : IAlertDispatcher
{
    public readonly List<(string Channel, Notification Note, bool Retry)> Sent = new();
    public Func<AlertChannel, DeliveryOutcome> Answer = _ => new DeliveryOutcome(true, 1, 1, null);
    public void Enqueue(AlertChannel channel, Notification n, bool retry, Action<DeliveryOutcome> done)
    {
        lock (Sent) Sent.Add((channel.Id, n, retry));
        done(Answer(channel));
    }
    public int Count { get { lock (Sent) return Sent.Count; } }
    public List<string> Events(string channel) { lock (Sent) return Sent.Where(s => s.Channel == channel).Select(s => s.Note.Event).ToList(); }
}

/// <summary>The alert engine: de-duplication, rate limiting, resolving, quiet hours, rules, acknowledge, mute, history.</summary>
public static class AlertEngineTests
{
    private static DateTimeOffset T0 = new(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public DateTimeOffset Now = T0;
        public FakeDispatcher D = new();
        public AlertConfig Cfg;
        public AlertEngine E;
        public Rig(Action<AlertConfig>? _ = null, AlertConfig? cfg = null, string? file = null)
        {
            Cfg = cfg ?? Config(new AlertRule { Id = "r", Name = "all", MinSeverity = AlertSeverity.Info, Channels = ["a"] });
            E = new AlertEngine(() => Cfg, D, file, () => Now, TimeZoneInfo.Utc, n => ("ROOM" + n, "Prog" + n), "testbox");
        }
        public void Minutes(int m) => Now += TimeSpan.FromMinutes(m);
    }

    public static AlertConfig Config(params AlertRule[] rules) => new()
    {
        Channels = [new AlertChannel { Id = "a", Name = "A", Type = "webhook", Url = "http://x/" }, new AlertChannel { Id = "b", Name = "B", Type = "webhook", Url = "http://y/" }],
        Rules = rules.ToList()
    };

    private static AlertCandidate C(string key = "slot.stuck:1", AlertSeverity sev = AlertSeverity.Warning, int? slot = 1, string kind = AlertKinds.SlotStuck, bool transient = false) =>
        new(kind, key, sev, "Slot 1 is stuck", "stuck in OnPoll", slot, transient);

    [Test]
    public static void The_same_alert_is_sent_once_per_repeat_window()
    {
        var r = new Rig();
        r.E.Raise(C());
        Assert.Equal(1, r.D.Count, "first raise is sent");
        r.Minutes(5); r.E.Raise(C());
        Assert.Equal(1, r.D.Count, "the same alert 5 min later is not sent again (repeat is 15 min)");
        var view = r.E.Active().Single();
        Assert.Equal(2, view.Count, "but it is counted");
        Assert.True(view.Deliveries.Any(d => d.Status == "suppressed" && d.Note!.Contains("at most once per 15 min")), "the history says why");
        r.Minutes(11); r.E.Raise(C());
        Assert.Equal(2, r.D.Count, "after 16 min it is sent again");
        Assert.Equal("reminder", r.D.Sent[1].Note.Event, "as a reminder");
        Assert.Equal(1, r.E.Active().Count, "still one alert");
    }

    [Test]
    public static void A_rule_can_set_its_own_repeat_time_and_zero_means_every_time()
    {
        var r = new Rig(cfg: Config(new AlertRule { Id = "r", Name = "x", MinSeverity = AlertSeverity.Info, Channels = ["a"], RepeatMinutes = 0 }));
        r.E.Raise(C()); r.E.Raise(C()); r.E.Raise(C());
        Assert.Equal(3, r.D.Count, "repeat 0 sends every time");
    }

    [Test]
    public static void Resolving_sends_a_resolved_message_to_the_channels_that_heard_about_it()
    {
        var r = new Rig();
        r.E.Raise(C());
        r.Minutes(2);
        Assert.True(r.E.Resolve("slot.stuck:1", "It is responding again."), "resolved");
        Assert.Equal("raised,resolved", string.Join(",", r.D.Events("a")), "raised then resolved");
        Assert.Equal("It is responding again.", r.D.Sent[1].Note.Text, "the resolve message is used");
        var v = r.E.History().Single();
        Assert.Equal("resolved", v.State, "state");
        Assert.Equal(0, r.E.Active().Count, "nothing active");
        Assert.True(!r.E.Resolve("slot.stuck:1"), "resolving twice does nothing");
        Assert.Equal(2, r.D.Count, "no extra message");
    }

    [Test]
    public static void Nothing_is_resolved_if_the_raise_was_never_sent()
    {
        var quiet = new QuietHours { Enabled = true, Start = "11:00", End = "13:00", AllowCritical = false };
        var r = new Rig(cfg: Config(new AlertRule { Id = "r", Name = "q", MinSeverity = AlertSeverity.Info, Channels = ["a"], Quiet = quiet }));
        r.E.Raise(C());
        Assert.Equal(0, r.D.Count, "quiet hours hold it back");
        Assert.True(r.E.History().Single().Deliveries.Any(d => d.Status == "suppressed" && d.Note == "quiet hours"), "recorded as suppressed");
        r.E.Resolve("slot.stuck:1");
        Assert.Equal(0, r.D.Count, "and no 'resolved' for something nobody was told about");
    }

    [Test]
    public static void Quiet_hours_wrap_midnight_and_critical_can_still_go_out()
    {
        var q = new QuietHours { Enabled = true, Start = "22:00", End = "07:00" };
        Assert.True(q.IsQuiet(new DateTime(2026, 1, 1, 23, 0, 0)), "23:00 is quiet");
        Assert.True(q.IsQuiet(new DateTime(2026, 1, 1, 6, 59, 0)), "06:59 is quiet");
        Assert.True(!q.IsQuiet(new DateTime(2026, 1, 1, 7, 0, 0)), "07:00 is not");
        Assert.True(!q.IsQuiet(new DateTime(2026, 1, 1, 12, 0, 0)), "noon is not");
        Assert.True(!(q with { Enabled = false }).IsQuiet(new DateTime(2026, 1, 1, 23, 0, 0)), "disabled is never quiet");
        var day = new QuietHours { Enabled = true, Start = "09:00", End = "17:00" };
        Assert.True(day.IsQuiet(new DateTime(2026, 1, 1, 12, 0, 0)) && !day.IsQuiet(new DateTime(2026, 1, 1, 18, 0, 0)), "a same-day period");

        var rule = new AlertRule { Id = "r", Name = "q", MinSeverity = AlertSeverity.Info, Channels = ["a"], Quiet = new QuietHours { Enabled = true, Start = "11:00", End = "13:00", AllowCritical = true } };
        var r = new Rig(cfg: Config(rule));
        r.E.Raise(C("w", AlertSeverity.Warning));
        r.E.Raise(C("c", AlertSeverity.Critical));
        Assert.Equal(1, r.D.Count, "only the critical one is sent");
        Assert.Equal(AlertSeverity.Critical, r.D.Sent[0].Note.Severity, "it is the critical one");
    }

    [Test]
    public static void Rules_choose_by_kind_slot_and_minimum_severity_and_route_to_their_channels()
    {
        var r = new Rig(cfg: Config(
            new AlertRule { Id = "1", Name = "stuck to A", Kinds = [AlertKinds.SlotStuck], MinSeverity = AlertSeverity.Warning, Channels = ["a"] },
            new AlertRule { Id = "2", Name = "slot 2 criticals to B", Slots = [2], MinSeverity = AlertSeverity.Critical, Channels = ["b"] }));
        r.E.Raise(C("k1", AlertSeverity.Warning, 1));                                   // stuck, slot 1, warning -> rule 1
        r.E.Raise(C("k2", AlertSeverity.Info, 1));                                      // below minimum
        r.E.Raise(C("k3", AlertSeverity.Critical, 2, AlertKinds.SlotFault));            // not stuck, slot 2 critical -> rule 2
        r.E.Raise(C("k4", AlertSeverity.Warning, 2, AlertKinds.SlotFault));             // slot 2 but only warning, not stuck
        r.E.Raise(C("k5", AlertSeverity.Critical, null, AlertKinds.DiskLow));           // no slot: rule 2 needs slot 2
        Assert.Equal(1, r.D.Events("a").Count, "A heard about k1 only");
        Assert.Equal(1, r.D.Events("b").Count, "B heard about k3 only");
        Assert.Equal(2, r.D.Count, "nothing else was sent");
        Assert.Equal("ROOM2", r.D.Sent.Single(s => s.Channel == "b").Note.Room, "the notification carries the slot's room and program");
    }

    [Test]
    public static void A_disabled_rule_or_channel_sends_nothing_and_two_rules_dont_double_send()
    {
        var cfg = Config(new AlertRule { Id = "1", Name = "off", Enabled = false, MinSeverity = AlertSeverity.Info, Channels = ["a"] },
                         new AlertRule { Id = "2", Name = "x", MinSeverity = AlertSeverity.Info, Channels = ["b"] },
                         new AlertRule { Id = "3", Name = "y", MinSeverity = AlertSeverity.Info, Channels = ["b"] });
        cfg.Channels[1] = cfg.Channels[1] with { Enabled = true };
        var r = new Rig(cfg: cfg);
        r.E.Raise(C());
        Assert.Equal(0, r.D.Events("a").Count, "the disabled rule sent nothing");
        Assert.Equal(1, r.D.Events("b").Count, "two rules to one channel send once");
        cfg.Channels[1] = cfg.Channels[1] with { Enabled = false };
        r.E.Raise(C("other"));
        Assert.Equal(1, r.D.Events("b").Count, "a disabled channel is skipped");
    }

    [Test]
    public static void A_flapping_alert_cannot_get_around_the_rate_limit_by_resolving()
    {
        var r = new Rig();
        r.E.Raise(C());           // sent
        r.Minutes(1); r.E.Resolve("slot.stuck:1");   // resolved (sent)
        r.Minutes(1); r.E.Raise(C());                // 2 min after the first: suppressed
        Assert.Equal(2, r.D.Count, "raised + resolved only");
        r.E.Resolve("slot.stuck:1");
        Assert.Equal(2, r.D.Count, "and the resolve for the suppressed one isn't sent either");
        r.Minutes(20); r.E.Raise(C());
        Assert.Equal(3, r.D.Count, "later it goes out again");
    }

    [Test]
    public static void Getting_worse_is_sent_at_once_even_inside_the_repeat_window()
    {
        var r = new Rig();
        r.E.Raise(C("t", AlertSeverity.Warning));
        r.Minutes(1); r.E.Raise(C("t", AlertSeverity.Critical));
        Assert.Equal(2, r.D.Count, "the escalation is sent");
        Assert.Equal(AlertSeverity.Critical, r.D.Sent[1].Note.Severity, "as critical");
        Assert.Equal(AlertSeverity.Critical, r.E.Active().Single().Severity, "the record is critical now");
        Assert.Equal(1, r.E.History().Count, "still one alert");
    }

    [Test]
    public static void Acknowledging_stops_reminders_but_not_the_resolved_message()
    {
        var r = new Rig();
        var id = r.E.Raise(C());
        Assert.True(r.E.Acknowledge(id, "alice"), "acknowledged");
        r.Minutes(20); r.E.Raise(C());
        Assert.Equal(1, r.D.Count, "no reminder for an acknowledged alert");
        Assert.True(r.E.History().Single().Deliveries.Any(d => d.Note == "acknowledged"), "the history says so");
        Assert.Equal("alice", r.E.Get(id)!.AckBy, "who");
        Assert.Equal(1, r.E.Status().Acknowledged, "counted as acknowledged");
        Assert.Equal("green", r.E.Status().Color, "and it no longer makes the tray amber");
        r.E.Resolve("slot.stuck:1");
        Assert.Equal(2, r.D.Count, "the resolved message still goes");
        Assert.True(!r.E.Acknowledge("nope", "x"), "unknown id");
    }

    [Test]
    public static void Muting_silences_an_alert_and_later_ones_with_the_same_key_until_it_runs_out()
    {
        var r = new Rig();
        var id = r.E.Raise(C());
        Assert.True(r.E.Mute(id, TimeSpan.FromHours(1)), "muted");
        Assert.Equal(1, r.E.Status().Muted, "counted");
        r.E.Resolve("slot.stuck:1");
        r.Minutes(20); r.E.Raise(C());
        Assert.Equal(1, r.D.Count, "a new alert with the same key is muted too (only the first went out)");
        Assert.True(r.E.Active().Single().MutedUntil != null, "the view says until when");
        r.Minutes(50); r.E.Resolve("slot.stuck:1"); r.Minutes(1); r.E.Raise(C());
        Assert.Equal(2, r.D.Count, "after the hour it is sent again");
        var id2 = r.E.Active().Single().Id;
        r.E.Mute(id2, null); Assert.True(r.E.Unmute(id2), "unmuted"); Assert.True(!r.E.Unmute(id2), "already unmuted");
    }

    [Test]
    public static void One_off_events_are_grouped_inside_the_repeat_window()
    {
        var r = new Rig();
        r.E.Raise(C("watch:1:w", AlertSeverity.Warning, 1, AlertKinds.Watch, transient: true));
        r.Minutes(3); r.E.Raise(C("watch:1:w", AlertSeverity.Warning, 1, AlertKinds.Watch, transient: true));
        Assert.Equal(1, r.D.Count, "one message");
        Assert.Equal(1, r.E.History().Count, "one history entry");
        Assert.Equal(2, r.E.History()[0].Count, "counted twice");
        Assert.Equal("event", r.E.History()[0].State, "it is an event, not an active alert");
        Assert.Equal(0, r.E.Active().Count, "never shown as active");
        r.Minutes(20); r.E.Raise(C("watch:1:w", AlertSeverity.Warning, 1, AlertKinds.Watch, transient: true));
        Assert.Equal(2, r.D.Count, "and again after the window");
        Assert.Equal(2, r.E.History().Count, "as a new entry");
    }

    [Test]
    public static void Delivery_results_are_recorded_and_a_failure_is_visible()
    {
        var r = new Rig();
        r.D.Answer = _ => new DeliveryOutcome(false, 4, 12.5, "The server answered 500 Internal Server Error");
        r.E.Raise(C());
        var d = r.E.History().Single().Deliveries.Single();
        Assert.Equal("failed", d.Status, "failed");
        Assert.Equal(4, d.Attempts, "attempts");
        Assert.True(d.Note!.Contains("500"), "reason");
    }

    [Test]
    public static void A_test_goes_to_one_channel_whatever_the_rules_say_and_is_not_retried()
    {
        var r = new Rig(cfg: Config());   // no rules at all
        var d = r.E.TestAsync("b").GetAwaiter().GetResult();
        Assert.Equal("sent", d.Status, "delivered");
        Assert.Equal("test", d.Event, "marked as a test");
        Assert.Equal(1, r.D.Count, "one send");
        Assert.True(!r.D.Sent[0].Retry, "tried once");
        Assert.Equal("b", r.D.Sent[0].Channel, "to that channel");
        Assert.Equal(1, r.E.History().Count, "it appears in the history");
        Assert.Equal(0, r.E.Between(T0.AddHours(-1), T0.AddHours(1)).Count, "but not in report counts");
        Assert.Throws<KeyNotFoundException>(() => r.E.TestAsync("zzz").GetAwaiter().GetResult());
    }

    [Test]
    public static void History_survives_a_restart_and_what_was_active_is_ended()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-alerts").FullName;
        var file = Path.Combine(dir, "state.json");
        string id;
        {
            var r = new Rig(file: file);
            id = r.E.Raise(C());
            r.E.Mute(id, TimeSpan.FromHours(2));
            r.E.Dispose();
        }
        var again = new Rig(file: file);
        var v = again.E.Get(id);
        Assert.True(v != null, "the alert is still in the history");
        Assert.Equal("resolved", v!.State, "an alert that was active when OpenVP stopped starts out ended (sources re-check)");
        Assert.Equal(0, again.E.Active().Count, "nothing active");
        Assert.True(again.E.Mutes().ContainsKey("slot.stuck:1"), "the mute is kept");
        Assert.Equal("sent", v.Deliveries.Single().Status, "with its delivery status");
        Directory.Delete(dir, true);
    }

    [Test]
    public static void The_status_counts_unacknowledged_alerts_by_severity()
    {
        var r = new Rig();
        Assert.Equal("green", r.E.Status().Color, "empty is green");
        r.E.Raise(C("a", AlertSeverity.Warning));
        Assert.Equal("amber", r.E.Status().Color, "a warning is amber");
        r.E.Raise(C("b", AlertSeverity.Critical));
        var s = r.E.Status();
        Assert.Equal("red", s.Color, "a critical is red"); Assert.Equal(1, s.Critical, "critical"); Assert.Equal(1, s.Warning, "warning");
        r.E.Resolve("b");
        Assert.Equal("amber", r.E.Status().Color, "back to amber");
    }

    [Test]
    public static void The_history_is_capped()
    {
        var r = new Rig();
        for (var i = 0; i < AlertEngine.MaxHistory + 20; i++) r.E.Raise(C("k" + i));
        Assert.Equal(AlertEngine.MaxHistory, r.E.History(10_000).Count, "capped at the maximum");
    }

    [Test]
    public static void Configuration_is_checked_with_plain_messages_and_defaults_are_sane()
    {
        var d = AlertConfig.Default();
        Assert.Equal("desktop", d.Channels.Single().Type, "the desktop channel exists out of the box");
        Assert.Equal("desktop", d.Rules.Single().Channels.Single(), "and a rule sends warnings to it");
        var v = AlertConfigValidator.Validate(d);
        Assert.Equal(15, v.Settings.RepeatMinutes, "default repeat");
        var e = Assert.Throws<ArgumentException>(() => AlertConfigValidator.Validate(d with { Rules = [], Channels = [new AlertChannel { Id = "x", Type = "email", Host = "h", From = "a@b", To = "" }] }));
        Assert.True(e.Message.Contains("To address"), "email needs a recipient: " + e.Message);
        Assert.Throws<ArgumentException>(() => AlertConfigValidator.Validate(d with { Rules = [], Channels = [new AlertChannel { Id = "x", Type = "carrier-pigeon" }] }));
        Assert.Throws<ArgumentException>(() => AlertConfigValidator.Validate(d with { Rules = [new AlertRule { Id = "r", Channels = ["nope"] }] }));
        Assert.Throws<ArgumentException>(() => AlertConfigValidator.Validate(d with { Rules = [new AlertRule { Id = "r", Kinds = ["made.up"] }] }));
        Assert.Throws<ArgumentException>(() => AlertConfigValidator.Validate(d with { Rules = [new AlertRule { Id = "r", Quiet = new QuietHours { Enabled = true, Start = "25:00" } }] }));
        Assert.Throws<ArgumentException>(() => AlertConfigValidator.Validate(d with { Report = new ReportSchedule { Time = "8am" } }));
        var ok = AlertConfigValidator.Validate(d with { Rules = [], Channels = [new AlertChannel { Id = "n", Type = "ntfy", Topic = "openvp-alerts" }] });
        Assert.Equal("https://ntfy.sh", ok.Channels[0].Url, "ntfy defaults to ntfy.sh");
    }
}
