using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenVP.SimplSharp.SshCore
{
    /// <summary>Authentication was refused: wrong password, key not accepted, or no method the server takes.</summary>
    internal sealed class SshAuthFailed : IOException
    {
        public SshAuthFailed(string message) : base(message) { }
    }

    /// <summary>
    /// A private key for public-key login: RSA or ECDSA (P-256, P-384, P-521), read from the PEM forms OpenSSL writes
    /// (PKCS#1, PKCS#8, SEC1, and PKCS#8 encrypted with a passphrase) or from an unencrypted <c>OPENSSH PRIVATE KEY</c> file.
    /// Ed25519 keys and passphrase-protected OpenSSH or legacy PEM keys are refused with a message saying so.
    /// </summary>
    internal sealed class SshKey
    {
        private RSA _rsa;
        private ECDsa _ec;
        private string _curve; // nistp256 ...

        public static SshKey Parse(string text, string passphrase = null)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("The key file is empty.");
            var m = Regex.Match(text, @"-----BEGIN ([A-Z0-9 ]+)-----");
            if (!m.Success) throw new InvalidDataException("This isn't a PEM private key (no BEGIN line).");
            var label = m.Groups[1].Value;
            try
            {
                switch (label)
                {
                    case "RSA PRIVATE KEY":
                        if (text.Contains("ENCRYPTED")) throw new NotSupportedException("This key is encrypted in the old OpenSSL format. Convert it: openssl pkcs8 -topk8 -v2 aes256 -in key -out key.pk8");
                        return FromRsa(text, null);
                    case "EC PRIVATE KEY":
                        if (text.Contains("ENCRYPTED")) throw new NotSupportedException("This key is encrypted in the old OpenSSL format. Convert it: openssl pkcs8 -topk8 -v2 aes256 -in key -out key.pk8");
                        return FromEc(text, null);
                    case "PRIVATE KEY":
                        try { return FromRsa(text, null); } catch (CryptographicException) { return FromEc(text, null); }
                    case "ENCRYPTED PRIVATE KEY":
                        if (string.IsNullOrEmpty(passphrase)) throw new InvalidDataException("This key needs a passphrase.");
                        try { return FromRsa(text, passphrase); } catch (CryptographicException) { return FromEc(text, passphrase); }
                    case "OPENSSH PRIVATE KEY":
                        return FromOpenSsh(text);
                    default:
                        throw new NotSupportedException($"Private keys of type '{label}' aren't supported.");
                }
            }
            catch (CryptographicException e)
            {
                throw new InvalidDataException("The key couldn't be read (wrong passphrase, or not an RSA or ECDSA key): " + e.Message, e);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or FormatException or OverflowException)
            {
                throw new InvalidDataException("The key file is damaged or isn't a private key: " + e.Message, e);
            }
        }

        private static SshKey FromRsa(string pem, string pass)
        {
            var rsa = RSA.Create();
            if (pass == null) rsa.ImportFromPem(pem); else rsa.ImportFromEncryptedPem(pem, pass);
            return new SshKey { _rsa = rsa };
        }

        private static SshKey FromEc(string pem, string pass)
        {
            var ec = ECDsa.Create();
            if (pass == null) ec.ImportFromPem(pem); else ec.ImportFromEncryptedPem(pem, pass);
            var curve = ec.ExportParameters(false).Curve;
            var name = curve.Oid.FriendlyName ?? "";
            var c = name.Contains("256") ? "nistp256" : name.Contains("384") ? "nistp384" : name.Contains("521") ? "nistp521" : throw new NotSupportedException("Only the NIST P-256, P-384 and P-521 curves are supported.");
            return new SshKey { _ec = ec, _curve = c };
        }

        private static SshKey FromOpenSsh(string pem)
        {
            var b64 = Regex.Replace(pem, @"-----[A-Z ]+-----|\s", "");
            var data = Convert.FromBase64String(b64);
            var magic = Encoding.ASCII.GetBytes("openssh-key-v1\0");
            if (data.Length < magic.Length || !data.AsSpan(0, magic.Length).SequenceEqual(magic)) throw new InvalidDataException("Not an OpenSSH key file.");
            var r = new SshConnection.Reader(data) { Pos = magic.Length };
            var cipher = Encoding.ASCII.GetString(r.String()); var kdf = Encoding.ASCII.GetString(r.String()); r.String();
            if (cipher != "none" || kdf != "none") throw new NotSupportedException("This OpenSSH key is protected with a passphrase, which isn't supported. Remove it (ssh-keygen -p) or use a PKCS#8 key.");
            if (r.UInt() != 1) throw new NotSupportedException("A key file with several keys isn't supported.");
            r.String(); // public blob
            var priv = new SshConnection.Reader(r.String());
            if (priv.UInt() != priv.UInt()) throw new InvalidDataException("The key file is damaged.");
            var type = Encoding.ASCII.GetString(priv.String());
            if (type == "ssh-rsa")
            {
                var n = priv.MpIntBytes(); var e = priv.MpIntBytes(); var d = priv.MpIntBytes(); var iq = priv.MpIntBytes(); var p = priv.MpIntBytes(); var q = priv.MpIntBytes();
                var bd = new BigInteger(d, true, true); var bp = new BigInteger(p, true, true); var bq = new BigInteger(q, true, true);
                var half = (n.Length + 1) / 2;
                byte[] Fit(byte[] v, int len) { var x = v.SkipWhile(b => b == 0).ToArray(); return x.Length >= len ? x : new byte[len - x.Length].Concat(x).ToArray(); }
                var prm = new RSAParameters
                {
                    Modulus = n, Exponent = e, D = Fit(d, n.Length), P = Fit(p, half), Q = Fit(q, half), InverseQ = Fit(iq, half),
                    DP = Fit((bd % (bp - 1)).ToByteArray(true, true), half), DQ = Fit((bd % (bq - 1)).ToByteArray(true, true), half)
                };
                var rsa = RSA.Create(); rsa.ImportParameters(prm);
                return new SshKey { _rsa = rsa };
            }
            if (type.StartsWith("ecdsa-sha2-nistp"))
            {
                var curve = Encoding.ASCII.GetString(priv.String()); var q = priv.String(); var d = priv.MpIntBytes();
                var oid = curve switch { "nistp256" => ECCurve.NamedCurves.nistP256, "nistp384" => ECCurve.NamedCurves.nistP384, "nistp521" => ECCurve.NamedCurves.nistP521, _ => throw new NotSupportedException("Unknown curve " + curve) };
                var size = (q.Length - 1) / 2;
                var ec = ECDsa.Create(new ECParameters
                {
                    Curve = oid, D = d.Length >= size ? d : new byte[size - d.Length].Concat(d).ToArray(),
                    Q = new ECPoint { X = q.Skip(1).Take(size).ToArray(), Y = q.Skip(1 + size).Take(size).ToArray() }
                });
                return new SshKey { _ec = ec, _curve = curve };
            }
            throw new NotSupportedException($"A key of type {type} isn't supported (RSA and ECDSA are).");
        }

        public string KeyType => _rsa != null ? "ssh-rsa" : "ecdsa-sha2-" + _curve;

        public byte[] PublicBlob
        {
            get
            {
                var w = new SshConnection.Writer();
                w.String(KeyType);
                if (_rsa != null)
                {
                    var p = _rsa.ExportParameters(false);
                    w.MpInt(p.Exponent); w.MpInt(p.Modulus);
                }
                else
                {
                    var p = _ec.ExportParameters(false);
                    w.String(_curve);
                    w.String(new byte[] { 4 }.Concat(p.Q.X).Concat(p.Q.Y).ToArray());
                }
                return w.ToArray();
            }
        }

        /// <summary>The signature algorithms to offer, best first.</summary>
        public IEnumerable<string> SignatureAlgorithms() => _rsa != null ? new[] { "rsa-sha2-256", "ssh-rsa" } : new[] { KeyType };

        /// <summary>The signature bytes for the userauth request (the caller wraps them with the algorithm name).</summary>
        public byte[] Sign(byte[] data, string algorithm)
        {
            if (_rsa != null)
            {
                var (hash, _) = algorithm switch { "rsa-sha2-256" => (HashAlgorithmName.SHA256, 0), "rsa-sha2-512" => (HashAlgorithmName.SHA512, 0), _ => (HashAlgorithmName.SHA1, 0) };
                return _rsa.SignData(data, hash, RSASignaturePadding.Pkcs1);
            }
            var h = _curve == "nistp256" ? HashAlgorithmName.SHA256 : _curve == "nistp384" ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA512;
            var raw = _ec.SignData(data, h, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var half = raw.Length / 2;
            var w = new SshConnection.Writer();
            w.MpInt(raw.AsSpan(0, half).ToArray()); w.MpInt(raw.AsSpan(half).ToArray());
            return w.ToArray();
        }
    }
}
