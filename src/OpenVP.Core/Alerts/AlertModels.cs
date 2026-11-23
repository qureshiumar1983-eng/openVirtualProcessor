using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenVP.Core.Alerts;

public enum AlertSeverity { Info = 0, Warning = 1, Critical = 2 }

/// <summary>The kinds of event the alert engine knows about. The id is what a rule lists and what the history shows.</summary>
public static class AlertKinds
{
    public const string SlotFault = "slot.fault", SlotStuck = "slot.stuck", SlotBacklog = "slot.backlog", CrashRestart = "crash.restart",
        CrashGiveUp = "crash.giveup", DeviceOffline = "device.offline", Oscillation = "oscillation", Watch = "watch",
        RestoreFailed = "restore.failed", TunnelStopped = "tunnel.stopped", DiskLow = "disk.low", Threshold = "threshold", BackupFailed = "backup.failed", ScheduleFailed = "schedule.failed", Test = "test";

    public static readonly (string Id, string Label)[] All =
    [
        (SlotFault, "A program faulted or is in Error"),
        (SlotStuck, "A slot is stuck inside one event"),
        (SlotBacklog, "Events are backing up for a slot"),
        (CrashRestart, "The crash policy restarted a program"),
        (CrashGiveUp, "The crash policy gave up on a program"),
        (DeviceOffline, "A panel or device was offline too long"),
        (Oscillation, "A signal is racing or oscillating"),
        (Watch, "A watch expression fired"),
        (RestoreFailed, "A program failed to come back at start-up"),
        (TunnelStopped, "The internet tunnel stopped by itself"),
        (DiskLow, "Disk space is low in the data folder"),
        (Threshold, "A metric threshold was crossed"),
        (BackupFailed, "A backup failed"),
        (ScheduleFailed, "A scheduled action failed"),
    ];

    public static bool IsKnown(string id) => id == Test || All.Any(k => k.Id == id);
}

/// <summary>What an event source hands the engine.</summary>
/// <param name="Key">Identifies "the same alert": the same key is de-duplicated and resolved together, e.g. <c>slot.stuck:3</c>.</param>
/// <param name="Transient">A one-off event (a watch firing) that has no "resolved" message; it is grouped with the last one of its key.</param>
public sealed record AlertCandidate(string Kind, string Key, AlertSeverity Severity, string Title, string Message, int? Slot = null, bool Transient = false,
    IReadOnlyList<KeyValuePair<string, string>>? Facts = null);

/// <summary>One try at sending to one channel, for the history.</summary>
public sealed record AlertDeliveryView(DateTimeOffset Time, string ChannelId, string ChannelName, string Event, string Status, string? Note, int Attempts, double Ms);

/// <summary>An alert as the API and the history show it.</summary>
public sealed record AlertView(string Id, string Key, string Kind, int? Slot, AlertSeverity Severity, string Title, string Message,
    DateTimeOffset FirstTime, DateTimeOffset LastTime, int Count, string State, DateTimeOffset? ResolvedTime,
    string? AckBy, DateTimeOffset? AckTime, DateTimeOffset? MutedUntil, IReadOnlyList<AlertDeliveryView> Deliveries);

/// <summary>What a channel is asked to send: an alert raised, resolved or repeated, a test, or a report.</summary>
public sealed record Notification(string Event, string Id, string Key, string Kind, AlertSeverity Severity, string Title, string Text, int? Slot,
    string? Room, string? Program, DateTimeOffset Time, int Count, IReadOnlyList<KeyValuePair<string, string>> Facts, string Host,
    string? Html = null, string? ConsoleUrl = null)
{
    /// <summary>Raised, reminder, resolved, test or report.</summary>
    public bool IsResolved => Event == "resolved";
}

/// <summary>Where an alert can go. Secrets (SMTP password, ntfy token, signing secret, Teams and Slack addresses) live in the <see cref="Integrations.SecretStore"/>, never here.</summary>
public sealed record AlertChannel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary><c>email</c>, <c>teams</c>, <c>slack</c>, <c>ntfy</c>, <c>webhook</c> or <c>desktop</c>.</summary>
    public string Type { get; init; } = "";
    public bool Enabled { get; init; } = true;
    /// <summary>Webhook: the address. ntfy: the server (default https://ntfy.sh). Teams and Slack keep their address in the secret store.</summary>
    public string? Url { get; init; }
    public string? Topic { get; init; }
    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    /// <summary><c>starttls</c> (default), <c>ssl</c> (implicit TLS, usually port 465) or <c>none</c>.</summary>
    public string Security { get; init; } = "starttls";
    public string? Username { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }

    public static readonly string[] Types = ["email", "teams", "slack", "ntfy", "webhook", "desktop"];
    public string SecretName(string what) => $"alert.{Id}.{what}";
}

public sealed record QuietHours
{
    public bool Enabled { get; init; }
    public string Start { get; init; } = "22:00";
    public string End { get; init; } = "07:00";
    /// <summary>Critical alerts still go out during quiet hours.</summary>
    public bool AllowCritical { get; init; } = true;

    public static bool TryTime(string? s, out TimeSpan t)
    {
        t = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h is < 0 or > 23 || m is < 0 or > 59) return false;
        t = new TimeSpan(h, m, 0);
        return true;
    }

    /// <summary>True if <paramref name="local"/> falls inside the quiet period. It may wrap midnight (22:00 to 07:00). Equal start and end is never quiet.</summary>
    public bool IsQuiet(DateTime local)
    {
        if (!Enabled || !TryTime(Start, out var s) || !TryTime(End, out var e) || s == e) return false;
        var t = local.TimeOfDay;
        return s < e ? t >= s && t < e : t >= s || t < e;
    }
}

/// <summary>Which events and slots go to which channels, how bad they must be, and when to keep quiet.</summary>
public sealed record AlertRule
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    /// <summary>Alert kinds (see <see cref="AlertKinds"/>); empty means all.</summary>
    public string[] Kinds { get; init; } = [];
    /// <summary>Slot numbers; empty means all (and alerts that belong to no slot).</summary>
    public int[] Slots { get; init; } = [];
    public AlertSeverity MinSeverity { get; init; } = AlertSeverity.Warning;
    public string[] Channels { get; init; } = [];
    public QuietHours Quiet { get; init; } = new();
    /// <summary>The same alert is sent at most once per this many minutes. Null uses the global setting.</summary>
    public int? RepeatMinutes { get; init; }
    public bool NotifyResolved { get; init; } = true;
}

public sealed record AlertSettings
{
    /// <summary>The same alert goes out at most once per this many minutes (per rule).</summary>
    public int RepeatMinutes { get; init; } = 15;
    /// <summary>A panel or device that stays offline this long raises an alert.</summary>
    public int DeviceOfflineSeconds { get; init; } = 60;
    /// <summary>Alert when the drive holding the data folder has less than this many MB free. 0 turns the check off.</summary>
    public int DiskLowMb { get; init; } = 1000;
    /// <summary>The address of this console as people reach it (for "Open console" links in messages), e.g. http://openvp.local:8080. Optional.</summary>
    public string? ConsoleUrl { get; init; }
}

/// <summary>When the health report goes out and where.</summary>
public sealed record ReportSchedule
{
    public bool Enabled { get; init; }
    /// <summary><c>daily</c> or <c>weekly</c>.</summary>
    public string Frequency { get; init; } = "daily";
    /// <summary>For weekly: 0 is Sunday.</summary>
    public int Day { get; init; } = 1;
    public string Time { get; init; } = "08:00";
    public string[] Channels { get; init; } = [];
}

public sealed record AlertConfig
{
    public AlertSettings Settings { get; init; } = new();
    public List<AlertChannel> Channels { get; init; } = [new AlertChannel { Id = "desktop", Name = "Desktop and browser notifications", Type = "desktop" }];
    public List<AlertRule> Rules { get; init; } = [new AlertRule { Id = "default", Name = "Warnings and worse to the desktop", Channels = ["desktop"] }];
    public List<ThresholdDef> Thresholds { get; init; } = [];
    public ReportSchedule Report { get; init; } = new();

    public static AlertConfig Default() => new();

    public AlertChannel? Channel(string id) => Channels.FirstOrDefault(c => c.Id == id);
}

public static class AlertJson
{
    public static readonly JsonSerializerOptions Options = Make();

    private static JsonSerializerOptions Make()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }
}

/// <summary>Checks and tidies a configuration before it is saved. Throws <see cref="ArgumentException"/> with a plain message.</summary>
public static class AlertConfigValidator
{
    public static AlertConfig Validate(AlertConfig c)
    {
        var s = c.Settings ?? new AlertSettings();
        s = s with
        {
            RepeatMinutes = Math.Clamp(s.RepeatMinutes, 0, 1440),
            DeviceOfflineSeconds = Math.Clamp(s.DeviceOfflineSeconds, 5, 86400),
            DiskLowMb = Math.Clamp(s.DiskLowMb, 0, 10_000_000),
            ConsoleUrl = string.IsNullOrWhiteSpace(s.ConsoleUrl) ? null : s.ConsoleUrl.Trim().TrimEnd('/')
        };
        if (s.ConsoleUrl != null && !(Uri.TryCreate(s.ConsoleUrl, UriKind.Absolute, out var cu) && cu.Scheme is "http" or "https"))
            throw new ArgumentException("The console address must start with http:// or https://.");

        var channels = new List<AlertChannel>();
        foreach (var ch0 in c.Channels ?? [])
        {
            var type = (ch0.Type ?? "").Trim().ToLowerInvariant();
            if (!AlertChannel.Types.Contains(type)) throw new ArgumentException($"'{ch0.Type}' isn't a channel type. Use email, teams, slack, ntfy, webhook or desktop.");
            var id = string.IsNullOrWhiteSpace(ch0.Id) ? NewId() : ch0.Id.Trim();
            if (id.Length > 40 || !id.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')) throw new ArgumentException("A channel id may only have letters, digits, - and _.");
            if (channels.Any(x => x.Id == id)) throw new ArgumentException($"Two channels have the id '{id}'.");
            var name = string.IsNullOrWhiteSpace(ch0.Name) ? type switch { "email" => "Email", "teams" => "Microsoft Teams", "slack" => "Slack", "ntfy" => "ntfy", "webhook" => "Webhook", _ => "Desktop" } : ch0.Name.Trim();
            if (name.Length > 80) throw new ArgumentException("A channel name can have at most 80 characters.");
            var ch = ch0 with { Id = id, Name = name, Type = type };
            switch (type)
            {
                case "email":
                    if (string.IsNullOrWhiteSpace(ch.Host)) throw new ArgumentException($"{name}: the SMTP server is empty.");
                    if (ch.Port is < 1 or > 65535) throw new ArgumentException($"{name}: the SMTP port must be 1-65535.");
                    var sec = (ch.Security ?? "starttls").Trim().ToLowerInvariant();
                    if (sec is not ("starttls" or "ssl" or "none")) throw new ArgumentException($"{name}: security must be starttls, ssl or none.");
                    if (!Addresses(ch.From).Any()) throw new ArgumentException($"{name}: the From address is empty.");
                    if (!Addresses(ch.To).Any()) throw new ArgumentException($"{name}: add at least one To address.");
                    foreach (var a in Addresses(ch.From).Concat(Addresses(ch.To)))
                        if (!a.Contains('@') || a.Contains(' ') || a.Length > 254) throw new ArgumentException($"{name}: '{a}' isn't an email address.");
                    ch = ch with { Security = sec, Host = ch.Host!.Trim(), Url = null, Topic = null };
                    break;
                case "ntfy":
                    if (string.IsNullOrWhiteSpace(ch.Topic)) throw new ArgumentException($"{name}: the topic is empty.");
                    if (ch.Topic.Trim().Any(x => !(char.IsLetterOrDigit(x) || x is '-' or '_'))) throw new ArgumentException($"{name}: a topic may only have letters, digits, - and _.");
                    var server = string.IsNullOrWhiteSpace(ch.Url) ? "https://ntfy.sh" : ch.Url.Trim().TrimEnd('/');
                    if (!(Uri.TryCreate(server, UriKind.Absolute, out var su) && su.Scheme is "http" or "https")) throw new ArgumentException($"{name}: the server must start with http:// or https://.");
                    ch = ch with { Url = server, Topic = ch.Topic.Trim() };
                    break;
                case "webhook":
                    if (!(Uri.TryCreate(ch.Url?.Trim(), UriKind.Absolute, out var wu) && wu.Scheme is "http" or "https")) throw new ArgumentException($"{name}: the address must start with http:// or https://.");
                    ch = ch with { Url = ch.Url!.Trim() };
                    break;
                default: // teams, slack: the address is kept in the secret store; desktop has nothing to set
                    ch = ch with { Url = null };
                    break;
            }
            channels.Add(ch);
        }
        if (channels.Count > 30) throw new ArgumentException("At most 30 channels.");

        var rules = new List<AlertRule>();
        foreach (var r0 in c.Rules ?? [])
        {
            var id = string.IsNullOrWhiteSpace(r0.Id) ? NewId() : r0.Id.Trim();
            if (rules.Any(x => x.Id == id)) throw new ArgumentException($"Two rules have the id '{id}'.");
            var name = string.IsNullOrWhiteSpace(r0.Name) ? "Rule" : r0.Name.Trim();
            var kinds = (r0.Kinds ?? []).Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().ToArray();
            foreach (var k in kinds) if (!AlertKinds.IsKnown(k)) throw new ArgumentException($"Rule '{name}': '{k}' isn't an alert kind.");
            var chans = (r0.Channels ?? []).Distinct().ToArray();
            foreach (var cid in chans) if (channels.All(x => x.Id != cid)) throw new ArgumentException($"Rule '{name}' sends to a channel that doesn't exist.");
            var q = r0.Quiet ?? new QuietHours();
            if (q.Enabled && (!QuietHours.TryTime(q.Start, out _) || !QuietHours.TryTime(q.End, out _))) throw new ArgumentException($"Rule '{name}': quiet hours need times like 22:00 and 07:00.");
            if (r0.RepeatMinutes is < 0 or > 1440) throw new ArgumentException($"Rule '{name}': repeat minutes must be 0-1440.");
            rules.Add(r0 with { Id = id, Name = name, Kinds = kinds, Slots = (r0.Slots ?? []).Distinct().Where(n => n >= 1).Order().ToArray(), Channels = chans, Quiet = q });
        }
        if (rules.Count > 50) throw new ArgumentException("At most 50 rules.");

        var th = ThresholdRules.Validate(c.Thresholds ?? []);
        var rep = c.Report ?? new ReportSchedule();
        var freq = (rep.Frequency ?? "daily").ToLowerInvariant();
        if (freq is not ("daily" or "weekly")) throw new ArgumentException("The report is daily or weekly.");
        if (!QuietHours.TryTime(rep.Time, out _)) throw new ArgumentException("The report time must look like 08:00.");
        if (rep.Day is < 0 or > 6) throw new ArgumentException("The report day must be 0 (Sunday) to 6 (Saturday).");
        var repChans = (rep.Channels ?? []).Distinct().Where(id => channels.Any(x => x.Id == id)).ToArray();
        return new AlertConfig { Settings = s, Channels = channels, Rules = rules, Thresholds = th, Report = rep with { Frequency = freq, Channels = repChans } };
    }

    public static IEnumerable<string> Addresses(string? list) =>
        (list ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
