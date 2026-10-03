using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Mark.Designer.ViewModels;
using Mark.Designer.Views;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Mark.Owner.ViewModels;

namespace Mark.Owner;

/// <summary>
/// MARK Owner: sign in (or set up the admin on a new licence server), then the main window. Signing out, or a session
/// that ended, goes back to the sign-in page. The last server address and User ID are remembered.
/// </summary>
public partial class App : Application
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MARK Owner", "settings.json");

    private sealed record OwnerSettings(string ServerUrl, string? UserId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        ShowSignIn();
    }

    private void ShowSignIn()
    {
        var settings = LoadSettings();
        var viewModel = new OwnerSignInViewModel(url => new OwnerApiClient(url), settings.ServerUrl, settings.UserId);
        var window = new SignInWindow(viewModel);
        OwnerApiClient? client = null;
        viewModel.SignedIn += c =>
        {
            client = c;
            window.DialogResult = true;
        };
        if (window.ShowDialog() != true || client is null)
        {
            Shutdown();
            return;
        }

        SaveSettings(new OwnerSettings(client.ServerUrl.TrimEnd('/'), viewModel.UserId.Trim()));
        MainWindow? main = null;
        var dialogs = new OwnerDialogs(() => main);
        var shell = new OwnerShellViewModel(client, dialogs, () =>
        {
            // Signed out, or the session ended: back to the sign-in page (once).
            if (main is null || !main.IsLoaded) return;
            var closing = main;
            main = null;
            closing.Close();
            Dispatcher.BeginInvoke(ShowSignIn);
        }, dialogs, Path.Combine(Path.GetDirectoryName(SettingsPath)!, "work"),
            Path.Combine(AppContext.BaseDirectory, "Library", "sample-catalogue.json"));
        main = new MainWindow { DataContext = shell };
        MainWindow = main;
        main.Closed += (_, _) =>
        {
            if (MainWindow == main || MainWindow is null) Shutdown();
        };
        main.Show();
        _ = shell.LoadAsync();
    }

    private static OwnerSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<OwnerSettings>(File.ReadAllText(SettingsPath)) ?? Default();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: start with the defaults.
        }
        return Default();

        static OwnerSettings Default() => new(LicenceDefaults.ServerUrl, null);
    }

    private static void SaveSettings(OwnerSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering the address is not worth an error.
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}", "MARK Owner", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

/// <summary>MARK Owner's dialogs: confirmations, choosing a logo, copying keys, and the Library Manager for the catalogue.</summary>
internal sealed class OwnerDialogs : IOwnerDialogs, ICatalogueEditorHost, IDialogService
{
    private readonly Func<Window?> _owner;

    public OwnerDialogs(Func<Window?> owner)
    {
        _owner = owner;
    }

    public bool Confirm(string title, string message)
        => (_owner() is { } owner
               ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
               : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question))
           == MessageBoxResult.Yes;

    public string? ChooseImageFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the company logo",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        return dialog.ShowDialog(_owner()) == true ? dialog.FileName : null;
    }

    public void CopyText(string text) => Clipboard.SetText(text);

    // ── The Library Manager for the catalogue ───────────────────────

    public IDialogService Dialogs => this;

    public void ShowLibraryManager(LibraryManagerViewModel manager)
        => new LibraryManagerWindow(manager) { Owner = _owner(), Title = "Catalogue — Library Manager" }.ShowDialog();

    public string? PromptText(string title, string label, string initialText) => null;

    public Guid? ChooseProject(ProjectListViewModel projects) => null;

    public string? ChooseOpenFile(string title, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(_owner()) == true ? dialog.FileName : null;
    }

    public string? ChooseSaveFile(string title, string filter, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = fileName };
        return dialog.ShowDialog(_owner()) == true ? dialog.FileName : null;
    }
}
