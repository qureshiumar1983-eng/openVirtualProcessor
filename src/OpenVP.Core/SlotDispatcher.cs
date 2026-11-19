using System.Diagnostics;
using System.Threading.Channels;

namespace OpenVP.Core;

/// <summary>
/// One worker thread per program slot. Every event delivered to a program (panel presses, timer
/// callbacks, socket data) runs here, one at a time, so user code never sees two of its own
/// events racing each other.
/// </summary>
public sealed class SlotDispatcher : IDisposable
{
    private const int Open = 0, InvokeOnly = 1, Closed = 2;

    /// <summary>
    /// One queued event. A struct so posting through the state overloads allocates nothing: the
    /// delegate is meant to be a static lambda and its arguments travel in the item.
    /// </summary>
    private readonly struct Item
    {
        public readonly Action? Plain;
        public readonly Action<object?>? WithState;
        public readonly Action<object?, object?, int>? WithArgs;
        public readonly object? A, B;
        public readonly int I;
        public readonly bool IsInvoke;
        public readonly Action? Cancel;
        /// <summary>What this event is, for the metrics page: a string, or a delegate described only when asked.</summary>
        public readonly object? Label;

        public Item(Action run, bool isInvoke = false, Action? cancel = null, object? label = null)
        { Plain = run; IsInvoke = isInvoke; Cancel = cancel; Label = label; WithState = null; WithArgs = null; A = B = null; I = 0; }

        public Item(Action<object?> run, object? state)
        { WithState = run; A = state; Plain = null; WithArgs = null; B = null; I = 0; IsInvoke = false; Cancel = null; Label = null; }

        public Item(Action<object?, object?, int> run, object? a, object? b, int i, object? label = null)
        { WithArgs = run; A = a; B = b; I = i; Plain = null; WithState = null; IsInvoke = false; Cancel = null; Label = label; }

        /// <summary>The label, or the delegate that runs (turned into a name only when the metrics are read).</summary>
        public object What => Label ?? (object?)Plain ?? (object?)WithState ?? WithArgs!;

        public void Run()
        {
            if (Plain != null) Plain();
            else if (WithState != null) WithState(A);
            else WithArgs!(A, B, I);
        }
    }

    // Unbounded, one reader (the slot thread): writers never block and never take a lock.
    // Synchronous continuations: waking the reader just completes the Task it blocks on, with no
    // thread-pool hop (the only continuation is our own wait, never user code).
    private readonly Channel<Item> _queue = Channel.CreateUnbounded<Item>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = true });
    private readonly Thread _thread;
    private readonly Action<Exception> _onError;
    private readonly Func<bool>? _isPaused;
    private readonly long _spinTicks; // extra busy-wait before blocking, in Stopwatch ticks (0 = none)
    private volatile int _mode = Open;

    // Metrics, written only by the slot thread. _currentStart (Environment.TickCount64 ms) is 0 while idle;
    // _current/_longest hold a label string or a delegate, named only when the metrics are read.
    private long _processed, _busyTicks, _currentStart, _longestMs, _dequeued;
    // The only counter writers touch (the channel can't count a single-reader queue). On its own cache line,
    // so posting threads don't keep stealing the line the slot thread writes its counters to.
    private PaddedCounter _posted;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 128)]
    private struct PaddedCounter { [System.Runtime.InteropServices.FieldOffset(64)] public long Value; }
    private volatile object? _current, _longest;

    /// <summary>What the slot thread is doing, for the metrics page.</summary>
    public sealed record Snapshot(
        int QueueLength, long Processed, double BusySeconds,
        string? CurrentLabel, double CurrentRunningSeconds,
        double LongestSeconds, string? LongestLabel, string ThreadState);

    /// <param name="isPaused">True while the debugger holds the slot at a step; Invoke then waits without timing out.</param>
    public SlotDispatcher(string name, Action<Exception> onError, Func<bool>? isPaused = null)
        : this(name, onError, isPaused, 0) { }

    /// <param name="spinMicroseconds">After the queue runs dry, keep polling it this long before blocking, so the next
    /// event of a request/response stream skips the ~60 µs kernel wake. Costs a core's worth of CPU while idle; 0 (the
    /// default) blocks after the short spin only.</param>
    public SlotDispatcher(string name, Action<Exception> onError, Func<bool>? isPaused, int spinMicroseconds)
    {
        _spinTicks = Math.Max(0, spinMicroseconds) * Stopwatch.Frequency / 1_000_000;
        _onError = onError;
        _isPaused = isPaused;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    public bool IsCurrentThread => Thread.CurrentThread == _thread;

    /// <summary>Queue an event. Dropped once the slot is shutting down.</summary>
    public void Post(Action action)
    {
        if (_mode != Open) return;
        if (_queue.Writer.TryWrite(new Item(action))) Interlocked.Increment(ref _posted.Value); // false once disposed: shutting down
    }

    /// <summary>Queue an event with a name for the metrics page (e.g. "CTimer Program.OnTick"). Dropped once the slot is shutting down.</summary>
    public void Post(Action action, string? label)
    {
        if (_mode != Open) return;
        if (_queue.Writer.TryWrite(new Item(action, label: label))) Interlocked.Increment(ref _posted.Value);
    }

    /// <summary>Queue an event without allocating: pass a static lambda and its state.</summary>
    public void Post(Action<object?> action, object? state)
    {
        if (_mode != Open) return;
        if (_queue.Writer.TryWrite(new Item(action, state))) Interlocked.Increment(ref _posted.Value);
    }

    /// <summary>Allocation-free post for events that carry two objects and an int (runtime-style "which node, which value").</summary>
    public void Post(Action<object?, object?, int> action, object? a, object? b, int i) => Post(action, a, b, i, null);

    /// <param name="label">What this event is, for the metrics page: a string, or the user delegate behind it (named only when the metrics are read).</param>
    public void Post(Action<object?, object?, int> action, object? a, object? b, int i, object? label)
    {
        if (_mode != Open) return;
        if (_queue.Writer.TryWrite(new Item(action, a, b, i, label))) Interlocked.Increment(ref _posted.Value);
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
            action);
        if (!_queue.Writer.TryWrite(item)) throw new ObjectDisposedException(_thread.Name, "The slot thread has shut down.");
        Interlocked.Increment(ref _posted.Value);
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
        var reader = _queue.Reader;
        while (true)
        {
            // Metrics cost a few ns per event: the coarse millisecond clock (each event's end is the next one's
            // start) and two plain writes. The precise clock is read once per batch, for busy time.
            var batchStart = Stopwatch.GetTimestamp();
            var start = Environment.TickCount64;
            var booked = start;
            while (reader.TryRead(out var item))
            {
                Volatile.Write(ref _dequeued, _dequeued + 1);
                var mode = _mode;
                if (mode == Closed) { item.Cancel?.Invoke(); continue; }
                if (mode == InvokeOnly && !item.IsInvoke) continue;
                _current = item.What;
                Volatile.Write(ref _currentStart, start);
                try { item.Run(); }
                catch (Exception e) { _onError(e); }
                var end = Environment.TickCount64;
                Volatile.Write(ref _currentStart, 0);
                Volatile.Write(ref _processed, _processed + 1);
                if (end - start > _longestMs) { _longest = _current; Volatile.Write(ref _longestMs, end - start); }
                start = end;
                if (end - booked >= 100)
                {
                    // A queue that never runs dry still reports its busy time about every 100 ms.
                    var now = Stopwatch.GetTimestamp();
                    Volatile.Write(ref _busyTicks, _busyTicks + (now - batchStart));
                    batchStart = now;
                    booked = end;
                }
            }
            Volatile.Write(ref _busyTicks, _busyTicks + (Stopwatch.GetTimestamp() - batchStart));
            // A short spin catches the next event of a back-to-back exchange without a kernel wait.
            var spin = new SpinWait();
            while (!spin.NextSpinWillYield && !reader.TryPeek(out _)) spin.SpinOnce(-1);
            if (_spinTicks > 0)
            {
                var until = Stopwatch.GetTimestamp() + _spinTicks;
                while (Stopwatch.GetTimestamp() < until && !reader.TryPeek(out _)) Thread.SpinWait(10);
            }
            if (reader.TryPeek(out _)) continue;
            // Blocks only when the queue is empty; completes false once the writer is completed and drained.
            if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult()) break;
        }
    }

    public Snapshot GetSnapshot()
    {
        var start = Volatile.Read(ref _currentStart);
        var current = _current;
        var runningMs = start == 0 ? 0 : Math.Max(0, Environment.TickCount64 - start);
        // Busy time is booked when a batch ends; add the running event so a stuck handler counts as busy.
        return new Snapshot(
            (int)Math.Clamp(Interlocked.Read(ref _posted.Value) - Volatile.Read(ref _dequeued), 0, int.MaxValue),
            Volatile.Read(ref _processed), Volatile.Read(ref _busyTicks) / (double)Stopwatch.Frequency + runningMs / 1000.0,
            start == 0 ? null : Describe(current), runningMs / 1000.0,
            Volatile.Read(ref _longestMs) / 1000.0, Describe(_longest),
            SlotMetrics.DescribeState(_thread));
    }

    /// <summary>A readable name for a label or delegate: "Type.Method", without compiler-generated closure names.</summary>
    public static string? Describe(object? what)
    {
        if (what is string s) return s;
        if (what is not Delegate d) return null;
        var m = d.Method;
        var type = m.DeclaringType;
        while (type is { DeclaringType: not null } && type.Name.Contains('<')) type = type.DeclaringType;
        var name = m.Name;
        if (name.StartsWith('<') && name.IndexOf('>') is var end and > 1) name = name[1..end];
        return $"{type?.Name ?? "?"}.{name}";
    }

    public void Dispose() => Dispose(TimeSpan.FromSeconds(5));

    /// <summary>
    /// Shut down and wait up to <paramref name="joinTimeout"/> for the thread to finish the event it is running.
    /// A thread can't be stopped from outside, so one that is still inside an event when the wait ends (a library's
    /// worker loop, say) is left to exit on its own: it is a background thread and its queue is closed.
    /// </summary>
    public void Dispose(TimeSpan joinTimeout)
    {
        if (_mode == Closed) return;
        _mode = Closed;
        _queue.Writer.TryComplete();
        if (!IsCurrentThread) _thread.Join(joinTimeout);
    }
}
