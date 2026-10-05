using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Mark.App.Dialogs;
using Mark.App.Licensing;
using Mark.Calculation;
using Mark.Core.Library;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;

namespace Mark.App;

/// <summary>
/// Application entry point. Composes the view model graph and shows the main window.
/// (StartupUri is intentionally not used so the composition root stays in one place.)
/// </summary>
public partial class App : Application
{
    /// <summary>The product library shipped next to the executable.</summary>
    private static readonly string LibraryPath = Path.Combine(AppContext.BaseDirectory, "Library", "library.json");

    /// <summary>Workshop fabrication and saw rules (kerf, trim, minimum offcut) shipped next to the executable.</summary>
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "Settings", "calculation-rules.json");

    /// <summary>How often MARK asks the licence server for the current licence while it runs.</summary>
    private static readonly TimeSpan CheckInInterval = TimeSpan.FromHours(6);

    /// <summary>How often the licence is re-evaluated (end of validity, offline grace) while MARK runs.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    private LicenceManager? _licence;
    private CatalogueSync? _catalogue;
    private string[] _args = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        _args = e.Args;

        // Nothing opens until the user is signed in: the sign-in page is the only window until then.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _licence = CreateLicenceManager(e.Args);
        if (!_licence.TryResume() && new SignInWindow(new SignInViewModel(_licence)).ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        var rules = LoadRules(out string? rulesError);
        var messages = new List<string?> { rulesError };
        var dialogs = new WpfDialogService();
        MainViewModel mainViewModel;
        try
        {
            // The local database is the source of truth for the library and saved projects. On first run it is created
            // and the shipped library.json is imported into it; afterwards library.json is only an import/export format.
            // Each company keeps its own database on this computer (%LOCALAPPDATA%\MARK\Companies\{id}\mark.db), so
            // nothing of one company shows in another's; --database <file> uses another database (e.g. for testing).
            var signedIn = _licence.Licence;
            string? databaseArgument = ArgumentAfter(e.Args, "--database");
            string databasePath = databaseArgument
                                  ?? (signedIn is { CompanyId: var companyId } && companyId != Guid.Empty
                                      ? CompanyDatabases.PathFor(companyId)
                                      : LocalStore.DefaultPath);
            if (databaseArgument is null)
                messages.Add(LocalStore.AdoptLegacyDatabase(LocalStore.DefaultPath, LocalStore.LegacyDefaultPath));
            var store = LocalStore.Open(databasePath, LibraryPath);
            messages.AddRange(store.StartupMessages);
            // Before, every company signed in here shared one database: bring over this login's own work, once.
            if (databaseArgument is null && signedIn is not null && databasePath != LocalStore.DefaultPath)
                messages.Add(CompanyDatabases.AdoptSharedWork(store, LocalStore.DefaultPath, signedIn.CompanyId, signedIn.UserId,
                    signedIn.UserName));
            // The owner's catalogue first, so the designer starts with the company's systems (waits briefly when online).
            _catalogue = new CatalogueSync(_licence, store);
            if (_catalogue.IsDue)
            {
                var sync = Task.Run(() => _catalogue.SyncAsync());
                if (sync.Wait(TimeSpan.FromSeconds(8))) messages.Add(sync.Result);
            }
            mainViewModel = new MainViewModel(store, rules, dialogs);
        }
        catch (DataStoreException ex)
        {
            // The database cannot be used: never touch it. Design with the shipped library file, without saving.
            var library = LoadLibrary(out string? libraryError);
            messages.Add($"{ex.Message} Working without the local database: projects cannot be saved and the library cannot be edited.");
            messages.Add(libraryError);
            mainViewModel = new MainViewModel(library, rules) { Dialogs = dialogs };
        }

        string message = string.Join(" ", messages.Where(m => !string.IsNullOrEmpty(m)));
        mainViewModel.DesignMessage = message.Length > 0 ? message : null;
        // With saved quotes available, start where the work is: the dashboard. Without a database, start drawing.
        mainViewModel.Page = mainViewModel.HasStore ? AppPage.Dashboard : AppPage.Quote;

        var licence = _licence;
        mainViewModel.Access.Apply(licence.Status);
        void OnLicenceChanged()
        {
            mainViewModel.Access.Apply(licence.Status);
            if (_catalogue is { IsDue: true } catalogue)
                _ = SyncCatalogueAsync(catalogue, mainViewModel);
        }
        licence.StatusChanged += () =>
        {
            if (Dispatcher.CheckAccess()) OnLicenceChanged();
            else Dispatcher.BeginInvoke(OnLicenceChanged);
        };
        mainViewModel.Account = new AccountViewModel(licence, () => SignOutAsync(licence, mainViewModel));
        // Staff logins: only the account owner manages them (Milestone 14).
        if (licence.CanManageStaff)
            mainViewModel.Staff = new StaffViewModel(licence, dialogs.Confirm);

        // A written quotation opens in the computer's PDF viewer.
        mainViewModel.OpenDocument = path =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No viewer: the file is saved where the user chose.
            }
        };

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        StartLicenceChecks(licence);
    }

    /// <summary>Applies a changed catalogue while MARK runs and says what changed.</summary>
    private static async Task SyncCatalogueAsync(CatalogueSync catalogue, MainViewModel mainViewModel)
    {
        if (await catalogue.SyncAsync() is { } message)
            mainViewModel.DesignMessage = message;
    }

    /// <summary>
    /// The licence of this computer, kept encrypted in <c>%LOCALAPPDATA%\MARK\licence.dat</c>
    /// (<c>--licence-state &lt;file&gt;</c> uses another file, e.g. for testing).
    /// </summary>
    private static LicenceManager CreateLicenceManager(string[] args)
    {
        string path = ArgumentAfter(args, "--licence-state") ?? FileLicenceStateStore.DefaultPath;
        var clients = new Dictionary<string, LicenceApiClient>();
        LicenceApiClient ClientFor(string url)
        {
            if (!clients.TryGetValue(url, out var client))
                clients[url] = client = new LicenceApiClient(url);
            return client;
        }
        return new LicenceManager(new FileLicenceStateStore(path, new DpapiProtector()), LicenceVerifier.ForMark(),
            MachineIdentity.Id(), MachineIdentity.Name, ClientFor)
        {
            AppVersion = typeof(App).Assembly.GetName().Version?.ToString()
        };
    }

    /// <summary>Checks in now and every few hours, and re-evaluates the licence regularly (validity, offline grace).</summary>
    private void StartLicenceChecks(LicenceManager licence)
    {
        _ = licence.CheckInAsync();
        var checkIn = new DispatcherTimer { Interval = CheckInInterval };
        checkIn.Tick += async (_, _) => await licence.CheckInAsync();
        checkIn.Start();
        var refresh = new DispatcherTimer { Interval = RefreshInterval };
        refresh.Tick += (_, _) => licence.Refresh();
        refresh.Start();
    }

    /// <summary>Signs out of this computer after asking, then restarts MARK at the sign-in page.</summary>
    private async Task SignOutAsync(LicenceManager licence, MainViewModel mainViewModel)
    {
        if (MessageBox.Show(MainWindow!, "Sign out of MARK on this computer?\n\nYour company's saved quotes stay on this computer, where another company " +
                                         "signing in does not see them. " +
                                         "You will need your User ID and password to sign in again.",
                "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        mainViewModel.QuotationSetup.SaveIfChanged();
        if (!mainViewModel.ConfirmDiscardChanges())
            return;
        await licence.SignOutAsync();
        mainViewModel.ForgetChanges();
        if (Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { Arguments = string.Join(" ", _args.Select(QuoteArgument)), UseShellExecute = false });
        Shutdown();
    }

    /// <summary>The value after <paramref name="name"/> on the command line, or null.</summary>
    private static string? ArgumentAfter(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string QuoteArgument(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;

    /// <summary>
    /// Last resort for an error no view model handled: show it instead of closing the application, so the open design is
    /// not lost. Expected failures (validation, database unavailable) are reported where they happen; this is a safety net.
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow!, $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
                                     "The design is still open. Save your work and restart the application if anything looks wrong.",
            "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>
    /// Loads the shipped library file (used only when the database cannot be opened). A missing or invalid library does
    /// not stop the designer: it starts with an empty library and says why.
    /// </summary>
    private static IProductLibrary LoadLibrary(out string? error)
    {
        error = null;
        if (!File.Exists(LibraryPath))
        {
            error = $"No product library found at {LibraryPath}. Glass and profiles cannot be priced.";
            return ProductLibrary.Empty;
        }

        try
        {
            return LibrarySerializer.Load(LibraryPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = $"The product library could not be loaded: {ex.Message}";
            return ProductLibrary.Empty;
        }
    }

    /// <summary>
    /// Loads the calculation rules. Without a rules file the defaults are used (no kerf, no trim, every leftover kept);
    /// an invalid file falls back to the defaults and says why.
    /// </summary>
    private static CalculationRules LoadRules(out string? error)
    {
        error = null;
        if (!File.Exists(RulesPath))
            return new CalculationRules();

        try
        {
            return CalculationRulesSerializer.Load(RulesPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = $"The calculation rules could not be loaded, defaults are used: {ex.Message}";
            return new CalculationRules();
        }
    }
}
