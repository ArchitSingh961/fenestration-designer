using System.IO;
using System.Windows;

namespace Mark.Setup;

/// <summary>
/// MARK Setup. Without arguments it shows the install (or update) window; <c>--uninstall</c> shows the remove window.
/// For scripts: <c>--silent [--target &lt;folder&gt;] [--no-shortcuts] [--no-register]</c> installs without a window and
/// sets the exit code (0 = installed).
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        if (args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
        {
            string target = After(args, "--target") ?? Installer.DefaultFolder;
            using var payload = SetupWindow.Payload();
            string? error = payload is null ? "This MARK Setup has no program in it."
                : Installer.Install(payload, new InstallOptions(target, !args.Contains("--no-shortcuts"), true, !args.Contains("--no-register")),
                    SetupWindow.Version, setupExe: Environment.ProcessPath);
            if (error is not null) File.WriteAllText(Path.Combine(Path.GetTempPath(), "MARK Setup.log"), error);
            Shutdown(error is null ? 0 : 1);
            return;
        }
        var window = new SetupWindow(uninstall: args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase));
        MainWindow = window;
        window.Show();
    }

    private static string? After(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
