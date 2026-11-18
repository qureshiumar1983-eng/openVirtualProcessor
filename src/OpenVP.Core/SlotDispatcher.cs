using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>
/// One worker thread per program slot. Every event delivered to a program (panel presses, timer
/// callbacks, socket data) runs here, one at a time, so user code never sees two of its own
/// events racing each other.
/// </summary>
public sealed class SlotDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly Action<Exception> _onError;
    private readonly Func<bool>? _isPaused;
    private volatile bool _disposed;

    /// <param name="isPaused">True while the debugger holds the slot at a step; Invoke then waits without timing out.</param>
    public SlotDispatcher(string name, Action<Exception> onError, Func<bool>? isPaused = null)
    {
        _onError = onError;
        _isPaused = isPaused;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    public bool IsCurrentThread => Thread.CurrentThread == _thread;

    public void Post(Action action)
    {
        if (_disposed) return;
        try { _queue.Add(action); } catch (InvalidOperationException) { /* shutting down */ }
    }

    /// <summary>Run synchronously on the slot thread (or inline if already on it).</summary>
    public void Invoke(Action action)
    {
        if (IsCurrentThread) { action(); return; }
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Post(() =>
        {
            try { action(); } catch (Exception e) { error = e; } finally { done.Set(); }
        });
        // A slot held by the step debugger is waiting on the user, not hung: keep waiting.
        while (!done.Wait(TimeSpan.FromSeconds(30)))
            if (_isPaused?.Invoke() != true)
                throw new TimeoutException("Slot thread did not respond within 30 s.");
        if (error != null) throw new AggregateException(error);
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception e) { _onError(e); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        if (!IsCurrentThread) _thread.Join(TimeSpan.FromSeconds(5));
    }
}
