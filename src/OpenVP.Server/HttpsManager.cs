using System.Security.Cryptography.X509Certificates;
using OpenVP.Core.Security;

namespace OpenVP.Server;

/// <summary>
/// The server's TLS certificate: made here (self-signed), uploaded (PFX or PEM) or obtained from Let's Encrypt, kept as <c>data/security/certs/server.pfx</c>
/// (no password; the file is owner-only on Linux and macOS) and handed to Kestrel through a <see cref="CertificateHolder"/>, so a new certificate
/// is used by the next connection with no restart. Whether the HTTPS port is open is decided at start-up (see <see cref="Listening"/>).
/// </summary>
public sealed class HttpsManager
{
    private readonly string _pfx;
    private readonly SecuritySettings _settings;
    private readonly object _gate = new();

    public CertificateHolder Holder { get; } = new();
    /// <summary>True if Kestrel was started with the HTTPS port open.</summary>
    public bool Listening { get; private set; }
    public int BoundPort { get; private set; }

    public HttpsManager(string dataDirectory, SecuritySettings settings)
    {
        _settings = settings;
        _pfx = Path.Combine(dataDirectory, "security", "certs", "server.pfx");
    }

    public string PfxPath => _pfx;
    public bool HasCertificate => Holder.Current != null;

    /// <summary>Loads the saved certificate. Returns false if there is none or it can't be read (HTTPS then stays off, with a message).</summary>
    public bool Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(_pfx)) { problem = "HTTPS is on, but there is no certificate yet."; return false; }
        try
        {
            var c = new X509Certificate2(File.ReadAllBytes(_pfx), "", X509KeyStorageFlags.Exportable);
            if (c.NotAfter < DateTime.Now) { problem = $"The saved certificate expired on {c.NotAfter:yyyy-MM-dd}."; }
            Holder.Set(c);
            return problem == null;
        }
        catch (Exception e) when (e is IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException) { problem = "Couldn't read the saved certificate: " + e.Message; return false; }
    }

    public void MarkListening(int port) { Listening = true; BoundPort = port; }

    /// <summary>Saves a certificate as the one to serve and starts using it for new connections.</summary>
    public void Install(X509Certificate2 cert, string source)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_pfx)!);
            var tmp = _pfx + ".tmp";
            File.WriteAllBytes(tmp, CertificateTools.ToPfx(cert, ""));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, _pfx, true);
            _settings.Update(s =>
            {
                s.Https.Source = source;
                s.Https.Subject = cert.Subject;
                s.Https.Fingerprint = CertificateTools.Fingerprint(cert);
                s.Https.NotAfter = new DateTimeOffset(cert.NotAfter);
                s.Https.Names = CertificateTools.Names(cert);
            });
            Holder.Set(cert);
        }
    }

    public X509Certificate2 CreateSelfSigned(string? extraHost)
    {
        var (dns, ips) = CertificateTools.LocalNames(extraHost);
        var cert = CertificateTools.CreateSelfSigned(dns, ips);
        Install(cert, "self-signed");
        return cert;
    }

    /// <summary>The public part of the certificate in use (DER), for trusting it on another machine.</summary>
    public byte[]? PublicCertificate() => Holder.Current?.RawData;

    public object Status()
    {
        var h = _settings.Get().Https;
        var c = Holder.Current;
        return new
        {
            enabled = h.Enabled, port = h.Port, redirect = h.Redirect,
            listening = Listening, boundPort = Listening ? BoundPort : (int?)null,
            restartNeeded = h.Enabled != Listening || (Listening && h.Port != BoundPort),
            hasCertificate = c != null, source = h.Source, subject = h.Subject, fingerprint = h.Fingerprint, notAfter = h.NotAfter, names = h.Names,
            expiresInDays = h.NotAfter == null ? (int?)null : (int)Math.Floor((h.NotAfter.Value - DateTimeOffset.Now).TotalDays),
            acme = new { enabled = h.Acme.Enabled, domain = h.Acme.Domain, email = h.Acme.Email, staging = h.Acme.Staging, lastAttempt = h.Acme.LastAttempt, lastResult = h.Acme.LastResult }
        };
    }
}
