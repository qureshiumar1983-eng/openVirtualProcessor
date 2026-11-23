using System.Globalization;
using System.Text;

namespace OpenVP.Core.Integrations;

/// <summary>
/// The Prometheus text exposition format (version 0.0.4): <c># HELP</c> and <c># TYPE</c> once per metric, then one line per
/// sample, <c>name{label="value"} number</c>. Label values escape backslash, double quote and newline.
/// </summary>
public sealed class PrometheusWriter
{
    private readonly StringBuilder _sb = new();
    private readonly HashSet<string> _seen = new();

    public PrometheusWriter Family(string name, string type, string help)
    {
        if (!_seen.Add(name)) throw new InvalidOperationException($"Metric {name} is written twice.");
        _sb.Append("# HELP ").Append(name).Append(' ').Append(help.Replace("\\", "\\\\").Replace("\n", "\\n")).Append('\n');
        _sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        return this;
    }

    public PrometheusWriter Sample(string name, double value, params (string Key, string Value)[] labels)
    {
        _sb.Append(name);
        if (labels.Length > 0)
        {
            _sb.Append('{');
            for (var i = 0; i < labels.Length; i++)
            {
                if (i > 0) _sb.Append(',');
                _sb.Append(labels[i].Key).Append("=\"").Append(Escape(labels[i].Value)).Append('"');
            }
            _sb.Append('}');
        }
        _sb.Append(' ').Append(Number(value)).Append('\n');
        return this;
    }

    /// <summary>A metric with a single sample and no labels.</summary>
    public PrometheusWriter Single(string name, string type, string help, double value) => Family(name, type, help).Sample(name, value);

    public static string Escape(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    public static string Number(double v) =>
        double.IsNaN(v) ? "NaN" : double.IsPositiveInfinity(v) ? "+Inf" : double.IsNegativeInfinity(v) ? "-Inf" : v.ToString("R", CultureInfo.InvariantCulture);

    public override string ToString() => _sb.ToString();
}

/// <summary>What the <c>/metrics</c> endpoint says: the process, every slot, and the integrations.</summary>
public static class PrometheusText
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";
    private static readonly string[] States = ["empty", "loaded", "running", "stopped", "error"];

    public static string Render(Processor processor, IntegrationsHost? integrations = null, Func<int, (long Crashes, long Restarts)>? crashTotals = null, string version = "0.1.0")
    {
        var m = processor.Metrics.Snapshot(includeHistory: false);
        var w = new PrometheusWriter();
        w.Family("openvp_info", "gauge", "OpenVP version (always 1).").Sample("openvp_info", 1, ("version", version));
        w.Single("openvp_uptime_seconds", "gauge", "Seconds since OpenVP started.", m.UptimeSeconds);
        w.Single("openvp_process_cpu_percent", "gauge", "CPU used by the OpenVP process, percent of all cores.", m.CpuPercent);
        w.Single("openvp_process_resident_memory_bytes", "gauge", "Working set of the OpenVP process.", Math.Round(m.WorkingSetMb * 1048576));
        w.Single("openvp_process_private_memory_bytes", "gauge", "Private memory of the OpenVP process.", Math.Round(m.PrivateMb * 1048576));
        w.Single("openvp_process_managed_heap_bytes", "gauge", "Managed heap size.", Math.Round(m.GcHeapMb * 1048576));
        w.Single("openvp_process_threads", "gauge", "Threads in the OpenVP process.", m.ProcessThreads);
        w.Single("openvp_threadpool_threads", "gauge", "Thread-pool threads.", m.PoolThreads);
        w.Single("openvp_threadpool_pending_items", "gauge", "Work items waiting for a thread-pool thread.", m.PoolPending);
        w.Family("openvp_gc_collections_total", "counter", "Garbage collections by generation.")
            .Sample("openvp_gc_collections_total", m.Gen0Collections, ("generation", "0"))
            .Sample("openvp_gc_collections_total", m.Gen1Collections, ("generation", "1"))
            .Sample("openvp_gc_collections_total", m.Gen2Collections, ("generation", "2"));

        string L(int n) => n.ToString(CultureInfo.InvariantCulture);
        w.Family("openvp_slot_info", "gauge", "The program in each slot (always 1).");
        foreach (var s in processor.Slots)
            w.Sample("openvp_slot_info", 1, ("slot", L(s.Number)), ("room", s.RoomId), ("name", s.Name ?? ""), ("program", s.Runtime?.ProgramName ?? ""), ("kind", s.Runtime?.Kind ?? ""));
        w.Family("openvp_slot_state", "gauge", "1 for the state a slot is in, 0 for the others.");
        foreach (var s in processor.Slots)
            foreach (var st in States) w.Sample("openvp_slot_state", string.Equals(s.State.ToString(), st, StringComparison.OrdinalIgnoreCase) ? 1 : 0, ("slot", L(s.Number)), ("state", st));
        void PerSlot(string name, string type, string help, Func<SlotMetricsSnapshot, double> f)
        {
            w.Family(name, type, help);
            foreach (var s in m.Slots) w.Sample(name, f(s), ("slot", L(s.Number)));
        }
        PerSlot("openvp_slot_running", "gauge", "1 while the slot's program is running.", s => s.State == "Running" ? 1 : 0);
        PerSlot("openvp_slot_events_per_second", "gauge", "Events the slot thread handled per second.", s => s.EventsPerSecond);
        PerSlot("openvp_slot_events_total", "counter", "Events the slot thread has handled since the program was loaded.", s => s.EventsProcessed);
        PerSlot("openvp_slot_busy_percent", "gauge", "How busy the slot thread is, percent.", s => s.BusyPercent);
        PerSlot("openvp_slot_queue_depth", "gauge", "Events waiting for the slot thread.", s => s.QueueLength);
        PerSlot("openvp_slot_longest_event_seconds", "gauge", "The longest single event so far.", s => s.LongestHandlerSeconds);
        PerSlot("openvp_slot_current_event_seconds", "gauge", "How long the event now running has taken (0 when idle).", s => s.CurrentHandler == null ? 0 : s.CurrentHandlerSeconds);
        PerSlot("openvp_slot_timers", "gauge", "Timers the program has running.", s => s.Timers);
        PerSlot("openvp_slot_warnings", "gauge", "Problems the metrics page lists for the slot (stuck, backlog, orphaned thread).", s => s.Warnings.Count);
        PerSlot("openvp_slot_stuck", "gauge", "1 while the slot's status is Stuck.", s => s.Status == "Stuck" ? 1 : 0);
        var now = DateTimeOffset.Now;
        w.Family("openvp_slot_alerts", "gauge", "Oscillation alerts raised in the last 30 seconds.");
        foreach (var s in processor.Slots) w.Sample("openvp_slot_alerts", s.Monitor.Oscillation.Recent(now), ("slot", L(s.Number)));
        w.Family("openvp_slot_devices", "gauge", "Panels and other devices (IP IDs) the program has registered.");
        foreach (var s in processor.Slots) w.Sample("openvp_slot_devices", s.Devices.Count, ("slot", L(s.Number)));
        w.Family("openvp_slot_devices_online", "gauge", "Registered devices with at least one connection.");
        foreach (var s in processor.Slots) w.Sample("openvp_slot_devices_online", s.Devices.Count(d => d.Online), ("slot", L(s.Number)));
        w.Family("openvp_slot_panel_connections", "gauge", "Panel and EISC connections attached to the slot's devices.");
        foreach (var s in processor.Slots) w.Sample("openvp_slot_panel_connections", s.Devices.Sum(d => d.ConnectionCount), ("slot", L(s.Number)));
        w.Single("openvp_devices_online", "gauge", "Devices online across all slots.", processor.AllDevices.Count(d => d.Online));
        w.Single("openvp_panel_connections", "gauge", "Panel and EISC connections across all slots.", processor.AllDevices.Sum(d => d.ConnectionCount));
        if (crashTotals != null)
        {
            w.Family("openvp_slot_crashes_total", "counter", "Times the slot's program faulted since OpenVP started.");
            foreach (var s in processor.Slots) w.Sample("openvp_slot_crashes_total", crashTotals(s.Number).Crashes, ("slot", L(s.Number)));
            w.Family("openvp_slot_restarts_total", "counter", "Times the crash policy restarted the slot's program since OpenVP started.");
            foreach (var s in processor.Slots) w.Sample("openvp_slot_restarts_total", crashTotals(s.Number).Restarts, ("slot", L(s.Number)));
        }
        w.Single("openvp_alerts", "gauge", "Oscillation alerts and metrics problems across all slots right now.", processor.Slots.Sum(s => s.Monitor.Oscillation.Recent(now)) + m.Slots.Sum(s => s.Warnings.Count));

        if (integrations != null)
        {
            var mq = integrations.Mqtt.Status();
            w.Single("openvp_mqtt_connected", "gauge", "1 while the MQTT bridge is connected to its broker.", mq.State == "connected" ? 1 : 0);
            w.Single("openvp_mqtt_messages_published_total", "counter", "MQTT messages published.", mq.Published);
            w.Single("openvp_mqtt_messages_received_total", "counter", "MQTT messages received.", mq.Received);
            w.Single("openvp_mqtt_messages_dropped_total", "counter", "MQTT changes dropped because the queue was full.", mq.Dropped);
            var wh = integrations.Webhooks.Status();
            w.Family("openvp_webhook_deliveries_total", "counter", "Outgoing webhook deliveries by result.")
                .Sample("openvp_webhook_deliveries_total", wh.Sent, ("result", "ok")).Sample("openvp_webhook_deliveries_total", wh.Failed, ("result", "failed"));
            w.Single("openvp_webhook_dropped_total", "counter", "Webhook deliveries dropped because a queue was full.", wh.Dropped);
            w.Single("openvp_webhook_pending", "gauge", "Webhook deliveries waiting or being retried.", wh.Pending);
            var osc = integrations.Osc.Status();
            w.Single("openvp_osc_messages_received_total", "counter", "OSC messages received.", osc.Received);
            w.Single("openvp_osc_messages_sent_total", "counter", "OSC messages sent.", osc.Sent);
            var an = integrations.ArtNet.Status();
            w.Single("openvp_artnet_packets_sent_total", "counter", "ArtDMX packets sent.", an.PacketsSent);
            w.Single("openvp_artnet_packets_received_total", "counter", "ArtDMX packets received.", an.PacketsReceived);
        }
        return w.ToString();
    }
}
