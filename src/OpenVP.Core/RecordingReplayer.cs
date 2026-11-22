using System.Diagnostics;

namespace OpenVP.Core;

public sealed record ReplayOptions
{
    /// <summary>Shorten long idle gaps between inputs to <see cref="FastGapCapMs"/>. Gaps at or under it stay as recorded.</summary>
    public bool Fast { get; init; }
    /// <summary>Send the inputs that came from the other end of an EISC link. If the other slot is also running and driving that
    /// EISC, turn this off.</summary>
    public bool IncludeEisc { get; init; } = true;
    public long FastGapCapMs { get; init; } = 1000;
    /// <summary>A recorded feedback change must happen within this many ms of where it is expected.</summary>
    public long ToleranceMs { get; init; } = 500;
}

/// <summary>One thing that differed between the recording and the replay.</summary>
public sealed class ReplayDifference
{
    public const string Missing = "missing", Different = "different", Extra = "extra", FinalState = "final", Input = "input", Baseline = "baseline";
    public string Kind { get; set; } = "";
    /// <summary>"fail" makes the replay FAIL; "warn" is only noted.</summary>
    public string Severity { get; set; } = "fail";
    public uint? Ip { get; set; }
    public string? Join { get; set; }
    public object? Expected { get; set; }
    public object? Actual { get; set; }
    /// <summary>Where in the recording the feedback was expected (ms since recording started).</summary>
    public long? TimeMs { get; set; }
    /// <summary>When the replay produced <see cref="Actual"/> (ms since the replay started).</summary>
    public long? ActualTimeMs { get; set; }
    public string Message { get; set; } = "";
}

public sealed class ReplayCounts
{
    public int FeedbackExpected { get; set; }
    public int Matched { get; set; }
    public int Missing { get; set; }
    public int Different { get; set; }
    public int Extra { get; set; }
    public int FinalDifferences { get; set; }
    public int InputsSent { get; set; }
    public int EiscInputsSent { get; set; }
    public int EiscInputsSkipped { get; set; }
    public int ForcesSent { get; set; }
    public int Undelivered { get; set; }
}

public sealed class ReplayReport
{
    public string Id { get; set; } = "";
    public string RecordingId { get; set; } = "";
    public string RecordingName { get; set; } = "";
    public int Slot { get; set; }
    public string? RecordedProgram { get; set; }
    public string? RecordedProgramFile { get; set; }
    public string? Program { get; set; }
    public string? ProgramFile { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public bool Fast { get; set; }
    public bool IncludeEisc { get; set; }
    public long ToleranceMs { get; set; }
    public bool Passed { get; set; }
    /// <summary>How long the recording was and how long the replay took.</summary>
    public long RecordedMs { get; set; }
    public long ReplayMs { get; set; }
    public ReplayCounts Counts { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<ReplayDifference> Differences { get; set; } = new();
    /// <summary>True if there were more differences than are listed (the counts are always complete).</summary>
    public bool Truncated { get; set; }
}

/// <summary>Join values as a map, for working out and comparing device state.</summary>
internal static class FeedbackState
{
    public static Dictionary<(uint Ip, JoinKey Key), object> ToMap(IEnumerable<RecordedDevice> devices)
    {
        var map = new Dictionary<(uint, JoinKey), object>();
        foreach (var d in devices)
            foreach (var j in d.Joins)
            {
                var key = j.Key;
                map[(d.Ip, key)] = SigValue.Coerce(key.Kind, j.Value);
            }
        return map;
    }

    public static Dictionary<(uint Ip, JoinKey Key), object> Apply(Dictionary<(uint Ip, JoinKey Key), object> state, IEnumerable<RecordedFeedback> feedback)
    {
        var map = new Dictionary<(uint, JoinKey), object>(state);
        foreach (var f in feedback.OrderBy(f => f.T).ThenBy(f => f.Seq))
        {
            var key = f.Key;
            map[(f.Ip, key)] = SigValue.Coerce(key.Kind, f.Value);
        }
        return map;
    }

    public static List<RecordedDevice> ToDevices(Dictionary<(uint Ip, JoinKey Key), object> map, IEnumerable<RecordedDevice> typesFrom)
    {
        var types = typesFrom.ToDictionary(d => d.Ip, d => d.Type);
        return map.GroupBy(p => p.Key.Ip).OrderBy(g => g.Key).Select(g => new RecordedDevice
        {
            Ip = g.Key,
            Type = types.GetValueOrDefault(g.Key),
            Joins = g.OrderBy(p => p.Key.Key.Kind).ThenBy(p => p.Key.Key.SmartObject).ThenBy(p => p.Key.Key.Join)
                .Select(p => RecordedJoin.From(p.Key.Key, p.Value)).ToList()
        }).ToList();
    }

    public static Dictionary<(uint Ip, JoinKey Key), object> Of(ProgramSlot slot)
    {
        var map = new Dictionary<(uint, JoinKey), object>();
        foreach (var d in slot.Devices)
            foreach (var (k, v) in d.FeedbackSnapshot()) map[(d.IpId, k)] = v;
        return map;
    }

    public static object Get(Dictionary<(uint Ip, JoinKey Key), object> map, uint ip, JoinKey key) =>
        map.TryGetValue((ip, key), out var v) ? v : SigValue.Default(key.Kind);

    public static string Show(object? v) => v switch { bool b => b ? "1" : "0", string s => "\"" + s + "\"", null => "-", _ => v.ToString() ?? "" };
}

/// <summary>
/// When each recorded event happens in a replay. Inputs and forces keep their recorded gaps, or in fast mode any gap
/// longer than the cap is shortened to the cap (shorter gaps stay as recorded, because programs often depend on
/// press/release timing). Feedback is expected at the same distance after the input before it as it was recorded:
/// a program's timers run in real time, so a lamp that came on 2 s after a press is expected 2 s after the press in the replay too.
/// </summary>
public sealed class ReplayTimeline
{
    private readonly long[] _recorded;   // anchor times in the recording, ascending
    private readonly long[] _replay;     // the same anchors in the replay
    private readonly long _end;          // replay time the recording's last moment maps to

    public ReplayTimeline(Recording rec, bool fast, long capMs = 1000)
    {
        var anchors = new SortedSet<long> { 0 };
        foreach (var i in rec.Inputs) anchors.Add(i.T);
        foreach (var f in rec.Forces) anchors.Add(f.T);
        _recorded = anchors.ToArray();
        _replay = new long[_recorded.Length];
        for (var n = 1; n < _recorded.Length; n++)
            _replay[n] = _replay[n - 1] + Gap(_recorded[n] - _recorded[n - 1], fast, capMs);
        var last = _recorded.Length - 1;
        _end = _replay[last] + Gap(Math.Max(0, rec.DurationMs - _recorded[last]), fast, capMs);
    }

    private static long Gap(long gap, bool fast, long cap) => fast ? Math.Min(gap, cap) : gap;

    /// <summary>When the replay sends the input or force the recording has at <paramref name="recordedMs"/>.</summary>
    public long InputTime(long recordedMs) => AnchorAtOrBefore(recordedMs, out var i) ? _replay[i] + (recordedMs - _recorded[i]) : 0;

    /// <summary>When the replay should show the feedback the recording has at <paramref name="recordedMs"/>.</summary>
    public long FeedbackTime(long recordedMs) => InputTime(recordedMs);

    /// <summary>The replay time the end of the recording maps to.</summary>
    public long EndTime => _end;

    private bool AnchorAtOrBefore(long t, out int index)
    {
        var i = Array.BinarySearch(_recorded, t);
        if (i < 0) i = ~i - 1;
        index = Math.Max(i, 0);
        return _recorded.Length > 0;
    }
}

/// <summary>Plays a recording back into a slot and compares the feedback. See the README, "Record and replay".</summary>
public sealed class RecordingReplayer
{
    private readonly ProgramSlot _slot;
    private readonly Recording _rec;
    private readonly ReplayOptions _opt;
    private readonly ReplayTimeline _timeline;
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();
    private readonly List<(long T, uint Ip, JoinKey Key, object Value)> _actual = new();
    private long _plannedMs;
    private volatile string? _abortReason;
    private readonly int _generation;

    public RecordingReplayer(ProgramSlot slot, Recording recording, ReplayOptions? options = null)
    {
        _slot = slot;
        _rec = recording;
        _generation = slot.Generation;
        _opt = options ?? new ReplayOptions();
        _timeline = new ReplayTimeline(recording, _opt.Fast, Math.Max(1, _opt.FastGapCapMs));
        var lastFeedback = recording.Feedback.Count == 0 ? 0 : _timeline.FeedbackTime(recording.Feedback.Max(f => f.T));
        _plannedMs = Math.Max(_timeline.EndTime, lastFeedback) + _opt.ToleranceMs;
    }

    /// <summary>The slot's generation when the replay was created; a different one means the program was replaced.</summary>
    public int Generation => _generation;

    public long ElapsedMs => _clock.ElapsedMilliseconds;

    /// <summary>How long the replay is planned to take (known once it has started).</summary>
    public long PlannedMs => Volatile.Read(ref _plannedMs);

    /// <summary>Set when the replay stopped early because the program was unloaded or replaced.</summary>
    public string? AbortReason => _abortReason;

    /// <summary>Replays on the calling thread. Throws <see cref="OperationCanceledException"/> if cancelled or aborted
    /// (see <see cref="AbortReason"/>). Takes the time the recording took, so run it on a thread of its own.</summary>
    public ReplayReport Run(CancellationToken ct)
    {
        var report = new ReplayReport
        {
            Id = RecordingIds.NewId(DateTimeOffset.UtcNow), RecordingId = _rec.Id, RecordingName = _rec.Name, Slot = _slot.Number,
            RecordedProgram = _rec.Program, RecordedProgramFile = _rec.ProgramFile,
            Program = _slot.Runtime?.ProgramName, ProgramFile = _slot.ProgramPath is null ? null : Path.GetFileName(_slot.ProgramPath),
            StartedAt = DateTimeOffset.Now, Fast = _opt.Fast, IncludeEisc = _opt.IncludeEisc, ToleranceMs = _opt.ToleranceMs,
            RecordedMs = _rec.DurationMs
        };
        var generation = _generation;
        var diffs = new List<ReplayDifference>();

        // Put the inputs back to what they held when recording began (a level left at 40000 by the last session
        // would otherwise swallow the recorded 40000), let the program settle, then see how the room looks.
        ResetInputs(report, ct, generation);
        CompareBaseline(diffs);

        var events = _rec.Inputs.Where(i => _opt.IncludeEisc || i.Source != RecordedInput.EiscSource)
            .Select(i => (T: i.T, Seq: i.Seq, Input: (RecordedInput?)i, Force: (RecordedForce?)null))
            .Concat(_rec.Forces.Select(f => (T: f.T, Seq: f.Seq, Input: (RecordedInput?)null, Force: (RecordedForce?)f)))
            .OrderBy(e => e.T).ThenBy(e => e.Seq).ToList();

        var endAt = PlannedMs;

        using var tap = new DeviceTap(_slot, null, (d, k, v) =>
        {
            lock (_gate) _actual.Add((_clock.ElapsedMilliseconds, d.IpId, k, v));
        });
        _clock.Restart();

        var counts = report.Counts;
        var warnedMissingDevice = new HashSet<uint>();
        foreach (var e in events)
        {
            WaitUntil(_timeline.InputTime(e.T), generation, ct);
            if (e.Input is { } i)
            {
                var dev = _slot.FindDevice(i.Ip);
                if (dev == null)
                {
                    counts.Undelivered++;
                    if (warnedMissingDevice.Add(i.Ip))
                        diffs.Add(new ReplayDifference
                        {
                            Kind = ReplayDifference.Input, Ip = i.Ip, Join = i.Describe(), Expected = i.Value, TimeMs = i.T,
                            Message = $"IP ID {i.Ip:X2} isn't registered in this program, so its recorded inputs (first: {i.Describe()} = {FeedbackState.Show(i.Value)}) couldn't be sent."
                        });
                    continue;
                }
                var isEisc = i.Source == RecordedInput.EiscSource;
                dev.ReceiveInput(i.Key, SigValue.Coerce(i.Key.Kind, i.Value), isEisc ? InputSource.Eisc : InputSource.Panel);
                if (isEisc) counts.EiscInputsSent++; else counts.InputsSent++;
            }
            else if (e.Force is { } f)
            {
                var rt = _slot.Runtime;
                bool ok;
                using (ForceScope.Enter())
                    ok = rt != null && (f.Pulse ? rt.TryPulseSignal(f.Signal) : rt.TrySetSignal(f.Signal, f.Value!));
                if (ok) counts.ForcesSent++;
                else
                {
                    counts.Undelivered++;
                    diffs.Add(new ReplayDifference
                    {
                        Kind = ReplayDifference.Input, Join = f.Signal, Expected = f.Pulse ? "pulse" : f.Value, TimeMs = f.T,
                        Message = $"The recorded force of {f.Signal} couldn't be applied: this program has no {(f.Pulse ? "digital " : "")}signal with that name."
                    });
                }
            }
        }
        counts.EiscInputsSkipped = _opt.IncludeEisc ? 0 : _rec.Inputs.Count(i => i.Source == RecordedInput.EiscSource);

        WaitUntil(endAt, generation, ct);
        var replayMs = _clock.ElapsedMilliseconds;
        var finalState = FeedbackState.Of(_slot);
        List<(long T, uint Ip, JoinKey Key, object Value)> actual;
        lock (_gate) actual = _actual.ToList();
        tap.Dispose();

        Compare(_rec, _timeline, actual, finalState, _opt.ToleranceMs, report, diffs);

        report.ReplayMs = replayMs;
        AddNotes(report);
        report.Differences = diffs.Take(MaxListed).ToList();
        report.Truncated = diffs.Count > MaxListed;
        report.Passed = !diffs.Any(d => d.Severity == "fail");
        return report;
    }

    private const int MaxListed = 500;

    private void AddNotes(ReplayReport report)
    {
        var eisc = _rec.Inputs.Count(i => i.Source == RecordedInput.EiscSource);
        if (eisc > 0 && _opt.IncludeEisc)
            report.Notes.Add($"{eisc} recorded EISC input(s) were sent. If another slot is running and driving the same EISC, it may have sent the same values too; turn off \"include EISC inputs\" to rely on that slot.");
        else if (eisc > 0)
            report.Notes.Add($"{eisc} recorded EISC input(s) were not sent. The result depends on the other slot driving the EISC the same way it did in the recording.");
        if (_opt.Fast)
            report.Notes.Add($"Fast mode: gaps longer than {_opt.FastGapCapMs} ms between inputs were shortened to {_opt.FastGapCapMs} ms. Timers in the program still run in real time, so use the original timing for a program that depends on long delays.");
        if (_rec.EndedBy != "stopped")
            report.Notes.Add($"The recording ended early ({_rec.EndedBy}).");
        if (_slot.Runtime?.ProgramName != _rec.Program && _rec.Program != null)
            report.Notes.Add($"Program name now \"{_slot.Runtime?.ProgramName}\", was \"{_rec.Program}\" when recorded.");
    }

    /// <summary>
    /// A replay can only send inputs that change something, so every input join the recording uses is first set back to the value
    /// it held when recording started (nothing, if it wasn't set). Serial text isn't reset. The program sees these as ordinary
    /// inputs, and its answers to them come before the replay clock starts, so they are not compared.
    /// </summary>
    private void ResetInputs(ReplayReport report, CancellationToken ct, int generation)
    {
        var start = new Dictionary<(uint, JoinKey), object>();
        foreach (var d in _rec.StartInputs)
            foreach (var j in d.Joins) start[(d.Ip, j.Key)] = SigValue.Coerce(j.Key.Kind, j.Value);
        var reset = new List<string>();
        foreach (var grp in _rec.Inputs.Where(i => _opt.IncludeEisc || i.Source != RecordedInput.EiscSource).GroupBy(i => (i.Ip, i.Key)))
        {
            var (ip, key) = grp.Key;
            if (key.Kind == SigKind.Serial || _slot.FindDevice(ip) is not { } dev) continue;
            var want = start.TryGetValue((ip, key), out var v) ? v : SigValue.Default(key.Kind);
            if (SigValue.AreEqual(dev.GetInput(key), want)) continue;
            dev.ReceiveInput(key, want, grp.First().Source == RecordedInput.EiscSource ? InputSource.Eisc : InputSource.Panel);
            reset.Add($"{JoinKey.KindLetter(key.Kind)}{key.Join}@{ip:X2}");
        }
        if (reset.Count == 0) return;
        report.Notes.Add($"Before the replay, {reset.Count} input(s) were set back to what they held when the recording started: {string.Join(", ", reset.Take(10))}{(reset.Count > 10 ? ", ..." : "")}.");
        using var done = new ManualResetEventSlim();
        _slot.Dispatcher.Post(() => done.Set());
        var waited = Stopwatch.StartNew();
        while (!done.Wait(50) && waited.ElapsedMilliseconds < 2000) CheckAlive(generation, ct);
    }

    private void CompareBaseline(List<ReplayDifference> diffs)
    {
        var now = FeedbackState.Of(_slot);
        var then = FeedbackState.ToMap(_rec.Start);
        foreach (var (ip, key) in then.Keys.Concat(now.Keys).Distinct().OrderBy(k => k.Item1).ThenBy(k => k.Item2.Kind).ThenBy(k => k.Item2.Join))
        {
            var a = FeedbackState.Get(then, ip, key);
            var b = FeedbackState.Get(now, ip, key);
            if (SigValue.AreEqual(a, b)) continue;
            diffs.Add(new ReplayDifference
            {
                Kind = ReplayDifference.Baseline, Severity = "warn", Ip = ip, Join = Describe(key), Expected = a, Actual = b,
                Message = $"Different starting state: IP ID {ip:X2} {Describe(key)} was {FeedbackState.Show(a)} when recording started and is {FeedbackState.Show(b)} now."
            });
        }
        // An input equal to what the device already holds isn't an input at all: the program wouldn't see it.
        var seen = new HashSet<(uint, JoinKey)>();
        foreach (var i in _rec.Inputs.Where(i => _opt.IncludeEisc || i.Source != RecordedInput.EiscSource))
        {
            if (!seen.Add((i.Ip, i.Key))) continue;
            var dev = _slot.FindDevice(i.Ip);
            if (dev == null || i.Key.Kind == SigKind.Serial) continue;
            var held = dev.GetInput(i.Key);
            if (SigValue.AreEqual(held, SigValue.Coerce(i.Key.Kind, i.Value)))
                diffs.Add(new ReplayDifference
                {
                    Kind = ReplayDifference.Baseline, Severity = "warn", Ip = i.Ip, Join = i.Describe(), Expected = held, TimeMs = i.T,
                    Message = $"IP ID {i.Ip:X2} {i.Describe()} already holds {FeedbackState.Show(held)}, so the first recorded input to it ({FeedbackState.Show(i.Value)}) won't register."
                });
        }
    }

    private static string Describe(JoinKey k) => (k.SmartObject == 0 ? "" : $"so{k.SmartObject}.") + $"{JoinKey.KindLetter(k.Kind)}{k.Join}";

    private void WaitUntil(long replayMs, int generation, CancellationToken ct)
    {
        while (true)
        {
            CheckAlive(generation, ct);
            var remaining = replayMs - _clock.ElapsedMilliseconds;
            if (remaining <= 0) return;
            if (remaining > 20) ct.WaitHandle.WaitOne((int)Math.Min(remaining - 10, 100));
            else Thread.SpinWait(200);
        }
    }

    private void CheckAlive(int generation, CancellationToken ct)
    {
        if (_slot.Generation != generation || _slot.Runtime == null)
        {
            _abortReason = "The program was unloaded or replaced during the replay.";
            throw new OperationCanceledException(_abortReason);
        }
        if (_slot.State != SlotState.Running)
        {
            _abortReason = "The program was stopped during the replay.";
            throw new OperationCanceledException(_abortReason);
        }
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// The comparison rules (also in the README):
    /// <list type="number">
    /// <item>Each recorded feedback change (join, value, time) must have a replay change to the same join with the same value
    /// within ±tolerance of its expected time (see <see cref="ReplayTimeline"/>). Each replay change matches at most once.</item>
    /// <item>No match, but the join changed to another value in that window: <b>different</b> (FAIL). Nothing in the window: <b>missing</b> (FAIL).</item>
    /// <item>A replay change nobody expected is <b>extra</b>: a warning.</item>
    /// <item>The final feedback of every device must equal the recording's. A join that ends different is <b>final</b> (FAIL), unless
    /// it already has a missing or different entry.</item>
    /// <item>Starting-state differences and inputs that couldn't be delivered are noted (the latter FAIL).</item>
    /// </list>
    /// </summary>
    public static void Compare(Recording rec, ReplayTimeline timeline, List<(long T, uint Ip, JoinKey Key, object Value)> actual,
        Dictionary<(uint Ip, JoinKey Key), object> finalActual, long toleranceMs, ReplayReport report, List<ReplayDifference> diffs)
    {
        var counts = report.Counts;
        counts.FeedbackExpected = rec.Feedback.Count;
        var failedJoins = new HashSet<(uint, JoinKey)>();

        var expectedBy = rec.Feedback.GroupBy(f => (f.Ip, f.Key)).ToDictionary(g => g.Key, g => g.OrderBy(f => f.T).ThenBy(f => f.Seq).ToList());
        var actualBy = actual.GroupBy(a => (a.Ip, a.Key)).ToDictionary(g => g.Key, g => g.OrderBy(a => a.T).ToList());

        foreach (var (join, exp) in expectedBy.OrderBy(p => p.Key.Ip).ThenBy(p => p.Key.Key.Kind).ThenBy(p => p.Key.Key.Join))
        {
            var act = actualBy.GetValueOrDefault(join) ?? new();
            var used = new bool[act.Count];
            var matched = new bool[exp.Count];
            var times = exp.Select(e => timeline.FeedbackTime(e.T)).ToArray();

            for (var n = 0; n < exp.Count; n++)
            {
                var want = SigValue.Coerce(join.Key.Kind, exp[n].Value);
                for (var m = 0; m < act.Count; m++)
                {
                    if (used[m] || Math.Abs(act[m].T - times[n]) > toleranceMs || !SigValue.AreEqual(want, act[m].Value)) continue;
                    used[m] = matched[n] = true;
                    counts.Matched++;
                    break;
                }
            }
            for (var n = 0; n < exp.Count; n++)
            {
                if (matched[n]) continue;
                var want = SigValue.Coerce(join.Key.Kind, exp[n].Value);
                var other = -1;
                for (var m = 0; m < act.Count && other < 0; m++)
                    if (!used[m] && Math.Abs(act[m].T - times[n]) <= toleranceMs) other = m;
                if (other >= 0)
                {
                    used[other] = true;
                    counts.Different++;
                    diffs.Add(new ReplayDifference
                    {
                        Kind = ReplayDifference.Different, Ip = join.Ip, Join = exp[n].Describe(), Expected = want, Actual = act[other].Value,
                        TimeMs = exp[n].T, ActualTimeMs = act[other].T,
                        Message = $"IP ID {join.Ip:X2} {exp[n].Describe()}: expected {FeedbackState.Show(want)} at {FormatMs(exp[n].T)}, got {FeedbackState.Show(act[other].Value)}."
                    });
                }
                else
                {
                    counts.Missing++;
                    diffs.Add(new ReplayDifference
                    {
                        Kind = ReplayDifference.Missing, Ip = join.Ip, Join = exp[n].Describe(), Expected = want, TimeMs = exp[n].T,
                        Message = $"IP ID {join.Ip:X2} {exp[n].Describe()}: expected {FeedbackState.Show(want)} at {FormatMs(exp[n].T)}, but nothing arrived within {toleranceMs} ms."
                    });
                }
                failedJoins.Add(join);
            }
            for (var m = 0; m < act.Count; m++)
                if (!used[m]) AddExtra(join, act[m], counts, diffs);
        }
        foreach (var (join, act) in actualBy)
            if (!expectedBy.ContainsKey(join))
                foreach (var a in act) AddExtra(join, a, counts, diffs);

        // Final state of every device.
        var expectedFinal = FeedbackState.ToMap(rec.End);
        foreach (var (ip, key) in expectedFinal.Keys.Concat(finalActual.Keys).Distinct().OrderBy(k => k.Item1).ThenBy(k => k.Item2.Kind).ThenBy(k => k.Item2.Join))
        {
            var want = FeedbackState.Get(expectedFinal, ip, key);
            var got = FeedbackState.Get(finalActual, ip, key);
            if (SigValue.AreEqual(want, got)) continue;
            counts.FinalDifferences++;
            if (failedJoins.Contains((ip, key))) continue; // already reported as missing or different
            diffs.Add(new ReplayDifference
            {
                Kind = ReplayDifference.FinalState, Ip = ip, Join = Describe(key), Expected = want, Actual = got, TimeMs = rec.DurationMs,
                Message = $"IP ID {ip:X2} {Describe(key)} ends as {FeedbackState.Show(got)}; the recording ended with {FeedbackState.Show(want)}."
            });
        }
    }

    private static void AddExtra((uint Ip, JoinKey Key) join, (long T, uint Ip, JoinKey Key, object Value) a, ReplayCounts counts, List<ReplayDifference> diffs)
    {
        counts.Extra++;
        diffs.Add(new ReplayDifference
        {
            Kind = ReplayDifference.Extra, Severity = "warn", Ip = join.Ip, Join = Describe(join.Key), Actual = a.Value, ActualTimeMs = a.T,
            Message = $"IP ID {join.Ip:X2} {Describe(join.Key)} changed to {FeedbackState.Show(a.Value)} at {FormatMs(a.T)} into the replay, which the recording doesn't have."
        });
    }

    private static string FormatMs(long ms) => $"{ms / 1000.0:0.00} s";
}
