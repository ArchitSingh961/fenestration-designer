using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mark.Licensing;

/// <summary>
/// Salted PBKDF2-SHA256 password hashes ("pbkdf2-sha256$iterations$salt$hash"). Passwords are never stored, only
/// these hashes: on the licence server, and on a client computer for signing in while offline.
/// </summary>
public static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int DefaultIterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public const int MinimumLength = 6;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('$', Scheme, iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string? password, string? stored)
    {
        if (password is null || string.IsNullOrEmpty(stored)) return false;
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) || iterations < 1)
            return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Why a new password is not acceptable, or null.</summary>
    public static string? Check(string? password)
        => string.IsNullOrEmpty(password) || password.Length < MinimumLength
            ? $"The password must have at least {MinimumLength} characters."
            : null;
}

/// <summary>Random secrets: device tokens, admin session tokens and licence keys.</summary>
public static class Secrets
{
    /// <summary>A random URL-safe token (256 bits).</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 of a token, hex: what the server stores instead of the token.</summary>
    public static string HashToken(string? token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));

    private const string KeyAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>A licence key like MARK-7KQ2M-X9TPA-3HRWD-ZC4NE (100 random bits, no 0/O/1/I).</summary>
    public static string NewLicenceKey()
    {
        var builder = new StringBuilder("MARK");
        for (int group = 0; group < 4; group++)
        {
            builder.Append('-');
            for (int i = 0; i < 5; i++)
                builder.Append(KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)]);
        }
        return builder.ToString();
    }

    /// <summary>A key as typed by a person: upper case, spaces removed.</summary>
    public static string NormaliseKey(string? key)
        => new string((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
