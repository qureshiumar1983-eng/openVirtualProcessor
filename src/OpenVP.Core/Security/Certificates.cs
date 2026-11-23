using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenVP.Core.Security;

/// <summary>Makes, reads and describes the server's TLS certificate. Nothing here needs a package.</summary>
public static class CertificateTools
{
    /// <summary>The names and addresses a self-signed certificate should cover: this computer's name, name.local, openvp.local, localhost, loopback and every LAN address.</summary>
    public static (List<string> Dns, List<IPAddress> Ips) LocalNames(string? extraHost = null)
    {
        var host = Environment.MachineName.ToLowerInvariant();
        var dns = new List<string> { host, host + ".local", "openvp.local", "localhost" };
        if (!string.IsNullOrWhiteSpace(extraHost) && !IPAddress.TryParse(extraHost, out _)) dns.Add(extraHost.Trim().ToLowerInvariant());
        var ips = new List<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork || (ua.Address.AddressFamily == AddressFamily.InterNetworkV6 && !ua.Address.IsIPv6LinkLocal))
                        ips.Add(ua.Address);
            }
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException) { }
        if (!string.IsNullOrWhiteSpace(extraHost) && IPAddress.TryParse(extraHost, out var xi)) ips.Add(xi);
        return (dns.Distinct().ToList(), ips.DistinctBy(i => i.ToString()).ToList());
    }

    /// <summary>A self-signed server certificate (RSA 2048, SHA-256, valid for two years) with the given names as subject alternative names.</summary>
    public static X509Certificate2 CreateSelfSigned(IEnumerable<string> dnsNames, IEnumerable<IPAddress> ips, int years = 2)
    {
        var dns = dnsNames.ToList();
        using var rsa = RSA.Create(2048);
        var cn = dns.FirstOrDefault() ?? "openvp";
        var req = new CertificateRequest($"CN={cn}, O=OpenVP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var d in dns) san.AddDnsName(d);
        foreach (var ip in ips) san.AddIpAddress(ip);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(years));
        // Round-trip through a PFX so the private key is exportable and usable by Kestrel on every platform.
        return new X509Certificate2(cert.Export(X509ContentType.Pfx, ""), "", X509KeyStorageFlags.Exportable);
    }

    public static string Fingerprint(X509Certificate2 c) => string.Join(':', Convert.ToHexString(SHA256.HashData(c.RawData)).Chunk(2).Select(x => new string(x)));

    public static List<string> Names(X509Certificate2 c)
    {
        var list = new List<string>();
        foreach (var ext in c.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            list.AddRange(ext.EnumerateDnsNames());
            list.AddRange(ext.EnumerateIPAddresses().Select(i => i.ToString()));
        }
        return list;
    }

    /// <summary>Reads a PFX/PKCS#12 file. Throws <see cref="ArgumentException"/> with a plain message if it isn't one, the password is wrong, or it has no private key.</summary>
    public static X509Certificate2 FromPfx(byte[] data, string? password)
    {
        try
        {
            var c = new X509Certificate2(data, password ?? "", X509KeyStorageFlags.Exportable);
            return Checked(c);
        }
        catch (CryptographicException e) { throw new ArgumentException("Couldn't open that file as a certificate (PFX). Is the password right? " + e.Message); }
    }

    /// <summary>Reads a certificate and its private key in PEM text (the key may be encrypted with <paramref name="password"/>).</summary>
    public static X509Certificate2 FromPem(string certPem, string keyPem, string? password)
    {
        try
        {
            using var c = string.IsNullOrEmpty(password) ? X509Certificate2.CreateFromPem(certPem, keyPem) : X509Certificate2.CreateFromEncryptedPem(certPem, keyPem, password);
            return Checked(new X509Certificate2(c.Export(X509ContentType.Pfx, ""), "", X509KeyStorageFlags.Exportable));
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException) { throw new ArgumentException("Couldn't read that certificate and key. Paste the certificate (and any chain) and the private key as PEM. " + e.Message); }
    }

    private static X509Certificate2 Checked(X509Certificate2 c)
    {
        if (!c.HasPrivateKey) throw new ArgumentException("That certificate has no private key.");
        if (c.NotAfter < DateTime.Now) throw new ArgumentException($"That certificate expired on {c.NotAfter:yyyy-MM-dd}.");
        return c;
    }

    public static byte[] ToPfx(X509Certificate2 c, string password) => c.Export(X509ContentType.Pfx, password);
}

/// <summary>
/// The certificate Kestrel serves. Kestrel asks for it on every new connection (<c>ServerCertificateSelector</c>), so replacing it here takes effect
/// for the next connection with no restart.
/// </summary>
public sealed class CertificateHolder
{
    private volatile X509Certificate2? _current;
    public X509Certificate2? Current => _current;
    public event Action? Changed;
    public void Set(X509Certificate2? c) { _current = c; Changed?.Invoke(); }
}
