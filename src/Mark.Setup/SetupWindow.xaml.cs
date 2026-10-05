using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Mark.Setup;

/// <summary>The install / update / remove window.</summary>
public partial class SetupWindow : Window
{
    private readonly bool _uninstall;
    private readonly (string Version, string Folder)? _installed;
    private bool _done;

    public SetupWindow(bool uninstall)
    {
        InitializeComponent();
        _uninstall = uninstall;
        _installed = Installer.Installed();
        if (uninstall)
        {
            Title = "Remove MARK";
            Heading.Text = "Remove MARK";
            Intro.Text = "MARK is removed from this computer: the program, its shortcuts and its entry in the installed apps.";
            InstallOptionsPanel.Visibility = Visibility.Collapsed;
            UninstallOptionsPanel.Visibility = Visibility.Visible;
            DataNote.Text = $"Leave it unticked to keep your data ({Installer.DataFolder}): installing MARK again finds it. "
                            + "The company's licence stays on the licence server either way.";
            ActionButton.Content = "Remove";
            return;
        }
        FolderBox.Text = _installed?.Folder ?? Installer.DefaultFolder;
        if (_installed is { } existing)
        {
            Heading.Text = existing.Version == Version ? $"Repair MARK {Version}" : $"Update MARK {existing.Version} to {Version}";
            Intro.Text = "Your quotes, settings and licence stay as they are. Close MARK first if it is open.";
            ActionButton.Content = "Update";
        }
        else
        {
            Heading.Text = $"Install MARK {Version}";
            Intro.Text = "MARK is installed for you on this computer (no administrator needed). Sign in with the User ID and password your MARK supplier gave you.";
        }
        if (Payload() is not { } payload)
        {
            Status.Text = "This MARK Setup has no program in it: it was built without one (installer\\build.ps1 makes a complete setup).";
            Status.Foreground = Brushes.Firebrick;
            ActionButton.IsEnabled = false;
        }
        else payload.Dispose();
    }

    /// <summary>The version of MARK in this setup (the setup's own version: they are released together).</summary>
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>The zipped program inside this setup, or null.</summary>
    public static Stream? Payload() => Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Install MARK in", InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null };
        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = Path.GetFileName(dialog.FolderName).Equals("MARK", StringComparison.OrdinalIgnoreCase)
                ? dialog.FolderName : Path.Combine(dialog.FolderName, "MARK");
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            Close();
            return;
        }
        ActionButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Status.Foreground = Brushes.Black;
        string? error;
        if (_uninstall)
        {
            if (RemoveDataBox.IsChecked == true && MessageBox.Show(this,
                    "Remove all of MARK's data from this computer? Quotes, orders and settings that are not backed up elsewhere are lost.",
                    "Remove MARK", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                Reset();
                return;
            }
            string folder = _installed?.Folder ?? Path.GetDirectoryName(Environment.ProcessPath) ?? Installer.DefaultFolder;
            bool removeData = RemoveDataBox.IsChecked == true;
            Status.Text = "Removing MARK…";
            error = await Task.Run(() => Installer.Uninstall(folder, removeData));
            if (error is null) Status.Text = "MARK has been removed." + (removeData ? "" : " Your data is kept.");
        }
        else
        {
            string target = FolderBox.Text.Trim();
            if (target.Length == 0 || !Path.IsPathFullyQualified(target))
            {
                Status.Text = "Choose the folder to install in.";
                Reset();
                return;
            }
            bool desktop = DesktopBox.IsChecked == true;
            error = await Task.Run(() =>
            {
                using var payload = Payload();
                return payload is null ? "This MARK Setup has no program in it."
                    : Installer.Install(payload, new InstallOptions(target, true, desktop, true), Version,
                        text => Dispatcher.Invoke(() => Status.Text = text), Environment.ProcessPath);
            });
            if (error is null && StartBox.IsChecked == true)
            {
                Process.Start(new ProcessStartInfo(Path.Combine(target, Installer.ProgramExe)) { UseShellExecute = true, WorkingDirectory = target });
                Close();
                return;
            }
        }
        Progress.Visibility = Visibility.Collapsed;
        if (error is not null)
        {
            Status.Text = error;
            Status.Foreground = Brushes.Firebrick;
            Reset();
            return;
        }
        _done = true;
        ActionButton.Content = "Close";
        ActionButton.IsEnabled = true;
        CloseButton.Visibility = Visibility.Collapsed;
    }

    private void Reset()
    {
        Progress.Visibility = Visibility.Collapsed;
        ActionButton.IsEnabled = true;
        CloseButton.IsEnabled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
