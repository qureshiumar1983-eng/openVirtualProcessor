namespace OpenVP.Core;

/// <summary>What a slot is doing with recordings, for the status endpoint. Plain data, safe to serialize.</summary>
public sealed class RecordingStatus
{
    /// <summary>"idle", "recording" or "replaying".</summary>
    public string Mode { get; set; } = "idle";
    public RecordingProgress? Recording { get; set; }
    public ReplayProgress? Replay { get; set; }
    /// <summary>Something that happened by itself, e.g. a recording that ended because the program was unloaded.</summary>
    public string? Notice { get; set; }
    /// <summary>The recording an automatic stop saved.</summary>
    public string? NoticeRecordingId { get; set; }
}

public sealed class RecordingProgress
{
    public long ElapsedMs { get; set; }
    /// <summary>Inputs and forces captured so far.</summary>
    public int Inputs { get; set; }
    public RecordingCounts Counts { get; set; } = new();
    public bool LimitReached { get; set; }
}

public sealed class ReplayProgress
{
    public string RecordingId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"running", "done", "cancelled", "aborted" or "error".</summary>
    public string State { get; set; } = "running";
    public bool Fast { get; set; }
    public bool IncludeEisc { get; set; }
    public long ElapsedMs { get; set; }
    public long TotalMs { get; set; }
    public double Progress { get; set; }
    public string? Message { get; set; }
    /// <summary>The finished replay's report (state "done").</summary>
    public ReplayReport? Report { get; set; }
}

/// <summary>
/// Records and replays slots, one thing at a time per slot, and keeps recordings in a <see cref="RecordingStore"/>.
/// A recording or replay ends by itself, cleanly, if the slot's program is unloaded or replaced.
/// </summary>
public sealed class SlotRecordings : IDisposable
{
    private sealed class State
    {
        public SlotRecorder? Recorder;
        public int RecorderGeneration;
        public ReplayRun? Replay;
        public string? Notice, NoticeRecordingId;
    }

    private sealed class ReplayRun
    {
        public string RecordingId = "", Name = "";
        public bool Fast, IncludeEisc;
        public RecordingReplayer Replayer = null!;
        public CancellationTokenSource Cts = new();
        public Task? Task;
        public volatile string StateName = "running";
        public string? Message;
        public ReplayReport? Report;
        public long TotalMs, ElapsedMs;
    }

    private readonly Processor _processor;
    private readonly State[] _states;
    private readonly Action<ProgramSlot>[] _handlers;
    private readonly object _gate = new();

    public SlotRecordings(Processor processor, RecordingStore store)
    {
        _processor = processor;
        Store = store;
        _states = processor.Slots.Select(_ => new State()).ToArray();
        _handlers = new Action<ProgramSlot>[processor.Slots.Count];
        for (var i = 0; i < processor.Slots.Count; i++)
        {
            var slot = processor.Slots[i];
            _handlers[i] = s => SlotChanged(s);
            slot.StateChanged += _handlers[i];
        }
    }

    public RecordingStore Store { get; }

    private State StateOf(int slot) => _states[slot - 1];

    // ---------- recording ----------

    /// <summary>Starts recording the slot. Throws InvalidOperationException if it is busy or has no program.</summary>
    public void StartRecording(int slotNumber)
    {
        var slot = _processor.Slot(slotNumber);
        lock (_gate)
        {
            var st = StateOf(slotNumber);
            if (st.Recorder != null) throw new InvalidOperationException("This slot is already recording.");
            if (st.Replay is { StateName: "running" }) throw new InvalidOperationException("This slot is replaying a recording. Cancel it first.");
            if (slot.Runtime == null) throw new InvalidOperationException("Load a program into this slot first.");
            st.Replay = null;
            st.Notice = st.NoticeRecordingId = null;
            st.RecorderGeneration = slot.Generation;
            st.Recorder = new SlotRecorder(slot);
        }
    }

    /// <summary>Stops recording and saves it. Without a name it is called after the program and time. Throws InvalidOperationException if not recording.</summary>
    public RecordingInfo StopRecording(int slotNumber, string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(name)) name = RecordingIds.ValidateName(name);
        SlotRecorder rec;
        lock (_gate)
        {
            var st = StateOf(slotNumber);
            rec = st.Recorder ?? throw new InvalidOperationException("This slot isn't recording.");
            st.Recorder = null;
        }
        var recording = rec.Finish("stopped");
        var info = Store.Add(slotNumber, recording, name);
        _processor.Slot(slotNumber).Log("Notice", $"Saved recording \"{info.Name}\" ({recording.Counts.Inputs + recording.Counts.EiscInputs + recording.Counts.Forces} inputs and forces, {recording.Counts.Feedback} feedback changes, {info.DurationMs / 1000.0:0.0} s).");
        return info;
    }

    // ---------- replay ----------

    /// <summary>
    /// Starts replaying a saved recording into the slot, on a thread of its own. Throws InvalidOperationException if the slot is busy
    /// or its program isn't running, and KeyNotFoundException for an unknown recording.
    /// </summary>
    public void StartReplay(int slotNumber, string? recordingId, ReplayOptions? options = null)
    {
        var slot = _processor.Slot(slotNumber);
        var rec = Store.Load(slotNumber, recordingId) ?? throw new KeyNotFoundException("There's no such recording.");
        options ??= new ReplayOptions();
        lock (_gate)
        {
            var st = StateOf(slotNumber);
            if (st.Recorder != null) throw new InvalidOperationException("This slot is recording. Stop the recording first.");
            if (st.Replay is { StateName: "running" }) throw new InvalidOperationException("This slot is already replaying a recording.");
            if (slot.State != SlotState.Running) throw new InvalidOperationException("The slot's program isn't running. Load and start it first.");
            st.Notice = st.NoticeRecordingId = null;
            var run = new ReplayRun
            {
                RecordingId = rec.Id, Name = rec.Name, Fast = options.Fast, IncludeEisc = options.IncludeEisc,
                Replayer = new RecordingReplayer(slot, rec, options)
            };
            st.Replay = run;
            slot.Log("Notice", $"Replaying recording \"{rec.Name}\"{(options.Fast ? " (fast)" : "")}.");
            run.Task = Task.Factory.StartNew(() => RunReplay(slotNumber, run), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void RunReplay(int slotNumber, ReplayRun run)
    {
        var slot = _processor.Slot(slotNumber);
        try
        {
            var report = run.Replayer.Run(run.Cts.Token);
            Store.AddReport(slotNumber, run.RecordingId, report);
            run.Report = report;
            run.StateName = "done";
            slot.Log(report.Passed ? "Notice" : "Warning",
                $"Replay of \"{run.Name}\": {(report.Passed ? "PASS" : "FAIL")} ({report.Counts.Matched} of {report.Counts.FeedbackExpected} feedback changes matched, {report.Differences.Count(d => d.Severity == "fail")} differences).");
        }
        catch (OperationCanceledException)
        {
            run.Message = run.Replayer.AbortReason;
            run.StateName = run.Replayer.AbortReason != null ? "aborted" : "cancelled";
            slot.Log("Notice", $"Replay of \"{run.Name}\" {run.StateName}{(run.Message != null ? ": " + run.Message : "")}.");
        }
        catch (Exception e)
        {
            run.Message = e.Message;
            run.StateName = "error";
            slot.Log("Error", $"Replay of \"{run.Name}\" failed: {e.Message}");
        }
        finally
        {
            run.ElapsedMs = run.Replayer.ElapsedMs;
            run.TotalMs = run.Replayer.PlannedMs;
        }
    }

    /// <summary>Cancels a replay under way. Returns false if there isn't one.</summary>
    public bool CancelReplay(int slotNumber)
    {
        ReplayRun? run;
        lock (_gate) run = StateOf(slotNumber).Replay;
        if (run == null || run.StateName != "running") return false;
        run.Cts.Cancel();
        return true;
    }

    // ---------- status ----------

    public RecordingStatus Status(int slotNumber)
    {
        lock (_gate)
        {
            var st = StateOf(slotNumber);
            var s = new RecordingStatus { Notice = st.Notice, NoticeRecordingId = st.NoticeRecordingId };
            if (st.Recorder is { } r)
            {
                s.Mode = "recording";
                var counts = r.Counts;
                s.Recording = new RecordingProgress
                {
                    ElapsedMs = r.ElapsedMs, Counts = counts, LimitReached = r.LimitReached,
                    Inputs = counts.Inputs + counts.EiscInputs + counts.Forces
                };
            }
            if (st.Replay is { } p)
            {
                var running = p.StateName == "running";
                if (running) s.Mode = "replaying";
                var elapsed = running ? p.Replayer.ElapsedMs : p.ElapsedMs;
                var total = running ? p.Replayer.PlannedMs : p.TotalMs;
                s.Replay = new ReplayProgress
                {
                    RecordingId = p.RecordingId, Name = p.Name, State = p.StateName, Fast = p.Fast, IncludeEisc = p.IncludeEisc,
                    ElapsedMs = elapsed, TotalMs = total,
                    Progress = p.StateName == "done" ? 1 : total <= 0 ? 0 : Math.Clamp((double)elapsed / total, 0, 1),
                    Message = p.Message, Report = p.Report
                };
            }
            return s;
        }
    }

    /// <summary>True while the slot is recording or replaying (for the Status board).</summary>
    public string? Busy(int slotNumber)
    {
        lock (_gate)
        {
            var st = StateOf(slotNumber);
            return st.Recorder != null ? "recording" : st.Replay is { StateName: "running" } ? "replaying" : null;
        }
    }

    // ---------- unload ----------

    private void SlotChanged(ProgramSlot slot)
    {
        SlotRecorder? rec = null;
        ReplayRun? run = null;
        lock (_gate)
        {
            var st = StateOf(slot.Number);
            if (st.Recorder != null && slot.Generation != st.RecorderGeneration)
            {
                rec = st.Recorder;
                st.Recorder = null;
            }
            if (st.Replay is { StateName: "running" } p && (slot.Generation != p.Replayer.Generation || slot.Runtime == null)) run = p;
        }
        // The replay thread notices the unload itself and ends as "aborted"; this just wakes it.
        run?.Cts.Cancel();
        if (rec == null) return;
        try
        {
            var recording = rec.Finish("program unloaded", deriveEnd: true);
            var info = Store.Add(slot.Number, recording);
            lock (_gate)
            {
                var st = StateOf(slot.Number);
                st.Notice = $"Recording stopped because the program was unloaded. What was captured is saved as \"{info.Name}\".";
                st.NoticeRecordingId = info.Id;
            }
            slot.Log("Warning", $"Recording stopped because the program was unloaded; saved \"{info.Name}\".");
        }
        catch (Exception e)
        {
            lock (_gate) StateOf(slot.Number).Notice = $"Recording stopped because the program was unloaded, and couldn't be saved: {e.Message}";
            slot.Log("Error", $"Couldn't save the recording: {e.Message}");
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < _handlers.Length; i++) _processor.Slots[i].StateChanged -= _handlers[i];
        foreach (var st in _states)
        {
            st.Recorder?.Dispose();
            st.Replay?.Cts.Cancel();
        }
    }
}
