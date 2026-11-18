using OpenVP.Core;

namespace OpenVP.Tests;

public static class MetricsTests
{
    private sealed class IdleRuntime : IProgramRuntime
    {
        public string Kind => "fake";
        public string ProgramName => "fake";
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
        public IReadOnlyList<SignalInfo> GetSignals() => Array.Empty<SignalInfo>();
        public bool TrySetSignal(string name, object value) => false;
    }

    [Test]
    public static void A_blocked_slot_thread_is_reported_as_stuck()
    {
        using var p = new Processor(Paths.TempData());
        var slot = p.Slot(1);
        slot.Load("fake", new IdleRuntime());
        slot.Start();

        using var gate = new ManualResetEventSlim();
        slot.Dispatcher.Post(() => gate.Wait(10_000), "Test.Hang");
        Assert.Eventually(() => p.Metrics.Snapshot(false).Slots[0].CurrentHandler == "Test.Hang", "handler shows as running");

        var s = p.Metrics.Snapshot(false).Slots[0];
        Assert.Equal("Busy", s.Status, "not stuck yet");

        Thread.Sleep(MetricsSampler.StuckHandler + TimeSpan.FromMilliseconds(200));
        s = p.Metrics.Snapshot(false).Slots[0];
        Assert.Equal("Stuck", s.Status, "status after the threshold");
        Assert.True(s.Warnings.Any(w => w.Contains("Test.Hang")), "warning names the handler");
        Assert.True(slot.RunConsoleCommand("taskstat").Contains("Test.Hang"), "taskstat shows it");

        gate.Set();
        Assert.Eventually(() => p.Metrics.Snapshot(false).Slots[0].CurrentHandler == null, "handler finished");
        s = p.Metrics.Snapshot(false).Slots[0];
        Assert.True(s.LongestHandlerSeconds >= MetricsSampler.StuckHandler.TotalSeconds, "longest event recorded");
        Assert.Equal("Test.Hang", s.LongestHandler, "longest event label");
    }

    [Test]
    public static void Threads_left_running_after_unload_are_reported_as_orphaned()
    {
        using var p = new Processor(Paths.TempData());
        var slot = p.Slot(1);
        slot.Load("fake", new IdleRuntime());
        slot.Start();

        using var stop = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var t = new Thread(() => { using (slot.Metrics.TrackThread("CrestronThread")) { started.Set(); stop.Wait(10_000); } }) { IsBackground = true, Name = "poller" };
        t.Start();
        started.Wait(2000);

        var info = p.Metrics.Snapshot(false).Slots[0].Threads.Single();
        Assert.Equal("poller", info.Name, "thread listed");
        Assert.True(!info.Orphaned, "not orphaned while its program runs");

        slot.Unload();
        var s = p.Metrics.Snapshot(false).Slots[0];
        Assert.True(s.Threads.Single().Orphaned, "orphaned after unload");
        Assert.True(s.Warnings.Any(w => w.Contains("poller")), "warning names the thread");

        stop.Set();
        t.Join(2000);
        Assert.Eventually(() => p.Metrics.Snapshot(false).Slots[0].Threads.Count == 0, "finished thread drops off");
    }

    [Test]
    public static void Background_work_and_event_rate_are_counted()
    {
        using var p = new Processor(Paths.TempData());
        var slot = p.Slot(1);
        slot.Load("fake", new IdleRuntime());
        slot.Start();

        using var gate = new ManualResetEventSlim();
        slot.Metrics.QueueWork("CrestronInvoke", "Test.Work", () => gate.Wait(5000));
        Assert.Eventually(() => p.Metrics.Snapshot(false).Slots[0].RunningWork.Any(w => w.Label == "Test.Work"), "work listed while running");
        gate.Set();
        Assert.Eventually(() => p.Metrics.Snapshot(false).Slots[0].BackgroundWorkCompleted == 1, "work counted when done");

        for (var i = 0; i < 20; i++) slot.Dispatcher.Post(() => { });
        Assert.Eventually(() => p.Metrics.Snapshot(false).Slots[0].EventsProcessed >= 20, "events processed");
        Assert.Eventually(() => p.Metrics.Snapshot().History.Count >= 1, "process sampled", 4000);
        var m = p.Metrics.Snapshot();
        Assert.True(m.WorkingSetMb > 0 && m.ProcessThreads > 0, "process numbers filled in");
        Assert.True(slot.RunConsoleCommand("ramfree").Contains("MB"), "ramfree");
        Assert.True(slot.RunConsoleCommand("cpuload").Contains("CPU"), "cpuload");
    }
}
