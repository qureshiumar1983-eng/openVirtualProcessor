namespace OpenVP.Core;

/// <summary>What the crash policy decided for one fault.</summary>
public sealed record CrashDecision(bool Restart, string Text);

/// <summary>What the console shows for a slot's crash handling.</summary>
public sealed record CrashStatus(string Mode, int RestartsInWindow, DateTimeOffset? LastTime, string? LastAction, string? LastReason);

/// <summary>
/// Acts on a program that faults: stays stopped, restarts it, or restarts it a limited number of times. A fault is a slot
/// going to Error after it was running (<see cref="ProgramSlot.Fault"/>: an unhandled exception when the slot's policy
/// counts those, or one event running longer than the policy's stuck time). A program that fails to load or start is not
/// a fault, so a broken program is never restarted over and over: when a restart itself fails, the slot stays in Error.
/// </summary>
public sealed class CrashSupervisor : IDisposable
{
    /// <summary>Even with plain "restart", this many restarts inside <see cref="LoopWindow"/> mean a restart loop, and the slot stays stopped.</summary>
    public const int LoopLimit = 10;
    public static readonly TimeSpan LoopWindow = TimeSpan.FromMinutes(1);

    private sealed class State
    {
        public readonly List<DateTimeOffset> Restarts = new();
        public DateTimeOffset? LastTime;
        public string? LastAction, LastReason;
        public long Crashes, TotalRestarts;
    }

    private readonly Processor _processor;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _restartDelay;
    private readonly State[] _state;
    private readonly Timer? _timer;
    private readonly Action<ProgramSlot, string> _onFault;
    private int _disposed;

    /// <param name="restartDelay">Pause between a fault and the restart, so a crashing program doesn't spin.</param>
    /// <param name="stuckCheck">How often to look for stuck slots; null turns that off (tests).</param>
    public CrashSupervisor(Processor processor, TimeSpan? restartDelay = null, TimeSpan? stuckCheck = null, Func<DateTimeOffset>? clock = null)
    {
        _processor = processor;
        _restartDelay = restartDelay ?? TimeSpan.FromSeconds(2);
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _state = processor.Slots.Select(_ => new State()).ToArray();
        _onFault = OnFault;
        foreach (var s in processor.Slots) s.Faulted += _onFault;
        var every = stuckCheck ?? TimeSpan.FromSeconds(1);
        if (every > TimeSpan.Zero) _timer = new Timer(_ => CheckStuck(), null, every, every);
    }

    /// <summary>The policy's decision for a fault now, given the restarts that already happened. Pure, so it can be tested alone.</summary>
    public static CrashDecision Decide(CrashPolicy policy, IReadOnlyList<DateTimeOffset> restarts, DateTimeOffset now)
    {
        if (policy.Mode == CrashPolicy.Stop) return new(false, "policy is to stay stopped");
        var loop = restarts.Count(t => now - t <= LoopWindow);
        if (policy.Mode == CrashPolicy.Restart)
            return loop >= LoopLimit
                ? new(false, $"{loop} restarts in the last minute, so it is stopped to avoid a restart loop")
                : new(true, "restarting");
        var window = TimeSpan.FromMinutes(policy.WindowMinutes);
        var n = restarts.Count(t => now - t <= window);
        return n >= policy.MaxRestarts
            ? new(false, $"already restarted {n} time{(n == 1 ? "" : "s")} in {policy.WindowMinutes} min, so it stays stopped")
            : new(true, $"restarting ({n + 1} of {policy.MaxRestarts} in {policy.WindowMinutes} min)");
    }

    public CrashStatus Status(int slot)
    {
        var st = _state[slot - 1];
        var policy = _processor.Slot(slot).Settings.Crash;
        lock (st)
        {
            var window = policy.Mode == CrashPolicy.Limited ? TimeSpan.FromMinutes(policy.WindowMinutes) : LoopWindow;
            var now = _now();
            return new CrashStatus(policy.Mode, st.Restarts.Count(t => now - t <= window), st.LastTime, st.LastAction, st.LastReason);
        }
    }

    /// <summary>Faults and automatic restarts of a slot since OpenVP started (for the Prometheus metrics).</summary>
    public (long Crashes, long Restarts) Totals(int slot)
    {
        var st = _state[slot - 1];
        lock (st) return (st.Crashes, st.TotalRestarts);
    }

    /// <summary>Forget the restarts counted for a slot, because a person started or loaded it.</summary>
    public void ClearHistory(int slot)
    {
        var st = _state[slot - 1];
        lock (st) st.Restarts.Clear();
    }

    /// <summary>Raised when the policy acts on a crash: slot number, what it decided ("restarting", "staying stopped"...) and why.</summary>
    public event Action<int, string, string>? Acted;

    private void OnFault(ProgramSlot slot, string reason) => Task.Run(() => Handle(slot, reason));

    /// <summary>Applies the slot's policy to a fault. Runs on its own thread; returns when it has restarted the slot or left it stopped.</summary>
    internal void Handle(ProgramSlot slot, string reason)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var st = _state[slot.Number - 1];
        var generation = slot.Generation;
        var path = slot.ProgramPath;
        CrashDecision d;
        lock (st)
        {
            var now = _now();
            st.Restarts.RemoveAll(t => now - t > TimeSpan.FromDays(1));
            d = Decide(slot.Settings.Crash, st.Restarts, now);
            st.LastTime = now;
            st.Crashes++;
            st.LastReason = reason;
            st.LastAction = d.Restart ? "restarted" : "stopped";
            if (d.Restart && path != null) { st.Restarts.Add(now); st.TotalRestarts++; }
        }
        slot.Log(d.Restart ? "Warning" : "Error", $"Crash policy: {d.Text} ({reason}).");
        try { Acted?.Invoke(slot.Number, d.Text, reason); } catch { }
        if (!d.Restart || path == null)
        {
            if (d.Restart) { lock (st) st.LastAction = "stopped"; slot.Log("Error", "Crash policy: there is no program file to restart from; the slot stays stopped."); }
            StopRuntime(slot);
            return;
        }
        if (_restartDelay > TimeSpan.Zero) Thread.Sleep(_restartDelay);
        if (Volatile.Read(ref _disposed) != 0) return;
        // Someone loaded, stopped or unloaded the slot meanwhile: leave it to them.
        if (slot.Generation != generation || slot.State != SlotState.Error)
        {
            slot.Log("Notice", "Crash policy: the slot was changed meanwhile, so it isn't restarted.");
            return;
        }
        try
        {
            _processor.LoadProgram(slot.Number, path, start: true);
            slot.Log("Notice", "Crash policy: restarted the program.");
        }
        catch (Exception e)
        {
            // Loading or starting failed: the slot is in Error, which isn't a fault, so nothing restarts it again.
            lock (st) st.LastAction = "restart failed";
            slot.Log("Error", $"Crash policy: the restart failed ({e.Message}); the slot stays in Error.");
        }
    }

    // Best effort: the program is done, so stop it without waiting forever for a stuck thread.
    private static void StopRuntime(ProgramSlot slot)
    {
        var rt = slot.Runtime;
        if (rt == null) return;
        var stop = Task.Run(() => { try { rt.Stop(); } catch { } });
        stop.Wait(TimeSpan.FromSeconds(5));
    }

    private void CheckStuck()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        foreach (var slot in _processor.Slots)
        {
            var secs = slot.Settings.Crash.StuckSeconds;
            if (secs <= 0 || slot.State != SlotState.Running || slot.Stepper.IsPaused) continue;
            var snap = slot.Dispatcher.GetSnapshot();
            if (snap.CurrentLabel != null && snap.CurrentRunningSeconds >= secs)
                slot.Fault($"stuck in {snap.CurrentLabel} for {snap.CurrentRunningSeconds:0.#} s");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer?.Dispose();
        foreach (var s in _processor.Slots) s.Faulted -= _onFault;
    }
}
