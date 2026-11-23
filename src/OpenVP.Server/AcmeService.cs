using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using OpenVP.Core.Security;

namespace OpenVP.Server;

/// <summary>
/// Optional Let's Encrypt certificate by ACME HTTP-01 (using the Certes library, MIT). Off unless an Admin clicks "Get certificate". Let's Encrypt must be able to
/// reach <c>http://your-host/.well-known/acme-challenge/TOKEN</c> on port 80, so the host name must be public and port 80 must lead to OpenVP's http port.
/// The challenge answers are served by <c>GET /.well-known/acme-challenge/{token}</c>. This has not been run against the real Let's Encrypt service in OpenVP's tests.
/// </summary>
public sealed class AcmeService
{
    private readonly HttpsManager _https;
    private readonly SecuritySettings _settings;
    private readonly string _accountKey;
    private readonly ConcurrentDictionary<string, string> _challenges = new();
    private readonly SemaphoreSlim _busy = new(1, 1);
    public Uri? DirectoryOverride { get; init; }

    public AcmeService(string dataDirectory, HttpsManager https, SecuritySettings settings)
    {
        _https = https; _settings = settings;
        _accountKey = Path.Combine(dataDirectory, "security", "acme-account.pem");
    }

    /// <summary>The answer for a challenge token, or null if there is none (the HTTP-01 response is the key authorization).</summary>
    public string? Answer(string token) => _challenges.TryGetValue(token, out var a) ? a : null;
    public void Offer(string token, string keyAuthorization) => _challenges[token] = keyAuthorization;

    public static string? DomainProblem(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return "Enter the public host name.";
        domain = domain.Trim().ToLowerInvariant();
        if (System.Net.IPAddress.TryParse(domain, out _)) return "Let's Encrypt gives certificates for host names, not IP addresses.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(domain, @"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")) return "That doesn't look like a public host name (like openvp.example.com).";
        if (domain.EndsWith(".local") || domain.EndsWith(".lan") || domain.EndsWith(".internal") || domain.EndsWith(".home.arpa")) return "That is a private name; Let's Encrypt can't reach it.";
        return null;
    }

    public async Task<string> IssueAsync(string domain, string email, bool staging, CancellationToken ct = default)
    {
        if (!await _busy.WaitAsync(0, ct)) throw new InvalidOperationException("A certificate request is already running.");
        try
        {
            domain = domain.Trim().ToLowerInvariant();
            var server = DirectoryOverride ?? (staging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2);
            AcmeContext acme;
            if (File.Exists(_accountKey) && (staging || DirectoryOverride != null) == false)
            {
                acme = new AcmeContext(server, KeyFactory.FromPem(File.ReadAllText(_accountKey)));
                await acme.Account();
            }
            else
            {
                acme = new AcmeContext(server);
                await acme.NewAccount(email, true);
                if (!staging && DirectoryOverride == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_accountKey)!);
                    File.WriteAllText(_accountKey, acme.AccountKey.ToPem());
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_accountKey, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            var order = await acme.NewOrder([domain]);
            var authz = (await order.Authorizations()).First();
            var challenge = await authz.Http();
            Offer(challenge.Token, challenge.KeyAuthz);
            try
            {
                await challenge.Validate();
                for (var i = 0; i < 40; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var res = await authz.Resource();
                    if (res.Status == Certes.Acme.Resource.AuthorizationStatus.Valid) break;
                    if (res.Status == Certes.Acme.Resource.AuthorizationStatus.Invalid)
                        throw new InvalidOperationException("Let's Encrypt couldn't verify the host name: " + (res.Challenges?.FirstOrDefault()?.Error?.Detail ?? "it could not reach /.well-known/acme-challenge/ on port 80") + ".");
                    await Task.Delay(1500, ct);
                }
            }
            finally { _challenges.TryRemove(challenge.Token, out _); }
            var key = KeyFactory.NewKey(KeyAlgorithm.RS256);
            var chain = await order.Generate(new CsrInfo { CommonName = domain }, key);
            var pfx = chain.ToPfx(key).Build("openvp", "");
            var cert = new X509Certificate2(pfx, "", X509KeyStorageFlags.Exportable);
            _https.Install(cert, staging ? "acme (staging, not trusted)" : "acme");
            return $"Certificate for {domain} issued; valid until {cert.NotAfter:yyyy-MM-dd}.";
        }
        finally { _busy.Release(); }
    }

    /// <summary>Renews a Let's Encrypt certificate that expires within 30 days, if automatic renewal is on. Called by a timer.</summary>
    public async Task<string?> RenewIfDueAsync(Action<string, string>? audit = null)
    {
        var h = _settings.Get().Https;
        if (!h.Acme.Enabled || string.IsNullOrWhiteSpace(h.Acme.Domain) || string.IsNullOrWhiteSpace(h.Acme.Email)) return null;
        if (h.Source?.StartsWith("acme") == true && h.NotAfter is { } na && na - DateTimeOffset.Now > TimeSpan.FromDays(30)) return null;
        string result; var ok = true;
        try { result = await IssueAsync(h.Acme.Domain!, h.Acme.Email!, h.Acme.Staging); }
        catch (Exception e) { result = "Renewal failed: " + e.Message; ok = false; }
        _settings.Update(s => { s.Https.Acme.LastAttempt = DateTimeOffset.UtcNow; s.Https.Acme.LastResult = result; });
        audit?.Invoke(ok ? "https.acme" : "https.acme.failed", result);
        return result;
    }
}
