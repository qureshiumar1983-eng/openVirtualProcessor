using System.Diagnostics;
using System.Text;

namespace OpenVP.Core;

public sealed record ProcessSample(DateTimeOffset Time, double CpuPercent, double WorkingSetMb, double GcHeapMb, int Threads, int PoolThreads, long PoolPending);

public sealed record SlotMetricsSnapshot(
    int Number, string State, string? Program, string Status, bool Paused,
    int QueueLength, double EventsPerSecond, double BusyPercent, long EventsProcessed,
    string? CurrentHandler, double CurrentHandlerSeconds, string? LongestHandler, double LongestHandlerSeconds,
    string SlotThreadState, int Timers, long BackgroundWorkCompleted,
    IReadOnlyList<ThreadInfo> Threads, IReadOnlyList<WorkInfo> RunningWork, IReadOnlyList<string> Warnings);

public sealed record MetricsSnapshot(
    DateTimeOffset Time, double UptimeSeconds, int CpuCores, double CpuPercent,
    double WorkingSetMb, double PrivateMb, double GcHeapMb, double MachineMemoryMb,
    int Gen0Collections, int Gen1Collections, int Gen2Collections,
    int ProcessThreads, int PoolThreads, long PoolPending, long PoolCompleted, int PoolMinThreads, int PoolMaxThreads,
    IReadOnlyList<SlotMetricsSnapshot> Slots, IReadOnlyList<string> Warnings, IReadOnlyList<ProcessSample> History);

/// <summary>
/// Samples the process (CPU, memory, threads) and every slot once a second and keeps five minutes of
/// history, for the metrics page and the cpuload / ramfree / taskstat console commands.
/// </summary>
public sealed class MetricsSampler : IDisposable
{
    /// <summary>The slot thread has been in one event this long: everything else in the slot is waiting.</summary>
    public static readonly TimeSpan StuckHandler = TimeSpan.FromSeconds(5);
    /// <summary>A background work item (CrestronInvoke, SIMPL+ event) has been running this long.</summary>
    public static readonly TimeSpan LongWork = TimeSpan.FromSeconds(30);
    public const int QueueBacklog = 1000;
    private const int HistoryLength = 300;

    private readonly Processor _processor;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly Queue<ProcessSample> _history = new();
    private readonly DateTimeOffset _started = DateTimeOffset.Now;
    private readonly (long Processed, double Busy)[] _lastSlot;
    private readonly (double EventsPerSecond, double BusyPercent)[] _slotRates;
    private TimeSpan _lastCpu;
    private long _lastTick;
    private double _cpuPercent;

    internal MetricsSampler(Processor processor)
    {
        _processor = processor;
        _lastSlot = new (long, double)[processor.Slots.Count];
        _slotRates = new (double, double)[processor.Slots.Count];
        using (var p = Process.GetCurrentProcess()) _lastCpu = p.TotalProcessorTime;
        _lastTick = Stopwatch.GetTimestamp();
        _timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Sample()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            var now = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                var wall = (now - _lastTick) / (double)Stopwatch.Frequency;
                if (wall <= 0) return;
                var cpu = p.TotalProcessorTime;
                _cpuPercent = Math.Clamp((cpu - _lastCpu).TotalSeconds / wall / Environment.ProcessorCount * 100, 0, 100);
                _lastCpu = cpu;
                _lastTick = now;

                for (var i = 0; i < _processor.Slots.Count; i++)
                {
                    var d = _processor.Slots[i].Dispatcher.GetSnapshot();
                    var (processed, busy) = _lastSlot[i];
                    // A new dispatcher (after unload) starts its counters at zero.
                    if (d.Processed < processed || d.BusySeconds < busy) (processed, busy) = (0, 0);
                    _slotRates[i] = ((d.Processed - processed) / wall, Math.Clamp((d.BusySeconds - busy) / wall * 100, 0, 100));
                    _lastSlot[i] = (d.Processed, d.BusySeconds);
                }

                _history.Enqueue(new ProcessSample(DateTimeOffset.Now, Math.Round(_cpuPercent, 1), Mb(p.WorkingSet64),
                    Mb(GC.GetTotalMemory(false)), p.Threads.Count, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount));
                while (_history.Count > HistoryLength) _history.Dequeue();
            }
        }
        catch { /* never let sampling take the process down */ }
    }

    public MetricsSnapshot Snapshot(bool includeHistory = true)
    {
        using var p = Process.GetCurrentProcess();
        ThreadPool.GetMinThreads(out var minW, out _);
        ThreadPool.GetMaxThreads(out var maxW, out _);
        var slots = _processor.Slots.Select(SlotSnapshot).ToList();
        var warnings = new List<string>();
        foreach (var s in slots) warnings.AddRange(s.Warnings.Select(w => $"Slot {s.Number}: {w}"));
        if (ThreadPool.PendingWorkItemCount > 100)
            warnings.Add($"{ThreadPool.PendingWorkItemCount} background work items are waiting for a thread-pool thread.");

        double cpu;
        List<ProcessSample> history;
        lock (_gate)
        {
            cpu = _cpuPercent;
            history = includeHistory ? _history.ToList() : new List<ProcessSample>();
        }
        return new MetricsSnapshot(
            DateTimeOffset.Now, (DateTimeOffset.Now - _started).TotalSeconds, Environment.ProcessorCount, Math.Round(cpu, 1),
            Mb(p.WorkingSet64), Mb(p.PrivateMemorySize64), Mb(GC.GetTotalMemory(false)), Mb(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            p.Threads.Count, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount, ThreadPool.CompletedWorkItemCount, minW, maxW,
            slots, warnings, history);
    }

    private SlotMetricsSnapshot SlotSnapshot(ProgramSlot slot)
    {
        var d = slot.Dispatcher.GetSnapshot();
        (double eps, double busy) rates;
        lock (_gate) rates = _slotRates[slot.Number - 1];
        var threads = slot.Metrics.Threads;
        var work = slot.Metrics.RunningWork;
        var paused = slot.Stepper.IsPaused;
        var warnings = new List<string>();

        var stuck = !paused && d.CurrentLabel != null && d.CurrentRunningSeconds >= StuckHandler.TotalSeconds;
        if (stuck)
            warnings.Add($"slot thread has been inside {d.CurrentLabel} for {Fmt(d.CurrentRunningSeconds)}; its other events are waiting.");
        if (slot.Metrics.Callbacks?.GetSnapshot() is { CurrentLabel: not null } cb && cb.CurrentRunningSeconds >= StuckHandler.TotalSeconds)
        {
            stuck = true;
            warnings.Add($"SIMPL# callback thread has been inside {cb.CurrentLabel} for {Fmt(cb.CurrentRunningSeconds)}; library callbacks are waiting.");
        }
        if (d.QueueLength >= QueueBacklog)
            warnings.Add($"{d.QueueLength} events are queued for the slot thread.");
        foreach (var t in threads.Where(t => t.Orphaned))
            warnings.Add($"thread '{t.Name}' ({t.Kind}) is still running from a program that has since been unloaded or replaced (started {Fmt(t.AgeSeconds)} ago).");
        foreach (var w in work.Where(w => w.RunningSeconds >= LongWork.TotalSeconds))
            warnings.Add($"{w.Kind} {w.Label} has been running for {Fmt(w.RunningSeconds)}.");

        var status = paused ? "Paused"
            : stuck ? "Stuck"
            : warnings.Count > 0 ? "Warning"
            : slot.State != SlotState.Running ? slot.State.ToString()
            : d.CurrentLabel != null || rates.busy >= 50 ? "Busy"
            : "OK";

        return new SlotMetricsSnapshot(
            slot.Number, slot.State.ToString(), slot.Runtime?.ProgramName, status, paused,
            d.QueueLength, Math.Round(rates.eps, 1), Math.Round(rates.busy, 1), d.Processed,
            d.CurrentLabel, Math.Round(d.CurrentRunningSeconds, 2), d.LongestLabel, Math.Round(d.LongestSeconds, 3),
            d.ThreadState, slot.Metrics.ActiveTimers, slot.Metrics.WorkCompleted, threads, work, warnings);
    }

    private static double Mb(long bytes) => Math.Round(bytes / 1048576.0, 1);

    internal static string Fmt(double seconds) => seconds switch
    {
        < 1 => $"{seconds * 1000:0} ms",
        < 120 => $"{seconds:0.#} s",
        < 7200 => $"{seconds / 60:0.#} min",
        _ => $"{seconds / 3600:0.#} h"
    };

    public void Dispose() => _timer.Dispose();
}

/// <summary>Text versions of the metrics for the slot console (cpuload, ramfree, taskstat, metrics).</summary>
public static class MetricsReport
{
    public static string Console(Processor processor, ProgramSlot slot, string command)
    {
        var m = processor.Metrics.Snapshot(includeHistory: false);
        var s = m.Slots[slot.Number - 1];
        var sb = new StringBuilder();
        switch (command)
        {
            case "cpuload":
                sb.AppendLine($"CPU: {m.CpuPercent:0.0}% of {m.CpuCores} cores (whole OpenVP process)");
                sb.AppendLine($"Slot {s.Number} thread: {s.BusyPercent:0.0}% busy, {s.EventsPerSecond:0.#} events/s, {s.QueueLength} queued");
                break;
            case "ramfree":
                sb.AppendLine($"Process memory: {m.WorkingSetMb:0.0} MB in use ({m.PrivateMb:0.0} MB private), managed heap {m.GcHeapMb:0.0} MB");
                sb.AppendLine($"Memory available to OpenVP: {m.MachineMemoryMb:0} MB");
                sb.AppendLine($"Garbage collections: gen0 {m.Gen0Collections}, gen1 {m.Gen1Collections}, gen2 {m.Gen2Collections}");
                break;
            case "taskstat":
                Threads(sb, m, s);
                break;
            default:
                sb.AppendLine($"Uptime {MetricsSampler.Fmt(m.UptimeSeconds)} · CPU {m.CpuPercent:0.0}% · RAM {m.WorkingSetMb:0.0} MB · {m.ProcessThreads} threads");
                sb.AppendLine($"Slot {s.Number}: {s.Status} ({s.State}) · {s.EventsPerSecond:0.#} events/s · {s.BusyPercent:0.0}% busy · {s.QueueLength} queued · {s.Timers} timers");
                if (s.LongestHandler != null) sb.AppendLine($"Longest event so far: {s.LongestHandler} ({MetricsSampler.Fmt(s.LongestHandlerSeconds)})");
                Threads(sb, m, s);
                break;
        }
        if (s.Warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var w in s.Warnings) sb.AppendLine("  ! " + w);
        }
        return sb.ToString().TrimEnd();
    }

    private static void Threads(StringBuilder sb, MetricsSnapshot m, SlotMetricsSnapshot s)
    {
        sb.AppendLine($"Process: {m.ProcessThreads} threads, thread pool {m.PoolThreads} threads / {m.PoolPending} waiting");
        var cur = s.CurrentHandler == null ? "idle" : $"in {s.CurrentHandler} for {MetricsSampler.Fmt(s.CurrentHandlerSeconds)}";
        sb.AppendLine($"Slot {s.Number} thread: {s.SlotThreadState}, {cur}");
        if (s.Threads.Count == 0) sb.AppendLine("Program threads: none");
        else
        {
            sb.AppendLine("Program threads:");
            foreach (var t in s.Threads)
                sb.AppendLine($"  {t.Id,5}  {t.Name,-28} {t.Kind,-16} {t.State,-8} up {MetricsSampler.Fmt(t.AgeSeconds)}{(t.Orphaned ? "  ORPHANED" : "")}");
        }
        if (s.RunningWork.Count > 0)
        {
            sb.AppendLine("Background work running now:");
            foreach (var w in s.RunningWork) sb.AppendLine($"  {w.Kind,-16} {w.Label,-36} {MetricsSampler.Fmt(w.RunningSeconds)}");
        }
    }
}
