using OpenVP.Core;
using OpenVP.Panels;

namespace OpenVP.Tests;

public static class CoreTests
{
    sealed class FakeRuntime(ProgramSlot slot) : IProgramRuntime
    {
        public int Started, Stopped, Disposed;
        public bool DisposedFlag;
        public string Kind => "fake";
        public string ProgramName => "fake";
        public void Start() { Interlocked.Increment(ref Started); Thread.Sleep(20); }
        public void Stop() { Interlocked.Increment(ref Stopped); Thread.Sleep(20); }
        public void Dispose()
        {
            // Cleanup on the slot thread must still work during unload.
            slot.Dispatcher.Invoke(() => { });
            DisposedFlag = true;
            Interlocked.Increment(ref Disposed);
        }
        public IReadOnlyList<SignalInfo> GetSignals() => Array.Empty<SignalInfo>();
        public bool TrySetSignal(string name, object value) => false;
    }

    [Test]
    public static void Events_queued_before_unload_do_not_run_after_it()
    {
        using var p = new Processor(Paths.TempData());
        var slot = p.Slot(1);
        var rt = new FakeRuntime(slot);
        slot.Load("fake", rt);
        slot.Start();

        var ranAfterDispose = 0;
        var gate = new ManualResetEventSlim();
        slot.Dispatcher.Post(() => gate.Wait(2000));                     // hold the slot thread
        for (var i = 0; i < 50; i++)
            slot.Dispatcher.Post(() => { if (rt.DisposedFlag) Interlocked.Increment(ref ranAfterDispose); });

        var unload = Task.Run(slot.Unload);
        Thread.Sleep(50);
        gate.Set();
        Assert.True(unload.Wait(5000), "unload finished");
        Thread.Sleep(50);
        Assert.Equal(0, ranAfterDispose, "events run against a disposed program");
        Assert.Equal(1, rt.Disposed, "runtime disposed once");
        Assert.Equal(SlotState.Empty, slot.State, "slot empty");
    }

    [Test]
    public static void Invoke_on_a_shut_down_dispatcher_fails_fast()
    {
        var d = new SlotDispatcher("t", _ => { });
        d.Dispose();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { d.Invoke(() => { }); throw new AssertException("expected ObjectDisposedException"); }
        catch (ObjectDisposedException) { }
        Assert.True(sw.ElapsedMilliseconds < 1000, $"failed fast ({sw.ElapsedMilliseconds} ms)");
    }

    [Test]
    public static void Dispatcher_runs_events_in_order_on_one_thread_for_every_post_form()
    {
        using var d = new SlotDispatcher("t", _ => { });
        var seen = new List<int>(); var threads = new HashSet<int>();
        for (var i = 0; i < 300; i++)
        {
            var n = i;
            void Rec(int v) { seen.Add(v); threads.Add(Environment.CurrentManagedThreadId); }
            if (n % 3 == 0) d.Post(() => Rec(n));
            else if (n % 3 == 1) d.Post(static s => { var (l, t, v) = ((List<int>, HashSet<int>, int))s!; l.Add(v); t.Add(Environment.CurrentManagedThreadId); }, (seen, threads, n));
            else d.Post(static (a, b, v) => { ((List<int>)a!).Add(v); ((HashSet<int>)b!).Add(Environment.CurrentManagedThreadId); }, seen, threads, n);
        }
        d.Invoke(() => { });
        Assert.Equal(300, seen.Count, "all ran");
        for (var i = 0; i < 300; i++) Assert.Equal(i, seen[i], "FIFO");
        Assert.Equal(1, threads.Count, "one thread");
    }

    [Test]
    public static void Dispatcher_drops_posts_but_runs_invokes_after_quiesce()
    {
        using var d = new SlotDispatcher("t", _ => { });
        var posted = 0; var invoked = 0;
        d.Quiesce();
        d.Post(() => Interlocked.Increment(ref posted));
        d.Post(_ => Interlocked.Increment(ref posted), null);
        d.Post((_, _, _) => Interlocked.Increment(ref posted), null, null, 0);
        d.Invoke(() => Interlocked.Increment(ref invoked));
        Assert.Equal(0, posted, "posts after quiesce dropped");
        Assert.Equal(1, invoked, "invoke after quiesce ran");
    }

    [Test]
    public static void Dispatcher_cancels_queued_invokes_on_dispose()
    {
        var d = new SlotDispatcher("t", _ => { });
        var gate = new ManualResetEventSlim();
        d.Post(() => gate.Wait(2000));                                   // hold the thread
        var ran = 0;
        var inv = Task.Run(() => { try { d.Invoke(() => Interlocked.Increment(ref ran)); return (Exception?)null; } catch (Exception e) { return e; } });
        Thread.Sleep(100);                                               // let the invoke queue up behind the gate
        var disposing = Task.Run(d.Dispose);
        Thread.Sleep(50);
        gate.Set();
        Assert.True(inv.Wait(5000) && disposing.Wait(7000), "both finished");
        Assert.True(inv.Result is AggregateException { InnerException: ObjectDisposedException }, "queued invoke cancelled with ObjectDisposedException");
        Assert.Equal(0, ran, "cancelled invoke did not run");
    }

    [Test]
    public static void Concurrent_lifecycle_calls_do_not_interleave()
    {
        using var p = new Processor(Paths.TempData());
        var slot = p.Slot(1);
        var runtimes = new System.Collections.Concurrent.ConcurrentBag<FakeRuntime>();
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            var rt = new FakeRuntime(slot); runtimes.Add(rt);
            slot.Load("fake" + i, rt);
            slot.Start();
            slot.Stop();
            slot.Start();
        })).ToArray();
        Assert.True(Task.WaitAll(tasks, 10_000), "all finished");
        slot.Unload();
        foreach (var rt in runtimes)
        {
            Assert.Equal(1, rt.Disposed, "each runtime disposed exactly once");
            Assert.True(rt.Stopped <= rt.Started, "never stopped more than started");
        }
    }

    [Test]
    public static void Unknown_slot_number_is_rejected()
    {
        using var p = new Processor(Paths.TempData(), 3);
        Assert.True(p.IsValidSlot(1) && p.IsValidSlot(3), "1 and 3 valid");
        Assert.True(!p.IsValidSlot(0) && !p.IsValidSlot(4), "0 and 4 invalid");
    }

    [Test]
    public static void Long_cip_serial_is_cut_to_fit_its_length_byte()
    {
        var text = new string('x', 400);
        var f = Cip.Serial(7, text);
        Assert.Equal(4 + Cip.MaxSerialBytes, (int)f[3 + 3], "length byte doesn't wrap");
        var d = Cip.DecodeJoin(f[0], f.AsSpan(3))!.Value;
        Assert.Equal(Cip.MaxSerialBytes, ((string)d.value).Length, "decoded length");

        try { Cip.Frame(Cip.Data, new byte[70_000]); throw new AssertException("expected ArgumentException"); }
        catch (ArgumentException) { }
    }

    [Test]
    public static void Timer_set_forgets_timers_that_fired_or_were_disposed()
    {
        var set = new TimerSet();
        int fired = 0;
        for (int i = 0; i < 50; i++) set.After(0, () => Interlocked.Increment(ref fired));
        Assert.Eventually(() => Volatile.Read(ref fired) == 50 && set.Count == 0, "fired timers leave the set");

        var cancelled = set.After(10_000, () => throw new AssertException("a disposed timer fired"));
        Assert.Equal(1, set.Count, "armed timer is tracked");
        cancelled.Dispose();
        cancelled.Dispose(); // idempotent, like Timer.Dispose
        Assert.Equal(0, set.Count, "disposed timer leaves the set");

        set.After(10_000, () => throw new AssertException("cancelled timer fired"));
        set.After(10_000, () => throw new AssertException("cancelled timer fired"));
        set.CancelAll();
        Assert.Equal(0, set.Count, "CancelAll empties the set");

        // A retriggered timer (dispose the old one, arm a new one) never accumulates.
        IDisposable? last = null;
        for (int i = 0; i < 1000; i++) { last?.Dispose(); last = set.After(10_000, () => { }); }
        Assert.Equal(1, set.Count, "one live timer after 1000 retriggers");
        set.CancelAll();
    }
}
