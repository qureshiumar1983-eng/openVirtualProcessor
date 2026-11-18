namespace OpenVP.Core;

/// <summary>
/// The one-shot timers a runtime has armed. Each timer takes itself out of the set when it fires or is
/// disposed, so a program that retriggers a Delay or One Shot millions of times doesn't pile up dead
/// timers until it stops. <see cref="CancelAll"/> drops whatever is still pending.
/// </summary>
public sealed class TimerSet
{
    private readonly object _gate = new();
    private readonly HashSet<OneShot> _live = new();

    /// <summary>Timers armed and not yet fired or disposed.</summary>
    public int Count { get { lock (_gate) return _live.Count; } }

    /// <summary>Run <paramref name="onFire"/> once, on a thread-pool thread, after <paramref name="ms"/>.</summary>
    public IDisposable After(long ms, Action onFire)
    {
        var t = new OneShot(this, onFire);
        lock (_gate) _live.Add(t);
        t.Arm(Math.Max(0, ms));
        return t;
    }

    public void CancelAll()
    {
        OneShot[] all;
        lock (_gate)
        {
            all = _live.ToArray();
            _live.Clear();
        }
        foreach (var t in all) t.Dispose();
    }

    private void Remove(OneShot t) { lock (_gate) _live.Remove(t); }

    private sealed class OneShot : IDisposable
    {
        private readonly TimerSet _owner;
        private readonly Action _onFire;
        private readonly Timer _timer;

        public OneShot(TimerSet owner, Action onFire)
        {
            _owner = owner;
            _onFire = onFire;
            // Created idle and armed after it is in the set, so even a 0 ms timer finds itself there.
            _timer = new Timer(static s => ((OneShot)s!).Fire(), this, Timeout.Infinite, Timeout.Infinite);
        }

        public void Arm(long ms)
        {
            try { _timer.Change(ms, Timeout.Infinite); }
            catch (ObjectDisposedException) { } // cancelled before it was armed
        }

        private void Fire()
        {
            Dispose(); // out of the set before the callback runs
            _onFire();
        }

        public void Dispose()
        {
            _owner.Remove(this);
            _timer.Dispose();
        }
    }
}
