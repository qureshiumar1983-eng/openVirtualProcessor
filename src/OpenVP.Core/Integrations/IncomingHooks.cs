using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenVP.Core.Integrations;

/// <summary>An incoming webhook: a secret token that may set signals in one slot, optionally only some of them.</summary>
public sealed record IncomingHook
{
    public string Id { get; init; } = "";
    public string Token { get; init; } = "";
    public string Label { get; init; } = "";
    public int Slot { get; init; } = 1;
    /// <summary>The signals this hook may set; empty means any signal of the slot.</summary>
    public List<string> Allow { get; init; } = new();
    public bool Enabled { get; init; } = true;
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsed { get; init; }
    public long Count { get; init; }
}

public readonly record struct HookResult(int Status, string Message, int RetryAfterSeconds = 0)
{
    public bool Ok => Status == 200;
}

/// <summary>
/// <c>POST /hooks/{token}</c> with <c>{"signal":"Room_On","value":1}</c> or <c>{"pulse":"Button1"}</c>. The token is the only
/// credential: 192 random bits, one per hook, scoped to one slot and optionally to a list of signals, and it can be switched
/// off or deleted at any time. Hooks are kept in <c>data/integrations/hooks.json</c> (owner-only; not part of snapshots, so
/// rolling a slot back never brings a revoked token back). Requests are rate limited per token, and wrong tokens are rate limited
/// per address so a token can't be guessed.
/// </summary>
public sealed class IncomingHooks : IDisposable
{
    public const int MaxHooks = 50;
    public const int MaxBody = 4096;

    private readonly string _file;
    private readonly SignalDirectory _directory;
    private readonly Processor _processor;
    private readonly object _gate = new();
    private List<IncomingHook> _hooks;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly ConcurrentDictionary<string, Fails> _fails = new();
    private readonly Timer _flush;
    private bool _dirty;
    private readonly Func<long> _clock;

    /// <summary>Requests per second one token may make, with <see cref="Burst"/> allowed at once. Settable for tests.</summary>
    public double RatePerSecond { get; set; } = 20;
    public int Burst { get; set; } = 40;
    /// <summary>Wrong tokens one address may try per minute before it is refused outright.</summary>
    public int BadTokensPerMinute { get; set; } = 10;

    private sealed class Bucket { public double Tokens; public long At; }
    private sealed class Fails { public int Count; public long WindowStart; }

    public IncomingHooks(Processor processor, SignalDirectory directory, Func<long>? clockMs = null)
    {
        _processor = processor; _directory = directory;
        _clock = clockMs ?? (() => Environment.TickCount64);
        _file = Path.Combine(processor.DataDirectory, "integrations", "hooks.json");
        _hooks = IntegrationFiles.Load<HookFile>(_file).Hooks;
        _flush = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private sealed class HookFile { public List<IncomingHook> Hooks { get; set; } = new(); }

    public IReadOnlyList<IncomingHook> List() { lock (_gate) return _hooks.ToList(); }

    public IncomingHook? Find(string id) { lock (_gate) return _hooks.FirstOrDefault(h => h.Id == id); }

    private void Save()
    {
        try { IntegrationFiles.Save(_file, new HookFile { Hooks = _hooks }, ownerOnly: true); _dirty = false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException($"Couldn't save the hooks: {e.Message}", e); }
    }

    private void Flush() { lock (_gate) if (_dirty) { try { Save(); } catch (InvalidOperationException) { } } }

    public IncomingHook Create(int slot, string? label, IEnumerable<string>? allow)
    {
        if (!_processor.IsValidSlot(slot)) throw new ArgumentException($"Slot must be 1 to {_processor.Slots.Count}.");
        var l = (label ?? "").Trim();
        if (l.Length > 60 || l.Any(char.IsControl)) throw new ArgumentException("The label can be at most 60 characters.");
        var list = CheckAllow(allow);
        lock (_gate)
        {
            if (_hooks.Count >= MaxHooks) throw new ArgumentException($"At most {MaxHooks} hooks.");
            var h = new IncomingHook
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('='),
                Label = l.Length == 0 ? $"Hook for slot {slot}" : l,
                Slot = slot, Allow = list, Created = DateTimeOffset.UtcNow
            };
            _hooks = new List<IncomingHook>(_hooks) { h };
            Save();
            return h;
        }
    }

    private static List<string> CheckAllow(IEnumerable<string>? allow)
    {
        var list = (allow ?? []).Select(a => a?.Trim() ?? "").Where(a => a.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (list.Count > 500) throw new ArgumentException("At most 500 allowed signals per hook.");
        return list;
    }

    public IncomingHook? Update(string id, string? label, IEnumerable<string>? allow, bool? enabled)
    {
        lock (_gate)
        {
            var h = _hooks.FirstOrDefault(x => x.Id == id);
            if (h == null) return null;
            if (label != null && (label.Trim().Length > 60 || label.Any(char.IsControl))) throw new ArgumentException("The label can be at most 60 characters.");
            var n = h with
            {
                Label = label == null ? h.Label : (label.Trim().Length == 0 ? h.Label : label.Trim()),
                Allow = allow == null ? h.Allow : CheckAllow(allow),
                Enabled = enabled ?? h.Enabled
            };
            _hooks = _hooks.Select(x => x.Id == id ? n : x).ToList();
            Save();
            return n;
        }
    }

    /// <summary>Deletes the hook; its token stops working at once.</summary>
    public bool Revoke(string id)
    {
        lock (_gate)
        {
            if (_hooks.All(h => h.Id != id)) return false;
            _hooks = _hooks.Where(h => h.Id != id).ToList();
            Save();
            return true;
        }
    }

    /// <summary>Handle a request. <paramref name="remote"/> is the caller's address, used only to limit wrong guesses.</summary>
    public HookResult Handle(string? token, ReadOnlySpan<byte> body, string remote)
    {
        var now = _clock();
        if (BadTooOften(remote, now, out var wait)) return new(429, "Too many wrong tokens from this address. Try again later.", wait);
        var hook = FindByToken(token);
        if (hook == null || !hook.Enabled)
        {
            NoteBad(remote, now);
            return new(404, "No such hook.");
        }
        if (!TakeToken(hook.Id, now, out var retry)) return new(429, "Too many requests for this hook.", retry);
        if (body.Length > MaxBody) return new(413, $"The body can be at most {MaxBody} bytes.");

        string? signal, value = null; var pulse = false;
        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return new(400, "Send a JSON object.");
            if (r.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String) { signal = p.GetString(); pulse = true; }
            else if (r.TryGetProperty("signal", out var s) && s.ValueKind == JsonValueKind.String && r.TryGetProperty("value", out var v))
            {
                signal = s.GetString();
                value = v.ValueKind switch
                {
                    JsonValueKind.True => "1",
                    JsonValueKind.False => "0",
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.String => v.GetString(),
                    _ => null
                };
                if (value == null) return new(400, "value must be a number, true, false or text.");
            }
            else return new(400, "Send {\"signal\":\"name\",\"value\":1} or {\"pulse\":\"name\"}.");
        }
        catch (JsonException) { return new(400, "The body isn't valid JSON."); }

        if (string.IsNullOrWhiteSpace(signal)) return new(400, "The signal name is empty.");
        if (hook.Allow.Count > 0 && !hook.Allow.Contains(signal!, StringComparer.Ordinal))
            return new(403, "This hook isn't allowed to set that signal.");

        var outcome = _directory.TrySetFromText(hook.Slot, signal!, pulse ? "pulse" : value!, out var err);
        Touch(hook.Id);
        return outcome switch
        {
            SignalDirectory.SetOutcome.Ok => new(200, "ok"),
            SignalDirectory.SetOutcome.NoProgram => new(409, err!),
            SignalDirectory.SetOutcome.UnknownSignal => new(404, err!),
            _ => new(400, err!)
        };
    }

    private IncomingHook? FindByToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return null;
        var t = Encoding.UTF8.GetBytes(token);
        IncomingHook? found = null;
        List<IncomingHook> hooks; lock (_gate) hooks = _hooks;
        // Compare against every hook, so how long it takes doesn't tell which one matched.
        foreach (var h in hooks)
        {
            var b = Encoding.UTF8.GetBytes(h.Token);
            if (b.Length == t.Length && CryptographicOperations.FixedTimeEquals(b, t)) found = h;
        }
        return found;
    }

    private void Touch(string id)
    {
        lock (_gate)
        {
            var i = _hooks.FindIndex(h => h.Id == id);
            if (i < 0) return;
            var copy = new List<IncomingHook>(_hooks);
            copy[i] = copy[i] with { LastUsed = DateTimeOffset.UtcNow, Count = copy[i].Count + 1 };
            _hooks = copy;
            _dirty = true;
        }
    }

    private bool TakeToken(string id, long now, out int retrySeconds)
    {
        var b = _buckets.GetOrAdd(id, _ => new Bucket { Tokens = Burst, At = now });
        lock (b)
        {
            b.Tokens = Math.Min(Burst, b.Tokens + (now - b.At) / 1000.0 * RatePerSecond);
            b.At = now;
            if (b.Tokens >= 1) { b.Tokens -= 1; retrySeconds = 0; return true; }
            retrySeconds = Math.Max(1, (int)Math.Ceiling((1 - b.Tokens) / RatePerSecond));
            return false;
        }
    }

    private bool BadTooOften(string remote, long now, out int retrySeconds)
    {
        retrySeconds = 0;
        if (!_fails.TryGetValue(remote, out var f)) return false;
        lock (f)
        {
            if (now - f.WindowStart > 60_000) { f.Count = 0; return false; }
            if (f.Count < BadTokensPerMinute) return false;
            retrySeconds = (int)Math.Max(1, (60_000 - (now - f.WindowStart)) / 1000);
            return true;
        }
    }

    private void NoteBad(string remote, long now)
    {
        if (_fails.Count > 10_000) _fails.Clear(); // a flood of addresses can't grow this without bound
        var f = _fails.GetOrAdd(remote, _ => new Fails { WindowStart = now });
        lock (f)
        {
            if (now - f.WindowStart > 60_000) { f.WindowStart = now; f.Count = 0; }
            f.Count++;
        }
    }

    public void Dispose() { _flush.Dispose(); Flush(); }
}
