using System.Text.Json;

namespace OpenVP.Core.Alerts;

/// <summary>How one send to one channel ended.</summary>
public sealed record DeliveryOutcome(bool Ok, int Attempts, double Ms, string? Note);

/// <summary>Sends notifications to channels on other threads. <see cref="AlertEngine"/> never waits on it.</summary>
public interface IAlertDispatcher
{
    /// <param name="retry">False for a Test: try once and report what happened.</param>
    void Enqueue(AlertChannel channel, Notification notification, bool retry, Action<DeliveryOutcome> done);
}

/// <summary>Counts for the tray icon and the console badge.</summary>
public sealed record AlertStatus(int Critical, int Warning, int Info, int Acknowledged, int Muted, string Color);

/// <summary>
/// Turns events into notifications. Event sources call <see cref="Raise"/> and <see cref="Resolve"/>; the engine keeps one record
/// per alert (the same <c>Key</c> is the same alert), decides which channels hear about it from the rules (kind, slot, minimum
/// severity, quiet hours), sends each alert at most once per rule's repeat time, sends a "resolved" message when it clears, and keeps
/// a history with the delivery status of every send. Raising never blocks: sends go to the dispatcher's own queues and threads.
/// </summary>
public sealed class AlertEngine : IDisposable
{
    public const int MaxHistory = 500, MaxDeliveriesPerAlert = 60;

    private sealed class Delivery
    {
        public DateTimeOffset Time; public string ChannelId = "", ChannelName = "", Event = "", Status = "queued"; public string? Note; public int Attempts; public double Ms;
        public AlertDeliveryView View() => new(Time, ChannelId, ChannelName, Event, Status, Note, Attempts, Ms);
    }

    private sealed class Entry
    {
        public string Id = "", Key = "", Kind = ""; public int? Slot; public AlertSeverity Severity; public string Title = "", Message = "";
        public DateTimeOffset FirstTime, LastTime; public int Count = 1; public string State = "active"; public DateTimeOffset? ResolvedTime;
        public string? AckBy; public DateTimeOffset? AckTime;
        public List<Delivery> Deliveries = new();
        public HashSet<string> Notified = new();
        public List<KeyValuePair<string, string>> Facts = new();
    }

    private sealed record Send(AlertChannel Channel, Notification Note, Delivery Delivery, bool Retry);

    private readonly Func<AlertConfig> _config;
    private readonly IAlertDispatcher _dispatcher;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _zone;
    private readonly Func<int, (string? Room, string? Program)> _slotInfo;
    private readonly string _host;
    private readonly string? _file;
    private readonly object _gate = new();
    private readonly List<Entry> _history = new();                       // oldest first
    private readonly Dictionary<string, Entry> _active = new();
    private readonly Dictionary<string, Entry> _lastTransient = new();
    private readonly Dictionary<(string Rule, string Key), DateTimeOffset> _lastSent = new();
    private readonly Dictionary<string, DateTimeOffset> _mutes = new();
    private Timer? _saveTimer;
    private int _dirty;
    private bool _disposed;

    /// <summary>Raised (outside the lock) after any change an open page or the tray cares about: a raise, a resolve, an acknowledge, a mute, a delivery result.</summary>
    public event Action? Changed;

    public AlertEngine(Func<AlertConfig> config, IAlertDispatcher dispatcher, string? stateFile = null, Func<DateTimeOffset>? clock = null, TimeZoneInfo? zone = null,
        Func<int, (string? Room, string? Program)>? slotInfo = null, string? host = null)
    {
        _config = config; _dispatcher = dispatcher; _file = stateFile;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _zone = zone ?? TimeZoneInfo.Local;
        _slotInfo = slotInfo ?? (_ => (null, null));
        _host = host ?? Environment.MachineName;
        Load();
    }

    // ------------------------------------------------------------------ raising and resolving

    /// <summary>Records an alert and sends it to the channels the rules choose. Returns the alert's id.</summary>
    public string Raise(AlertCandidate c)
    {
        var cfg = _config();
        var now = _clock();
        var sends = new List<Send>();
        string id;
        lock (_gate)
        {
            Entry? e = null; var isNew = true; var upgraded = false;
            var repeat = TimeSpan.FromMinutes(cfg.Settings.RepeatMinutes);
            if (!c.Transient && _active.TryGetValue(c.Key, out var live)) { e = live; isNew = false; }
            else if (c.Transient && repeat > TimeSpan.Zero && _lastTransient.TryGetValue(c.Key, out var recent) && now - recent.FirstTime < repeat) { e = recent; isNew = false; }
            if (e == null)
            {
                e = new Entry
                {
                    Id = Guid.NewGuid().ToString("N")[..12], Key = c.Key, Kind = c.Kind, Slot = c.Slot, Severity = c.Severity, Title = c.Title, Message = c.Message,
                    FirstTime = now, LastTime = now, State = c.Transient ? "event" : "active", Facts = c.Facts?.ToList() ?? new()
                };
                _history.Add(e);
                if (_history.Count > MaxHistory) TrimHistory();
                if (c.Transient) _lastTransient[c.Key] = e; else _active[c.Key] = e;
            }
            else
            {
                e.Count++; e.LastTime = now; e.Message = c.Message; e.Title = c.Title;
                if (c.Facts != null) e.Facts = c.Facts.ToList();
                if (c.Severity > e.Severity) { e.Severity = c.Severity; upgraded = true; e.AckBy = null; e.AckTime = null; }
            }
            Route(e, cfg, now, isNew ? "raised" : upgraded ? "raised" : "reminder", bypassRate: upgraded, only: null, sends);
            id = e.Id;
        }
        Finish(sends);
        return id;
    }

    /// <summary>The condition behind <paramref name="key"/> is gone. Marks the alert resolved and tells the channels that heard about it. Does nothing if it isn't active.</summary>
    public bool Resolve(string key, string? message = null)
    {
        var cfg = _config();
        var now = _clock();
        var sends = new List<Send>();
        lock (_gate)
        {
            if (!_active.TryGetValue(key, out var e)) return false;
            _active.Remove(key);
            e.State = "resolved"; e.ResolvedTime = now; e.LastTime = now;
            if (message != null) e.Message = message;
            Route(e, cfg, now, "resolved", bypassRate: true, only: e.Notified.ToArray(), sends);
        }
        Finish(sends);
        return true;
    }

    public bool IsActive(string key) { lock (_gate) return _active.ContainsKey(key); }

    private void Route(Entry e, AlertConfig cfg, DateTimeOffset now, string evt, bool bypassRate, IReadOnlyCollection<string>? only, List<Send> sends)
    {
        var plan = new Dictionary<string, (AlertChannel Ch, AlertRule Rule, string? Reason)>();
        foreach (var rule in cfg.Rules)
        {
            if (!rule.Enabled || !Matches(rule, e)) continue;
            if (evt == "resolved" && !rule.NotifyResolved) continue;
            foreach (var cid in rule.Channels)
            {
                if (only != null && !only.Contains(cid)) continue;
                var ch = cfg.Channel(cid);
                if (ch == null || !ch.Enabled) continue;
                var reason = Suppression(rule, e, evt, now, bypassRate, cfg);
                if (plan.TryGetValue(cid, out var prev) && prev.Reason == null) continue;   // already going out through another rule
                plan[cid] = (ch, rule, reason);
            }
        }
        foreach (var (cid, (ch, rule, reason)) in plan)
        {
            var d = new Delivery { Time = now, ChannelId = cid, ChannelName = ch.Name, Event = evt };
            if (reason != null)
            {
                if (e.Deliveries.Count >= MaxDeliveriesPerAlert) continue;
                d.Status = "suppressed"; d.Note = reason; e.Deliveries.Add(d);
                continue;
            }
            if (e.Deliveries.Count >= MaxDeliveriesPerAlert) e.Deliveries.RemoveAt(0);
            e.Deliveries.Add(d);
            if (evt != "resolved") { _lastSent[(rule.Id, e.Key)] = now; e.Notified.Add(cid); }
            sends.Add(new Send(ch, MakeNotification(e, evt, cfg, now), d, true));
        }
        MarkDirty();
    }

    private string? Suppression(AlertRule rule, Entry e, string evt, DateTimeOffset now, bool bypassRate, AlertConfig cfg)
    {
        if (_mutes.TryGetValue(e.Key, out var until))
        {
            if (until > now) return "muted" + (until.Year > now.Year + 5 ? "" : $" until {until.ToLocalTime():MMM d HH:mm}");
            _mutes.Remove(e.Key);
        }
        if (evt == "reminder" && e.AckTime != null) return "acknowledged";
        var local = TimeZoneInfo.ConvertTime(now, _zone).DateTime;
        if (rule.Quiet.IsQuiet(local) && !(rule.Quiet.AllowCritical && e.Severity == AlertSeverity.Critical)) return "quiet hours";
        if (evt != "resolved" && !bypassRate)
        {
            var repeat = rule.RepeatMinutes ?? cfg.Settings.RepeatMinutes;
            if (repeat > 0 && _lastSent.TryGetValue((rule.Id, e.Key), out var last) && now - last < TimeSpan.FromMinutes(repeat))
                return $"the same alert went out {Math.Max(1, (int)Math.Round((now - last).TotalMinutes))} min ago (at most once per {repeat} min)";
        }
        return null;
    }

    private static bool Matches(AlertRule r, Entry e) =>
        e.Severity >= r.MinSeverity
        && (r.Kinds.Length == 0 || r.Kinds.Contains(e.Kind))
        && (r.Slots.Length == 0 || (e.Slot is { } s && r.Slots.Contains(s)));

    private Notification MakeNotification(Entry e, string evt, AlertConfig cfg, DateTimeOffset now)
    {
        var (room, program) = e.Slot is { } s ? _slotInfo(s) : (null, null);
        return new Notification(evt, e.Id, e.Key, e.Kind, e.Severity, e.Title, e.Message, e.Slot, room, program, now, e.Count, e.Facts, _host, null, cfg.Settings.ConsoleUrl);
    }

    private void Finish(List<Send> sends)
    {
        foreach (var s in sends)
        {
            var send = s;
            try
            {
                _dispatcher.Enqueue(send.Channel, send.Note, send.Retry, o =>
                {
                    lock (_gate) { send.Delivery.Status = o.Ok ? "sent" : "failed"; send.Delivery.Attempts = o.Attempts; send.Delivery.Ms = o.Ms; send.Delivery.Note = o.Note; MarkDirty(); }
                    RaiseChanged();
                });
            }
            catch (Exception ex)
            {
                lock (_gate) { send.Delivery.Status = "failed"; send.Delivery.Note = ex.Message; }
            }
        }
        RaiseChanged();
    }

    private void RaiseChanged() { try { Changed?.Invoke(); } catch { } }

    // ------------------------------------------------------------------ test, acknowledge, mute

    /// <summary>Sends a test message to one channel, whatever the rules say, and waits for the outcome (up to <paramref name="timeout"/>).</summary>
    public async Task<AlertDeliveryView> TestAsync(string channelId, TimeSpan? timeout = null)
    {
        var cfg = _config();
        var ch = cfg.Channel(channelId) ?? throw new KeyNotFoundException("There's no such channel.");
        var now = _clock();
        var e = new Entry
        {
            Id = Guid.NewGuid().ToString("N")[..12], Key = $"test:{channelId}:{now.ToUnixTimeMilliseconds()}", Kind = AlertKinds.Test, Severity = AlertSeverity.Info,
            Title = $"Test message for {ch.Name}", Message = "This is a test from OpenVP. If you can read it, this channel works.", FirstTime = now, LastTime = now, State = "event"
        };
        var res = await SendDirectAsync(e, "test", [ch], n => n, retry: false, timeout);
        return res[0];
    }

    /// <summary>
    /// Sends a health report to the given channels (those that exist and are enabled), outside the rules, records it in the history and waits for
    /// every channel's outcome. <paramref name="build"/> turns the prepared notification into the report's own (HTML and facts).
    /// </summary>
    public async Task<AlertView> SendReportAsync(string title, string text, string? html, IReadOnlyList<KeyValuePair<string, string>> facts, IEnumerable<string> channelIds, TimeSpan? timeout = null)
    {
        var cfg = _config();
        var chans = channelIds.Distinct().Select(cfg.Channel).Where(c => c is { Enabled: true }).Select(c => c!).ToList();
        var now = _clock();
        var e = new Entry
        {
            Id = Guid.NewGuid().ToString("N")[..12], Key = $"report:{now.ToUnixTimeMilliseconds()}", Kind = "report", Severity = AlertSeverity.Info,
            Title = title, Message = text, FirstTime = now, LastTime = now, State = "event", Facts = facts.ToList()
        };
        if (chans.Count == 0)
        {
            lock (_gate) { _history.Add(e); if (_history.Count > MaxHistory) TrimHistory(); MarkDirty(); }
            RaiseChanged();
            throw new InvalidOperationException("The report has no channel to go to. Choose at least one enabled channel.");
        }
        await SendDirectAsync(e, "report", chans, n => n with { Html = html }, retry: true, timeout);
        lock (_gate) return View(e);
    }

    private async Task<List<AlertDeliveryView>> SendDirectAsync(Entry e, string evt, IReadOnlyList<AlertChannel> chans, Func<Notification, Notification> shape, bool retry, TimeSpan? timeout)
    {
        var cfg = _config();
        var now = _clock();
        var pending = new List<(Delivery D, Task<DeliveryOutcome> Result)>();
        lock (_gate)
        {
            _history.Add(e);
            if (_history.Count > MaxHistory) TrimHistory();
            foreach (var ch in chans)
            {
                var d = new Delivery { Time = now, ChannelId = ch.Id, ChannelName = ch.Name, Event = evt };
                e.Deliveries.Add(d);
                var tcs = new TaskCompletionSource<DeliveryOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                var note = shape(MakeNotification(e, evt, cfg, now));
                try
                {
                    _dispatcher.Enqueue(ch, note, retry, o => { lock (_gate) { d.Status = o.Ok ? "sent" : "failed"; d.Attempts = o.Attempts; d.Ms = o.Ms; d.Note = o.Note; MarkDirty(); } tcs.TrySetResult(o); RaiseChanged(); });
                }
                catch (Exception ex) { d.Status = "failed"; d.Note = ex.Message; tcs.TrySetResult(new DeliveryOutcome(false, 0, 0, ex.Message)); }
                pending.Add((d, tcs.Task));
            }
            MarkDirty();
        }
        RaiseChanged();
        var all = Task.WhenAll(pending.Select(p => p.Result));
        var wait = timeout ?? TimeSpan.FromSeconds(retry ? 120 : 30);
        if (await Task.WhenAny(all, Task.Delay(wait)) != all)
            lock (_gate) foreach (var (d, r) in pending) if (!r.IsCompleted) { d.Status = "failed"; d.Note = $"No result within {wait.TotalSeconds:0} s."; }
        lock (_gate) return pending.Select(p => p.D.View()).ToList();
    }

    public bool Acknowledge(string id, string who)
    {
        lock (_gate)
        {
            var e = _history.FirstOrDefault(x => x.Id == id);
            if (e == null) return false;
            e.AckBy = who; e.AckTime = _clock();
            MarkDirty();
        }
        RaiseChanged();
        return true;
    }

    /// <summary>Stops sending anything about this alert (and later ones with the same key) for <paramref name="duration"/>; null is until unmuted.</summary>
    public bool Mute(string id, TimeSpan? duration)
    {
        lock (_gate)
        {
            var e = _history.FirstOrDefault(x => x.Id == id);
            if (e == null) return false;
            _mutes[e.Key] = duration == null ? DateTimeOffset.MaxValue : _clock() + duration.Value;
            MarkDirty();
        }
        RaiseChanged();
        return true;
    }

    public bool Unmute(string id)
    {
        bool ok;
        lock (_gate)
        {
            var e = _history.FirstOrDefault(x => x.Id == id);
            ok = e != null && _mutes.Remove(e.Key);
            if (ok) MarkDirty();
        }
        if (ok) RaiseChanged();
        return ok;
    }

    // ------------------------------------------------------------------ reading

    private AlertView View(Entry e)
    {
        var now = _clock();
        DateTimeOffset? muted = _mutes.TryGetValue(e.Key, out var u) && u > now ? u : null;
        return new AlertView(e.Id, e.Key, e.Kind, e.Slot, e.Severity, e.Title, e.Message, e.FirstTime, e.LastTime, e.Count, e.State, e.ResolvedTime, e.AckBy, e.AckTime, muted,
            e.Deliveries.Select(d => d.View()).ToList());
    }

    /// <summary>Alerts that are still going on, worst and newest first.</summary>
    public IReadOnlyList<AlertView> Active()
    {
        lock (_gate) return _active.Values.OrderByDescending(e => e.Severity).ThenByDescending(e => e.FirstTime).Select(View).ToList();
    }

    /// <summary>Everything, newest first (active ones, resolved ones, one-off events and tests).</summary>
    public IReadOnlyList<AlertView> History(int limit = 200)
    {
        lock (_gate) return Enumerable.Reverse(_history).Take(Math.Clamp(limit, 1, MaxHistory)).Select(View).ToList();
    }

    public AlertView? Get(string id) { lock (_gate) return _history.Where(e => e.Id == id).Select(View).FirstOrDefault(); }

    public IReadOnlyList<AlertView> Between(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_gate) return _history.Where(e => e.FirstTime >= from && e.FirstTime < to && e.Kind is not (AlertKinds.Test or "report")).Select(View).ToList();
    }

    public AlertStatus Status()
    {
        lock (_gate)
        {
            var now = _clock();
            var live = _active.Values.ToList();
            var muted = live.Count(e => _mutes.TryGetValue(e.Key, out var u) && u > now);
            var open = live.Where(e => e.AckTime == null && !(_mutes.TryGetValue(e.Key, out var u) && u > now)).ToList();
            var crit = open.Count(e => e.Severity == AlertSeverity.Critical);
            var warn = open.Count(e => e.Severity == AlertSeverity.Warning);
            var info = open.Count(e => e.Severity == AlertSeverity.Info);
            return new AlertStatus(crit, warn, info, live.Count(e => e.AckTime != null), muted, crit > 0 ? "red" : warn > 0 ? "amber" : "green");
        }
    }

    public IReadOnlyDictionary<string, DateTimeOffset> Mutes() { lock (_gate) return _mutes.Where(m => m.Value > _clock()).ToDictionary(m => m.Key, m => m.Value); }

    // ------------------------------------------------------------------ persistence

    private void TrimHistory()
    {
        while (_history.Count > MaxHistory)
        {
            var old = _history[0];
            _history.RemoveAt(0);
            if (_active.TryGetValue(old.Key, out var a) && a == old) _active.Remove(old.Key);
        }
    }

    private sealed record StateFile(List<AlertView> History, Dictionary<string, DateTimeOffset> Mutes);

    private void Load()
    {
        if (_file == null || !File.Exists(_file)) return;
        try
        {
            var s = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(_file), AlertJson.Options);
            if (s == null) return;
            foreach (var v in s.History ?? [])
            {
                // Whatever was active when OpenVP stopped is re-checked by the sources, so it starts out ended.
                var ended = v.State == "active";
                var e = new Entry
                {
                    Id = v.Id, Key = v.Key, Kind = v.Kind, Slot = v.Slot, Severity = v.Severity, Title = v.Title, Message = v.Message, FirstTime = v.FirstTime, LastTime = v.LastTime,
                    Count = v.Count, State = ended ? "resolved" : v.State, ResolvedTime = ended ? v.LastTime : v.ResolvedTime, AckBy = v.AckBy, AckTime = v.AckTime
                };
                foreach (var d in v.Deliveries ?? []) e.Deliveries.Add(new Delivery { Time = d.Time, ChannelId = d.ChannelId, ChannelName = d.ChannelName, Event = d.Event, Status = d.Status == "queued" ? "failed" : d.Status, Note = d.Status == "queued" ? "OpenVP stopped before this was sent." : d.Note, Attempts = d.Attempts, Ms = d.Ms });
                _history.Add(e);
            }
            foreach (var (k, u) in s.Mutes ?? new()) if (u > _clock()) _mutes[k] = u;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { _history.Clear(); }
    }

    private void MarkDirty()
    {
        if (_file == null || _disposed) return;
        if (Interlocked.Exchange(ref _dirty, 1) == 0) _saveTimer = new Timer(_ => SaveNow(), null, 1000, Timeout.Infinite);
    }

    private void SaveNow()
    {
        if (_file == null) return;
        string json;
        lock (_gate)
        {
            Interlocked.Exchange(ref _dirty, 0);
            json = JsonSerializer.Serialize(new StateFile(_history.Select(View).ToList(), _mutes.ToDictionary(m => m.Key, m => m.Value)), AlertJson.Options);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* history stays in memory */ }
    }

    public void Dispose()
    {
        _disposed = true;
        _saveTimer?.Dispose();
        SaveNow();
    }
}
