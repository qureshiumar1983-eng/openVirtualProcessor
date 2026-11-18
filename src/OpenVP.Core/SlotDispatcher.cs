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

        public Item(Action run, bool isInvoke = false, Action? cancel = null)
        { Plain = run; IsInvoke = isInvoke; Cancel = cancel; WithState = null; WithArgs = null; A = B = null; I = 0; }

        public Item(Action<object?> run, object? state)
        { WithState = run; A = state; Plain = null; WithArgs = null; B = null; I = 0; IsInvoke = false; Cancel = null; }

        public Item(Action<object?, object?, int> run, object? a, object? b, int i)
        { WithArgs = run; A = a; B = b; I = i; Plain = null; WithState = null; IsInvoke = false; Cancel = null; }

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
        _queue.Writer.TryWrite(new Item(action)); // false once disposed: shutting down
    }

    /// <summary>Queue an event without allocating: pass a static lambda and its state.</summary>
    public void Post(Action<object?> action, object? state)
    {
        if (_mode != Open) return;
        _queue.Writer.TryWrite(new Item(action, state));
    }

    /// <summary>Allocation-free post for events that carry two objects and an int (runtime-style "which node, which value").</summary>
    public void Post(Action<object?, object?, int> action, object? a, object? b, int i)
    {
        if (_mode != Open) return;
        _queue.Writer.TryWrite(new Item(action, a, b, i));
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
            () => { error = new ObjectDisposedException(_thread.Name, "The slot thread shut down before running this."); done.Set(); });
        if (!_queue.Writer.TryWrite(item)) throw new ObjectDisposedException(_thread.Name, "The slot thread has shut down.");
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
            while (reader.TryRead(out var item))
            {
                var mode = _mode;
                if (mode == Closed) { item.Cancel?.Invoke(); continue; }
                if (mode == InvokeOnly && !item.IsInvoke) continue;
                try { item.Run(); }
                catch (Exception e) { _onError(e); }
            }
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

    public void Dispose()
    {
        if (_mode == Closed) return;
        _mode = Closed;
        _queue.Writer.TryComplete();
        if (!IsCurrentThread) _thread.Join(TimeSpan.FromSeconds(5));
    }
}
