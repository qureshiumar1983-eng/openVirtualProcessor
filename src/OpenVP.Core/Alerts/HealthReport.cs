using System.Text;
using System.Text.Json;

namespace OpenVP.Core.Alerts;

public sealed record ReportSlotRow(int Slot, string? Name, string? Program, double? Availability, double DownSeconds, int Crashes, int Restarts, int Alerts);
public sealed record ReportDeviceRow(int Slot, int IpId, int Drops, double OfflineSeconds, double? Availability);
public sealed record ReportEventRow(string Kind, string Title, int? Slot, int Count);
public sealed record ReportChange(DateTimeOffset Time, string Action, string Text, int? Slot);

/// <summary>Everything a health report says, before it is turned into HTML or a chat summary.</summary>
public sealed record ReportData(string Period, DateTimeOffset From, DateTimeOffset To, DateTimeOffset? Since, string Host,
    IReadOnlyList<ReportSlotRow> Slots, IReadOnlyList<ReportDeviceRow> Devices, IReadOnlyList<ReportEventRow> TopEvents,
    int AlertsTotal, int Critical, int Warning, double PeakCpu, double PeakMemoryMb, IReadOnlyList<ReportChange> Changes);

public static class HealthReport
{
    /// <summary>Activity actions that mean a program was loaded, replaced or removed.</summary>
    public static readonly string[] ChangeActions = ["load", "auto-reload", "unload", "rollback", "restore", "demo"];

    public static ReportData Build(string period, DateTimeOffset from, DateTimeOffset to, UptimeReport uptime, IReadOnlyList<AlertView> alerts,
        IReadOnlyList<ActivityEntry> activity, (double Cpu, double Mem, double Threads) peaks, Func<int, (string? Name, string? Program)> slotInfo, string host)
    {
        var inPeriod = activity.Where(a => a.Time >= from && a.Time < to).OrderBy(a => a.Time).ToList();
        var slots = new SortedSet<int>(uptime.Entities.Where(e => e.Key.Kind == 's').Select(e => e.Key.Slot));
        foreach (var a in alerts) if (a.Slot is { } s) slots.Add(s);
        foreach (var a in inPeriod) if (a.Slot is { } s && a.Action == "crash") slots.Add(s);

        var rows = new List<ReportSlotRow>();
        foreach (var n in slots)
        {
            var u = uptime.Entities.FirstOrDefault(e => e.Key.Kind == 's' && e.Key.Slot == n);
            var crashes = inPeriod.Where(a => a.Action == "crash" && a.Slot == n).ToList();
            var (name, program) = slotInfo(n);
            rows.Add(new ReportSlotRow(n, name, program, u?.Availability, u?.DownSeconds ?? 0, crashes.Count,
                crashes.Count(c => c.Text.Contains("restarting", StringComparison.OrdinalIgnoreCase)), alerts.Count(a => a.Slot == n)));
        }

        var devices = uptime.Entities.Where(e => e.Key.Kind == 'd' && e.Drops > 0)
            .OrderByDescending(e => e.Drops).ThenByDescending(e => e.DownSeconds).Take(25)
            .Select(e => new ReportDeviceRow(e.Key.Slot, e.Key.IpId, e.Drops, e.DownSeconds, e.Availability)).ToList();

        var top = alerts.GroupBy(a => (a.Kind, a.Title, a.Slot))
            .Select(g => new ReportEventRow(g.Key.Kind, g.Key.Title, g.Key.Slot, g.Sum(a => a.Count)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Title).Take(8).ToList();

        var changes = inPeriod.Where(a => ChangeActions.Contains(a.Action)).TakeLast(40).Select(a => new ReportChange(a.Time, a.Action, a.Text, a.Slot)).ToList();
        return new ReportData(period, from, to, uptime.Since, host, rows, devices, top, alerts.Count,
            alerts.Count(a => a.Severity == AlertSeverity.Critical), alerts.Count(a => a.Severity == AlertSeverity.Warning), peaks.Cpu, peaks.Mem, changes);
    }

    public static string PeriodLabel(string period) => period == "weekly" || period == "7d" ? "weekly" : "daily";
    public static string Title(ReportData d) => $"OpenVP {PeriodLabel(d.Period)} health report";

    public static string Duration(double seconds)
    {
        if (seconds < 1) return "0 s";
        if (seconds < 90) return $"{seconds:0} s";
        if (seconds < 5400) return $"{seconds / 60:0} min";
        if (seconds < 172800) return $"{seconds / 3600:0.#} h";
        return $"{seconds / 86400:0.#} d";
    }

    private static string Pct(double? v) => v == null ? "–" : $"{v:0.##}%";
    private static string Esc(string? s) => AlertFormats.Esc(s);
    private static string SlotLabel(ReportSlotRow r) => $"Slot {r.Slot}" + (string.IsNullOrWhiteSpace(r.Name) ? "" : " · " + r.Name);

    /// <summary>A short text summary for chat and push channels.</summary>
    public static string Summary(ReportData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{d.From.ToLocalTime():MMM d HH:mm} to {d.To.ToLocalTime():MMM d HH:mm}");
        if (d.Slots.Count == 0) sb.AppendLine("No slot had a program in this period.");
        foreach (var s in d.Slots)
            sb.AppendLine($"{SlotLabel(s)}{(s.Program != null ? " (" + s.Program + ")" : "")}: {Pct(s.Availability)} up" +
                (s.Crashes > 0 ? $", {s.Crashes} crash{(s.Crashes == 1 ? "" : "es")} ({s.Restarts} restart{(s.Restarts == 1 ? "" : "s")})" : "") +
                (s.Alerts > 0 ? $", {s.Alerts} alert{(s.Alerts == 1 ? "" : "s")}" : ""));
        sb.AppendLine($"Alerts: {d.AlertsTotal} ({d.Critical} critical, {d.Warning} warning). Peak CPU {d.PeakCpu:0.#}%, peak memory {d.PeakMemoryMb:0} MB.");
        if (d.Devices.Count > 0) sb.AppendLine("Dropped: " + string.Join(", ", d.Devices.Take(5).Select(x => $"slot {x.Slot} IP ID {x.IpId:X2} ({x.Drops}x, {Duration(x.OfflineSeconds)})")) + (d.Devices.Count > 5 ? $" and {d.Devices.Count - 5} more" : ""));
        else sb.AppendLine("No device dropped.");
        if (d.Changes.Count > 0) sb.AppendLine($"{d.Changes.Count} program change{(d.Changes.Count == 1 ? "" : "s")}.");
        return sb.ToString().TrimEnd();
    }

    public static IReadOnlyList<KeyValuePair<string, string>> Facts(ReportData d)
    {
        var f = new List<KeyValuePair<string, string>>
        {
            new("Alerts", $"{d.AlertsTotal} ({d.Critical} critical, {d.Warning} warning)"),
            new("Peak CPU", $"{d.PeakCpu:0.#}%"),
            new("Peak memory", $"{d.PeakMemoryMb:0} MB"),
            new("Devices that dropped", d.Devices.Count.ToString()),
            new("Program changes", d.Changes.Count.ToString())
        };
        foreach (var s in d.Slots.Take(6)) f.Add(new($"{SlotLabel(s)} uptime", Pct(s.Availability)));
        return f;
    }

    /// <summary>The report as one self-contained HTML page with inline styles, so it works as an email body and as a download.</summary>
    public static string Html(ReportData d)
    {
        const string th = "text-align:left;padding:6px 8px;border-bottom:2px solid #d2d9e2;color:#586374;font-size:12px;white-space:nowrap";
        const string td = "padding:6px 8px;border-bottom:1px solid #e6eaf0;font-size:13px;vertical-align:top";
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>")
          .Append(Esc(Title(d))).Append("</title></head><body style=\"margin:0;padding:16px;background:#f3f5f8;font-family:Segoe UI,Arial,sans-serif;color:#1b2330\">");
        sb.Append("<div style=\"max-width:760px;margin:0 auto;background:#fff;border:1px solid #d2d9e2;border-radius:8px;overflow:hidden\">");
        sb.Append($"<div style=\"background:#1d5fd6;color:#fff;padding:14px 18px\"><div style=\"font-size:12px;opacity:.9\">{Esc(d.Host)}</div><div style=\"font-size:20px;font-weight:600\">{Esc(Title(d))}</div>")
          .Append($"<div style=\"font-size:13px;opacity:.9\">{d.From.ToLocalTime():ddd MMM d HH:mm} to {d.To.ToLocalTime():ddd MMM d HH:mm}</div></div>");
        sb.Append("<div style=\"padding:14px 18px\">");

        // headline numbers
        sb.Append("<table role=\"presentation\" style=\"border-collapse:collapse;width:100%;margin-bottom:12px\"><tr>");
        void Stat(string label, string value, string color = "#1b2330") =>
            sb.Append($"<td style=\"padding:8px 10px;border:1px solid #e6eaf0;border-radius:6px;text-align:center\"><div style=\"font-size:20px;font-weight:600;color:{color}\">{Esc(value)}</div><div style=\"font-size:11px;color:#586374\">{Esc(label)}</div></td>");
        Stat("alerts", d.AlertsTotal.ToString(), d.Critical > 0 ? "#c62828" : d.Warning > 0 ? "#9a6700" : "#1b2330");
        Stat("critical", d.Critical.ToString(), d.Critical > 0 ? "#c62828" : "#1b2330");
        Stat("peak CPU", $"{d.PeakCpu:0.#}%");
        Stat("peak memory", $"{d.PeakMemoryMb:0} MB");
        Stat("devices dropped", d.Devices.Count.ToString(), d.Devices.Count > 0 ? "#9a6700" : "#1b2330");
        sb.Append("</tr></table>");

        sb.Append("<h3 style=\"margin:14px 0 6px;font-size:14px\">Slots</h3>");
        if (d.Slots.Count == 0) sb.Append("<p style=\"color:#586374;font-size:13px\">No slot had a program in this period.</p>");
        else
        {
            sb.Append($"<table style=\"border-collapse:collapse;width:100%\"><tr><th style=\"{th}\">Slot</th><th style=\"{th}\">Program</th><th style=\"{th};text-align:right\">Uptime</th><th style=\"{th};text-align:right\">Down for</th><th style=\"{th};text-align:right\">Crashes</th><th style=\"{th};text-align:right\">Restarts</th><th style=\"{th};text-align:right\">Alerts</th></tr>");
            foreach (var s in d.Slots)
            {
                var color = s.Availability is null ? "#586374" : s.Availability >= 99.9 ? "#14633f" : s.Availability >= 95 ? "#6e4a00" : "#a31d1d";
                sb.Append($"<tr><td style=\"{td}\">{Esc(SlotLabel(s))}</td><td style=\"{td}\">{Esc(s.Program)}</td><td style=\"{td};text-align:right;color:{color};font-weight:600\">{Pct(s.Availability)}</td><td style=\"{td};text-align:right\">{Duration(s.DownSeconds)}</td><td style=\"{td};text-align:right\">{s.Crashes}</td><td style=\"{td};text-align:right\">{s.Restarts}</td><td style=\"{td};text-align:right\">{s.Alerts}</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("<h3 style=\"margin:16px 0 6px;font-size:14px\">Top events</h3>");
        if (d.TopEvents.Count == 0) sb.Append("<p style=\"color:#586374;font-size:13px\">No alerts in this period.</p>");
        else
        {
            sb.Append($"<table style=\"border-collapse:collapse;width:100%\"><tr><th style=\"{th}\">Event</th><th style=\"{th}\">Kind</th><th style=\"{th};text-align:right\">Times</th></tr>");
            foreach (var e in d.TopEvents) sb.Append($"<tr><td style=\"{td}\">{Esc(e.Title)}</td><td style=\"{td};color:#586374\">{Esc(e.Kind)}</td><td style=\"{td};text-align:right\">{e.Count}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<h3 style=\"margin:16px 0 6px;font-size:14px\">Devices that dropped</h3>");
        if (d.Devices.Count == 0) sb.Append("<p style=\"color:#586374;font-size:13px\">No panel or device went offline.</p>");
        else
        {
            sb.Append($"<table style=\"border-collapse:collapse;width:100%\"><tr><th style=\"{th}\">Slot</th><th style=\"{th}\">IP ID</th><th style=\"{th};text-align:right\">Drops</th><th style=\"{th};text-align:right\">Offline for</th><th style=\"{th};text-align:right\">Available</th></tr>");
            foreach (var x in d.Devices) sb.Append($"<tr><td style=\"{td}\">{x.Slot}</td><td style=\"{td}\">{x.IpId:X2}</td><td style=\"{td};text-align:right\">{x.Drops}</td><td style=\"{td};text-align:right\">{Duration(x.OfflineSeconds)}</td><td style=\"{td};text-align:right\">{Pct(x.Availability)}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<h3 style=\"margin:16px 0 6px;font-size:14px\">Programs loaded or changed</h3>");
        if (d.Changes.Count == 0) sb.Append("<p style=\"color:#586374;font-size:13px\">No program was loaded or changed.</p>");
        else
        {
            sb.Append($"<table style=\"border-collapse:collapse;width:100%\"><tr><th style=\"{th}\">When</th><th style=\"{th}\">What</th></tr>");
            foreach (var c in d.Changes) sb.Append($"<tr><td style=\"{td};white-space:nowrap;color:#586374\">{c.Time.ToLocalTime():MMM d HH:mm}</td><td style=\"{td}\">{Esc(c.Text)}</td></tr>");
            sb.Append("</table>");
        }
        if (d.Since is { } since && since > d.From.AddMinutes(5))
            sb.Append($"<p style=\"color:#586374;font-size:12px;margin-top:14px\">Uptime is counted from {since.ToLocalTime():MMM d HH:mm}, when OpenVP began recording.</p>");
        sb.Append("<p style=\"color:#586374;font-size:12px\">A slot counts as down while it holds a program that isn't running, including while OpenVP itself isn't running. Empty slots aren't counted.</p>");
        sb.Append("</div></div></body></html>");
        return sb.ToString();
    }
}

/// <summary>
/// Sends the health report on a daily or weekly schedule. A timer looks twice a minute; the report goes out once for each scheduled time,
/// and one missed while OpenVP was off goes out when it starts. Turning the schedule on doesn't send for a time already past.
/// </summary>
public sealed class ReportScheduler : IDisposable
{
    private readonly Func<AlertConfig> _config;
    private readonly Func<ReportSchedule, Task> _send;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string? _stateFile;
    private readonly TimeZoneInfo _zone;
    private DateTimeOffset? _lastSent;
    private Timer? _timer;
    private int _busy;

    public ReportScheduler(Func<AlertConfig> config, Func<ReportSchedule, Task> send, string? stateFile = null, Func<DateTimeOffset>? clock = null, TimeZoneInfo? zone = null, bool autoStart = true)
    {
        _config = config; _send = send; _stateFile = stateFile; _clock = clock ?? (() => DateTimeOffset.Now); _zone = zone ?? TimeZoneInfo.Local;
        try { if (stateFile != null && File.Exists(stateFile)) _lastSent = JsonSerializer.Deserialize<DateTimeOffset?>(File.ReadAllText(stateFile)); } catch { }
        if (autoStart) _timer = new Timer(_ => Tick().GetAwaiter().GetResult(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
    }

    public DateTimeOffset? LastSent => _lastSent;

    /// <summary>The latest scheduled time at or before <paramref name="now"/>, in the server's time zone.</summary>
    public static DateTimeOffset? LastDue(ReportSchedule s, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!QuietHours.TryTime(s.Time, out var at)) return null;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var day = local.Date;
        if (s.Frequency == "weekly") while ((int)day.DayOfWeek != s.Day) day = day.AddDays(-1);
        var due = day + at;
        if (due > local.DateTime) due = due.AddDays(s.Frequency == "weekly" ? -7 : -1);
        return new DateTimeOffset(due, zone.GetUtcOffset(due));
    }

    /// <summary>Checks the schedule once and sends if a scheduled time has passed since the last report. Returns true if it sent.</summary>
    public async Task<bool> Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return false;
        try
        {
            var s = _config().Report;
            if (!s.Enabled)
            {
                // Turned off: forget the last send, so turning it on again waits for the next scheduled time.
                if (_lastSent != null) { _lastSent = null; try { if (_stateFile != null) File.Delete(_stateFile); } catch (IOException) { } }
                return false;
            }
            var now = _clock();
            if (_lastSent == null) { Save(now); return false; }
            var due = LastDue(s, now, _zone);
            if (due == null || due <= _lastSent) return false;
            Save(now);
            try { await _send(s); } catch { /* the failure is in the report's history */ }
            return true;
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Notes that a report was sent now (a manual "Send report now" doesn't change the schedule).</summary>
    private void Save(DateTimeOffset t)
    {
        _lastSent = t;
        if (_stateFile == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!); File.WriteAllText(_stateFile, JsonSerializer.Serialize(t)); } catch (IOException) { }
    }

    public void Dispose() => _timer?.Dispose();
}
