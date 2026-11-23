using System.Security.Cryptography;

namespace OpenVP.Core.Ops;

/// <summary>An export or import that can't go ahead. <see cref="Code"/> is <c>bad-archive</c>, <c>too-big</c>, <c>unsafe-path</c>, <c>wrong-version</c>, <c>need-password</c> or <c>wrong-password</c>.</summary>
public sealed class ArchiveException(string message, string code = "bad-archive") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Protects the secrets in an export with a password: AES-256-GCM, with a key derived from the password by PBKDF2-SHA256 (210,000 iterations, a random 16-byte salt).
/// Layout: <c>"OVPE1" | iterations (4 bytes) | salt (16) | nonce (12) | tag (16) | ciphertext</c>; the header is authenticated too.
/// </summary>
public static class ArchiveCrypto
{
    private static readonly byte[] Magic = "OVPE1"u8.ToArray();
    public const int DefaultIterations = 210_000, MaxIterations = 5_000_000, MinPasswordLength = 8;

    public static byte[] Encrypt(byte[] plain, string password, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength) throw new ArgumentException($"Use a password of at least {MinPasswordLength} characters.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var header = new byte[Magic.Length + 4 + 16];
        Magic.CopyTo(header, 0);
        BitConverter.GetBytes(iterations).CopyTo(header, Magic.Length);
        salt.CopyTo(header, Magic.Length + 4);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(key, 16)) gcm.Encrypt(nonce, plain, cipher, tag, header);
        CryptographicOperations.ZeroMemory(key);
        return [.. header, .. nonce, .. tag, .. cipher];
    }

    /// <summary>Throws <see cref="ArchiveException"/> with code <c>wrong-password</c> if the password is wrong or the data was changed.</summary>
    public static byte[] Decrypt(byte[] blob, string password)
    {
        var headerLen = Magic.Length + 4 + 16;
        if (blob.Length < headerLen + 12 + 16 || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new ArchiveException("The secrets in this file are in a format OpenVP doesn't recognise.", "bad-archive");
        var iterations = BitConverter.ToInt32(blob, Magic.Length);
        if (iterations is < 1000 or > MaxIterations) throw new ArchiveException("The secrets in this file have an invalid key setting.", "bad-archive");
        var salt = blob.AsSpan(Magic.Length + 4, 16).ToArray();
        var nonce = blob.AsSpan(headerLen, 12).ToArray();
        var tag = blob.AsSpan(headerLen + 12, 16).ToArray();
        var cipher = blob.AsSpan(headerLen + 28).ToArray();
        var key = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain, blob.AsSpan(0, headerLen));
        }
        catch (CryptographicException) { throw new ArchiveException("That password doesn't open the secrets in this file.", "wrong-password"); }
        finally { CryptographicOperations.ZeroMemory(key); }
        return plain;
    }
}
