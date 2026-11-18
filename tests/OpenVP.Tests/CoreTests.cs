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
}
