using System.Security.Cryptography;
using System.Text;

namespace OpenVP.Core.Security;

/// <summary>
/// Password hashing with PBKDF2-SHA256 (built into .NET, no package): a random 16-byte salt per password, 210,000 iterations
/// (the figure the exports already use), 32-byte result. Stored as <c>pbkdf2-sha256$iterations$salt$hash</c> (base64) so the
/// cost can be raised later and old hashes still verify.
/// </summary>
public static class PasswordHasher
{
    public const int DefaultIterations = 210_000;
    public const int MinLength = 8;
    private const string Tag = "pbkdf2-sha256";

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"{Tag}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != Tag || !int.TryParse(p[1], out var iter) || iter < 1000 || iter > 10_000_000) return false;
        try
        {
            var salt = Convert.FromBase64String(p[2]);
            var want = Convert.FromBase64String(p[3]);
            var got = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iter, HashAlgorithmName.SHA256, want.Length);
            return CryptographicOperations.FixedTimeEquals(want, got);
        }
        catch (FormatException) { return false; }
    }

    public static bool NeedsRehash(string stored) => !stored.StartsWith($"{Tag}${DefaultIterations}$", StringComparison.Ordinal);

    private static readonly string Dummy = Hash("openvp-dummy-password");
    /// <summary>Spends the time a real check would, so a wrong user name isn't quicker to answer than a wrong password.</summary>
    public static void Burn() => Verify("x", Dummy);

    /// <summary>Null if the password is acceptable, else what is wrong with it.</summary>
    public static string? Problem(string? password, string? userName = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength) return $"The password must be at least {MinLength} characters.";
        if (password.Length > 200) return "The password is too long (200 characters at most).";
        if (userName != null && password.Equals(userName, StringComparison.OrdinalIgnoreCase)) return "The password can't be the same as the user name.";
        return null;
    }
}
