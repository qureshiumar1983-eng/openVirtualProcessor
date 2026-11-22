using System.Text.Json;
using OpenVP.Core;

namespace OpenVP.Tests;

/// <summary>Per-slot settings, restore at start-up, crash policy, recent programs, trash and undo.</summary>
public static class ConsoleFeatureTests
{
    static string Data() => Directory.CreateTempSubdirectory("openvp-console").FullName;

    // A loader for files ending in .fake. Behavior is steered by static counters/flags per test via the Fake instance.
    sealed class FakeLoader : IProgramLoader
    {
        public readonly List<(int Slot, string File)> Loads = new();
        public Func<int, string, bool> FailLoad = (_, _) => false;
        public Func<FakeRuntime, Exception?>? OnStart;
        public bool CanLoad(string path) => path.EndsWith(".fake", StringComparison.OrdinalIgnoreCase);
        public IProgramRuntime Load(string path, ProgramSlot slot)
        {
            lock (Loads) Loads.Add((slot.Number, Path.GetFileName(path)));
            if (FailLoad(slot.Number, path)) throw new InvalidDataException("bad program");
            return new FakeRuntime(slot, this);
        }
        public int Count { get { lock (Loads) return Loads.Count; } }
    }

    sealed class FakeRuntime(ProgramSlot slot, FakeLoader loader) : IProgramRuntime
    {
        public readonly ProgramSlot Slot = slot;
        public string Kind => "fake";
        public string ProgramName => "fake";
        public void Start() { if (loader.OnStart?.Invoke(this) is { } e) throw e; }
        public void Stop() { }
        public void Dispose() { }
        public IReadOnlyList<SignalInfo> GetSignals() => Array.Empty<SignalInfo>();
        public bool TrySetSignal(string name, object value) => false;
    }

    static (Processor P, FakeLoader L, string Dir) Make(string? dir = null, int slots = 4)
    {
        dir ??= Data();
        var p = new Processor(dir, slots);
        var l = new FakeLoader();
        p.AddLoader(l);
        return (p, l, dir);
    }

    static string Upload(string dir, int slot, string name)
    {
        var d = Path.Combine(dir, "uploads", $"slot{slot:00}");
        Directory.CreateDirectory(d);
        var f = Path.Combine(d, name);
        File.WriteAllText(f, "x");
        return f;
    }

    // ---------------------------------------------------------------- settings

    [Test]
    public static void Settings_are_saved_in_slot_json_next_to_the_room_id_and_survive_a_restart()
    {
        var (p, _, dir) = Make();
        var s = p.Slot(2);
        s.SetRoomId("ROOM2");
        s.UpdateSettings(c => c.WithPatch(JsonDocument.Parse("""{"name":"Boardroom","color":"#3B82F6","autostart":false,"startOrder":5,"startDelaySeconds":12,"crash":{"mode":"limited","maxRestarts":4,"windowMinutes":20,"stuckSeconds":30,"faultOnUnhandled":true}}""").RootElement));
        s.SetRoomId("ROOM2B");   // changing the Room ID later keeps the rest
        p.Dispose();

        var p2 = new Processor(dir, 4);
        var t = p2.Slot(2).Settings;
        Assert.Equal("ROOM2B", p2.Slot(2).RoomId, "room id");
        Assert.Equal("Boardroom", t.Name, "name");
        Assert.Equal("#3b82f6", t.Color, "colour is normalized");
        Assert.True(!t.Autostart && t.StartOrder == 5 && t.StartDelaySeconds == 12, "start-up settings");
        Assert.True(t.Crash is { Mode: "limited", MaxRestarts: 4, WindowMinutes: 20, StuckSeconds: 30, FaultOnUnhandled: true }, "crash policy");
        Assert.True(File.ReadAllText(Path.Combine(dir, "program02", "slot.json")).Contains("roomId"), "same file as the Room ID");
        p2.Dispose();
    }

    [Test]
    public static void Old_slot_json_with_only_a_room_id_still_loads_and_bad_values_are_refused()
    {
        var dir = Data();
        Directory.CreateDirectory(Path.Combine(dir, "program01"));
        File.WriteAllText(Path.Combine(dir, "program01", "slot.json"), "{\"roomId\":\"OLD\"}");
        var p = new Processor(dir, 2);
        Assert.Equal("OLD", p.Slot(1).RoomId, "room id");
        Assert.True(p.Slot(1).Settings.Autostart && p.Slot(1).Settings.Crash.Mode == "stop", "defaults");

        void Bad(string json, string what)
        {
            var threw = false;
            try { p.Slot(1).UpdateSettings(c => c.WithPatch(JsonDocument.Parse(json).RootElement)); } catch (ArgumentException) { threw = true; }
            Assert.True(threw, what);
        }
        Bad("""{"color":"red"}""", "bad colour");
        Bad("""{"name":"%s"}""".Replace("%s", new string('x', 41)), "name too long");
        Bad("""{"startDelaySeconds":-1}""", "negative delay");
        Bad("""{"crash":{"mode":"explode"}}""", "unknown crash mode");
        Bad("""{"autostart":"yes"}""", "wrong type");
        Assert.True(p.Slot(1).Settings.Color == null, "nothing was saved by the bad ones");
        p.Dispose();
    }

    // ---------------------------------------------------------------- restore at start-up

    [Test]
    public static void Restore_plan_follows_start_order_then_slot_number_and_config_wins()
    {
        var (p, _, dir) = Make(slots: 5);
        foreach (var n in new[] { 1, 2, 3, 4, 5 }) p.Slot(n).RememberProgram(new ProgramMemory { File = $"p{n}.fake", Running = true });
        p.Slot(1).UpdateSettings(c => c with { StartOrder = 3, StartDelaySeconds = 5 });
        p.Slot(2).UpdateSettings(c => c with { StartOrder = 1 });
        p.Slot(3).UpdateSettings(c => c with { Autostart = false });
        p.Slot(4).UpdateSettings(c => c with { StartOrder = 3 });
        // slot 5 has no order: it sorts as 5. Slot 3 has autostart off but is named in Autoload, which wins.
        var plan = StartupRestorer.Plan(p.Slots, new Dictionary<int, string> { [3] = "cfg.fake" });
        Assert.Equal("2,1,3,4,5", string.Join(",", plan.Select(e => e.Slot)), "order");
        Assert.Equal("config", plan.Single(e => e.Slot == 3).Source, "config entry");
        Assert.Equal("remembered", plan.Single(e => e.Slot == 1).Source, "remembered entry");
        Assert.Equal(5, plan.Single(e => e.Slot == 1).DelaySeconds, "delay");

        p.Slot(2).UpdateSettings(c => c with { Autostart = false });
        Assert.True(!StartupRestorer.Plan(p.Slots, null).Any(e => e.Slot == 2), "autostart off is left out");
        p.Dispose();
    }

    [Test]
    public static void Restore_loads_in_order_with_delays_restores_running_state_and_one_failure_does_not_stop_the_rest()
    {
        var (p, loader, dir) = Make();
        Upload(dir, 1, "a.fake"); Upload(dir, 2, "b.fake"); Upload(dir, 3, "bad.fake"); Upload(dir, 4, "d.fake");
        p.Slot(1).RememberProgram(new ProgramMemory { File = "a.fake", Running = true });
        p.Slot(2).RememberProgram(new ProgramMemory { File = "b.fake", Running = false });
        p.Slot(3).RememberProgram(new ProgramMemory { File = "bad.fake", Running = true });
        p.Slot(4).RememberProgram(new ProgramMemory { File = "gone.fake", Running = true });   // its copy is missing
        p.Slot(1).UpdateSettings(c => c with { StartOrder = 4, StartDelaySeconds = 7 });
        p.Slot(2).UpdateSettings(c => c with { StartOrder = 1 });
        p.Slot(3).UpdateSettings(c => c with { StartOrder = 2, StartDelaySeconds = 2 });
        p.Slot(4).UpdateSettings(c => c with { StartOrder = 3 });
        loader.FailLoad = (_, path) => path.EndsWith("bad.fake");
        p.Dispose();

        // A new process: the same data folder.
        var (p2, l2, _) = Make(dir);
        l2.FailLoad = (_, path) => path.EndsWith("bad.fake");
        var slept = new List<double>();
        var r = new StartupRestorer(p2, d => slept.Add(d.TotalSeconds));
        var ok = r.Run(StartupRestorer.Plan(p2.Slots, null));

        Assert.Equal("2,3,1", string.Join(",", l2.Loads.Select(x => x.Slot)), "load order (slot 4 has no copy so it never reaches the loader)");
        Assert.Equal("2,7", string.Join(",", slept), "delays were waited in sequence");
        Assert.Equal(2, ok, "two slots came back");
        Assert.Equal(SlotState.Loaded, p2.Slot(2).State, "was stopped: loaded but not started");
        Assert.Equal(SlotState.Running, p2.Slot(1).State, "was running: started");
        Assert.Equal(SlotState.Error, p2.Slot(3).State, "load failure puts that slot in Error");
        Assert.Equal(SlotState.Error, p2.Slot(4).State, "missing copy puts that slot in Error");
        Assert.True(p2.RecentLog(4).Any(e => e.Message.Contains("Couldn't restore")), "the failure is in the slot log");
        Assert.True(p2.RecentLog(1).Any(e => e.Message.Contains("Restored")), "what was restored is logged");
        p2.Dispose();
    }

    [Test]
    public static void Remembered_program_is_kept_when_OpenVP_shuts_down_and_cleared_only_by_an_unload_from_the_user()
    {
        var (p, _, dir) = Make();
        var f = Upload(dir, 1, "a.fake");
        p.Slot(1).RememberProgram(new ProgramMemory { File = "a.fake", Running = true });
        p.LoadProgram(1, f);
        p.Dispose();   // shutdown unloads the slot
        var p2 = new Processor(dir, 4);
        Assert.Equal("a.fake", p2.Slot(1).Settings.Program?.File, "still remembered after a shutdown");
        p2.Slot(1).RememberRunning(false);
        p2.Slot(1).RememberWatch(true);
        p2.Dispose();
        var p3 = new Processor(dir, 4);
        Assert.True(p3.Slot(1).Settings.Program is { Running: false, Watch: true }, "running intent and watch are remembered");
        p3.Slot(1).RememberProgram(null);
        p3.Dispose();
        Assert.True(new Processor(dir, 4).Slot(1).Settings.Program == null, "forgotten");
    }

    [Test]
    public static void Config_autoload_takes_precedence_for_its_slots()
    {
        var (p, l, dir) = Make();
        var cfg = Upload(dir, 1, "cfg.fake"); Upload(dir, 1, "mem.fake");
        p.Slot(1).RememberProgram(new ProgramMemory { File = "mem.fake", Running = true });
        new StartupRestorer(p, _ => { }).Run(StartupRestorer.Plan(p.Slots, new Dictionary<int, string> { [1] = cfg }));
        Assert.Equal("cfg.fake", l.Loads.Single().File, "the Autoload program is loaded, not the remembered one");
        Assert.Equal(SlotState.Running, p.Slot(1).State, "and started");
        p.Dispose();
    }

    // ---------------------------------------------------------------- crash policy

    static readonly DateTimeOffset T0 = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public static void Crash_policy_decisions()
    {
        Assert.True(!CrashSupervisor.Decide(new CrashPolicy { Mode = "stop" }, [], T0).Restart, "stay stopped");
        Assert.True(CrashSupervisor.Decide(new CrashPolicy { Mode = "restart" }, [], T0).Restart, "restart");
        var lim = new CrashPolicy { Mode = "limited", MaxRestarts = 3, WindowMinutes = 10 };
        Assert.True(CrashSupervisor.Decide(lim, [T0.AddMinutes(-1), T0.AddMinutes(-2)], T0).Restart, "two so far: a third is allowed");
        Assert.True(!CrashSupervisor.Decide(lim, [T0.AddMinutes(-1), T0.AddMinutes(-2), T0.AddMinutes(-3)], T0).Restart, "three in the window: stay stopped");
        Assert.True(CrashSupervisor.Decide(lim, [T0.AddMinutes(-11), T0.AddMinutes(-12), T0.AddMinutes(-13)], T0).Restart, "old ones fall out of the window");
        var loop = Enumerable.Range(0, CrashSupervisor.LoopLimit).Select(i => T0.AddSeconds(-i)).ToList();
        Assert.True(!CrashSupervisor.Decide(new CrashPolicy { Mode = "restart" }, loop, T0).Restart, "plain restart still stops a restart loop");
    }

    static void WaitState(ProgramSlot s, SlotState st, string what, int ms = 5000) => Assert.Eventually(() => s.State == st, what, ms);

    [Test]
    public static void A_crashed_program_is_restarted_and_a_stay_stopped_one_is_not()
    {
        var (p, l, dir) = Make();
        using var sup = new CrashSupervisor(p, TimeSpan.Zero, stuckCheck: TimeSpan.Zero);
        p.LoadProgram(1, Upload(dir, 1, "a.fake"));
        p.LoadProgram(2, Upload(dir, 2, "b.fake"));
        p.Slot(1).UpdateSettings(c => c with { Crash = new CrashPolicy { Mode = "restart" } });
        var gen = p.Slot(1).Generation;
        p.Slot(1).Fault("test");
        Assert.Eventually(() => p.Slot(1).State == SlotState.Running && p.Slot(1).Generation != gen, "restarted");
        Assert.Equal("restarted", sup.Status(1).LastAction, "status says so");
        Assert.True(p.RecentLog(1).Any(e => e.Message.Contains("Crash policy")), "logged");

        p.Slot(2).Fault("test");
        Thread.Sleep(300);
        Assert.Equal(SlotState.Error, p.Slot(2).State, "stay stopped: the slot stays in Error");
        Assert.Equal(1, l.Loads.Count(x => x.Slot == 2), "and was not loaded again");
        p.Dispose();
    }

    [Test]
    public static void Limited_restarts_stop_after_the_limit()
    {
        var (p, l, dir) = Make();
        using var sup = new CrashSupervisor(p, TimeSpan.Zero, stuckCheck: TimeSpan.Zero);
        p.LoadProgram(1, Upload(dir, 1, "a.fake"));
        p.Slot(1).UpdateSettings(c => c with { Crash = new CrashPolicy { Mode = "limited", MaxRestarts = 2, WindowMinutes = 5 } });
        for (var i = 0; i < 2; i++)
        {
            var g = p.Slot(1).Generation;
            p.Slot(1).Fault("again");
            Assert.Eventually(() => p.Slot(1).State == SlotState.Running && p.Slot(1).Generation != g, $"restart {i + 1}");
        }
        p.Slot(1).Fault("third");
        Thread.Sleep(300);
        Assert.Equal(SlotState.Error, p.Slot(1).State, "third crash: stays stopped");
        Assert.Equal(3, l.Count, "initial load plus two restarts");
        Assert.Equal("stopped", sup.Status(1).LastAction, "status");
        p.Dispose();
    }

    [Test]
    public static void A_program_that_fails_to_load_again_is_not_restarted_in_a_loop()
    {
        var (p, l, dir) = Make();
        using var sup = new CrashSupervisor(p, TimeSpan.Zero, stuckCheck: TimeSpan.Zero);
        p.LoadProgram(1, Upload(dir, 1, "a.fake"));
        p.Slot(1).UpdateSettings(c => c with { Crash = new CrashPolicy { Mode = "restart" } });
        l.FailLoad = (_, _) => true;   // every load from now on fails
        p.Slot(1).Fault("crash");
        Assert.Eventually(() => l.Count == 2, "one restart attempt");
        Thread.Sleep(500);
        Assert.Equal(2, l.Count, "no further attempts");
        Assert.Equal(SlotState.Error, p.Slot(1).State, "the slot is in Error");
        Assert.Equal("restart failed", sup.Status(1).LastAction, "status");
        p.Dispose();
    }

    [Test]
    public static void A_program_that_fails_when_it_starts_is_not_restarted_either()
    {
        var (p, l, dir) = Make();
        using var sup = new CrashSupervisor(p, TimeSpan.Zero, stuckCheck: TimeSpan.Zero);
        p.LoadProgram(1, Upload(dir, 1, "a.fake"));
        p.Slot(1).UpdateSettings(c => c with { Crash = new CrashPolicy { Mode = "restart" } });
        l.OnStart = _ => new InvalidOperationException("start failed");
        p.Slot(1).Fault("crash");
        Assert.Eventually(() => l.Count == 2, "one restart attempt");
        Thread.Sleep(500);
        Assert.Equal(2, l.Count, "no loop");
        Assert.Equal(SlotState.Error, p.Slot(1).State, "Error");
        p.Dispose();
    }

    [Test]
    public static void Unhandled_exceptions_count_as_crashes_only_when_the_slot_says_so()
    {
        var (p, l, dir) = Make();
        using var sup = new CrashSupervisor(p, TimeSpan.Zero, stuckCheck: TimeSpan.Zero);
        p.LoadProgram(1, Upload(dir, 1, "a.fake"));
        p.Slot(1).Dispatcher.Post(() => throw new InvalidOperationException("boom"));
        Thread.Sleep(200);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "by default an exception is only logged");

        p.Slot(1).UpdateSettings(c => c with { Crash = new CrashPolicy { Mode = "restart", FaultOnUnhandled = true } });
        var g = p.Slot(1).Generation;
        p.Slot(1).Dispatcher.Post(() => throw new InvalidOperationException("boom"));
        Assert.Eventually(() => p.Slot(1).Generation != g && p.Slot(1).State == SlotState.Running, "restarted after the exception");
        p.Dispose();
    }

    [Test]
    public static void A_stuck_slot_is_treated_as_a_crash_after_the_configured_time()
    {
        var (p, l, dir) = Make();
        using var sup = new CrashSupervisor(p, TimeSpan.Zero, stuckCheck: TimeSpan.FromMilliseconds(200));
        p.LoadProgram(1, Upload(dir, 1, "a.fake"));
        p.Slot(1).UpdateSettings(c => c with { Crash = new CrashPolicy { Mode = "stop", StuckSeconds = 2 } });
        var release = new ManualResetEventSlim();
        p.Slot(1).Dispatcher.Post(() => release.Wait(TimeSpan.FromSeconds(20)), "Test.Hang");
        Assert.Eventually(() => p.Slot(1).State == SlotState.Error, "the stuck slot faulted", 6000);
        Assert.True(sup.Status(1).LastReason!.Contains("stuck"), "reason says stuck");
        release.Set();
        p.Dispose();
    }

    // ---------------------------------------------------------------- recent programs

    [Test]
    public static void Recent_keeps_the_last_ten_replaces_repeats_and_keeps_upload_bytes_within_the_cap()
    {
        var dir = Data();
        var r = new RecentPrograms(dir);
        for (var i = 0; i < 12; i++) r.Add($"prog{i}.cpz", "SIMPL#", 1, null, [($"prog{i}.cpz", new byte[] { (byte)i, 1, 2 }), ("prog.sig", new byte[] { 9 })]);
        Assert.Equal(10, r.List().Count, "capped at ten");
        Assert.Equal("prog11.cpz", r.List()[0].Name, "newest first");
        Assert.True(!r.List().Any(e => e.Name == "prog0.cpz"), "oldest dropped");
        Assert.True(!Directory.Exists(Path.Combine(dir, "recent", "files")) || Directory.GetDirectories(Path.Combine(dir, "recent", "files")).Length == 10, "dropped entries' bytes are deleted");

        // Loading the same upload again replaces the entry and keeps only the latest bytes.
        var again = r.Add("prog5.cpz", "SIMPL#", 3, null, [("prog5.cpz", new byte[] { 77 })]);
        Assert.Equal(1, r.List().Count(e => e.Name == "prog5.cpz"), "no duplicate");
        Assert.Equal(3, r.Get(again.Id)!.Slot, "last slot");
        Assert.Equal(77, r.ReadCopy(again.Id)![0].Data[0], "latest bytes");
        Assert.Equal(1, r.ReadCopy(again.Id)!.Count, "latest files only");

        // A path load keeps the path, not bytes.
        var pe = r.Add("Room.lpz", "SIMPL Windows", 2, "/x/Room.lpz", null, ["/x/Room.sig"]);
        Assert.True(pe is { HasCopy: false, Path: "/x/Room.lpz" } && pe.Files![0] == "/x/Room.sig", "path entry");
        Assert.True(r.ReadCopy(pe.Id) == null, "no copy for a path entry");

        // A new instance reads what was saved.
        var r2 = new RecentPrograms(dir);
        Assert.Equal(r.List().Count, r2.List().Count, "persisted");
        Assert.Equal(77, r2.ReadCopy(again.Id)![0].Data[0], "bytes persisted");
        Assert.True(r2.Remove(again.Id) && r2.Get(again.Id) == null && !Directory.Exists(Path.Combine(dir, "recent", "files", again.Id)), "remove deletes the bytes");
    }

    [Test]
    public static void Recent_does_not_keep_bytes_over_the_per_program_or_total_cap()
    {
        var dir = Data();
        var r = new RecentPrograms(dir, maxCopyBytes: 100, maxTotalBytes: 150, clock: new Clock().Now);
        var big = r.Add("big.cpz", "SIMPL#", 1, null, [("big.cpz", new byte[101])]);
        Assert.True(!big.HasCopy && big.Size == 101, "over the per-program cap: remembered without bytes");
        var a = r.Add("a.cpz", "SIMPL#", 1, null, [("a.cpz", new byte[90])]);
        var b = r.Add("b.cpz", "SIMPL#", 1, null, [("b.cpz", new byte[90])]);
        Assert.True(!r.Get(a.Id)!.HasCopy && r.Get(b.Id)!.HasCopy, "over the total: the oldest copy goes first");
        Assert.True(r.ReadCopy(a.Id) == null, "and can't be read");
        Assert.True(r.Get(a.Id) != null, "but stays in the list");
    }

    sealed class Clock { DateTimeOffset _t = T0; public DateTimeOffset Now() => _t = _t.AddSeconds(1); }

    // ---------------------------------------------------------------- trash and undo

    [Test]
    public static void Trash_moves_files_and_folders_and_puts_them_back()
    {
        var dir = Data();
        var t = new Trash(dir);
        var f = Path.Combine(dir, "program01", "user", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "hello");
        var folder = Path.Combine(dir, "program01", "user", "cfg");
        Directory.CreateDirectory(Path.Combine(folder, "deep")); File.WriteAllText(Path.Combine(folder, "deep", "x"), "deep");

        var e1 = t.Move("file", "file a.txt", 1, f)!;
        var e2 = t.Move("folder", "folder cfg", 1, folder)!;
        Assert.True(!File.Exists(f) && !Directory.Exists(folder), "moved out");
        Assert.Equal(2, t.List().Count, "listed");

        t.Restore(e1.Id); t.Restore(e2.Id);
        Assert.Equal("hello", File.ReadAllText(f), "file back");
        Assert.Equal("deep", File.ReadAllText(Path.Combine(folder, "deep", "x")), "folder back with its contents");
        Assert.Equal(0, t.List().Count, "trash is empty");
        Assert.True(t.Restore(e1.Id) == null, "can't restore twice");
    }

    [Test]
    public static void Trash_will_not_overwrite_and_refuses_ids_and_paths_it_did_not_write()
    {
        var dir = Data();
        var t = new Trash(dir);
        var f = Path.Combine(dir, "program01", "user", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "old");
        var e = t.Move("file", "a", 1, f)!;
        File.WriteAllText(f, "new");   // someone made a new one
        var threw = false;
        try { t.Restore(e.Id); } catch (IOException) { threw = true; }
        Assert.True(threw, "conflict is an error");
        Assert.Equal("new", File.ReadAllText(f), "the new file is untouched");
        Assert.True(t.Get(e.Id) != null, "still in the trash");

        Assert.True(t.Get("../../etc") == null && t.Restore("..") == null && t.Get(null) == null, "bad ids");

        // A tampered entry that points outside the data folder is refused.
        var outside = Path.Combine(Path.GetTempPath(), "openvp-outside-" + Guid.NewGuid().ToString("N"));
        var e2 = t.Move("file", "b", 1, f)!;
        var meta = Path.Combine(dir, "trash", e2.Id, "trash.json");
        File.WriteAllText(meta, File.ReadAllText(meta).Replace(JsonSerializer.Serialize(f).Trim('"'), outside.Replace("\\", "\\\\")));
        threw = false;
        try { t.Restore(e2.Id); } catch (UnauthorizedAccessException) { threw = true; }
        Assert.True(threw && !File.Exists(outside), "refused");
    }

    [Test]
    public static void Trash_purges_after_24_hours()
    {
        var dir = Data();
        var now = T0;
        var t = new Trash(dir, clock: () => now);
        var f = Path.Combine(dir, "program01", "user", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x");
        var e = t.Move("file", "a", 1, f)!;
        now = T0.AddHours(23);
        Assert.Equal(0, t.Purge(), "not yet");
        Assert.True(t.Get(e.Id) != null, "still there");
        now = T0.AddHours(25);
        Assert.Equal(1, t.Purge(), "purged");
        Assert.True(t.Get(e.Id) == null && !Directory.Exists(Path.Combine(dir, "trash", e.Id)), "gone for good");
        Assert.True(t.Restore(e.Id) == null, "can't undo any more");
    }

    [Test]
    public static void Deleted_snapshots_and_recordings_can_be_undone()
    {
        var dir = Data();
        var t = new Trash(dir);
        // Snapshot
        Directory.CreateDirectory(Path.Combine(dir, "uploads", "slot01"));
        File.WriteAllText(Path.Combine(dir, "uploads", "slot01", "p.fake"), "prog");
        var snaps = new SlotSnapshots(dir);
        var info = snaps.Take(1, "Prog", "p.fake", SlotSnapshots.Manual)!;
        var undo = snaps.Delete(1, info.Id, t)!;
        Assert.True(snaps.Get(1, info.Id) == null, "snapshot is gone from the list");
        t.Restore(undo.Id);
        Assert.True(snaps.Get(1, info.Id) != null && snaps.List(1).Count == 1, "snapshot is back");

        // Recording (the file and its reports)
        var store = new RecordingStore(dir);
        var rec = new Recording();
        var ri = store.Add(1, rec, "My recording");
        var undo2 = store.Delete(1, ri.Id, t)!;
        Assert.True(store.Get(1, ri.Id) == null, "recording is gone");
        t.Restore(undo2.Id);
        Assert.Equal("My recording", store.Get(1, ri.Id)?.Name, "recording is back");
    }

    [Test]
    public static void A_revoked_share_comes_back_with_the_same_token_within_the_window_only()
    {
        var dir = Data();
        var now = T0;
        var shares = new ShareStore(dir, () => now);
        var link = shares.Create(1, 3, "Client", "1234", TimeSpan.FromDays(1));
        Assert.True(shares.Revoke(link.Id), "revoked");
        Assert.True(shares.FindByToken(link.Token) == null, "dead");
        now = T0.AddMinutes(5);
        var back = shares.Undo(link.Id);
        Assert.True(back != null && back.Token == link.Token && back.HasPin, "same token and PIN");
        Assert.True(shares.FindByToken(link.Token) != null, "live again");
        Assert.True(shares.Undo(link.Id) == null, "only once");

        var l2 = shares.Create(1, 4, "Other", null, null);
        shares.Revoke(l2.Id);
        now = T0.AddMinutes(30);
        Assert.True(shares.Undo(l2.Id) == null, "too late");
    }
}
