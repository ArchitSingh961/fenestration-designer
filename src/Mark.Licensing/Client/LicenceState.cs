using System.Text.Json;

namespace Mark.Licensing.Client;

/// <summary>Where a new copy of MARK looks for the licence server until another address is entered.</summary>
public static class LicenceDefaults
{
    public const string ServerUrl = "http://localhost:5180";

    /// <summary>The file next to MARK.exe with the licence server's address (written by the installer build).</summary>
    public const string ServerFileName = "licence-server.txt";

    /// <summary>
    /// The address a new copy of MARK uses: the first line of <see cref="ServerFileName"/> in <paramref name="folder"/>
    /// that is an http(s) address (lines starting with # are notes), else <see cref="ServerUrl"/>.
    /// </summary>
    public static string ServerUrlIn(string folder)
    {
        try
        {
            string path = Path.Combine(folder, ServerFileName);
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                {
                    string url = line.Trim();
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return url.TrimEnd('/');
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable: the built-in address.
        }
        return ServerUrl;
    }
}

/// <summary>
/// What MARK keeps on a computer between runs: the signed licence, the device token for check-ins, the last User ID,
/// a password hash for signing in while offline, and the latest time seen (to notice a clock moved back).
/// </summary>
public sealed record LicenceState
{
    public string ServerUrl { get; init; } = LicenceDefaults.ServerUrl;

    public SignedLicence? Licence { get; init; }

    public string? DeviceToken { get; init; }

    public string? UserId { get; init; }

    public string? PasswordHash { get; init; }

    /// <summary>Open MARK without asking for the password while the licence is valid.</summary>
    public bool KeepSignedIn { get; init; } = true;

    public DateTime LastSeenUtc { get; init; }
}

public interface ILicenceStateStore
{
    /// <summary>The saved state, or null when there is none (or it cannot be read: then the user signs in again).</summary>
    LicenceState? Load();

    void Save(LicenceState state);
}

/// <summary>Encrypts the saved state for the current Windows user (DPAPI in MARK; a stand-in in tests).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}

/// <summary>The licence state in one encrypted file (normally <c>%LOCALAPPDATA%\MARK\licence.dat</c>).</summary>
public sealed class FileLicenceStateStore : ILicenceStateStore
{
    private readonly ISecretProtector _protector;

    public FileLicenceStateStore(string path, ISecretProtector protector)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public string Path { get; }

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MARK", "licence.dat");

    public LicenceState? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            byte[] json = _protector.Unprotect(File.ReadAllBytes(Path));
            return JsonSerializer.Deserialize<LicenceState>(json, LicenceJson.Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or System.Security.Cryptography.CryptographicException or NotSupportedException)
        {
            return null;
        }
    }

    public void Save(LicenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        byte[] data = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state, LicenceJson.Options));
        string temp = Path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, Path, overwrite: true);
    }
}
