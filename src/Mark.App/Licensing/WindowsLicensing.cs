using System.Security.Cryptography;
using System.Text;
using Mark.Licensing.Client;
using Microsoft.Win32;

namespace Mark.App.Licensing;

/// <summary>
/// Identifies this computer for licensing: a hash of Windows' machine GUID (the GUID itself never leaves the
/// computer). It stays the same across MARK updates and Windows users, and changes when Windows is reinstalled.
/// </summary>
internal static class MachineIdentity
{
    public static string Id()
    {
        string source = ReadMachineGuid() ?? $"{Environment.MachineName}|{Environment.UserDomainName}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("MARK machine|" + source));
        return Convert.ToHexString(hash, 0, 16);
    }

    public static string Name => Environment.MachineName;

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }
}

/// <summary>Encrypts the saved licence state for the current Windows user (DPAPI), so it cannot be read or copied.</summary>
internal sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MARK licence state");

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
