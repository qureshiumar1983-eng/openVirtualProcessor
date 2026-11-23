using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Ops;

/// <summary>What a schedule does when it runs.</summary>
/// <param name="Type"><c>loadVersion</c> (a saved program version into a slot), <c>loadPath</c> (a program file on this computer), <c>restart</c>, <c>start</c>, <c>stop</c>, <c>backup</c>, <c>setSignal</c> or <c>pulse</c>.</param>
public sealed record ScheduleAction
{
    public const string LoadVersion = "loadVersion", LoadPath = "loadPath", Restart = "restart", Start = "start", Stop = "stop", Backup = "backup", SetSignal = "setSignal", Pulse = "pulse";
    public static readonly string[] Types = [LoadVersion, LoadPath, Restart, Start, Stop, Backup, SetSignal, Pulse];
    public string Type { get; init; } = Restart;
    public int? Slot { get; init; }
    public string? VersionId { get; init; }
    public string? Path { get; init; }
    public string? Signal { get; init; }
    public string? Value { get; init; }
    /// <summary>For load actions: start the program after loading it (default true).</summary>
    public bool StartAfter { get; init; } = true;

    [JsonIgnore] public bool NeedsSlot => Type != Backup;
}

/// <summary>
/// One scheduled action. <see cref="Mode"/> is how the person described it: <c>once</c> (at <see cref="At"/>), <c>daily</c> (at <see cref="Time"/>),
/// <c>weekly</c> (on <see cref="Days"/> at <see cref="Time"/>) or <c>cron</c> (<see cref="Cron"/>). All times are wall-clock times in the processor's time zone.
/// </summary>
public sealed record ScheduleDef
{
    public const int MaxSchedules = 200;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string Mode { get; init; } = "daily";
    /// <summary>For daily and weekly: <c>HH:mm</c>.</summary>
    public string? Time { get; init; }
    /// <summary>For weekly: 0 (Sunday) to 6 (Saturday).</summary>
    public int[]? Days { get; init; }
    /// <summary>For cron: the five-field expression.</summary>
    public string? Cron { get; init; }
    /// <summary>For once: <c>yyyy-MM-ddTHH:mm</c>, local time.</summary>
    public string? At { get; init; }
    /// <summary>If OpenVP was not running at the scheduled time, run it once when OpenVP starts (within 24 hours of the missed time). Off: the missed run is skipped and noted in the history.</summary>
    public bool RunMissed { get; init; }
    public ScheduleAction Action { get; init; } = new();

    // State, kept with the definition.
    public DateTimeOffset? NextDue { get; init; }
    public DateTimeOffset? LastRun { get; init; }
    public string? LastResult { get; init; }
    public bool? LastOk { get; init; }
    public int Runs { get; init; }

    /// <summary>The cron expression this schedule runs on (null for a one-off).</summary>
    public CronExpression? Compile()
    {
        switch (Mode)
        {
            case "daily":
                var (h, m) = ParseTime(Time);
                return CronExpression.Parse($"{m} {h} * * *");
            case "weekly":
            {
                var (h2, m2) = ParseTime(Time);
                if (Days == null || Days.Length == 0) throw new ArgumentException("Choose at least one day of the week.");
                if (Days.Any(d => d is < 0 or > 6)) throw new ArgumentException("Days of the week are 0 (Sunday) to 6 (Saturday).");
                return CronExpression.Parse($"{m2} {h2} * * {string.Join(',', Days.Distinct().Order())}");
            }
            case "cron":
                return CronExpression.Parse(Cron);
            case "once":
                return null;
            default:
                throw new ArgumentException("Choose once, daily, weekly or cron.");
        }
    }

    public static (int Hour, int Minute) ParseTime(string? t)
    {
        var parts = (t ?? "").Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h is < 0 or > 23 || m is < 0 or > 59)
            throw new ArgumentException("Enter the time as HH:mm, for example 03:30.");
        return (h, m);
    }

    /// <summary>The wall-clock time of a one-off.</summary>
    public DateTime OnceTime() =>
        DateTime.TryParseExact(At, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d : throw new ArgumentException("Enter the date and time as yyyy-MM-dd HH:mm.");

    /// <summary>Checks everything a person typed. Throws <see cref="ArgumentException"/> with a plain message.</summary>
    public ScheduleDef Validated()
    {
        var name = (Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Give the schedule a name.");
        if (name.Length > 80) throw new ArgumentException("The name can be at most 80 characters.");
        if (name.Any(char.IsControl)) throw new ArgumentException("The name can't contain control characters.");
        try
        {
            if (Mode == "once") OnceTime(); else Compile();
        }
        catch (FormatException e) { throw new ArgumentException(e.Message); }
        var a = Action ?? new ScheduleAction();
        if (!ScheduleAction.Types.Contains(a.Type)) throw new ArgumentException("Choose what the schedule does.");
        if (a.NeedsSlot && (a.Slot is null or < 1)) throw new ArgumentException("Choose a slot.");
        switch (a.Type)
        {
            case ScheduleAction.LoadVersion when string.IsNullOrWhiteSpace(a.VersionId): throw new ArgumentException("Choose a program version.");
            case ScheduleAction.LoadPath when string.IsNullOrWhiteSpace(a.Path): throw new ArgumentException("Enter the path of the program file on this computer.");
            case ScheduleAction.SetSignal or ScheduleAction.Pulse when string.IsNullOrWhiteSpace(a.Signal): throw new ArgumentException("Enter the signal name.");
            case ScheduleAction.SetSignal when a.Value == null: throw new ArgumentException("Enter the value to set.");
        }
        if ((a.Signal?.Length ?? 0) > 200 || (a.Value?.Length ?? 0) > 1000 || (a.Path?.Length ?? 0) > 1000) throw new ArgumentException("A field is too long.");
        return this with { Name = name };
    }
}

/// <summary>One run (or skipped run) in the history.</summary>
public sealed record ScheduleRun(DateTimeOffset Time, string ScheduleId, string Name, string Status, string Message, string Action, int? Slot, string Trigger);

/// <summary>A schedule with what the console shows next to it.</summary>
public sealed record ScheduleView(ScheduleDef Def, DateTimeOffset? NextRun, bool Running, string Summary);

/// <summary>
/// Runs actions at times. Each schedule is a one-off or a cron-style recurrence in the processor's time zone (see <see cref="CronExpression"/> for
/// how daylight saving changes are treated). <see cref="Tick"/> (called once a second) starts what is due.
/// <para><b>Runs missed while OpenVP was not running:</b> if a schedule's time passed while OpenVP was stopped (or the computer slept), it is skipped
/// and the history says so, unless the schedule has <see cref="ScheduleDef.RunMissed"/> on, in which case it runs once, when OpenVP starts, as long as
/// the missed time was within the last 24 hours. A run is "missed" when it is more than <see cref="LateAfter"/> late. Several missed times of one schedule
/// run once, not once each.</para>
/// </summary>
public sealed class Scheduler : IDisposable
{
    public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MissedWindow = TimeSpan.FromHours(24);
    public const int MaxHistory = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class FileDto { public List<ScheduleDef> Schedules { get; set; } = new(); }

    private readonly string _file, _historyFile;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<ScheduleDef, string, Task<string>> _execute;
    private readonly HashSet<string> _running = new();
    private readonly List<ScheduleRun> _history = new();
    private List<ScheduleDef> _defs = new();
    private Timer? _timer;
    private int _busy;

    public TimeZoneInfo Zone { get; }

    /// <summary>Raised (outside the lock) after a run finishes or is skipped.</summary>
    public event Action<ScheduleRun>? Ran;

    /// <param name="execute">Does the action; returns a short result ("Restarted slot 1"). Throwing marks the run failed with the exception's message. The second argument is the trigger: <c>schedule</c>, <c>missed</c> or <c>manual</c>.</param>
    public Scheduler(string dataDirectory, Func<ScheduleDef, string, Task<string>> execute, TimeZoneInfo? zone = null, Func<DateTimeOffset>? clock = null, bool autoStart = true)
    {
        _file = Path.Combine(dataDirectory, "schedules.json");
        _historyFile = Path.Combine(dataDirectory, "schedules-history.jsonl");
        _execute = execute;
        Zone = zone ?? TimeZoneInfo.Local;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Reload();
        if (autoStart) _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Reads the schedules and history from disk again (after an import).</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _defs = IntegrationFiles.Load<FileDto>(_file).Schedules.Where(d => !string.IsNullOrEmpty(d.Id)).ToList();
            _history.Clear();
            try
            {
                if (File.Exists(_historyFile))
                    foreach (var line in File.ReadLines(_historyFile, Encoding.UTF8).TakeLast(MaxHistory))
                        try { if (JsonSerializer.Deserialize<ScheduleRun>(line, Json) is { } r) _history.Add(r); } catch (JsonException) { }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    // ------------------------------------------------------------------ definitions

    public IReadOnlyList<ScheduleView> List()
    {
        lock (_gate) return _defs.Select(View).ToList();
    }

    public ScheduleDef? Get(string id) { lock (_gate) return _defs.FirstOrDefault(d => d.Id == id); }

    private ScheduleView View(ScheduleDef d) => new(d, d.Enabled ? d.NextDue : null, _running.Contains(d.Id), Describe(d));

    /// <summary>When a schedule would run after <paramref name="after"/>, ignoring its state (for the preview in the editor).</summary>
    public DateTimeOffset? NextAfter(ScheduleDef d, DateTimeOffset after)
    {
        if (d.Mode == "once") return CronExpression.InstantOf(d.OnceTime(), Zone) is var t && t > after ? t : null;
        return d.Compile()!.Next(after, Zone);
    }

    public List<DateTimeOffset> Preview(ScheduleDef d, int count = 5)
    {
        var now = _clock();
        if (d.Mode == "once") return NextAfter(d, now) is { } t ? [t] : [];
        return d.Compile()!.Upcoming(now, Zone, count);
    }

    public ScheduleDef Add(ScheduleDef def)
    {
        var v = def.Validated();
        lock (_gate)
        {
            if (_defs.Count >= ScheduleDef.MaxSchedules) throw new ArgumentException($"At most {ScheduleDef.MaxSchedules} schedules.");
            v = v with { Id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(), NextDue = null, LastRun = null, LastResult = null, LastOk = null, Runs = 0 };
            v = v with { NextDue = v.Enabled ? ComputeNext(v, _clock()) : null };
            _defs.Add(v);
            Save();
            return v;
        }
    }

    public ScheduleDef? Update(string id, Func<ScheduleDef, ScheduleDef> change)
    {
        lock (_gate)
        {
            var i = _defs.FindIndex(d => d.Id == id);
            if (i < 0) return null;
            var old = _defs[i];
            var next = change(old).Validated() with { Id = id, LastRun = old.LastRun, LastResult = old.LastResult, LastOk = old.LastOk, Runs = old.Runs };
            next = next with { NextDue = next.Enabled ? ComputeNext(next, _clock()) : null };
            _defs[i] = next;
            Save();
            return next;
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            var n = _defs.RemoveAll(d => d.Id == id);
            if (n > 0) Save();
            return n > 0;
        }
    }

    private DateTimeOffset? ComputeNext(ScheduleDef d, DateTimeOffset after)
    {
        if (d.Mode == "once")
        {
            var t = CronExpression.InstantOf(d.OnceTime(), Zone);
            return t > after ? t : null;
        }
        return d.Compile()!.Next(after, Zone);
    }

    // ------------------------------------------------------------------ running

    /// <summary>Starts everything that is due. Returns how many runs were started or skipped.</summary>
    public int Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return 0;
        try
        {
            var now = _clock();
            var started = 0;
            List<(ScheduleDef Def, string Trigger)> toRun = new();
            List<ScheduleRun> skipped = new();
            lock (_gate)
            {
                var changed = false;
                for (var i = 0; i < _defs.Count; i++)
                {
                    var d = _defs[i];
                    if (!d.Enabled) continue;
                    if (d.NextDue == null)
                    {
                        // Newly enabled or never computed; a one-off whose time already passed stays done.
                        var n = ComputeNext(d, now);
                        if (n != null) { _defs[i] = d with { NextDue = n }; changed = true; }
                        else if (d.Mode == "once" && d.Runs == 0 && d.LastRun == null) { /* in the past when it was made: nothing to do */ }
                        continue;
                    }
                    if (now < d.NextDue) continue;
                    var late = now - d.NextDue.Value;
                    var one = d.Mode == "once";
                    var next = one ? null : ComputeNext(d, now);
                    var after = d with { NextDue = next, Enabled = one ? false : d.Enabled };
                    changed = true;
                    if (_running.Contains(d.Id))
                    {
                        skipped.Add(new ScheduleRun(now, d.Id, d.Name, "skipped", "The previous run was still going.", d.Action.Type, d.Action.Slot, "schedule"));
                        _defs[i] = after;
                    }
                    else if (late <= LateAfter) { toRun.Add((d, "schedule")); _defs[i] = after; }
                    else if (d.RunMissed && late <= MissedWindow) { toRun.Add((d, "missed")); _defs[i] = after; }
                    else
                    {
                        var when = TimeZoneInfo.ConvertTime(d.NextDue.Value, Zone);
                        skipped.Add(new ScheduleRun(now, d.Id, d.Name, "skipped", $"OpenVP wasn't running at {when:yyyy-MM-dd HH:mm}, so this run was skipped.", d.Action.Type, d.Action.Slot, "missed"));
                        _defs[i] = after with { LastResult = "Skipped: OpenVP wasn't running at the scheduled time.", LastOk = null };
                    }
                }
                if (changed) Save();
            }
            foreach (var s in skipped) { Record(s); started++; }
            foreach (var (d, trigger) in toRun) { _ = RunAsync(d, trigger); started++; }
            return started;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>Runs a schedule now, whatever its time or whether it is enabled.</summary>
    public Task<ScheduleRun?> RunNowAsync(string id)
    {
        ScheduleDef? d;
        lock (_gate) d = _defs.FirstOrDefault(x => x.Id == id);
        return d == null ? Task.FromResult<ScheduleRun?>(null) : RunAsync(d, "manual")!;
    }

    private async Task<ScheduleRun?> RunAsync(ScheduleDef d, string trigger)
    {
        lock (_gate) { if (!_running.Add(d.Id)) return null; }
        string status, message;
        try { message = await _execute(d, trigger); status = "ok"; }
        catch (Exception e) { message = e.Message; status = "failed"; }
        var now = _clock();
        var run = new ScheduleRun(now, d.Id, d.Name, status, message, d.Action.Type, d.Action.Slot, trigger);
        lock (_gate)
        {
            _running.Remove(d.Id);
            var i = _defs.FindIndex(x => x.Id == d.Id);
            if (i >= 0)
            {
                _defs[i] = _defs[i] with { LastRun = now, LastResult = message, LastOk = status == "ok", Runs = _defs[i].Runs + 1 };
                Save();
            }
        }
        Record(run);
        return run;
    }

    // ------------------------------------------------------------------ history

    public IReadOnlyList<ScheduleRun> History(int limit = 100, string? scheduleId = null)
    {
        lock (_gate)
            return _history.Where(r => scheduleId == null || r.ScheduleId == scheduleId).Reverse().Take(Math.Clamp(limit, 1, MaxHistory)).ToList();
    }

    private void Record(ScheduleRun run)
    {
        lock (_gate)
        {
            _history.Add(run);
            if (_history.Count > MaxHistory + 50) _history.RemoveRange(0, _history.Count - MaxHistory);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
                File.AppendAllText(_historyFile, JsonSerializer.Serialize(run, Json).Replace("\r", "").Replace("\n", "") + "\n", Encoding.UTF8);
                if (new FileInfo(_historyFile).Length > 512 * 1024)
                    File.WriteAllLines(_historyFile, _history.Select(r => JsonSerializer.Serialize(r, Json).Replace("\r", "").Replace("\n", "")), Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        try { Ran?.Invoke(run); } catch { }
    }

    private void Save()
    {
        try { IntegrationFiles.Save(_file, new FileDto { Schedules = _defs }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>One line in plain words: "Every day at 03:30", "Mon, Wed at 06:00", "Once on 2026-01-05 09:00", or the cron text.</summary>
    public static string Describe(ScheduleDef d)
    {
        try
        {
            return d.Mode switch
            {
                "daily" => $"Every day at {d.Time}",
                "weekly" => $"{string.Join(", ", (d.Days ?? []).Distinct().Order().Select(x => new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[x]))} at {d.Time}",
                "once" => $"Once on {d.At?.Replace('T', ' ')}",
                _ => $"Cron {d.Cron}"
            };
        }
        catch (Exception) { return d.Mode; }
    }

    public void Dispose() { _timer?.Dispose(); _timer = null; }
}
