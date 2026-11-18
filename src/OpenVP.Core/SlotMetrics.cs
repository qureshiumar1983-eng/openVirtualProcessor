using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenVP.Core;

/// <summary>A thread a program started (CrestronThread, SIMPL+ Function Main), as seen by the metrics page.</summary>
public sealed record ThreadInfo(
    int Id, string Name, string Kind, DateTimeOffset Started, double AgeSeconds, string State, bool Orphaned);

/// <summary>Background work a program queued (CrestronInvoke, SIMPL+ event handlers) that is running right now.</summary>
public sealed record WorkInfo(string Kind, string Label, double RunningSeconds);

/// <summary>
/// Live counters for one slot: what the slot thread is doing, the threads and background work the program
/// started, and its timers. Lives on the slot, so it outlasts each program and can report threads a program
/// left running after it was unloaded.
/// </summary>
public sealed class SlotMetrics
{
    private sealed class TrackedThread(Thread thread, string kind, int generation)
    {
        public readonly Thread Thread = thread;
        public readonly string Kind = kind;
        public readonly int Generation = generation;
        public readonly DateTimeOffset Started = DateTimeOffset.Now;
        public readonly long StartTicks = Stopwatch.GetTimestamp();
    }

    private sealed class Work(string kind, string label)
    {
        public readonly string Kind = kind;
        public readonly string Label = label;
        public readonly long StartTicks = Stopwatch.GetTimestamp();
    }

    private readonly ConcurrentDictionary<Thread, TrackedThread> _threads = new();
    private readonly ConcurrentDictionary<Work, byte> _work = new();
    private readonly Func<int> _generation;
    private int _timers;
    private long _workCompleted;

    internal SlotMetrics(Func<int> generation) => _generation = generation;

    /// <summary>Timers (CTimer) the program has created and not disposed.</summary>
    public int ActiveTimers => Math.Max(0, Volatile.Read(ref _timers));
    public void TimerCreated() => Interlocked.Increment(ref _timers);
    public void TimerDisposed() => Interlocked.Decrement(ref _timers);
    internal void ResetTimers() => Interlocked.Exchange(ref _timers, 0);

    public long WorkCompleted => Interlocked.Read(ref _workCompleted);

    /// <summary>Record a thread the program started. Call from inside the thread; dispose when it ends.</summary>
    public IDisposable TrackThread(string kind)
    {
        var t = Thread.CurrentThread;
        _threads[t] = new TrackedThread(t, kind, _generation());
        return new Scope(() => _threads.TryRemove(t, out _));
    }

    /// <summary>Record background work while it runs. Dispose when it finishes.</summary>
    public IDisposable BeginWork(string kind, string label)
    {
        var w = new Work(kind, label);
        _work[w] = 0;
        return new Scope(() => { _work.TryRemove(w, out _); Interlocked.Increment(ref _workCompleted); });
    }

    /// <summary>Run <paramref name="action"/> on the thread pool, recorded as background work.</summary>
    public void QueueWork(string kind, string label, Action action) =>
        ThreadPool.UnsafeQueueUserWorkItem(_ => { using (BeginWork(kind, label)) action(); }, null);

    public IReadOnlyList<ThreadInfo> Threads
    {
        get
        {
            var gen = _generation();
            var list = new List<ThreadInfo>();
            foreach (var t in _threads.Values)
            {
                if (!t.Thread.IsAlive) { _threads.TryRemove(t.Thread, out _); continue; }
                list.Add(new ThreadInfo(t.Thread.ManagedThreadId, t.Thread.Name ?? $"thread {t.Thread.ManagedThreadId}", t.Kind, t.Started,
                    Stopwatch.GetElapsedTime(t.StartTicks).TotalSeconds, DescribeState(t.Thread), t.Generation != gen));
            }
            return list.OrderBy(t => t.Started).ToList();
        }
    }

    public IReadOnlyList<WorkInfo> RunningWork =>
        _work.Keys.Select(w => new WorkInfo(w.Kind, w.Label, Stopwatch.GetElapsedTime(w.StartTicks).TotalSeconds))
            .OrderByDescending(w => w.RunningSeconds).ToList();

    internal static string DescribeState(Thread t)
    {
        try
        {
            var s = t.ThreadState;
            if ((s & System.Threading.ThreadState.WaitSleepJoin) != 0) return "Waiting";
            if ((s & System.Threading.ThreadState.Unstarted) != 0) return "Created";
            if ((s & System.Threading.ThreadState.Stopped) != 0) return "Finished";
            return "Running";
        }
        catch (ThreadStateException) { return "Unknown"; }
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}
