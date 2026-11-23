using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenVP.Core.Integrations;

/// <summary>One attempt-by-attempt outcome of a webhook delivery, for the delivery log.</summary>
public sealed record WebhookDelivery(long Id, DateTimeOffset Time, int Slot, string RuleId, string RuleName, string Url, string Event,
    bool Ok, int? Status, int Attempts, double Ms, string? Error, bool Test);

public sealed record WebhookStatus(int Rules, long Sent, long Failed, long Dropped, int Pending, DateTimeOffset? LastDelivery);

/// <summary>Signs webhook requests: <c>X-OpenVP-Signature: sha256=hex(HMAC-SHA256(secret, timestamp + "." + body))</c>.</summary>
public static class WebhookSigner
{
    public static string Sign(string secret, string timestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var data = new byte[prefix.Length + body.Length];
        prefix.CopyTo(data, 0);
        body.CopyTo(data.AsSpan(prefix.Length));
        return "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data)).ToLowerInvariant();
    }

    /// <summary>Checks a signature the way a receiver should (constant time).</summary>
    public static bool Verify(string secret, string timestamp, ReadOnlySpan<byte> body, string signature) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(secret, timestamp, body)), Encoding.ASCII.GetBytes(signature ?? ""));
}

/// <summary>
/// Sends outgoing webhooks. A rule fires when its signal changes, or when its condition (the watch language of
/// <see cref="SignalExpression"/>, including <c>for 2s</c>) becomes true. Each rule has its own bounded queue and its own
/// task, so a slow or dead endpoint only ever delays itself; when its queue is full the oldest deliveries are dropped (and
/// counted). Failed requests are retried with a growing pause (<see cref="Backoff"/>). Every delivery is written to a log.
/// </summary>
public sealed class WebhookService : IDisposable
{
    public const int QueueSize = 200, LogSize = 500;

    private sealed record Job(WebhookRule Rule, int Slot, string Event, DateTimeOffset Time, string? Signal, SigKind Kind, object? Value,
        IReadOnlyDictionary<string, object?>? Values, TaskCompletionSource<WebhookDelivery>? Test);

    private sealed class RuleWorker : IDisposable
    {
        public WebhookRule Rule = null!;
        public int Slot;
        public Channel<Job> Queue = null!;
        public Task Task = Task.CompletedTask;
        public CancellationTokenSource Cts = new();
        public void Dispose() { Queue.Writer.TryComplete(); Cts.Cancel(); }
    }

    private sealed class SlotRules
    {
        public SignalValueCache Cache = new();
        public WatchEngine? Engine;
        public Timer? Tick;
        public List<IDisposable> Subs = new();
        public Dictionary<string, RuleWorker> Workers = new();
        public Action<ProgramSlot>? OnState;
    }

    private readonly Processor _processor;
    private readonly IntegrationStore _store;
    private readonly SignalDirectory _directory;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Dictionary<int, SlotRules> _slots = new();
    private readonly ConcurrentQueue<WebhookDelivery> _log = new();
    private long _nextId, _sent, _failed, _dropped, _pending;
    private DateTimeOffset? _lastDelivery;
    private bool _disposed;

    /// <summary>Pauses before each retry. A request is tried once plus once per entry.</summary>
    public TimeSpan[] Backoff { get; set; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public WebhookService(Processor processor, IntegrationStore store, SignalDirectory directory, HttpMessageHandler? handler = null)
    {
        _processor = processor; _store = store; _directory = directory;
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // each attempt has its own timeout
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenVP/0.1");
    }

    public WebhookStatus Status()
    {
        int rules; lock (_gate) rules = _slots.Values.Sum(s => s.Workers.Count);
        return new WebhookStatus(rules, Interlocked.Read(ref _sent), Interlocked.Read(ref _failed), Interlocked.Read(ref _dropped),
            (int)Math.Max(0, Interlocked.Read(ref _pending)), _lastDelivery);
    }

    public IReadOnlyList<WebhookDelivery> Deliveries(int? slot = null, int max = 100) =>
        _log.Where(d => slot == null || d.Slot == slot).TakeLast(Math.Clamp(max, 1, LogSize)).Reverse().ToList();

    /// <summary>Read every slot's webhook rules again and (re)subscribe to exactly the signals they use.</summary>
    public void Apply()
    {
        for (var n = 1; n <= _processor.Slots.Count; n++) ApplySlot(n);
    }

    public void ApplySlot(int n)
    {
        if (_disposed) return;
        var rules = _store.Slot(n).Webhooks.Where(r => r.Enabled).ToList();
        var slot = _processor.Slot(n);
        List<RuleWorker> gone = new();
        lock (_gate)
        {
            if (!_slots.TryGetValue(n, out var sr)) _slots[n] = sr = new SlotRules();
            foreach (var s in sr.Subs) s.Dispose();
            sr.Subs.Clear();
            sr.Tick?.Dispose(); sr.Tick = null;
            if (sr.OnState != null) { slot.StateChanged -= sr.OnState; sr.OnState = null; }

            // Workers: keep one per rule id, replace it when the rule changed, drop the rest.
            var keep = new Dictionary<string, RuleWorker>();
            foreach (var r in rules)
            {
                if (sr.Workers.TryGetValue(r.Id, out var w) && w.Rule == r) { keep[r.Id] = w; continue; }
                if (w != null) gone.Add(w);
                keep[r.Id] = StartWorker(n, r);
            }
            foreach (var (id, w) in sr.Workers) if (!keep.ContainsKey(id)) gone.Add(w);
            sr.Workers = keep;

            // Rules on one signal.
            foreach (var g in rules.Where(r => r.Signal != null).GroupBy(r => r.Signal!))
            {
                var targets = g.Select(r => keep[r.Id]).ToArray();
                sr.Subs.Add(slot.Hub.Subscribe([g.Key], e =>
                {
                    foreach (var w in targets) Enqueue(w, new Job(w.Rule, n, "signal.change", e.Time, e.Signal, e.Kind, e.Value, null, null));
                }));
            }

            // Rules on a condition share one watch engine for the slot.
            var exprRules = rules.Where(r => r.Expression != null).ToList();
            sr.Engine = null;
            if (exprRules.Count > 0)
            {
                sr.Cache = new SignalValueCache();
                var engine = new WatchEngine(sr.Cache);
                var cache = sr.Cache;
                var byId = exprRules.ToDictionary(r => r.Id);
                engine.Set(exprRules.Select(r => new WatchDef(r.Id, r.Expression!, false, true, r.Name)));
                engine.Fired += (hit, def) =>
                {
                    if (!byId.TryGetValue(def.Id, out var r) || !keep.TryGetValue(r.Id, out var w)) return;
                    var vals = new Dictionary<string, object?>();
                    if (SignalExpression.TryParse(r.Expression, out var ex, out _))
                        foreach (var s in ex!.Signals) vals[s] = cache.Get(s) is { } v ? (v is string ? v : SignalValues.ForJson(v is bool ? SigKind.Digital : SigKind.Analog, v)) : null;
                    Enqueue(w, new Job(r, n, "expression.true", hit.Time, null, default, null, vals, null));
                };
                var names = exprRules.SelectMany(r => SignalExpression.Parse(r.Expression).Signals).Distinct().ToArray();
                sr.Engine = engine;
                sr.Subs.Add(slot.Hub.Subscribe(names, e =>
                {
                    cache.Set(e.Signal, e.Value);
                    engine.OnChange(e.Signal, e.Time);
                }));
                sr.Tick = new Timer(_ => { try { engine.Tick(DateTimeOffset.Now); } catch { } }, null, 100, 100);
                Seed(slot, cache, engine, exprRules);
                sr.OnState = s => { if (s.State == SlotState.Running) Seed(s, cache, engine, exprRules); };
                slot.StateChanged += sr.OnState;
            }
        }
        foreach (var w in gone) w.Dispose();
    }

    // The values a condition starts from: what the program has now, so it only fires on a change to true.
    private static void Seed(ProgramSlot slot, SignalValueCache cache, WatchEngine engine, List<WebhookRule> rules)
    {
        try { if (slot.Runtime is { } rt) cache.Seed(rt.GetSignals()); } catch { }
        engine.Set(rules.Select(r => new WatchDef(r.Id, r.Expression!, false, true, r.Name)));
    }

    private RuleWorker StartWorker(int slot, WebhookRule rule)
    {
        var w = new RuleWorker { Rule = rule, Slot = slot };
        w.Queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(QueueSize) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            j => { Interlocked.Increment(ref _dropped); Interlocked.Decrement(ref _pending); });
        var token = w.Cts.Token;
        w.Task = Task.Run(async () =>
        {
            try
            {
                await foreach (var job in w.Queue.Reader.ReadAllAsync(token))
                {
                    try { await Deliver(job, token, retry: true); }
                    finally { Interlocked.Decrement(ref _pending); }
                }
            }
            catch (OperationCanceledException) { }
        });
        return w;
    }

    private void Enqueue(RuleWorker w, Job job)
    {
        Interlocked.Increment(ref _pending);
        if (!w.Queue.Writer.TryWrite(job)) Interlocked.Decrement(ref _pending);
    }

    /// <summary>Sends a sample event to one rule's URL now, once, and returns what happened. Throws KeyNotFoundException for an unknown rule.</summary>
    public async Task<WebhookDelivery> TestAsync(int slot, string ruleId, CancellationToken ct = default)
    {
        var rule = _store.Slot(slot).Webhooks.FirstOrDefault(r => r.Id == ruleId) ?? throw new KeyNotFoundException("There's no such webhook.");
        object? sample = true; var kind = SigKind.Digital;
        if (rule.Signal != null && _directory.TryGet(slot, rule.Signal, out var info)) { kind = info.Kind; sample = info.Value; }
        var job = new Job(rule, slot, "test", DateTimeOffset.Now, rule.Signal, kind, sample, null, null);
        return await Deliver(job, ct, retry: false);
    }

    private byte[] Body(Job j)
    {
        var slot = _processor.Slot(j.Slot);
        var o = new Dictionary<string, object?>
        {
            ["event"] = j.Event,
            ["time"] = j.Time.ToString("O"),
            ["slot"] = j.Slot,
            ["room"] = slot.RoomId,
            ["program"] = slot.Runtime?.ProgramName,
            ["webhook"] = j.Rule.Label
        };
        if (j.Signal != null)
        {
            o["signal"] = j.Signal;
            o["kind"] = SignalValues.KindName(j.Kind);
            o["value"] = SignalValues.ForJson(j.Kind, j.Value);
        }
        if (j.Rule.Expression != null) o["expression"] = j.Rule.Expression;
        if (j.Values != null) o["values"] = j.Values;
        return JsonSerializer.SerializeToUtf8Bytes(o);
    }

    private async Task<WebhookDelivery> Deliver(Job job, CancellationToken ct, bool retry)
    {
        var body = Body(job);
        var id = Interlocked.Increment(ref _nextId);
        var sw = Stopwatch.StartNew();
        var attempts = 0; int? status = null; string? error = null; var ok = false;
        var pauses = retry ? Backoff : [];
        for (var i = 0; ; i++)
        {
            attempts++;
            var retryable = true;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, job.Rule.Url) { Content = new ByteArrayContent(body) };
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                foreach (var (k, v) in job.Rule.Headers) req.Headers.TryAddWithoutValidation(k, v);
                req.Headers.TryAddWithoutValidation("X-OpenVP-Event", job.Event);
                req.Headers.TryAddWithoutValidation("X-OpenVP-Delivery", id.ToString());
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                req.Headers.TryAddWithoutValidation("X-OpenVP-Timestamp", ts);
                if (!string.IsNullOrEmpty(job.Rule.Secret)) req.Headers.TryAddWithoutValidation("X-OpenVP-Signature", WebhookSigner.Sign(job.Rule.Secret, ts, body));
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(Timeout);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, attempt.Token);
                status = (int)resp.StatusCode;
                if (resp.IsSuccessStatusCode) { ok = true; error = null; break; }
                error = $"The server answered {(int)resp.StatusCode} {resp.ReasonPhrase}";
                retryable = status >= 500 || status is 408 or 429;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { error = "Cancelled."; retryable = false; }
            catch (OperationCanceledException) { error = $"No answer within {Timeout.TotalSeconds:0} s."; }
            catch (Exception e) { error = e is HttpRequestException { InnerException: { } inner } ? inner.Message : e.Message; }
            if (!retryable || i >= pauses.Length || ct.IsCancellationRequested) break;
            try { await Task.Delay(pauses[i], ct); } catch (OperationCanceledException) { break; }
        }
        var d = new WebhookDelivery(id, DateTimeOffset.Now, job.Slot, job.Rule.Id, job.Rule.Label, job.Rule.Url, job.Event, ok, status, attempts, Math.Round(sw.Elapsed.TotalMilliseconds, 1), error, job.Event == "test");
        _log.Enqueue(d);
        while (_log.Count > LogSize && _log.TryDequeue(out _)) { }
        _lastDelivery = d.Time;
        if (!job.Event.Equals("test")) Interlocked.Increment(ref ok ? ref _sent : ref _failed);
        return d;
    }

    public void Dispose()
    {
        List<RuleWorker> all = new();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var (n, sr) in _slots)
            {
                foreach (var s in sr.Subs) s.Dispose();
                sr.Tick?.Dispose();
                if (sr.OnState != null) _processor.Slot(n).StateChanged -= sr.OnState;
                all.AddRange(sr.Workers.Values);
            }
            _slots.Clear();
        }
        foreach (var w in all) w.Dispose();
        _http.Dispose();
    }
}
