using System.IO;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Saving and opening projects, project-file import/export and the library manager. Storage goes through the
/// <see cref="LocalStore"/> services (<see cref="IProjectRepository"/>, <see cref="LibraryService"/>); this class only
/// orchestrates and reports. Undo history is session state: it is cleared when another project is opened and is never
/// saved. Without a store (e.g. the database could not be opened) the designer works as before but cannot save.
/// </summary>
public partial class MainViewModel
{
    /// <param name="store">The local database: library and saved projects.</param>
    /// <param name="calculationRules">Fabrication rules for the calculation (defaults if null).</param>
    /// <param name="dialogs">Dialogs for names, confirmations and files (null: no prompts, e.g. in tests).</param>
    public MainViewModel(LocalStore store, CalculationRules? calculationRules = null, IDialogService? dialogs = null)
        : this((store ?? throw new ArgumentNullException(nameof(store))).Library.Current, calculationRules)
    {
        Store = store;
        Dialogs = dialogs;
        store.Projects.User = CurrentUser;
        store.Library.Changed += ReplaceLibrary;
        // The first new quote was created before the store was known: give it the company's default pricing.
        Project.Pricing = DefaultPricing();
        Pricing.Load(Project.Pricing);
        RefreshQuoteViews();
        RaisePersistenceCanExecute();
    }

    /// <summary>The local database, or null when the designer runs without one.</summary>
    public LocalStore? Store { get; private set; }

    public IDialogService? Dialogs { get; set; }

    public bool HasStore => Store is not null;

    private bool _isDirty;
    /// <summary>True when the design changed since it was last saved, opened or created.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                OnPropertyChanged(nameof(Title));
        }
    }

    public ICommand SaveProjectCommand { get; private set; } = null!;
    public ICommand SaveProjectAsCommand { get; private set; } = null!;
    public ICommand OpenProjectCommand { get; private set; } = null!;
    public ICommand ImportProjectFileCommand { get; private set; } = null!;
    public ICommand ExportProjectFileCommand { get; private set; } = null!;
    public ICommand OpenLibraryManagerCommand { get; private set; } = null!;

    private void CreatePersistenceCommands()
    {
        SaveProjectCommand = new RelayCommand(() => Report(SaveProject()), () => HasStore && Access.ReadOnlyMessage is null);
        SaveProjectAsCommand = new RelayCommand(SaveProjectAsInteractive, () => HasStore && Access.ReadOnlyMessage is null);
        OpenProjectCommand = new RelayCommand(OpenProjectInteractive, () => HasStore);
        ImportProjectFileCommand = new RelayCommand(ImportProjectFileInteractive, () => Access.CanUseProjectFiles);
        ExportProjectFileCommand = new RelayCommand(ExportProjectFileInteractive, () => Access.CanUseProjectFiles);
        OpenLibraryManagerCommand = new RelayCommand(OpenLibraryManager, CanOpenLibraryManager);
    }

    /// <summary>
    /// The Library Manager changes the library, so it needs a licence that is not read-only, and the feature, unless the
    /// library follows the owner's catalogue (then it only sets the company's own prices, which every company needs).
    /// </summary>
    private bool CanOpenLibraryManager() => HasStore && !Access.IsReadOnly && Access.CanUseLibrary;

    private void RaisePersistenceCanExecute()
    {
        foreach (var command in new[]
                 {
                     SaveProjectCommand, SaveProjectAsCommand, OpenProjectCommand, OpenLibraryManagerCommand,
                     ImportProjectFileCommand, ExportProjectFileCommand
                 })
            ((RelayCommand)command).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasStore));
    }

    // ── Save ────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the open project to the database (insert, or replace the saved version with the same Id). A project that was
    /// never saved is first given a name (if a dialog service is available). Returns an error message, or null.
    /// </summary>
    public string? SaveProject()
    {
        if (Store is null)
            return "There is no local database, so the project cannot be saved.";
        if (Access.ReadOnlyMessage is { } readOnly)
            return readOnly;
        try
        {
            // A quote named on its Client tab is saved under that name; only a still-default name is asked for.
            if (!Store.Projects.Exists(Project.Id) && Dialogs is not null && IsDefaultName(Project.Name))
            {
                string? name = Dialogs.PromptText("Save project", "Project name", Project.Name);
                if (string.IsNullOrWhiteSpace(name))
                    return null;                                  // cancelled
                Project.Name = name.Trim();
            }
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        return Persist($"Saved '{Project.Name}'.");
    }

    private static bool IsDefaultName(string name)
        => string.IsNullOrWhiteSpace(name) || name is "New quote" or "New Project" or "Untitled Project";

    /// <summary>
    /// Saves a copy of the open project under a new name and makes the copy the open project. The copy gets new Ids for
    /// every object (the original stays as it was saved). Returns an error message, or null.
    /// </summary>
    public string? SaveProjectAs(string name)
    {
        if (Store is null)
            return "There is no local database, so the project cannot be saved.";
        if (Access.ReadOnlyMessage is { } readOnly)
            return readOnly;
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a project name.";

        var copy = Project.Clone();
        copy.Name = name.Trim();
        ShowProject(copy);
        return Persist($"Saved a copy as '{copy.Name}'.");
    }

    private string? Persist(string success)
    {
        if (Access.ReadOnlyMessage is { } readOnly)
            return readOnly;
        try
        {
            Store!.Projects.Save(Project, QuoteValueOf());
            IsDirty = false;
            OnPropertyChanged(nameof(Title));
            RefreshQuoteViews();
            RefreshHistory();
            Hint = success;
            HintIsError = false;
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    private void SaveProjectAsInteractive()
    {
        string? name = Dialogs?.PromptText("Save a copy", "Name of the copy", $"{Project.Name} (copy)");
        if (name is not null)
            Report(SaveProjectAs(name));
    }

    // ── Open ────────────────────────────────────────────────────────

    /// <summary>Opens a saved project (replacing the open one without asking). Returns an error message, or null.</summary>
    public string? OpenProject(Guid id)
    {
        if (Store is null)
            return "There is no local database.";
        try
        {
            var project = Store.Projects.Load(id);
            ShowProject(project);
            Hint = $"Opened '{project.Name}'.";
            HintIsError = false;
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    private void OpenProjectInteractive()
    {
        if (Store is null || Dialogs is null || !ConfirmDiscardChanges())
            return;
        ProjectListViewModel list;
        try
        {
            list = new ProjectListViewModel(Store.Projects, Project.Id, Dialogs) { Blocked = () => Access.ReadOnlyMessage };
        }
        catch (DataStoreException ex)
        {
            Report(ex.Message);
            return;
        }
        if (Dialogs.ChooseProject(list) is { } id)
            Report(OpenProject(id));
    }

    /// <summary>
    /// True when it is fine to replace the open design: it has no unsaved changes, or the user agreed to discard them
    /// (without a dialog service there is nobody to ask, so it is fine).
    /// </summary>
    /// <summary>Treats the open design as saved, after the user agreed to discard its changes (e.g. when signing out).</summary>
    public void ForgetChanges() => IsDirty = false;

    public bool ConfirmDiscardChanges()
        => !IsDirty || Dialogs is null
           || Dialogs.Confirm("Unsaved changes", $"'{Project.Name}' has unsaved changes. Discard them?");

    // ── Project files (import / export) ─────────────────────────────

    /// <summary>
    /// Opens a project file (the same JSON format the database stores). If a project with the same Id is already saved,
    /// the import becomes a copy with new Ids, so it never overwrites the saved one. Returns an error message, or null.
    /// </summary>
    public string? ImportProjectFile(string path)
    {
        Project project;
        try
        {
            project = ProjectSerializer.Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return $"The project file could not be read: {ex.Message}";
        }

        bool copied = false;
        bool alreadySaved;
        try
        {
            alreadySaved = Store?.Projects.Exists(project.Id) == true;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        if (alreadySaved)
        {
            project = project.Clone();
            project.Name = $"{project.Name} (imported)";
            copied = true;
        }
        ShowProject(project);
        IsDirty = true;                                          // not in the database until saved
        Hint = copied ? $"Imported as a copy ('{project.Name}'): a project with the same Id is already saved."
                      : $"Imported '{project.Name}'. Save it to keep it in the database.";
        HintIsError = false;
        return null;
    }

    /// <summary>Writes the open project as a project file. Returns an error message, or null.</summary>
    public string? ExportProjectFile(string path)
    {
        try
        {
            File.WriteAllText(path, ProjectSerializer.Serialize(Project));
            Hint = $"Exported '{Project.Name}' to {Path.GetFileName(path)}.";
            HintIsError = false;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The project file could not be written: {ex.Message}";
        }
    }

    private const string ProjectFileFilter = "Project files (*.json)|*.json|All files (*.*)|*.*";

    private void ImportProjectFileInteractive()
    {
        if (Dialogs is null || !ConfirmDiscardChanges())
            return;
        if (Dialogs.ChooseOpenFile("Import project file", ProjectFileFilter) is { } path)
            Report(ImportProjectFile(path));
    }

    private void ExportProjectFileInteractive()
    {
        if (Dialogs?.ChooseSaveFile("Export project file", ProjectFileFilter, $"{Project.Name}.json") is { } path)
            Report(ExportProjectFile(path));
    }

    // ── Library ─────────────────────────────────────────────────────

    /// <summary>
    /// Switches the designer to a new library snapshot (after an edit in the library manager): pickers offer the new
    /// products, and the BOM, cost and cutting plan are recalculated from the new definitions. References in the design
    /// are ids, so they stay valid; the undo history is unaffected.
    /// </summary>
    public void ReplaceLibrary(IProductLibrary library)
    {
        Library = library ?? throw new ArgumentNullException(nameof(library));
        Properties.Library = library;
        Calculation.UseLibrary(library);
        OnPropertyChanged(nameof(Library));
        RefreshProperties();
        RefreshCalculation();
        RefreshQuoteViews();
    }

    internal void OpenLibraryManager()
    {
        if (Store is null || Dialogs is null)
            return;
        if (!CanOpenLibraryManager())
        {
            Report(Access.ReadOnlyMessage ?? AccessViewModel.LockedMessage(Licensing.Features.LibraryManager));
            return;
        }
        Mark.Core.Library.OwnItemsLabel? ownItems = null;
        try
        {
            if (Access.IsCatalogueManaged) ownItems = Store.Settings.LoadOwnItems();
        }
        catch (DataStoreException)
        {
            // Without the label the company's own items are listed with the others.
        }
        Dialogs.ShowLibraryManager(new LibraryManagerViewModel(Store.Library, Store.Projects, () => Project, Dialogs,
            pricesOnly: Access.IsCatalogueManaged, ownItems: ownItems is null ? null : OwnItemsSection.Of(ownItems)));
    }

    private void Report(string? error)
    {
        if (error is not null)
            DesignMessage = error;
    }
}
