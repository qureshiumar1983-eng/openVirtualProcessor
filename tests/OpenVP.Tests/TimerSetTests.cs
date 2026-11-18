using System.Diagnostics;
using OpenVP.Core;

namespace OpenVP.Tests;

/// <summary>The per-slot timer service: one OS timer, 10 ms ticks, batches posted to the slot thread (item 3.1).</summary>
public static class TimerSetTests
{
    private static SlotDispatcher NewSlot(List<Exception>? errors = null) =>
        new("timer-test", e => { if (errors != null) lock (errors) errors.Add(e); });

    /// <summary>Hold the slot thread until the returned gate is opened.</summary>
    private static ManualResetEventSlim Block(SlotDispatcher d)
    {
        var started = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        d.Post(() => { started.Set(); gate.Wait(); });
        started.Wait();
        return gate;
    }

    [Test]
    public static void Timers_run_on_the_slot_thread_in_due_then_arming_order()
    {
        using var d = NewSlot();
        using var set = new TimerSet(() => d);
        var order = new List<string>();
        var onSlot = true;
        void Add(string name) { onSlot &= d.IsCurrentThread; lock (order) order.Add(name); }
        // Armed back to back, so a later arm with the same delay is never due earlier.
        set.After(80, () => Add("a80"));
        set.After(30, () => Add("b30"));
        set.After(30, () => Add("c30"));
        set.After(60, () => Add("d60"));
        set.After(30, () => Add("e30"));
        set.After(0, () => Add("f0"));
        Assert.Eventually(() => { lock (order) return order.Count == 6; }, "all six fired");
        Assert.Equal("f0 b30 c30 e30 d60 a80", string.Join(' ', order), "due order, then arming order");
        Assert.True(onSlot, "callbacks ran on the slot thread");
        Assert.Equal(0, set.Count, "nothing retained after firing");
    }

    [Test]
    public static void A_zero_delay_is_posted_at_once_not_at_the_next_tick()
    {
        using var d = NewSlot();
        using var set = new TimerSet(() => d);
        var ran = new List<string>();
        var gate = Block(d); // the slot is busy, so a posted entry just waits in its queue
        set.After(0, () => { lock (ran) ran.Add("a"); });
        set.After(-5, () => { lock (ran) ran.Add("b"); });
        Assert.Equal(0, set.Count, "both were handed to the slot thread by the After call itself");
        Assert.Equal(0, set.Wakeups, "the OS timer was never needed");
        set.After(50, () => { lock (ran) ran.Add("late"); });
        set.After(0, () => { lock (ran) ran.Add("c"); });
        gate.Set();
        Assert.Eventually(() => { lock (ran) return ran.Count == 4; }, "all ran");
        Assert.Equal("a b c late", string.Join(' ', ran), "zero delays run in arming order, ahead of the later timer");
    }

    [Test]
    public static void Timers_never_fire_early_and_round_up_to_a_tick()
    {
        using var d = NewSlot();
        using var set = new TimerSet(() => d);
        foreach (var ms in new long[] { 1, 15, 25, 100 })
        {
            var sw = Stopwatch.StartNew();
            double at = -1;
            set.After(ms, () => at = sw.Elapsed.TotalMilliseconds);
            Assert.Eventually(() => at >= 0, $"{ms} ms timer fired");
            Assert.True(at >= ms, $"{ms} ms timer fired after {at:N2} ms");
        }
    }

    [Test]
    public static void A_disposed_timer_never_runs_even_when_it_is_already_queued()
    {
        var errors = new List<Exception>();
        using var d = NewSlot(errors);
        using var set = new TimerSet(() => d);
        var ran = new List<string>();
        IDisposable? c = null;
        var gate = Block(d); // the slot is busy, so everything that comes due queues up behind this
        set.After(20, () => { lock (ran) ran.Add("a"); c!.Dispose(); });
        set.After(20, () => { lock (ran) ran.Add("b"); });
        c = set.After(20, () => { lock (ran) ran.Add("c"); });
        var late = set.After(40, () => { lock (ran) ran.Add("late"); });
        Assert.Eventually(() => set.Count == 0, "all four were handed to the slot thread");
        late.Dispose(); // queued too, disposed from another thread before it runs
        gate.Set();
        Assert.Eventually(() => { lock (ran) return ran.Count >= 2; }, "a and b ran");
        Thread.Sleep(150);
        lock (ran) Assert.Equal("a b", string.Join(' ', ran), "c (disposed by a) and late (disposed from outside) never ran");
        Assert.Equal(0, errors.Count, "no errors");
    }

    [Test]
    public static void Everything_due_together_goes_to_the_slot_thread_in_one_post()
    {
        using var d = NewSlot();
        using var set = new TimerSet(() => d);
        int fired = 0;
        for (int i = 0; i < 1000; i++) set.After(50, () => Interlocked.Increment(ref fired));
        Assert.Eventually(() => Volatile.Read(ref fired) == 1000, "1,000 timers fired");
        // All armed within a millisecond or two: one 10 ms tick, or two if they straddle a boundary.
        Assert.True(set.Batches <= 2, $"1,000 timers came in {set.Batches} batches");
        Assert.True(set.Wakeups <= 2, $"1,000 timers woke the OS timer {set.Wakeups} times");
    }

    [Test]
    public static void The_os_timer_follows_the_timers_and_stops_when_none_are_left()
    {
        using var d = NewSlot();
        using var set = new TimerSet(() => d);
        Thread.Sleep(100);
        Assert.Equal(0, set.Wakeups, "a set with nothing armed never wakes");

        int fired = 0;
        set.After(30, () => Interlocked.Increment(ref fired));
        set.After(300, () => Interlocked.Increment(ref fired));
        Assert.Eventually(() => Volatile.Read(ref fired) == 2, "both fired");
        Assert.True(set.Wakeups <= 3, $"two timers woke the OS timer {set.Wakeups} times");
        var wakes = set.Wakeups;
        Thread.Sleep(300);
        Assert.Equal(wakes, set.Wakeups, "no wake-ups once the last timer has fired");

        // A cancelled timer leaves nothing behind either: the OS timer is released by CancelAll.
        set.After(100, () => throw new AssertException("a cancelled timer fired"));
        set.CancelAll();
        Thread.Sleep(250);
        Assert.Equal(wakes, set.Wakeups, "CancelAll disarms the OS timer");
    }

    [Test]
    public static void Cancel_all_drops_pending_and_queued_timers_and_the_set_keeps_working()
    {
        var errors = new List<Exception>();
        using var d = NewSlot(errors);
        using var set = new TimerSet(() => d);
        var gate = Block(d);
        set.After(10, () => throw new AssertException("a queued timer ran after CancelAll"));
        Assert.Eventually(() => set.Batches == 1, "the first timer is queued behind the busy slot");
        set.After(10_000, () => throw new AssertException("a pending timer ran after CancelAll"));
        Assert.Equal(1, set.Count, "one pending");
        set.CancelAll();
        Assert.Equal(0, set.Count, "nothing pending");
        gate.Set();
        d.Invoke(() => { });
        Assert.Equal(0, errors.Count, "nothing ran");

        int fired = 0;
        set.After(10, () => Interlocked.Increment(ref fired));
        Assert.Eventually(() => Volatile.Read(ref fired) == 1, "timers armed after CancelAll work");

        set.Dispose();
        set.After(10, () => throw new AssertException("a timer armed after Dispose ran"));
        Assert.Equal(0, set.Count, "a disposed set arms nothing");
        Thread.Sleep(100);
    }

    [Test]
    public static void A_throwing_callback_doesnt_stop_the_rest_of_the_batch()
    {
        var errors = new List<Exception>();
        using var d = NewSlot();
        using var set = new TimerSet(() => d, onError: e => { lock (errors) errors.Add(e); });
        int fired = 0;
        set.After(20, () => throw new InvalidOperationException("boom"));
        set.After(20, () => Interlocked.Increment(ref fired));
        Assert.Eventually(() => Volatile.Read(ref fired) == 1, "the second callback ran");
        lock (errors) Assert.Equal(1, errors.Count, "the error was reported");
    }

    [Test]
    public static void Arming_and_cancelling_in_any_order_keeps_the_rest_in_order()
    {
        using var d = NewSlot();
        using var set = new TimerSet(() => d);
        var rng = new Random(7);
        var fired = new List<(int group, int index)>();
        var handles = new List<(IDisposable h, int group, int index, bool keep)>();
        for (int i = 0; i < 300; i++)
        {
            var group = rng.Next(1, 8); // delays 30..100 ms in steps of 10
            var idx = i;
            var h = set.After(20 + group * 10, () => { lock (fired) fired.Add((group, idx)); });
            handles.Add((h, group, idx, true));
        }
        var expected = new List<(int group, int index)>();
        for (int i = 0; i < handles.Count; i++)
        {
            if (rng.Next(3) == 0) { handles[i].h.Dispose(); handles[i].h.Dispose(); }
            else expected.Add((handles[i].group, handles[i].index));
        }
        expected = expected.OrderBy(x => x.group).ThenBy(x => x.index).ToList();
        Assert.Eventually(() => { lock (fired) return fired.Count == expected.Count; }, "every kept timer fired");
        Thread.Sleep(100);
        lock (fired) Assert.True(fired.SequenceEqual(expected), "cancelled timers dropped, the others in delay then arming order");
        Assert.Equal(0, set.Count, "nothing retained");
    }
}
