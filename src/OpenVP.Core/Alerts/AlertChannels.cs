using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Alerts;

/// <summary>Where and how to reach an SMTP server.</summary>
public sealed record SmtpEndpoint(string Host, int Port, string Security, string? Username, string? Password);

/// <summary>Sends one email. The server project provides the MailKit implementation; tests supply their own.</summary>
public interface IEmailSender
{
    Task SendAsync(SmtpEndpoint server, EmailMessage message, CancellationToken ct);
}

/// <summary>
/// The server-sent-events feed behind <c>/api/alerts/stream</c>: the Electron app and the console pages listen to it to show desktop and
/// browser notifications. Each subscriber has a small queue; a listener that stops reading is dropped rather than held in memory.
/// </summary>
public sealed class AlertStream
{
    private readonly object _gate = new();
    private readonly List<Channel<string>> _subs = new();
    public const int MaxSubscribers = 20, QueueSize = 100;
    private bool _closed;

    public int Listeners { get { lock (_gate) return _subs.Count; } }

    /// <summary>Returns a reader of ready-to-send SSE frames, or null if too many are listening. Dispose the registration to stop.</summary>
    public (ChannelReader<string> Reader, IDisposable Registration)? Subscribe()
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueSize) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate)
        {
            if (_closed || _subs.Count >= MaxSubscribers) return null;
            _subs.Add(ch);
        }
        return (ch.Reader, new Reg(this, ch));
    }

    private sealed class Reg(AlertStream s, Channel<string> ch) : IDisposable
    {
        public void Dispose() { lock (s._gate) s._subs.Remove(ch); ch.Writer.TryComplete(); }
    }

    /// <summary>Ends every listener's stream (so a shutting-down server isn't held up by open connections) and accepts no more.</summary>
    public void Close()
    {
        Channel<string>[] subs;
        lock (_gate) { _closed = true; subs = _subs.ToArray(); _subs.Clear(); }
        foreach (var s in subs) s.Writer.TryComplete();
    }

    /// <summary>Sends one event to every listener. Returns how many there were.</summary>
    public int Publish(string eventName, string json)
    {
        var frame = $"event: {eventName}\ndata: {json}\n\n";
        Channel<string>[] subs;
        lock (_gate) subs = _subs.ToArray();
        foreach (var s in subs) s.Writer.TryWrite(frame);
        return subs.Length;
    }

    public static string NotificationJson(Notification n) => JsonSerializer.Serialize(new
    {
        id = n.Id, @event = n.Event, kind = n.Kind, key = n.Key, severity = AlertFormats.Label(n.Severity).ToLowerInvariant(), title = n.Title, message = n.Text,
        slot = n.Slot, program = n.Program, time = n.Time, count = n.Count
    }, AlertJson.Options);
}

/// <summary>
/// Sends notifications to channels. Each channel has its own bounded queue and its own task, so a slow or dead SMTP server or webhook
/// delays only itself; when its queue is full the oldest message is dropped and counted. Failed sends are retried after the pauses in
/// <see cref="Backoff"/> (network errors, timeouts, 5xx, 408 and 429), like outgoing webhooks. Generic webhooks, Teams, Slack and ntfy go
/// through <see cref="WebhookHttp"/>, the same signed sender the integrations use.
/// </summary>
public sealed class ChannelDispatcher : IAlertDispatcher, IDisposable
{
    public const int QueueSize = 100;

    private sealed record Job(AlertChannel Channel, Notification Note, bool Retry, Action<DeliveryOutcome> Done);

    private sealed class Worker : IDisposable
    {
        public Channel<Job> Queue = null!;
        public CancellationTokenSource Cts = new();
        public void Dispose() { Queue.Writer.TryComplete(); Cts.Cancel(); }
    }

    private readonly Func<string, string?> _secret;
    private readonly IEmailSender? _email;
    private readonly AlertStream _stream;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, Worker> _workers = new();
    private long _nextId, _dropped, _sent, _failed;
    private bool _disposed;

    public TimeSpan[] Backoff { get; set; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Sent => Interlocked.Read(ref _sent);
    public long Failed => Interlocked.Read(ref _failed);

    public ChannelDispatcher(Func<string, string?> secret, IEmailSender? email, AlertStream stream, HttpMessageHandler? handler = null)
    {
        _secret = secret; _email = email; _stream = stream;
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenVP/0.1");
    }

    public void Enqueue(AlertChannel channel, Notification note, bool retry, Action<DeliveryOutcome> done)
    {
        Worker w;
        lock (_gate)
        {
            if (_disposed) { done(new DeliveryOutcome(false, 0, 0, "OpenVP is stopping.")); return; }
            if (!_workers.TryGetValue(channel.Id, out w!)) _workers[channel.Id] = w = Start();
        }
        if (!w.Queue.Writer.TryWrite(new Job(channel, note, retry, done))) done(new DeliveryOutcome(false, 0, 0, "The channel was removed."));
    }

    private Worker Start()
    {
        var w = new Worker();
        w.Queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(QueueSize) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            dropped => { Interlocked.Increment(ref _dropped); try { dropped.Done(new DeliveryOutcome(false, 0, 0, "Dropped: this channel's queue was full.")); } catch { } });
        var token = w.Cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var job in w.Queue.Reader.ReadAllAsync(token))
                {
                    DeliveryOutcome o;
                    try { o = await SendAsync(job, token); }
                    catch (OperationCanceledException) { o = new DeliveryOutcome(false, 0, 0, "Cancelled."); }
                    catch (Exception e) { o = new DeliveryOutcome(false, 1, 0, e.Message); }
                    Interlocked.Increment(ref o.Ok ? ref _sent : ref _failed);
                    try { job.Done(o); } catch { }
                }
            }
            catch (OperationCanceledException) { }
        });
        return w;
    }

    /// <summary>Sends one notification now (no queue). Used by the queue's task, and directly by tests.</summary>
    public async Task<DeliveryOutcome> SendAsync(AlertChannel ch, Notification n, bool retry, CancellationToken ct = default) =>
        await SendAsync(new Job(ch, n, retry, _ => { }), ct);

    private async Task<DeliveryOutcome> SendAsync(Job job, CancellationToken ct)
    {
        var ch = job.Channel; var n = job.Note;
        var pauses = job.Retry ? Backoff : [];
        switch (ch.Type)
        {
            case "desktop":
            {
                var listeners = _stream.Publish("alert", AlertStream.NotificationJson(n));
                return new DeliveryOutcome(true, 1, 0, listeners == 0 ? "Nobody was listening (no console page or desktop app open)." : $"{listeners} listener{(listeners == 1 ? "" : "s")}");
            }
            case "email":
            {
                if (_email == null) return new DeliveryOutcome(false, 0, 0, "Email isn't available in this build.");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var ep = new SmtpEndpoint(ch.Host ?? "", ch.Port, ch.Security, string.IsNullOrEmpty(ch.Username) ? null : ch.Username, _secret(ch.SecretName("password")));
                var msg = AlertFormats.Email(n, AlertConfigValidator.Addresses(ch.From).First(), AlertConfigValidator.Addresses(ch.To));
                var attempts = 0; string? error = null;
                for (var i = 0; ; i++)
                {
                    attempts++;
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(Timeout.TotalSeconds, 30)));
                        await _email.SendAsync(ep, msg, cts.Token);
                        return new DeliveryOutcome(true, attempts, Math.Round(sw.Elapsed.TotalMilliseconds, 1), null);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new DeliveryOutcome(false, attempts, Math.Round(sw.Elapsed.TotalMilliseconds, 1), "Cancelled."); }
                    catch (Exception e) { error = e is OperationCanceledException ? "The mail server didn't answer in time." : e.Message; }
                    if (i >= pauses.Length) break;
                    try { await Task.Delay(pauses[i], ct); } catch (OperationCanceledException) { break; }
                }
                return new DeliveryOutcome(false, attempts, Math.Round(sw.Elapsed.TotalMilliseconds, 1), error);
            }
        }

        string? url; byte[] body; string? secret = null; Dictionary<string, string>? headers = null;
        switch (ch.Type)
        {
            case "teams":
                url = _secret(ch.SecretName("url"));
                body = AlertFormats.Bytes(AlertFormats.TeamsCard(n));
                break;
            case "slack":
                url = _secret(ch.SecretName("url"));
                body = AlertFormats.Bytes(AlertFormats.SlackBlocks(n));
                break;
            case "ntfy":
                url = ch.Url ?? "https://ntfy.sh";
                body = AlertFormats.Bytes(AlertFormats.Ntfy(n, ch.Topic ?? ""));
                if (_secret(ch.SecretName("token")) is { Length: > 0 } token) headers = new() { ["Authorization"] = "Bearer " + token };
                break;
            case "webhook":
                url = ch.Url;
                body = AlertFormats.Bytes(AlertFormats.Webhook(n));
                secret = _secret(ch.SecretName("secret"));
                break;
            default:
                return new DeliveryOutcome(false, 0, 0, $"Unknown channel type '{ch.Type}'.");
        }
        if (string.IsNullOrEmpty(url)) return new DeliveryOutcome(false, 0, 0, ch.Type is "teams" or "slack" ? "No webhook address is saved for this channel." : "This channel has no address.");
        var r = await WebhookHttp.PostAsync(_http, url, body, headers, ch.Type == "webhook" ? secret : null, AlertFormats.EventName(n), Interlocked.Increment(ref _nextId), pauses, Timeout, ct);
        return new DeliveryOutcome(r.Ok, r.Attempts, r.Ms, r.Ok ? (r.Status is { } s ? $"HTTP {s}" : null) : r.Error);
    }

    public void Dispose()
    {
        List<Worker> all;
        lock (_gate) { if (_disposed) return; _disposed = true; all = _workers.Values.ToList(); _workers.Clear(); }
        foreach (var w in all) w.Dispose();
        _http.Dispose();
    }
}
