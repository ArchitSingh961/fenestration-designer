using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Fenestration.Core.Library;
using Fenestration.Core.Models;
using Fenestration.Data;

namespace Fenestration.Designer.ViewModels;

/// <summary>One product in the library manager's list.</summary>
public sealed record LibraryItemRow(LibraryItemKind Kind, string Id, string Name, string Detail, bool IsActive)
{
    public string Status => IsActive ? "" : "retired";
}

/// <summary>
/// The library manager: browse, search and filter the products of one kind, and add, edit, retire, reinstate, delete,
/// import and export them. Searching runs on the in-memory library snapshot (<see cref="IProductLibrary"/> search with
/// <see cref="LibraryQuery"/> filters), never on the database. Every change goes through <see cref="LibraryService"/>,
/// which validates it against the whole library and writes it in one transaction; the designer picks the new snapshot
/// up through <see cref="LibraryService.Changed"/> and recalculates.
/// </summary>
public sealed class LibraryManagerViewModel : ViewModelBase
{
    private const string All = "(all)";
    private const string LibraryFileFilter = "Library files (*.json)|*.json|All files (*.*)|*.*";

    private readonly LibraryService _library;
    private readonly IProjectRepository? _projects;
    private readonly Func<Project?> _openProject;
    private readonly IDialogService? _dialogs;
    private bool _refreshing;

    public LibraryManagerViewModel(LibraryService library, IProjectRepository? projects = null, Func<Project?>? openProject = null,
        IDialogService? dialogs = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _projects = projects;
        _openProject = openProject ?? (() => null);
        _dialogs = dialogs;

        NewCommand = new RelayCommand(() => Editor = LibraryItemEditorViewModel.ForNew(Kind));
        SaveCommand = new RelayCommand(Save, () => Editor is not null);
        DeleteCommand = new RelayCommand(Delete, () => SelectedItem is not null);
        ToggleActiveCommand = new RelayCommand(ToggleActive, () => SelectedItem is not null);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export);
        Refresh();
    }

    public static IReadOnlyList<LibraryItemKind> Kinds { get; } = Enum.GetValues<LibraryItemKind>();

    private LibraryItemKind _kind = LibraryItemKind.Profile;
    public LibraryItemKind Kind
    {
        get => _kind;
        set
        {
            if (!SetProperty(ref _kind, value)) return;
            _manufacturerFilter = All;
            _groupFilter = All;
            OnPropertyChanged(nameof(ManufacturerFilter));
            OnPropertyChanged(nameof(GroupFilter));
            OnPropertyChanged(nameof(GroupLabel));
            Refresh();
        }
    }

    /// <summary>"Series" for profiles, "Category" for glass and materials.</summary>
    public string GroupLabel => Kind == LibraryItemKind.Profile ? "Series" : "Category";

    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Refresh(); }
    }

    public ObservableCollection<string> Manufacturers { get; } = new();

    private string _manufacturerFilter = All;
    public string ManufacturerFilter
    {
        get => _manufacturerFilter;
        set { if (SetProperty(ref _manufacturerFilter, value ?? All) && !_refreshing) Refresh(); }
    }

    public ObservableCollection<string> Groups { get; } = new();

    private string _groupFilter = All;
    public string GroupFilter
    {
        get => _groupFilter;
        set { if (SetProperty(ref _groupFilter, value ?? All) && !_refreshing) Refresh(); }
    }

    private bool _showInactive;
    /// <summary>Also list retired products.</summary>
    public bool ShowInactive
    {
        get => _showInactive;
        set { if (SetProperty(ref _showInactive, value)) Refresh(); }
    }

    public ObservableCollection<LibraryItemRow> Items { get; } = new();

    private string _resultSummary = "";
    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    private LibraryItemRow? _selectedItem;
    public LibraryItemRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ToggleActiveCommand).RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ToggleActiveText));
            if (!_refreshing)
                Editor = value is null ? null : EditorFor(value);
            UsageText = value is null ? "" : DescribeUsage(value);
        }
    }

    private LibraryItemEditorViewModel? _editor;
    public LibraryItemEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>Where the selected product is used (saved projects, the open project), e.g. before deleting it.</summary>
    private string _usageText = "";
    public string UsageText
    {
        get => _usageText;
        private set => SetProperty(ref _usageText, value);
    }

    public string ToggleActiveText => SelectedItem is { IsActive: false } ? "Reinstate" : "Retire";

    private string? _message;
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    private bool _messageIsError;
    public bool MessageIsError
    {
        get => _messageIsError;
        private set => SetProperty(ref _messageIsError, value);
    }

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ToggleActiveCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }

    // ── List ────────────────────────────────────────────────────────

    /// <summary>Rebuilds the filter choices and the list from the current library snapshot.</summary>
    public void Refresh(string? select = null)
    {
        _refreshing = true;
        try
        {
            var library = _library.Current;
            string? selectedId = select ?? SelectedItem?.Id;
            RefreshChoices(library);

            var query = new LibraryQuery(SearchText)
            {
                IncludeInactive = ShowInactive,
                Manufacturer = ManufacturerFilter == All ? null : ManufacturerFilter,
                Group = GroupFilter == All || Kind == LibraryItemKind.Material ? null : GroupFilter,
                MaterialCategory = Kind == LibraryItemKind.Material && GroupFilter != All
                                   && Enum.TryParse<MaterialCategory>(GroupFilter, out var category) ? category : null
            };
            var rows = Kind switch
            {
                LibraryItemKind.Profile => library.SearchProfiles(query).Select(p => new LibraryItemRow(Kind, p.Id, p.Name,
                    Join($"{Number(p.FaceWidthMm)} mm", p.Series, p.Manufacturer, $"{Money(p.CostPerMetre)}/m",
                        string.Join("/", p.Roles.Select(r => r.ToString().ToLowerInvariant()))), p.IsActive)),
                LibraryItemKind.Glass => library.SearchGlass(query).Select(g => new LibraryItemRow(Kind, g.Id, g.Name,
                    Join($"{Number(g.ThicknessMm)} mm", g.Category, g.Manufacturer, $"{Money(g.CostPerSquareMetre)}/m²"), g.IsActive)),
                _ => library.SearchMaterials(query).Select(m => new LibraryItemRow(Kind, m.Id, m.Name,
                    Join(m.Category.ToString(), m.Manufacturer, $"{Money(m.CostPerUnit)}/{m.Unit.ToString().ToLowerInvariant()}"), m.IsActive))
            };

            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);
            int total = Kind switch
            {
                LibraryItemKind.Profile => library.Profiles.Count,
                LibraryItemKind.Glass => library.Glass.Count,
                _ => library.Materials.Count
            };
            ResultSummary = $"{Items.Count} of {total} {Kind.ToString().ToLowerInvariant()} item(s)";
            SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId);
        }
        finally
        {
            _refreshing = false;
        }
        Editor = SelectedItem is null ? (Editor is { IsNew: true } ? Editor : null) : EditorFor(SelectedItem);
    }

    private void RefreshChoices(IProductLibrary library)
    {
        IEnumerable<string?> manufacturers = Kind switch
        {
            LibraryItemKind.Profile => library.Profiles.Select(p => p.Manufacturer),
            LibraryItemKind.Glass => library.Glass.Select(g => g.Manufacturer),
            _ => library.Materials.Select(m => m.Manufacturer)
        };
        IEnumerable<string?> groups = Kind switch
        {
            LibraryItemKind.Profile => library.Profiles.Select(p => p.Series),
            LibraryItemKind.Glass => library.Glass.Select(g => g.Category),
            _ => Enum.GetNames<MaterialCategory>()
        };
        Replace(Manufacturers, manufacturers, ManufacturerFilter, v => _manufacturerFilter = v, nameof(ManufacturerFilter));
        Replace(Groups, groups, GroupFilter, v => _groupFilter = v, nameof(GroupFilter));
    }

    /// <summary>Fills a filter list with "(all)" plus the distinct values in ordinal order; keeps the choice if still offered.</summary>
    private void Replace(ObservableCollection<string> target, IEnumerable<string?> values, string current, Action<string> set, string property)
    {
        var distinct = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        target.Clear();
        target.Add(All);
        foreach (var v in distinct) target.Add(v);
        set(target.Contains(current) ? current : All);
        OnPropertyChanged(property);
    }

    private LibraryItemEditorViewModel? EditorFor(LibraryItemRow row) => row.Kind switch
    {
        LibraryItemKind.Profile => _library.Current.FindProfile(row.Id) is { } p ? LibraryItemEditorViewModel.For(p) : null,
        LibraryItemKind.Glass => _library.Current.FindGlass(row.Id) is { } g ? LibraryItemEditorViewModel.For(g) : null,
        _ => _library.Current.FindMaterial(row.Id) is { } m ? LibraryItemEditorViewModel.For(m) : null
    };

    private string DescribeUsage(LibraryItemRow row)
    {
        try
        {
            var parts = new List<string>();
            if (row.Kind != LibraryItemKind.Material && _projects is not null)
            {
                var saved = _projects.FindUsing(row.Kind, row.Id);
                if (saved.Count > 0) parts.Add($"Used by {saved.Count} saved project(s): {string.Join(", ", saved.Select(p => p.Name))}.");
            }
            var blockers = _library.FindBlockers(row.Kind, row.Id, _openProject());
            parts.AddRange(blockers.Where(b => !b.StartsWith("Saved project", StringComparison.Ordinal)));
            return parts.Count == 0 ? "Not used by any saved project, the open project or another product." : string.Join(" ", parts);
        }
        catch (DataStoreException ex)
        {
            return $"Where this product is used could not be checked: {ex.Message}";
        }
    }

    // ── Changes ─────────────────────────────────────────────────────

    private void Save()
    {
        if (Editor is not { } editor)
            return;
        if (editor.Build(out string? error) is not { } definition)
        {
            Fail(error ?? "The form is incomplete.");
            return;
        }

        Try(() =>
        {
            switch (definition)
            {
                case ProfileDefinition p when editor.IsNew: _library.Add(p); break;
                case ProfileDefinition p: _library.Update(p); break;
                case GlassDefinition g when editor.IsNew: _library.Add(g); break;
                case GlassDefinition g: _library.Update(g); break;
                case MaterialDefinition m when editor.IsNew: _library.Add(m); break;
                case MaterialDefinition m: _library.Update(m); break;
            }
            string id = editor.Id.Trim();
            Refresh(select: id);
            if (SelectedItem?.Id != id)                         // saved but filtered out: show it anyway
            {
                SearchText = null;
                ShowInactive = true;
                Refresh(select: id);
            }
            Succeed(editor.IsNew ? $"Added '{id}'." : $"Saved '{id}'. Designs using it are recalculated.");
        });
    }

    private void Delete()
    {
        if (SelectedItem is not { } row)
            return;
        if (_dialogs is not null && !_dialogs.Confirm("Delete product", $"Delete '{row.Name}' ({row.Id}) from the library permanently?"))
            return;
        Try(() =>
        {
            _library.Delete(row.Kind, row.Id, _openProject());
            Refresh();
            Succeed($"Deleted '{row.Id}'.");
        });
    }

    private void ToggleActive()
    {
        if (SelectedItem is not { } row)
            return;
        Try(() =>
        {
            _library.SetActive(row.Kind, row.Id, !row.IsActive);
            if (row.IsActive) ShowInactive = true;              // keep the retired item visible
            Refresh(select: row.Id);
            Succeed(row.IsActive
                ? $"Retired '{row.Id}': it is no longer offered for new designs; existing designs still use it."
                : $"Reinstated '{row.Id}'.");
        });
    }

    private void Import()
    {
        if (_dialogs?.ChooseOpenFile("Import library file", LibraryFileFilter) is not { } path)
            return;
        Try(() =>
        {
            var result = _library.Import(LibrarySerializer.Load(path));
            Refresh();
            Succeed($"Imported {result.Added.Count} product(s)." +
                    (result.Skipped.Count > 0 ? $" Skipped {result.Skipped.Count} already in the library (not overwritten): " +
                                                string.Join(", ", result.Skipped.Take(10)) + (result.Skipped.Count > 10 ? ", …" : "") + "." : ""));
        });
    }

    private void Export()
    {
        if (_dialogs?.ChooseSaveFile("Export library file", LibraryFileFilter, "library.json") is not { } path)
            return;
        Try(() =>
        {
            File.WriteAllText(path, _library.Export());
            Succeed($"Exported the library to {Path.GetFileName(path)}.");
        });
    }

    /// <summary>Runs a change; any refusal (in use, invalid, duplicate id, storage error) is shown, and nothing changed.</summary>
    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (LibraryOperationException ex)
        {
            Fail(ex.Reasons.Count > 0 && ex.Reasons[0] != "Not found." ? $"{ex.Message} {string.Join(" ", ex.Reasons)}" : ex.Message);
        }
        catch (LibraryValidationException ex)
        {
            Fail(string.Join(" ", ex.Errors));
        }
        catch (Exception ex) when (ex is DataStoreException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Fail(ex.Message);
        }
    }

    private void Succeed(string message) => (Message, MessageIsError) = (message, false);

    private void Fail(string message) => (Message, MessageIsError) = (message, true);

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
