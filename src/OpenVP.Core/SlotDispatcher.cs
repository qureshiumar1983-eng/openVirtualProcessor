using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>
/// One worker thread per program slot. Every event delivered to a program (panel presses, timer
/// callbacks, socket data) runs here, one at a time, so user code never sees two of its own
/// events racing each other.
/// </summary>
public sealed class SlotDispatcher : IDisposable
{
    private const int Open = 0, InvokeOnly = 1, Closed = 2;

    private sealed record Item(Action Run, bool IsInvoke, Action? Cancel);

    private readonly BlockingCollection<Item> _queue = new();
    private readonly Thread _thread;
    private readonly Action<Exception> _onError;
    private volatile int _mode = Open;

    public SlotDispatcher(string name, Action<Exception> onError)
    {
        _onError = onError;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    public bool IsCurrentThread => Thread.CurrentThread == _thread;

    /// <summary>Queue an event. Dropped once the slot is shutting down.</summary>
    public void Post(Action action)
    {
        if (_mode != Open) return;
        try { _queue.Add(new Item(action, false, null)); } catch (InvalidOperationException) { /* shutting down */ }
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
        try { _queue.Add(item); }
        catch (InvalidOperationException) { throw new ObjectDisposedException(_thread.Name, "The slot thread has shut down."); }
        if (!done.Wait(TimeSpan.FromSeconds(30)))
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
            try { item.Run(); }
            catch (Exception e) { _onError(e); }
        }
    }

    public void Dispose()
    {
        if (_mode == Closed) return;
        _mode = Closed;
        _queue.CompleteAdding();
        if (!IsCurrentThread) _thread.Join(TimeSpan.FromSeconds(5));
    }
}
