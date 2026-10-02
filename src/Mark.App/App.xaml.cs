using System.IO;
using System.Windows;
using System.Windows.Threading;
using Mark.App.Dialogs;
using Mark.Calculation;
using Mark.Core.Library;
using Mark.Data;
using Mark.Designer.ViewModels;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var rules = LoadRules(out string? rulesError);
        var messages = new List<string?> { rulesError };
        var dialogs = new WpfDialogService();
        MainViewModel mainViewModel;
        try
        {
            // The local database is the source of truth for the library and saved projects. On first run it is created
            // and the shipped library.json is imported into it; afterwards library.json is only an import/export format.
            messages.Add(LocalStore.AdoptLegacyDatabase(LocalStore.DefaultPath, LocalStore.LegacyDefaultPath));
            var store = LocalStore.Open(LocalStore.DefaultPath, LibraryPath);
            messages.AddRange(store.StartupMessages);
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
        var window = new MainWindow { DataContext = mainViewModel };
        window.Show();
    }

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
