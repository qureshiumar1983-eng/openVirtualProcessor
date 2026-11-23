namespace OpenVP.Core.Alerts;

/// <summary>
/// Watches OpenVP and raises alerts: slots faulting, getting stuck or backing up, crash-policy actions, devices staying offline, oscillation
/// alerts, watches firing, programs that didn't come back, the tunnel stopping, a full disk and metric thresholds. It also feeds the metrics
/// history. Events come from the places that already know (a slot's state change, the crash supervisor, the oscillation detector and watch
/// engine); everything else is checked once a second from the same snapshot the metrics page uses. Nothing here runs on a slot's thread except
/// the cheap event handlers, which only hand a candidate to the engine.
/// </summary>
public sealed class MonitorLoop : IDisposable
{
    private readonly Processor _processor;
    private readonly AlertEngine _engine;
    private readonly Func<AlertConfig> _config;
    private readonly MetricsHistory? _history;
    private readonly Func<long?> _freeDiskMb;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ThresholdEvaluator _thresholds = new();
    private readonly HashSet<string> _devOffline = new();
    private readonly HashSet<int> _backlog = new();
    private readonly HashSet<int> _stuck = new();
    private readonly List<Action> _unhook = new();
    private Timer? _timer;
    private DateTimeOffset _nextDisk;
    private bool _diskLow;
    private TunnelState _tunnelState = TunnelState.Off;
    private int _busy, _disposed;

    public MonitorLoop(Processor processor, AlertEngine engine, Func<AlertConfig> config, MetricsHistory? history, Func<long?>? freeDiskMb = null, Func<DateTimeOffset>? clock = null, bool autoStart = true)
    {
        _processor = processor; _engine = engine; _config = config; _history = history;
        _freeDiskMb = freeDiskMb ?? (() => FreeMb(processor.DataDirectory));
        _clock = clock ?? (() => DateTimeOffset.Now);
        foreach (var slot in processor.Slots) Hook(slot);
        if (autoStart) _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Free space on the drive that holds <paramref name="path"/>, in MB, or null if it can't be read.</summary>
    public static long? FreeMb(string path)
    {
        try { return new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace / 1048576; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    private static string Label(ProgramSlot s) => $"Slot {s.Number}" + (string.IsNullOrWhiteSpace(s.Name) ? "" : $" ({s.Name})");
    private static string FirstLine(string? s) => (s ?? "").Split('\n', 2)[0].Trim();

    // ------------------------------------------------------------------ event sources

    private void Hook(ProgramSlot slot)
    {
        Action<ProgramSlot> state = s => OnState(s);
        slot.StateChanged += state;
        _unhook.Add(() => slot.StateChanged -= state);

        Action<OscillationAlert> osc = a => _engine.Raise(new AlertCandidate(AlertKinds.Oscillation, $"oscillation:{slot.Number}:{a.Signal}", AlertSeverity.Warning,
            $"{Label(slot)}: {a.Signal} is oscillating", a.Message, slot.Number, Transient: true, [new("Signal", a.Signal), new("Changes", a.Count.ToString())]));
        slot.Monitor.Oscillation.Alert += osc;
        _unhook.Add(() => slot.Monitor.Oscillation.Alert -= osc);

        Action<WatchHit, WatchDef> watch = (hit, def) => _engine.Raise(new AlertCandidate(AlertKinds.Watch, $"watch:{slot.Number}:{def.Id}", AlertSeverity.Warning,
            $"{Label(slot)}: watch '{(string.IsNullOrWhiteSpace(def.Name) ? def.Expression : def.Name)}' fired", hit.Message, slot.Number, Transient: true, [new("Expression", def.Expression)]));
        slot.Monitor.Watches.Fired += watch;
        _unhook.Add(() => slot.Monitor.Watches.Fired -= watch);
    }

    private void OnState(ProgramSlot s)
    {
        var n = s.Number;
        if (s.State == SlotState.Error)
            _engine.Raise(new AlertCandidate(AlertKinds.SlotFault, $"slot.fault:{n}", AlertSeverity.Critical, $"{Label(s)} is in Error",
                string.IsNullOrWhiteSpace(s.LastError) ? "The program faulted or failed to start." : FirstLine(s.LastError), n, false,
                [new("Program", s.Runtime?.ProgramName ?? "")]));
        else
        {
            _engine.Resolve($"slot.fault:{n}", $"{Label(s)} is {s.State.ToString().ToLowerInvariant()} again.");
            if (s.State == SlotState.Running)
            {
                _engine.Resolve($"crash.giveup:{n}", $"{Label(s)} is running again.");
                _engine.Resolve($"restore.failed:{n}", $"{Label(s)} is running.");
            }
        }
        if (s.State != SlotState.Running)
        {
            _engine.Resolve($"slot.stuck:{n}", "The slot is no longer running.");
            _engine.Resolve($"slot.backlog:{n}", "The slot is no longer running.");
        }
    }

    /// <summary>The crash supervisor acted on a fault (see <see cref="CrashSupervisor.Acted"/>).</summary>
    public void OnCrash(int slotNumber, string what, string why)
    {
        var s = _processor.Slot(slotNumber);
        if (what.StartsWith("restarting", StringComparison.OrdinalIgnoreCase))
            _engine.Raise(new AlertCandidate(AlertKinds.CrashRestart, $"crash.restart:{slotNumber}", AlertSeverity.Warning, $"{Label(s)} crashed and is being restarted",
                $"{why}. Crash policy: {what}.", slotNumber, Transient: true));
        else
            _engine.Raise(new AlertCandidate(AlertKinds.CrashGiveUp, $"crash.giveup:{slotNumber}", AlertSeverity.Critical, $"{Label(s)} crashed and stays stopped",
                $"{why}. Crash policy: {what}.", slotNumber));
    }

    /// <summary>A program couldn't be brought back at start-up (see <see cref="StartupRestorer.Restored"/>).</summary>
    public void OnRestored(RestoreEntry e, string? error)
    {
        if (error == null) return;
        var s = _processor.Slot(e.Slot);
        _engine.Raise(new AlertCandidate(AlertKinds.RestoreFailed, $"restore.failed:{e.Slot}", AlertSeverity.Critical, $"{Label(s)}: the program didn't come back at start-up", error, e.Slot));
    }

    /// <summary>The internet tunnel changed state. Going from on to error (cloudflared died) is an alert; stopping it yourself is not.</summary>
    public void OnTunnel(TunnelState state, string? message)
    {
        var was = _tunnelState;
        _tunnelState = state;
        if (was == TunnelState.On && state == TunnelState.Error)
            _engine.Raise(new AlertCandidate(AlertKinds.TunnelStopped, "tunnel.stopped", AlertSeverity.Warning, "The internet tunnel stopped",
                message ?? "cloudflared stopped unexpectedly. Shared panel links no longer work from outside.", null));
        else if (state is TunnelState.On or TunnelState.Off)
            _engine.Resolve("tunnel.stopped", state == TunnelState.On ? "The internet tunnel is on again." : "The internet tunnel was turned off.");
    }

    // ------------------------------------------------------------------ once a second

    /// <summary>One pass: thresholds, stuck and backlog, devices, disk, and a sample for the history. Safe to call from tests.</summary>
    public void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var now = _clock();
            var cfg = _config();
            var m = _processor.Metrics.Snapshot(includeHistory: false);
            var offline = DevicesOffline();

            StuckAndBacklog(m);
            Devices(cfg, now);
            Thresholds(cfg, m, offline, now);
            Disk(cfg, now);
            _history?.Add(new MetricSample(now.ToUniversalTime(), m.CpuPercent, m.WorkingSetMb, m.ProcessThreads, m.PoolPending, offline.Total,
                m.Slots.Where(s => s.State != "Empty").Select(s => new SlotSample(s.Number, s.BusyPercent, s.EventsPerSecond, s.QueueLength, s.CurrentHandlerSeconds * 1000)).ToList()));
        }
        catch { /* never let monitoring take the process down */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private void StuckAndBacklog(MetricsSnapshot m)
    {
        foreach (var s in m.Slots)
        {
            var slot = _processor.Slot(s.Number);
            if (s.Status == "Stuck")
            {
                if (_stuck.Add(s.Number))
                    _engine.Raise(new AlertCandidate(AlertKinds.SlotStuck, $"slot.stuck:{s.Number}", AlertSeverity.Critical, $"{Label(slot)} is stuck",
                        s.Warnings.FirstOrDefault() is { } w ? char.ToUpper(w[0]) + w[1..] : "The slot thread has been inside one event for too long.", s.Number,
                        false, [new("Running now", s.CurrentHandler ?? "")]));
            }
            else if (_stuck.Remove(s.Number)) _engine.Resolve($"slot.stuck:{s.Number}", "The slot is responding again.");

            if (s.State == "Running" && s.QueueLength >= MetricsSampler.QueueBacklog)
            {
                if (_backlog.Add(s.Number))
                    _engine.Raise(new AlertCandidate(AlertKinds.SlotBacklog, $"slot.backlog:{s.Number}", AlertSeverity.Warning, $"{Label(slot)} has a backlog",
                        $"{s.QueueLength} events are queued for the slot thread.", s.Number));
            }
            else if (_backlog.Contains(s.Number) && s.QueueLength < MetricsSampler.QueueBacklog / 2) { _backlog.Remove(s.Number); _engine.Resolve($"slot.backlog:{s.Number}", "The backlog has cleared."); }
        }
    }

    private (int Total, Dictionary<int, int> BySlot) DevicesOffline()
    {
        var by = new Dictionary<int, int>();
        var total = 0;
        foreach (var s in _processor.Slots)
        {
            if (s.State != SlotState.Running) continue;
            var c = s.Devices.Count(d => !d.Online && d.LastOnline != null);
            by[s.Number] = c; total += c;
        }
        return (total, by);
    }

    private void Devices(AlertConfig cfg, DateTimeOffset now)
    {
        var limit = TimeSpan.FromSeconds(cfg.Settings.DeviceOfflineSeconds);
        var seen = new HashSet<string>();
        foreach (var s in _processor.Slots)
        {
            if (s.State != SlotState.Running) continue;
            foreach (var d in s.Devices)
            {
                var key = $"device.offline:{d.Slot}:{d.IpId:X2}";
                if (d.Online || d.LastOnline == null || d.LastOffline == null || d.LastOffline < d.LastOnline) continue;
                seen.Add(key);
                if (now - d.LastOffline.Value < limit || !_devOffline.Add(key)) continue;
                _engine.Raise(new AlertCandidate(AlertKinds.DeviceOffline, key, AlertSeverity.Warning, $"{Label(s)}: IP ID {d.IpId:X2} is offline",
                    $"{(string.IsNullOrWhiteSpace(d.Description) ? d.Type : d.Description)} at IP ID {d.IpId:X2} has been offline for more than {cfg.Settings.DeviceOfflineSeconds} s (since {d.LastOffline.Value.ToLocalTime():HH:mm:ss}).",
                    d.Slot, false, [new("IP ID", d.IpId.ToString("X2")), new("Offline since", d.LastOffline.Value.ToLocalTime().ToString("HH:mm:ss"))]));
            }
        }
        foreach (var key in _devOffline.ToList())
            if (!seen.Contains(key)) { _devOffline.Remove(key); _engine.Resolve(key, "It is back online."); }
    }

    private void Thresholds(AlertConfig cfg, MetricsSnapshot m, (int Total, Dictionary<int, int> BySlot) offline, DateTimeOffset now)
    {
        var defs = cfg.Thresholds;
        var readings = new List<MetricReading>
        {
            new(ThresholdMetrics.Cpu, null, m.CpuPercent), new(ThresholdMetrics.Memory, null, m.WorkingSetMb), new(ThresholdMetrics.DevicesOffline, null, offline.Total)
        };
        foreach (var s in m.Slots.Where(s => s.State == "Running"))
        {
            readings.Add(new(ThresholdMetrics.SlotBusy, s.Number, s.BusyPercent));
            readings.Add(new(ThresholdMetrics.Queue, s.Number, s.QueueLength));
            readings.Add(new(ThresholdMetrics.LongestEvent, s.Number, s.CurrentHandlerSeconds * 1000));
            readings.Add(new(ThresholdMetrics.EventsPerSecond, s.Number, s.EventsPerSecond));
        }
        foreach (var (slot, count) in offline.BySlot) readings.Add(new(ThresholdMetrics.DevicesOffline, slot, count));

        var changes = _thresholds.Evaluate(defs, readings, now);
        foreach (var c in changes)
        {
            var key = ThresholdEvaluator.KeyFor(c.Def, c.Slot);
            if (c.Level == ThresholdLevel.Ok) { _engine.Resolve(key, "It is back under the limit."); continue; }
            var info = ThresholdMetrics.Find(c.Def.Metric);
            var limit = c.Level == ThresholdLevel.Critical ? c.Def.Crit : c.Def.Warn;
            var where = c.Slot is { } sn ? $"Slot {sn}" : "OpenVP";
            _engine.Raise(new AlertCandidate(AlertKinds.Threshold, key, c.Level == ThresholdLevel.Critical ? AlertSeverity.Critical : AlertSeverity.Warning,
                $"{where}: {info?.Label ?? c.Def.Metric} is {c.Level.ToString().ToLowerInvariant()}",
                $"{info?.Label ?? c.Def.Metric} is {Fmt(c.Value)} {info?.Unit}, at or above the {c.Level.ToString().ToLowerInvariant()} level of {Fmt(limit ?? 0)} {info?.Unit}" + (c.Def.ForSeconds > 0 ? $" for {c.Def.ForSeconds} s." : "."),
                c.Slot, false, [new("Metric", info?.Label ?? c.Def.Metric), new("Value", $"{Fmt(c.Value)} {info?.Unit}"), new("Limit", $"{Fmt(limit ?? 0)} {info?.Unit}")]));
        }
    }

    private static string Fmt(double v) => v >= 100 ? v.ToString("0") : v.ToString("0.#");

    private void Disk(AlertConfig cfg, DateTimeOffset now)
    {
        if (now < _nextDisk) return;
        _nextDisk = now.AddSeconds(60);
        var limit = cfg.Settings.DiskLowMb;
        if (limit <= 0) { if (_diskLow) { _diskLow = false; _engine.Resolve("disk.low"); } return; }
        if (_freeDiskMb() is not { } free) return;
        if (free < limit)
        {
            if (_diskLow && free >= limit / 4) return;      // already raised; only worse if it gets very low
            var crit = free < limit / 4;
            _diskLow = true;
            _engine.Raise(new AlertCandidate(AlertKinds.DiskLow, "disk.low", crit ? AlertSeverity.Critical : AlertSeverity.Warning, "Disk space is low in the data folder",
                $"Only {free:N0} MB are free on the drive that holds {_processor.DataDirectory} (the alert level is {limit:N0} MB). Logs, history and uploads may stop being saved.", null, false,
                [new("Free", $"{free:N0} MB"), new("Folder", _processor.DataDirectory)]));
        }
        else if (_diskLow && free >= limit * 1.1) { _diskLow = false; _engine.Resolve("disk.low", $"{free:N0} MB are free again."); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer?.Dispose();
        foreach (var u in _unhook) { try { u(); } catch { } }
        _unhook.Clear();
    }
}
