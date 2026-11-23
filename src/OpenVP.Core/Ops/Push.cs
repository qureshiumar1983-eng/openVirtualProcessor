using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Ops;

/// <summary>Another OpenVP server that programs and exports can be pushed to. The access key is kept in the secret store, never here.</summary>
/// <param name="Fingerprint">For an https target with a certificate that isn't signed by a trusted authority: the SHA-256 fingerprint of the one certificate to trust (hex). When set, only that certificate is accepted.</param>
public sealed record PushTarget
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string? Fingerprint { get; init; }
    public DateTimeOffset? LastTest { get; init; }
    public bool? LastOk { get; init; }
    public string? LastMessage { get; init; }
}

/// <summary>The targets list in <c>data/targets.json</c>, with each target's access key in the <see cref="SecretStore"/> (<c>target.&lt;id&gt;.key</c>).</summary>
public sealed class TargetStore
{
    public const int MaxTargets = 50;
    private sealed class FileDto { public List<PushTarget> Targets { get; set; } = new(); }
    private readonly string _file;
    private readonly SecretStore _secrets;
    private readonly object _gate = new();
    private List<PushTarget> _targets;

    public TargetStore(string dataDirectory, SecretStore secrets)
    {
        _file = Path.Combine(dataDirectory, "targets.json"); _secrets = secrets;
        _targets = IntegrationFiles.Load<FileDto>(_file).Targets.Where(t => !string.IsNullOrEmpty(t.Id)).ToList();
    }

    public void Reload() { lock (_gate) _targets = IntegrationFiles.Load<FileDto>(_file).Targets.Where(t => !string.IsNullOrEmpty(t.Id)).ToList(); }

    public IReadOnlyList<PushTarget> List() { lock (_gate) return _targets.ToList(); }
    public PushTarget? Get(string? id) { lock (_gate) return _targets.FirstOrDefault(t => t.Id == id); }
    public bool HasKey(string id) => _secrets.Has(KeyName(id));
    public string? KeyOf(string id) => _secrets.Get(KeyName(id));
    private static string KeyName(string id) => $"target.{id}.key";

    /// <summary>Checks a target's address: http or https, a host, nothing else. Returns it without a trailing slash.</summary>
    public static string NormalizeUrl(string? url)
    {
        var t = (url ?? "").Trim();
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || string.IsNullOrEmpty(u.Host))
            throw new ArgumentException("Enter the address of the other OpenVP, like http://192.168.1.20:8080 or https://openvp.example.com.");
        if (!string.IsNullOrEmpty(u.UserInfo)) throw new ArgumentException("Don't put a user name or password in the address. The access key is entered separately.");
        if ((u.AbsolutePath != "/" && u.AbsolutePath != "") || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
            throw new ArgumentException("The address should be just the server (no path), like http://192.168.1.20:8080.");
        return u.GetLeftPart(UriPartial.Authority);
    }

    public static string? NormalizeFingerprint(string? fp)
    {
        if (string.IsNullOrWhiteSpace(fp)) return null;
        var hex = Regex.Replace(fp, @"[\s:\-]", "").ToUpperInvariant();
        if (!Regex.IsMatch(hex, "^[0-9A-F]{64}$")) throw new ArgumentException("A certificate fingerprint is the SHA-256 value: 64 hex digits (colons are fine).");
        return hex;
    }

    /// <summary><paramref name="key"/> null keeps the saved key; an empty string removes it.</summary>
    public PushTarget Add(string name, string url, string? key, string? fingerprint)
    {
        var t = Build(new PushTarget { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() }, name, url, fingerprint);
        lock (_gate)
        {
            if (_targets.Count >= MaxTargets) throw new ArgumentException($"At most {MaxTargets} targets.");
            if (_targets.Any(x => x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"There's already a target called '{t.Name}'.");
            if (!string.IsNullOrEmpty(key)) _secrets.Set(KeyName(t.Id), key);
            _targets.Add(t);
            Save();
        }
        return t;
    }

    public PushTarget? Update(string id, string name, string url, string? key, string? fingerprint)
    {
        lock (_gate)
        {
            var old = _targets.FirstOrDefault(t => t.Id == id);
            if (old == null) return null;
            var t = Build(old, name, url, fingerprint) with { LastTest = null, LastOk = null, LastMessage = null };
            if (_targets.Any(x => x.Id != id && x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"There's already a target called '{t.Name}'.");
            if (key != null) _secrets.Set(KeyName(id), key.Length == 0 ? null : key);
            _targets[_targets.IndexOf(old)] = t;
            Save();
            return t;
        }
    }

    private static PushTarget Build(PushTarget basis, string name, string url, string? fingerprint)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0 || n.Length > 60 || n.Any(char.IsControl)) throw new ArgumentException("Give the target a name (up to 60 characters).");
        var u = NormalizeUrl(url);
        var fp = NormalizeFingerprint(fingerprint);
        if (fp != null && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A certificate fingerprint only applies to https addresses.");
        return basis with { Name = n, Url = u, Fingerprint = fp };
    }

    public void RecordTest(string id, bool ok, string message)
    {
        lock (_gate)
        {
            var i = _targets.FindIndex(t => t.Id == id);
            if (i < 0) return;
            _targets[i] = _targets[i] with { LastTest = DateTimeOffset.UtcNow, LastOk = ok, LastMessage = message };
            Save();
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            var n = _targets.RemoveAll(t => t.Id == id);
            if (n == 0) return false;
            _secrets.Set(KeyName(id), null);
            Save();
            return true;
        }
    }

    private void Save() { try { IntegrationFiles.Save(_file, new FileDto { Targets = _targets }); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
}

public sealed record TargetCheck(bool Ok, string Message, string? Version = null, int? Slots = null, bool? KeyAccepted = null);
public sealed record CertInfo(string Fingerprint, string Subject, string Issuer, DateTimeOffset NotBefore, DateTimeOffset NotAfter, bool Trusted, string? Error);

/// <summary>Talks to another OpenVP's REST API.</summary>
public sealed class PushClient
{
    private readonly TargetStore _targets;
    private readonly Func<PushTarget, HttpMessageHandler?>? _handlerFactory;

    /// <param name="handlerFactory">For tests: supplies the handler (null means the real one, which checks certificates).</param>
    public PushClient(TargetStore targets, Func<PushTarget, HttpMessageHandler?>? handlerFactory = null) { _targets = targets; _handlerFactory = handlerFactory; }

    /// <summary>
    /// Decides whether to accept a server's certificate. With a pinned fingerprint, only the certificate with that SHA-256 is accepted (whatever the chain says), so a pinned self-signed certificate works
    /// and any other certificate is refused. Without one, the normal check applies: the certificate must be valid, trusted and match the host name.
    /// </summary>
    public static bool ValidateCertificate(X509Certificate? cert, SslPolicyErrors errors, string? pinnedFingerprint)
    {
        if (string.IsNullOrEmpty(pinnedFingerprint)) return errors == SslPolicyErrors.None;
        if (cert == null) return false;
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(pinnedFingerprint),
            SHA256.HashData(cert.GetRawCertData()));
    }

    public static string FingerprintOf(X509Certificate cert) => Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));

    private HttpClient Client(PushTarget t, TimeSpan timeout)
    {
        var handler = _handlerFactory?.Invoke(t) ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, errors) => ValidateCertificate(cert, errors, t.Fingerprint)
            }
        };
        var c = new HttpClient(handler, true) { BaseAddress = new Uri(t.Url), Timeout = timeout };
        if (_targets.KeyOf(t.Id) is { Length: > 0 } key) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("OpenVP-Push/0.1");
        return c;
    }

    private static string Explain(Exception e) => e switch
    {
        TaskCanceledException or TimeoutException => "No answer in time.",
        HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException a } => "The secure connection failed: " + a.Message + " If the other OpenVP uses a self-signed certificate, fetch its certificate and trust it for this target.",
        HttpRequestException h => "Couldn't connect: " + (h.InnerException?.Message ?? h.Message),
        _ => e.Message
    };

    private static async Task<string> ErrorText(HttpResponseMessage r)
    {
        if (r.StatusCode == HttpStatusCode.Unauthorized) return "The other OpenVP refused the access key.";
        var body = "";
        try { body = await r.Content.ReadAsStringAsync(); } catch { }
        try { if (JsonDocument.Parse(body).RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) return e.GetString()!; } catch { }
        return r.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "The other OpenVP refused the access key.",
            HttpStatusCode.Forbidden => "The other OpenVP refused the request (403).",
            HttpStatusCode.NotFound => "The other OpenVP doesn't have that endpoint. It may be an older version.",
            _ => $"The other OpenVP answered {(int)r.StatusCode} {r.ReasonPhrase}."
        };
    }

    /// <summary>Checks that the target answers, and that its access key (if the target needs one) is accepted.</summary>
    public async Task<TargetCheck> TestAsync(PushTarget t, CancellationToken ct = default)
    {
        try
        {
            using var c = Client(t, TimeSpan.FromSeconds(15));
            using var info = await c.GetAsync("/api/info", ct);
            if (!info.IsSuccessStatusCode) return new TargetCheck(false, "That address answered, but not like OpenVP: " + await ErrorText(info));
            JsonElement j;
            try { j = JsonDocument.Parse(await info.Content.ReadAsStringAsync(ct)).RootElement; }
            catch (JsonException) { return new TargetCheck(false, "That address answered, but not like OpenVP."); }
            if (!j.TryGetProperty("name", out var nm) || nm.GetString() != "OpenVP") return new TargetCheck(false, "That address answered, but it isn't OpenVP.");
            var version = j.TryGetProperty("version", out var v) ? v.GetString() : null;
            var slots = j.TryGetProperty("slots", out var s) && s.TryGetInt32(out var sn) ? sn : (int?)null;
            using var probe = await c.GetAsync("/api/slots", ct);
            if (probe.StatusCode == HttpStatusCode.Unauthorized)
                return new TargetCheck(false, _targets.HasKey(t.Id) ? "OpenVP is there, but it refused the access key." : "OpenVP is there and needs an access key. Enter it for this target.", version, slots, false);
            if (!probe.IsSuccessStatusCode) return new TargetCheck(false, "OpenVP is there, but: " + await ErrorText(probe), version, slots);
            return new TargetCheck(true, $"Connected to OpenVP {version} with {slots} slots.", version, slots, true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException or OperationCanceledException) { return new TargetCheck(false, Explain(e)); }
    }

    /// <summary>Connects to an https address and reads its certificate, so the person can decide to trust it. Doesn't send anything.</summary>
    public static async Task<CertInfo> FetchCertificateAsync(string url, CancellationToken ct = default)
    {
        var u = new Uri(TargetStore.NormalizeUrl(url));
        if (u.Scheme != "https") throw new ArgumentException("Only https addresses have a certificate.");
        X509Certificate2? seen = null; SslPolicyErrors seenErrors = SslPolicyErrors.None;
        using var h = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, cert, _, errors) => { seen = cert == null ? null : new X509Certificate2(cert); seenErrors = errors; return true; } }
        };
        using var c = new HttpClient(h, false) { Timeout = TimeSpan.FromSeconds(15) };
        try { using var _ = await c.GetAsync(new Uri(u, "/api/info"), HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { if (seen == null) throw new InvalidOperationException(Explain(e)); }
        if (seen == null) throw new InvalidOperationException("The server didn't present a certificate.");
        return new CertInfo(FingerprintOf(seen), seen.Subject, seen.Issuer, seen.NotBefore, seen.NotAfter, seenErrors == SslPolicyErrors.None, seenErrors == SslPolicyErrors.None ? null : seenErrors.ToString());
    }

    /// <summary>Uploads a program (the program file first, then its sidecars) to a slot of the target. Returns what the other OpenVP did.</summary>
    public async Task<string> PushProgramAsync(PushTarget t, int slot, IReadOnlyList<(string Name, byte[] Data)> files, bool start, CancellationToken ct = default)
    {
        try
        {
            using var c = Client(t, TimeSpan.FromMinutes(10));
            using var form = new MultipartFormDataContent();
            foreach (var (name, data) in files)
            {
                var part = new ByteArrayContent(data);
                part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(part, "file", name);
            }
            form.Add(new StringContent(start ? "true" : "false"), "start");
            form.Add(new StringContent("push"), "how");
            using var r = await c.PostAsync($"/api/slots/{slot}/program", form, ct);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorText(r));
            return start ? "loaded and started" : "loaded";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException) { throw new InvalidOperationException(Explain(e)); }
    }

    /// <summary>Uploads an export to the target and imports it there. Returns the target's own summary of what happened.</summary>
    public async Task<(string Summary, bool Ok)> PushExportAsync(PushTarget t, string exportFile, IReadOnlyCollection<int>? slots, bool replaceSettings, string? password, bool restoreSecrets, CancellationToken ct = default)
    {
        try
        {
            using var c = Client(t, TimeSpan.FromMinutes(20));
            using var form = new MultipartFormDataContent();
            await using var fs = File.OpenRead(exportFile);
            var part = new StreamContent(fs);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "file", Path.GetFileName(exportFile));
            if (slots is { Count: > 0 }) form.Add(new StringContent(string.Join(',', slots)), "slots");
            form.Add(new StringContent(replaceSettings ? "true" : "false"), "replaceSettings");
            form.Add(new StringContent(restoreSecrets ? "true" : "false"), "restoreSecrets");
            if (!string.IsNullOrEmpty(password)) form.Add(new StringContent(password), "password");
            using var r = await c.PostAsync("/api/import", form, ct);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorText(r));
            var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)).RootElement;
            var parts = new List<string>();
            var ok = true;
            if (j.TryGetProperty("slots", out var arr))
                foreach (var s in arr.EnumerateArray())
                {
                    var sok = s.TryGetProperty("ok", out var o) && o.GetBoolean();
                    ok &= sok;
                    parts.Add($"slot {s.GetProperty("slot").GetInt32()}: {(s.TryGetProperty("message", out var m) ? m.GetString() : sok ? "ok" : "failed")}");
                }
            if (j.TryGetProperty("settingsReplaced", out var sr) && sr.GetBoolean()) parts.Add("settings replaced");
            return (parts.Count == 0 ? "imported" : string.Join("; ", parts), ok);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException) { throw new InvalidOperationException(Explain(e)); }
    }
}

/// <summary>One target's progress in a push.</summary>
public sealed class PushTargetStatus
{
    public string TargetId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    /// <summary><c>queued</c>, <c>running</c>, <c>ok</c> or <c>failed</c>.</summary>
    public string State { get; set; } = "queued";
    public string Message { get; set; } = "";
    public List<PushSlotStatus> Slots { get; } = new();
}

public sealed class PushSlotStatus
{
    public int Slot { get; init; }
    public string State { get; set; } = "queued";
    public string Message { get; set; } = "";
}

public sealed class PushJob
{
    public string Id { get; init; } = "";
    public string What { get; init; } = "";
    public DateTimeOffset Started { get; init; }
    public DateTimeOffset? Finished { get; set; }
    public List<PushTargetStatus> Targets { get; } = new();
    public bool Done => Finished != null;
}

/// <summary>Runs pushes to several targets at once and keeps their progress so the page can poll it.</summary>
public sealed class PushManager
{
    public const int MaxJobs = 20;
    private readonly PushClient _client;
    private readonly object _gate = new();
    private readonly List<PushJob> _jobs = new();

    public PushManager(PushClient client) { _client = client; }

    public event Action<PushJob>? Finished;

    public IReadOnlyList<PushJob> Jobs() { lock (_gate) return _jobs.ToList(); }
    public PushJob? Get(string id) { lock (_gate) return _jobs.FirstOrDefault(j => j.Id == id); }

    /// <summary>Pushes a program to the chosen slots of each target, all targets in parallel (the slots of one target one after another).</summary>
    public PushJob PushProgram(string label, IReadOnlyList<(string Name, byte[] Data)> files, IReadOnlyList<(PushTarget Target, int[] Slots)> targets, bool start)
    {
        var job = NewJob($"program {label}", targets.Select(t => t.Target));
        foreach (var (target, slots) in targets)
        {
            var st = job.Targets.First(x => x.TargetId == target.Id);
            foreach (var s in slots) st.Slots.Add(new PushSlotStatus { Slot = s });
        }
        foreach (var (target, slots) in targets)
        {
            var st = job.Targets.First(x => x.TargetId == target.Id);
            _ = Task.Run(async () =>
            {
                st.State = "running";
                var bad = 0;
                foreach (var s in st.Slots)
                {
                    s.State = "running"; st.Message = $"Sending to slot {s.Slot}…";
                    try { s.Message = await _client.PushProgramAsync(target, s.Slot, files, start); s.State = "ok"; }
                    catch (Exception e) { s.Message = e.Message; s.State = "failed"; bad++; }
                }
                st.State = bad == 0 ? "ok" : "failed";
                st.Message = bad == 0 ? $"{st.Slots.Count} slot{(st.Slots.Count == 1 ? "" : "s")} done." : $"{bad} of {st.Slots.Count} slot{(st.Slots.Count == 1 ? "" : "s")} failed.";
                Complete(job);
            });
        }
        return job;
    }

    /// <summary>Sends an export file to each target and imports it there. <paramref name="cleanup"/> runs when every target is done.</summary>
    public PushJob PushExport(string exportFile, IReadOnlyList<PushTarget> targets, IReadOnlyCollection<int>? slots, bool replaceSettings, string? password, bool restoreSecrets, Action? cleanup = null)
    {
        var job = NewJob("a whole-processor export", targets);
        foreach (var target in targets)
        {
            var st = job.Targets.First(x => x.TargetId == target.Id);
            _ = Task.Run(async () =>
            {
                st.State = "running"; st.Message = "Sending the export…";
                try
                {
                    var (summary, ok) = await _client.PushExportAsync(target, exportFile, slots, replaceSettings, password, restoreSecrets);
                    st.State = ok ? "ok" : "failed"; st.Message = summary;
                }
                catch (Exception e) { st.State = "failed"; st.Message = e.Message; }
                if (Complete(job)) { try { cleanup?.Invoke(); } catch { } }
            });
        }
        return job;
    }

    private PushJob NewJob(string what, IEnumerable<PushTarget> targets)
    {
        var job = new PushJob { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(), What = what, Started = DateTimeOffset.UtcNow };
        foreach (var t in targets) job.Targets.Add(new PushTargetStatus { TargetId = t.Id, Name = t.Name, Url = t.Url });
        lock (_gate)
        {
            _jobs.Add(job);
            while (_jobs.Count > MaxJobs) _jobs.RemoveAt(0);
        }
        return job;
    }

    // True when this call finished the job.
    private bool Complete(PushJob job)
    {
        lock (_gate)
        {
            if (job.Done || job.Targets.Any(t => t.State is "queued" or "running")) return false;
            job.Finished = DateTimeOffset.UtcNow;
        }
        try { Finished?.Invoke(job); } catch { }
        return true;
    }
}
