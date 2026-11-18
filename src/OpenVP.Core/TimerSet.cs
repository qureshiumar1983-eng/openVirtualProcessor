using System.Diagnostics;

namespace OpenVP.Core;

/// <summary>
/// The timers one program slot has armed (every Delay, One Shot and Stepper step). One OS timer serves
/// them all: it is armed only for the earliest entry and only while something is pending, so an idle slot
/// has no timer running and a slot with 10,000 pending one-shots still has one.
///
/// Times are kept in 10 ms ticks, the unit SIMPL times are compiled in: a delay is rounded up to the next
/// tick boundary, so a timer never fires early. Everything due by the time the OS timer wakes goes to the
/// slot thread in one post and runs in due order, then arming order; each callback is its own step (its own
/// solve, in a runtime), as when every timer had its own post.
///
/// A timer takes itself out of the set when it fires or is disposed, so a program that retriggers a Delay or
/// One Shot millions of times doesn't pile up dead timers. A disposed timer never runs, even when it was
/// already queued in the batch on its way to the slot thread. <see cref="CancelAll"/> drops everything
/// pending or queued.
/// </summary>
public sealed class TimerSet : IDisposable
{
    /// <summary>The resolution: one tick is 10 ms.</summary>
    public const int TickMs = 10;

    private const int Pending = 0, Queued = 1, Done = 2, Cancelled = 3;
    private const long Disarmed = long.MaxValue;
    private const long MaxDueMs = 4_294_967_294; // Timer.Change's limit; a longer wait just wakes early and re-arms

    private static readonly double ToMs = 1000.0 / Stopwatch.Frequency;

    private readonly object _gate = new();
    private readonly long _t0 = Stopwatch.GetTimestamp();
    private readonly Func<SlotDispatcher>? _dispatcher;
    private readonly Func<bool>? _accept;
    private readonly Action<Exception>? _onError;
    private readonly Stack<List<Entry>> _spareBatches = new();
    private Timer? _timer;

    // Min-heap on (Due, Seq); each entry knows its index, so a disposed timer is removed at once.
    private Entry[] _heap = new Entry[16];
    private int _count;
    private long _seq;
    private long _armedDue = Disarmed; // the tick the OS timer is set for (never later than the earliest entry)
    private int _generation;
    private bool _disposed;
    private int _wakeups, _batches;

    /// <summary>Callbacks run on a thread-pool thread, one batch at a time, as the OS timer wakes.</summary>
    public TimerSet() { }

    /// <param name="dispatcher">Each batch of due callbacks is posted to this dispatcher, in one post, and runs on its
    /// thread. A function, because a slot gets a new dispatcher when it loads a program, after the runtime is built.</param>
    /// <param name="accept">Asked before each callback runs; false skips it (the program has stopped).</param>
    /// <param name="onError">Gets an exception a callback threw; the rest of the batch still runs.</param>
    public TimerSet(Func<SlotDispatcher> dispatcher, Func<bool>? accept = null, Action<Exception>? onError = null)
    {
        _dispatcher = dispatcher;
        _accept = accept;
        _onError = onError;
    }

    /// <summary>Timers armed and not yet fired or disposed.</summary>
    public int Count { get { lock (_gate) return _count; } }

    /// <summary>How often the OS timer has woken (a diagnostic: it should follow the timers, not the clock).</summary>
    public int Wakeups => Volatile.Read(ref _wakeups);

    /// <summary>Batches of due callbacks delivered.</summary>
    public int Batches => Volatile.Read(ref _batches);

    /// <summary>Run <paramref name="onFire"/> once after <paramref name="ms"/>, rounded up to the next 10 ms tick.</summary>
    public IDisposable After(long ms, Action onFire)
    {
        var e = new Entry(this, onFire);
        List<Entry>? now = null;
        lock (_gate)
        {
            if (_disposed) { e.State = Cancelled; return e; }
            var nowMs = NowMs();
            e.Seq = ++_seq;
            e.Generation = _generation;
            if (ms <= 0)
            {
                // Due now: the current tick, so it sorts after anything already due and before anything later.
                e.Due = (long)(nowMs / TickMs);
                if (_dispatcher != null)
                {
                    // Post at once (with everything already due, ahead of it) instead of waiting for the OS timer.
                    now = DrainDue(nowMs);
                    e.State = Queued;
                    now.Add(e);
                    _batches++;
                    if (_count > 0) ArmFor(_heap[0].Due, nowMs); else Disarm();
                }
            }
            else e.Due = (long)Math.Ceiling((nowMs + ms) / TickMs);
            if (now == null)
            {
                Push(e);
                if (e.Due < _armedDue) ArmFor(e.Due, nowMs);
            }
        }
        if (now != null) Post(now);
        return e;
    }

    /// <summary>Drop every timer that is pending or on its way to the slot thread. The set stays usable.</summary>
    public void CancelAll()
    {
        lock (_gate)
        {
            _generation++; // anything already queued in a batch is from the old generation and is skipped
            for (int i = 0; i < _count; i++)
            {
                _heap[i].State = Cancelled;
                _heap[i].Index = -1;
                _heap[i] = null!;
            }
            _count = 0;
            Disarm();
        }
    }

    /// <summary>Cancel everything and release the OS timer. Later <see cref="After"/> calls arm nothing.</summary>
    public void Dispose()
    {
        CancelAll();
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private double NowMs() => (Stopwatch.GetTimestamp() - _t0) * ToMs;

    // ------------------------------------------------------------------ the OS timer (all under _gate)

    private void ArmFor(long dueTick, double nowMs)
    {
        _armedDue = dueTick;
        var wait = Math.Clamp((long)Math.Ceiling(dueTick * (double)TickMs - nowMs), 0, MaxDueMs);
        _timer ??= new Timer(static s => ((TimerSet)s!).Wake(), this, Timeout.Infinite, Timeout.Infinite);
        _timer.Change(wait, Timeout.Infinite);
    }

    private void Disarm()
    {
        if (_armedDue == Disarmed) return;
        _armedDue = Disarmed;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Wake()
    {
        List<Entry>? batch;
        lock (_gate)
        {
            if (_disposed) return;
            _wakeups++;
            var nowMs = NowMs();
            _armedDue = Disarmed;
            batch = DrainDue(nowMs);
            // The earliest entry that is left (an entry cancelled since the OS timer was set can make this a wake
            // for nothing; that costs one wake, then the timer follows the entries again).
            if (_count > 0) ArmFor(_heap[0].Due, nowMs);
            if (batch.Count == 0) { _spareBatches.Push(batch); return; }
            _batches++;
        }
        Post(batch);
    }

    // Everything due by nowMs, in due order then arming order (under _gate).
    private List<Entry> DrainDue(double nowMs)
    {
        var batch = _spareBatches.Count > 0 ? _spareBatches.Pop() : new List<Entry>();
        while (_count > 0 && _heap[0].Due * (double)TickMs <= nowMs)
        {
            var e = PopRoot();
            e.State = Queued;
            batch.Add(e);
        }
        return batch;
    }

    private void Post(List<Entry> batch)
    {
        if (_dispatcher != null) _dispatcher().Post(static (self, b, _) => ((TimerSet)self!).Run((List<Entry>)b!), this, batch, 0);
        else Run(batch);
    }

    /// <summary>On the target thread: run what is still wanted, in the order the batch was built.</summary>
    private void Run(List<Entry> batch)
    {
        Exception? first = null;
        var generation = Volatile.Read(ref _generation);
        foreach (var e in batch)
        {
            // Done only if nobody disposed it since it was queued, and the set wasn't cancelled wholesale.
            if (e.Generation != generation || Interlocked.CompareExchange(ref e.State, Done, Queued) != Queued) continue;
            if (_accept != null && !_accept()) continue;
            try { e.OnFire(); }
            catch (Exception ex)
            {
                if (_onError != null) _onError(ex);
                else first ??= ex;
            }
            generation = Volatile.Read(ref _generation); // a callback may have cancelled everything
        }
        batch.Clear();
        lock (_gate) if (_spareBatches.Count < 4) _spareBatches.Push(batch);
        if (first != null) throw first;
    }

    // ------------------------------------------------------------------ the heap (all under _gate)

    private static bool Before(Entry a, Entry b) => a.Due != b.Due ? a.Due < b.Due : a.Seq < b.Seq;

    private void Push(Entry e)
    {
        if (_count == _heap.Length) Array.Resize(ref _heap, _count * 2);
        _heap[_count] = e;
        e.Index = _count++;
        SiftUp(e.Index);
    }

    private Entry PopRoot()
    {
        var root = _heap[0];
        RemoveAt(0);
        return root;
    }

    private void RemoveAt(int i)
    {
        var removed = _heap[i];
        var last = _heap[--_count];
        _heap[_count] = null!;
        removed.Index = -1;
        if (i == _count) return;
        _heap[i] = last;
        last.Index = i;
        SiftDown(i);
        SiftUp(last.Index);
    }

    private void SiftUp(int i)
    {
        var e = _heap[i];
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (!Before(e, _heap[parent])) break;
            _heap[i] = _heap[parent];
            _heap[i].Index = i;
            i = parent;
        }
        _heap[i] = e;
        e.Index = i;
    }

    private void SiftDown(int i)
    {
        var e = _heap[i];
        while (true)
        {
            var child = 2 * i + 1;
            if (child >= _count) break;
            if (child + 1 < _count && Before(_heap[child + 1], _heap[child])) child++;
            if (!Before(_heap[child], e)) break;
            _heap[i] = _heap[child];
            _heap[i].Index = i;
            i = child;
        }
        _heap[i] = e;
        e.Index = i;
    }

    // ------------------------------------------------------------------ one armed timer

    private sealed class Entry : IDisposable
    {
        private readonly TimerSet _owner;
        private readonly Action _onFire;
        public long Due, Seq;
        public int Index = -1, State, Generation;

        public Entry(TimerSet owner, Action onFire) { _owner = owner; _onFire = onFire; }

        public void OnFire() => _onFire();

        public void Dispose()
        {
            if (Volatile.Read(ref State) is Done or Cancelled) return;
            lock (_owner._gate)
            {
                if (State == Pending)
                {
                    if (Index >= 0) _owner.RemoveAt(Index);
                    State = Cancelled;
                }
                else Interlocked.CompareExchange(ref State, Cancelled, Queued); // already on its way: Run will skip it
            }
        }
    }
}
