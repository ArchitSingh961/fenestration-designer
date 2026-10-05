using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;

namespace Mark.Setup;

/// <summary>Where and how MARK is installed.</summary>
/// <param name="Shortcuts">Start menu (and, with <paramref name="DesktopShortcut"/>, desktop) shortcuts.</param>
/// <param name="Register">Listed in Windows' installed apps, with Uninstall.</param>
public sealed record InstallOptions(string TargetFolder, bool Shortcuts = true, bool DesktopShortcut = true, bool Register = true);

/// <summary>
/// Installs, updates and removes MARK for the signed-in Windows user (no administrator rights needed): the program files
/// go to <c>%LOCALAPPDATA%\Programs\MARK</c>, shortcuts to the Start menu and the desktop, and an entry to the user's
/// installed apps. The user's data (<c>%LOCALAPPDATA%\MARK</c>: quotes, settings, licence, backups) is never touched by an
/// install or an update, and is removed by an uninstall only when asked.
/// </summary>
public static class Installer
{
    public const string ProgramExe = "MARK.exe";
    public const string SetupExe = "MARK Setup.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MARK";

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MARK");

    /// <summary>MARK's data on this computer (kept on uninstall unless asked).</summary>
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MARK");

    private static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MARK.lnk");
    private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MARK.lnk");

    /// <summary>The installed version and folder from the installed-apps entry, or null when MARK is not installed.</summary>
    public static (string Version, string Folder)? Installed()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("InstallLocation") is string folder && folder.Length > 0
            ? (key.GetValue("DisplayVersion") as string ?? "", folder)
            : null;
    }

    /// <summary>MARK programs running from <paramref name="folder"/> (they must be closed before installing or removing).</summary>
    public static IReadOnlyList<int> RunningFrom(string folder)
    {
        string full = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        var running = new List<int>();
        foreach (var process in Process.GetProcessesByName("MARK"))
        {
            try
            {
                if (process.MainModule?.FileName is { } path && path.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                    running.Add(process.Id);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Another user's process: not ours to check.
            }
            finally
            {
                process.Dispose();
            }
        }
        return running;
    }

    /// <summary>
    /// Installs (or updates) MARK from <paramref name="payload"/> (a zip of the published program). Files of an earlier
    /// version that the new one no longer has are removed; the user's data is not touched. Returns an error, or null.
    /// </summary>
    public static string? Install(Stream payload, InstallOptions options, string version, Action<string>? progress = null, string? setupExe = null)
    {
        string target = Path.GetFullPath(options.TargetFolder);
        if (RunningFrom(target).Count > 0) return "MARK is open. Close MARK, then install again.";
        string staging = target.TrimEnd('\\') + ".new";
        try
        {
            progress?.Invoke("Unpacking MARK…");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true))
                zip.ExtractToDirectory(staging, overwriteFiles: true);
            if (!File.Exists(Path.Combine(staging, ProgramExe))) return "The setup file is damaged: MARK.exe is missing from it.";

            progress?.Invoke("Copying the program files…");
            Directory.CreateDirectory(target);
            var newFiles = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(staging, f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in newFiles)
            {
                string to = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(Path.Combine(staging, relative), to, overwrite: true);
            }
            // Files the new version no longer has (but never the setup itself).
            foreach (string old in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(target, old);
                if (!newFiles.Contains(relative) && !relative.Equals(SetupExe, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
            }
            if (setupExe is not null && File.Exists(setupExe)
                && !Path.GetFullPath(setupExe).Equals(Path.Combine(target, SetupExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(setupExe, Path.Combine(target, SetupExe), overwrite: true);

            string program = Path.Combine(target, ProgramExe);
            if (options.Shortcuts)
            {
                progress?.Invoke("Adding the shortcuts…");
                Shortcut(StartMenuShortcut, program, target);
                if (options.DesktopShortcut) Shortcut(DesktopShortcut, program, target);
                else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);
            }
            if (options.Register)
            {
                progress?.Invoke("Adding MARK to the installed apps…");
                using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
                key.SetValue("DisplayName", "MARK");
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "MARK");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", program);
                key.SetValue("UninstallString", $"\"{Path.Combine(target, SetupExe)}\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(Directory.GetFiles(target, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024),
                    RegistryValueKind.DWord);
            }
            progress?.Invoke("MARK is installed.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        {
            return $"MARK could not be installed: {ex.Message}";
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left for the next install to clear.
            }
        }
    }

    /// <summary>
    /// Removes MARK: shortcuts, the installed-apps entry and the program folder (the setup running from it is removed just
    /// after it closes). The data is removed only with <paramref name="removeData"/>. Returns an error, or null.
    /// </summary>
    public static string? Uninstall(string folder, bool removeData, bool unregister = true)
    {
        string target = Path.GetFullPath(folder);
        if (RunningFrom(target).Count > 0) return "MARK is open. Close MARK, then remove it again.";
        try
        {
            foreach (string link in new[] { StartMenuShortcut, DesktopShortcut })
                if (unregister && File.Exists(link)) File.Delete(link);
            if (unregister) Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            string? running = Environment.ProcessPath;
            foreach (string file in Directory.Exists(target) ? Directory.GetFiles(target, "*", SearchOption.AllDirectories) : Array.Empty<string>())
                if (!string.Equals(file, running, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            if (running is not null && running.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            {
                // This setup is in the folder: a short-lived command removes it (and the folder) once it has closed.
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{target}\"")
                {
                    CreateNoWindow = true, UseShellExecute = false
                });
            }
            else if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            if (removeData && Directory.Exists(DataFolder)) Directory.Delete(DataFolder, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $"MARK could not be removed completely: {ex.Message}";
        }
    }

    /// <summary>A Windows shortcut (.lnk) through the shell's own COM object.</summary>
    private static void Shortcut(string link, string target, string workingFolder)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = workingFolder;
            shortcut.IconLocation = target + ",0";
            shortcut.Description = "MARK: windows and doors";
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
