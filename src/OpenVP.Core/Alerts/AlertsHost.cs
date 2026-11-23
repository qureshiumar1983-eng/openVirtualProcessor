using System.Text.Json;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Alerts;

/// <summary>
/// Puts the monitoring pieces together: the alert settings, the engine and the channel senders, the monitor loop that raises alerts, the uptime
/// and metrics history, and the scheduled health report. Create it once, hook the event sources you have (<see cref="MonitorLoop.OnCrash"/>,
/// <see cref="MonitorLoop.OnRestored"/>, <see cref="MonitorLoop.OnTunnel"/>), and dispose it when the server stops.
/// </summary>
public sealed class AlertsHost : IDisposable
{
    public AlertConfigStore Config { get; }
    public AlertEngine Engine { get; }
    public ChannelDispatcher Dispatcher { get; }
    public AlertStream Stream { get; } = new();
    public UptimeStore Uptime { get; }
    public MetricsHistory History { get; }
    public MonitorLoop Monitor { get; }
    public ReportScheduler Reports { get; }

    private readonly Processor _processor;
    private readonly ActivityLog _activity;

    public AlertsHost(Processor processor, ActivityLog activity, SecretStore secrets, IEmailSender? email, HttpMessageHandler? handler = null, bool autoStart = true)
    {
        _processor = processor; _activity = activity;
        var dir = processor.DataDirectory;
        Config = new AlertConfigStore(dir, secrets);
        Dispatcher = new ChannelDispatcher(secrets.Get, email, Stream, handler);
        Engine = new AlertEngine(() => Config.Current, Dispatcher, Path.Combine(dir, "alerts", "state.json"),
            slotInfo: n => processor.IsValidSlot(n) ? (processor.Slot(n).RoomId, processor.Slot(n).Runtime?.ProgramName) : (null, null));
        Uptime = new UptimeStore(dir, processor, heartbeat: autoStart);
        History = new MetricsHistory(dir);
        Monitor = new MonitorLoop(processor, Engine, () => Config.Current, History, autoStart: autoStart);
        Reports = new ReportScheduler(() => Config.Current, s => SendReportAsync(s.Frequency == "weekly" ? "weekly" : "daily", s.Channels, scheduled: true), Path.Combine(dir, "alerts", "report-state.json"), autoStart: autoStart);
        Engine.Changed += () =>
        {
            try { Stream.Publish("status", JsonSerializer.Serialize(StatusDto(), AlertJson.Options)); } catch { }
        };
    }

    public object StatusDto() => new { status = Engine.Status(), listeners = Stream.Listeners };

    public static TimeSpan PeriodSpan(string period) => period is "weekly" or "7d" or "week" ? TimeSpan.FromDays(7) : TimeSpan.FromHours(24);
    public static string PeriodName(string period) => period is "weekly" or "7d" or "week" ? "weekly" : "daily";

    /// <summary>Gathers the report for the last day or week.</summary>
    public ReportData BuildReport(string period, DateTimeOffset? now = null)
    {
        var to = now ?? DateTimeOffset.UtcNow;
        var from = to - PeriodSpan(period);
        var uptime = Uptime.Report(from, to, 48);
        var alerts = Engine.Between(from, to);
        var activity = _activity.Query(null, ActivityLog.DefaultMax);
        var peaks = History.Peaks(PeriodSpan(period));
        return HealthReport.Build(PeriodName(period), from, to, uptime, alerts, activity, peaks,
            n => _processor.IsValidSlot(n) ? (_processor.Slot(n).Name, _processor.Slot(n).Runtime?.ProgramName) : (null, null), Environment.MachineName);
    }

    /// <summary>Builds the report and sends it to the given channels (or the schedule's). Returns the history entry with each channel's result.</summary>
    public async Task<AlertView> SendReportAsync(string period, IEnumerable<string>? channels = null, bool scheduled = false)
    {
        var data = BuildReport(period);
        var ids = (channels ?? Config.Current.Report.Channels).ToList();
        var view = await Engine.SendReportAsync(HealthReport.Title(data), HealthReport.Summary(data), HealthReport.Html(data), HealthReport.Facts(data), ids);
        var ok = view.Deliveries.Count(d => d.Status == "sent"); var bad = view.Deliveries.Count(d => d.Status == "failed");
        _activity.Add("report", $"{(scheduled ? "Sent the scheduled" : "Sent the")} {HealthReport.PeriodLabel(data.Period)} health report ({ok} channel{(ok == 1 ? "" : "s")} ok{(bad > 0 ? $", {bad} failed" : "")})", null, scheduled ? Actors.Auto : Actors.ThisComputer);
        return view;
    }

    public void Dispose()
    {
        Stream.Close();
        Reports.Dispose();
        Monitor.Dispose();
        Uptime.Dispose();
        History.Dispose();
        Engine.Dispose();
        Dispatcher.Dispose();
    }
}
