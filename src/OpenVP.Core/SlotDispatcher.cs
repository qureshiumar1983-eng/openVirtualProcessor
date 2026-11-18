using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenVP.Core;

/// <summary>
/// One worker thread per program slot. Every event delivered to a program (panel presses, timer
/// callbacks, socket data) runs here, one at a time, so user code never sees two of its own
/// events racing each other.
/// </summary>
public sealed class SlotDispatcher : IDisposable
{
    private const int Open = 0, InvokeOnly = 1, Closed = 2;

    private sealed record Item(Action Run, bool IsInvoke, Action? Cancel, string? Label);

    /// <summary>What the slot thread is doing, for the metrics page.</summary>
    public sealed record Snapshot(
        int QueueLength, long Processed, double BusySeconds,
        string? CurrentLabel, double CurrentRunningSeconds,
        double LongestSeconds, string? LongestLabel, string ThreadState);

    private readonly BlockingCollection<Item> _queue = new();
    private readonly Thread _thread;
    private readonly Action<Exception> _onError;
    private readonly Func<bool>? _isPaused;
    private volatile int _mode = Open;

    // Metrics. _currentStart is 0 while idle.
    private long _processed, _busyTicks, _currentStart, _longestTicks;
    private volatile string? _currentLabel, _longestLabel;

    /// <param name="isPaused">True while the debugger holds the slot at a step; Invoke then waits without timing out.</param>
    public SlotDispatcher(string name, Action<Exception> onError, Func<bool>? isPaused = null)
    {
        _onError = onError;
        _isPaused = isPaused;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    public bool IsCurrentThread => Thread.CurrentThread == _thread;

    /// <summary>Queue an event. Dropped once the slot is shutting down.</summary>
    /// <param name="label">What this event is (e.g. "CTimer Program.OnTick"), shown on the metrics page.</param>
    public void Post(Action action, string? label = null)
    {
        if (_mode != Open) return;
        try { _queue.Add(new Item(action, false, null, label)); } catch (InvalidOperationException) { /* shutting down */ }
    }

    /// <summary>Run synchronously on the slot thread (or inline if already on it).</summary>
    public void Invoke(Action action)
    {
        if (IsCurrentThread) { action(); return; }
        if (_mode == Closed) throw new ObjectDisposedException(_thread.Name, "The slot thread has shut down.");
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        var item = new Item(
            () => { try { action(); } catch (Exception e) { error = e; } finally { done.Set(); } },
            true,
            () => { error = new ObjectDisposedException(_thread.Name, "The slot thread shut down before running this."); done.Set(); },
            Describe(action));
        try { _queue.Add(item); }
        catch (InvalidOperationException) { throw new ObjectDisposedException(_thread.Name, "The slot thread has shut down."); }
        // A slot held by the step debugger is waiting on the user, not hung: keep waiting.
        while (!done.Wait(TimeSpan.FromSeconds(30)))
            if (_isPaused?.Invoke() != true)
                throw new TimeoutException("Slot thread did not respond within 30 s.");
        if (error != null) throw new AggregateException(error);
    }

    /// <summary>
    /// Start shutting down: queued and new events are dropped, but <see cref="Invoke"/> still works so the
    /// runtime can run its own cleanup on the slot thread.
    /// </summary>
    public void Quiesce()
    {
        if (_mode == Open) _mode = InvokeOnly;
    }

    private void Run()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            var mode = _mode;
            if (mode == Closed) { item.Cancel?.Invoke(); continue; }
            if (mode == InvokeOnly && !item.IsInvoke) continue;
            _currentLabel = item.Label ?? Describe(item.Run);
            var start = Stopwatch.GetTimestamp();
            Volatile.Write(ref _currentStart, start);
            try { item.Run(); }
            catch (Exception e) { _onError(e); }
            finally
            {
                var took = Stopwatch.GetTimestamp() - start;
                Volatile.Write(ref _currentStart, 0);
                Interlocked.Add(ref _busyTicks, took);
                Interlocked.Increment(ref _processed);
                if (took > Interlocked.Read(ref _longestTicks)) { Interlocked.Exchange(ref _longestTicks, took); _longestLabel = _currentLabel; }
            }
        }
    }

    public Snapshot GetSnapshot()
    {
        var start = Volatile.Read(ref _currentStart);
        var busy = Interlocked.Read(ref _busyTicks);
        var running = start == 0 ? 0 : Stopwatch.GetTimestamp() - start;
        return new Snapshot(
            _queue.Count, Interlocked.Read(ref _processed), (busy + running) / (double)Stopwatch.Frequency,
            start == 0 ? null : _currentLabel, running / (double)Stopwatch.Frequency,
            Interlocked.Read(ref _longestTicks) / (double)Stopwatch.Frequency, _longestLabel,
            SlotMetrics.DescribeState(_thread));
    }

    /// <summary>A readable name for a delegate: "Type.Method", without compiler-generated closure names.</summary>
    internal static string Describe(Delegate d)
    {
        var m = d.Method;
        var type = m.DeclaringType;
        while (type is { DeclaringType: not null } && type.Name.Contains('<')) type = type.DeclaringType;
        var name = m.Name;
        if (name.StartsWith('<') && name.IndexOf('>') is var end and > 1) name = name[1..end];
        return $"{type?.Name ?? "?"}.{name}";
    }

    public void Dispose()
    {
        if (_mode == Closed) return;
        _mode = Closed;
        _queue.CompleteAdding();
        if (!IsCurrentThread) _thread.Join(TimeSpan.FromSeconds(5));
    }
}
