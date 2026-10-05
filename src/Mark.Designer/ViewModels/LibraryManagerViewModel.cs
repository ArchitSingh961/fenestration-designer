using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>One product in the library manager's list.</summary>
public sealed record LibraryItemRow(LibraryItemKind Kind, string Id, string Name, string Detail, bool IsActive)
{
    public string Status => IsActive ? "" : "retired";

    public override string ToString() => Name;

    /// <summary>"Catalogue" or "Sozluk — own items" when the list is in sections, otherwise null.</summary>
    public string? Section { get; init; }
}

/// <summary>
/// Shows a company's own items apart from the catalogue, in the tabs the owner made for them ("Sozluk — 50 Series"),
/// then those in no tab ("Sozluk — own items"). When <see cref="CanEditTabs"/> (the owner editing them in MARK Owner),
/// tabs can be made, renamed and removed and items moved between them; <see cref="ToTabs"/> gives the result.
/// </summary>
public sealed class OwnItemsSection
{
    public const string CatalogueTitle = "Catalogue";

    private readonly List<string> _tabs = new();
    private readonly Dictionary<string, string> _tabOf = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <param name="isOwn">Which ids are the company's (the rest are the catalogue's).</param>
    /// <param name="tabs">The tabs and the items in them.</param>
    /// <param name="canEditTabs">Tabs can be changed (the owner); otherwise they are only shown.</param>
    public OwnItemsSection(string companyName, Func<string, bool> isOwn, IEnumerable<OwnItemTab>? tabs = null, bool canEditTabs = false)
    {
        CompanyName = companyName;
        IsOwn = isOwn ?? throw new ArgumentNullException(nameof(isOwn));
        CanEditTabs = canEditTabs;
        foreach (var tab in tabs ?? Array.Empty<OwnItemTab>())
        {
            if (AddTab(tab.Name) is not null) continue;
            string name = _tabs.First(t => string.Equals(t, tab.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            foreach (string id in tab.Ids) Assign(id, name);
        }
    }

    public static OwnItemsSection Of(OwnItemsLabel label) => new(label.CompanyName, label.Contains, label.Tabs);

    public string CompanyName { get; }

    public Func<string, bool> IsOwn { get; }

    public bool CanEditTabs { get; }

    /// <summary>The section of the own items in no tab: "Sozluk — own items".</summary>
    public string Title => $"{CompanyName} — own items";

    public IReadOnlyList<string> TabNames => _tabs;

    /// <summary>The tab the item is in, or null.</summary>
    public string? TabOf(string id) => _tabOf.TryGetValue(id, out var tab) ? tab : null;

    /// <summary>"Catalogue", "Sozluk — 50 Series" or "Sozluk — own items".</summary>
    public string SectionOf(string id) => !IsOwn(id) ? CatalogueTitle : TabOf(id) is { } tab ? TitleOf(tab) : Title;

    public string TitleOf(string tab) => $"{CompanyName} — {tab}";

    /// <summary>Where a section comes in the list: the catalogue, the tabs in order, then the items in no tab.</summary>
    public int OrderOf(string section)
        => section == CatalogueTitle ? 0 : _tabs.FindIndex(t => TitleOf(t) == section) is var i and >= 0 ? 1 + i : 1 + _tabs.Count;

    /// <summary>Adds a tab; returns why it cannot be added, or null.</summary>
    public string? AddTab(string? name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "Enter a name for the tab.";
        if (trimmed.Length > 60) return "The name of a tab can have at most 60 characters.";
        if (_tabs.Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase))) return $"There is already a tab \"{trimmed}\".";
        _tabs.Add(trimmed);
        return null;
    }

    /// <summary>Renames a tab (its items stay in it); returns why it cannot be renamed, or null.</summary>
    public string? RenameTab(string tab, string? name)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return "The tab no longer exists.";
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "Enter a name for the tab.";
        if (trimmed.Length > 60) return "The name of a tab can have at most 60 characters.";
        if (_tabs.Where((t, i) => i != index).Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)))
            return $"There is already a tab \"{trimmed}\".";
        _tabs[index] = trimmed;
        foreach (string id in _tabOf.Where(p => p.Value == tab).Select(p => p.Key).ToList())
            _tabOf[id] = trimmed;
        return null;
    }

    /// <summary>Removes a tab; its items are then in no tab.</summary>
    public void RemoveTab(string tab)
    {
        _tabs.Remove(tab);
        foreach (string id in _tabOf.Where(p => p.Value == tab).Select(p => p.Key).ToList())
            _tabOf.Remove(id);
    }

    /// <summary>Puts an item in a tab (null or an unknown tab: in no tab).</summary>
    public void Assign(string id, string? tab)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (tab is not null && _tabs.Contains(tab))
        {
            _tabOf[id] = tab;
            if (!_order.Contains(id)) _order.Add(id);
        }
        else
        {
            _tabOf.Remove(id);
        }
    }

    /// <summary>The tabs with their items (in the order they were put in), for saving.</summary>
    public IReadOnlyList<OwnItemTab> ToTabs()
        => _tabs.Select(t => new OwnItemTab(t, _order.Where(id => _tabOf.TryGetValue(id, out var tab) && tab == t).ToList())).ToList();
}

/// <summary>A tab above the list: everything, the catalogue, a tab of the company's own items, or its items in no tab.</summary>
public sealed record ListTabChoice(string Name, string? Section, string? Tab, bool IsOwnTab)
{
    public override string ToString() => Name;
}

/// <summary>A kind of library entry as offered in the manager's "Show" list.</summary>
public sealed record LibraryKindChoice(LibraryItemKind Kind, string Name)
{
    public override string ToString() => Name;
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
    private readonly OwnItemsSection? _ownItems;
    private bool _refreshing;

    /// <param name="pricesOnly">The library follows the owner's catalogue: only the company's own prices can be changed.</param>
    /// <param name="ownItems">A company's own items, shown as a section of their own after the catalogue; null: one list.</param>
    public LibraryManagerViewModel(LibraryService library, IProjectRepository? projects = null, Func<Project?>? openProject = null,
        IDialogService? dialogs = null, bool pricesOnly = false, OwnItemsSection? ownItems = null)
    {
        _ownItems = ownItems;
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _projects = projects;
        _openProject = openProject ?? (() => null);
        _dialogs = dialogs;
        PricesOnly = pricesOnly;

        NewCommand = new RelayCommand(New, () => !PricesOnly);
        SaveCommand = new RelayCommand(Save, () => Editor is not null && (!PricesOnly || Kind is LibraryItemKind.Profile or LibraryItemKind.Glass
                                                                                                   or LibraryItemKind.Material));
        DeleteCommand = new RelayCommand(Delete, () => SelectedItem is not null && !PricesOnly);
        ToggleActiveCommand = new RelayCommand(ToggleActive, () => SelectedItem is not null && !PricesOnly);
        ImportCommand = new RelayCommand(Import, () => !PricesOnly);
        ExportCommand = new RelayCommand(Export);
        AddTabCommand = new RelayCommand(AddTab, () => CanEditTabs);
        RenameTabCommand = new RelayCommand(RenameTab, () => CanEditTabs && SelectedListTab is { Tab: not null });
        RemoveTabCommand = new RelayCommand(RemoveTab, () => CanEditTabs && SelectedListTab is { Tab: not null });
        Refresh();
    }

    /// <summary>Only prices can be changed: products, systems and bundles come from the owner's catalogue.</summary>
    public bool PricesOnly { get; }

    public bool CanEdit => !PricesOnly;

    // ── Tabs of a company's own items ───────────────────────────────

    private const string NoTab = "(no tab)";

    /// <summary>A company's own items are shown apart: the tabs above the list.</summary>
    public bool HasListTabs => _ownItems is not null;

    /// <summary>Tabs can be made, renamed and removed, and items moved between them (the owner, in MARK Owner).</summary>
    public bool CanEditTabs => _ownItems is { CanEditTabs: true } && !PricesOnly;

    /// <summary>All · Catalogue · each tab of the company's own items · its items in no tab.</summary>
    public ObservableCollection<ListTabChoice> ListTabs { get; } = new();

    private ListTabChoice? _selectedListTab;
    /// <summary>The tab whose items the list shows ("All": everything).</summary>
    public ListTabChoice? SelectedListTab
    {
        get => _selectedListTab;
        set
        {
            if (value is null && _refreshing) return;                  // the list of tabs being rebuilt
            if (!SetProperty(ref _selectedListTab, value)) return;
            ((RelayCommand)RenameTabCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RemoveTabCommand).RaiseCanExecuteChanged();
            if (!_refreshing) Refresh();
        }
    }

    /// <summary>"(no tab)" and the tabs, to put the chosen own item in.</summary>
    public IReadOnlyList<string> ItemTabChoices
        => _ownItems is null ? Array.Empty<string>() : new[] { NoTab }.Concat(_ownItems.TabNames).ToList();

    /// <summary>The tab chooser is shown: the owner, with one of the company's own items (or a new item) in the form.</summary>
    public bool ShowsItemTab => CanEditTabs && Editor is { } editor && (editor.IsNew || _ownItems!.IsOwn(editor.Id.Trim()));

    private string? _newItemTab;
    /// <summary>The tab of the item in the form; changing it moves the item at once (a new item: when it is saved).</summary>
    public string ItemTab
    {
        get
        {
            if (_ownItems is null || Editor is not { } editor) return NoTab;
            return (editor.IsNew ? _newItemTab : _ownItems.TabOf(editor.Id.Trim())) ?? NoTab;
        }
        set
        {
            if (!CanEditTabs || Editor is not { } editor || value is null) return;
            string? tab = value == NoTab ? null : value;
            if (editor.IsNew)
            {
                _newItemTab = tab;
                OnPropertyChanged();
                return;
            }
            string id = editor.Id.Trim();
            if (!_ownItems!.IsOwn(id) || _ownItems.TabOf(id) == tab) return;
            _ownItems.Assign(id, tab);
            ShowTabOf(id);
            Succeed(tab is null ? $"'{id}' is in no tab now." : $"Moved '{id}' to the tab {tab}.");
        }
    }

    public ICommand AddTabCommand { get; }
    public ICommand RenameTabCommand { get; }
    public ICommand RemoveTabCommand { get; }

    private void AddTab()
    {
        if (_ownItems is null || _dialogs?.PromptText("New tab", "Name of the tab, e.g. 50 Series", "") is not { } name) return;
        if (_ownItems.AddTab(name) is { } error)
        {
            Fail(error);
            return;
        }
        string added = name.Trim();
        _selectedListTab = null;
        Refresh();
        SelectedListTab = ListTabs.FirstOrDefault(t => t.Tab == added);
        Succeed($"Made the tab {added}. Choose New to add an item to it, or move an item to it with Tab in its form.");
    }

    private void RenameTab()
    {
        if (_ownItems is null || SelectedListTab is not { Tab: { } tab }
            || _dialogs?.PromptText("Rename tab", "New name of the tab", tab) is not { } name) return;
        if (_ownItems.RenameTab(tab, name) is { } error)
        {
            Fail(error);
            return;
        }
        string renamed = name.Trim();
        _selectedListTab = null;
        Refresh();
        SelectedListTab = ListTabs.FirstOrDefault(t => t.Tab == renamed);
        Succeed($"Renamed the tab {tab} to {renamed}.");
    }

    private void RemoveTab()
    {
        if (_ownItems is null || SelectedListTab is not { Tab: { } tab }) return;
        if (_dialogs is not null && !_dialogs.Confirm("Remove tab",
                $"Remove the tab {tab}? Its items are not deleted: they are listed under {_ownItems.Title}."))
            return;
        _ownItems.RemoveTab(tab);
        _selectedListTab = null;
        Refresh();
        Succeed($"Removed the tab {tab}.");
    }

    /// <summary>Shows the item in the list again after it moved: its tab when one tab is shown.</summary>
    private void ShowTabOf(string id)
    {
        if (_ownItems is not null && _selectedListTab is { Section: not null })
            _selectedListTab = new ListTabChoice("", _ownItems.SectionOf(id), _ownItems.TabOf(id), false);
        Refresh(select: id);
    }

    /// <summary>Rebuilds the tabs above the list, keeping the chosen one when it is still there.</summary>
    private void RefreshListTabs()
    {
        if (_ownItems is not { } own) return;
        var tabs = new List<ListTabChoice>
        {
            new("All", null, null, false),
            new(OwnItemsSection.CatalogueTitle, OwnItemsSection.CatalogueTitle, null, false)
        };
        tabs.AddRange(own.TabNames.Select(t => new ListTabChoice(t, own.TitleOf(t), t, true)));
        tabs.Add(new ListTabChoice(own.TabNames.Count == 0 ? "Own items" : "Other own items", own.Title, null, true));
        string? keep = _selectedListTab?.Section;
        // Only when the tabs changed: rebuilding them while one is being clicked would upset the list and the filters.
        if (!ListTabs.SequenceEqual(tabs))
        {
            ListTabs.Clear();
            foreach (var tab in tabs) ListTabs.Add(tab);
            OnPropertyChanged(nameof(ItemTabChoices));
        }
        var chosen = ListTabs.FirstOrDefault(t => t.Section == keep) ?? ListTabs[0];
        if (!Equals(chosen, _selectedListTab))
        {
            _selectedListTab = chosen;
            OnPropertyChanged(nameof(SelectedListTab));
        }
        ((RelayCommand)RenameTabCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveTabCommand).RaiseCanExecuteChanged();
    }

    public static IReadOnlyList<LibraryItemKind> Kinds { get; } = Enum.GetValues<LibraryItemKind>();

    public static IReadOnlyList<LibraryKindChoice> KindChoices { get; } = new[]
    {
        new LibraryKindChoice(LibraryItemKind.Profile, "Profiles"),
        new LibraryKindChoice(LibraryItemKind.Glass, "Glass"),
        new LibraryKindChoice(LibraryItemKind.Material, "Hardware and accessories"),
        new LibraryKindChoice(LibraryItemKind.System, "Systems"),
        new LibraryKindChoice(LibraryItemKind.Bundle, "Bundles")
    };

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
            OnPropertyChanged(nameof(HasFilters));
            ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            Refresh();
        }
    }

    /// <summary>Manufacturer and series/category filters apply to products only.</summary>
    public bool HasFilters => Kind is LibraryItemKind.Profile or LibraryItemKind.Glass or LibraryItemKind.Material;

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

    /// <summary>The editor when it is a product (profile, glass, material) form, else null.</summary>
    public LibraryItemEditorViewModel? ItemEditor => _editor as LibraryItemEditorViewModel;

    private ILibraryEditor? _editor;
    public ILibraryEditor? Editor
    {
        get => _editor;
        private set
        {
            if (!SetProperty(ref _editor, value)) return;
            ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ItemTab));
            OnPropertyChanged(nameof(ShowsItemTab));
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
            RefreshListTabs();

            var query = new LibraryQuery(SearchText)
            {
                IncludeInactive = ShowInactive,
                Manufacturer = ManufacturerFilter == All ? null : ManufacturerFilter,
                Group = GroupFilter == All || Kind == LibraryItemKind.Material ? null : GroupFilter,
                MaterialCategory = Kind == LibraryItemKind.Material && GroupFilter != All
                                   && Enum.TryParse<MaterialCategory>(GroupFilter, out var category) ? category : null
            };
            var words = (SearchText ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bool Matches(params string?[] fields) => words.All(w => fields.Any(f => f?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false));
            var rows = Kind switch
            {
                LibraryItemKind.System => library.Systems.Where(x => (x.IsActive || ShowInactive) && Matches(x.Id, x.Name, x.Description))
                    .Select(x => new LibraryItemRow(Kind, x.Id, x.Name, Join(EditorText.MaterialName(x.Material), EditorText.UseName(x.Use),
                        library.FindProfile(x.FrameProfileId)?.Name, x.GlassRangeText.Length > 0 ? $"glass {x.GlassRangeText}" : null), x.IsActive)),
                LibraryItemKind.Bundle => library.Bundles.Where(b => (b.IsActive || ShowInactive) && Matches(b.Id, b.Name, b.Description))
                    .Select(b => new LibraryItemRow(Kind, b.Id, b.Name, Join(library.FindSystem(b.SystemId)?.Name ?? "Any system",
                        b.IsOpeningSet ? "with openings" : $"with {library.FindProfile(b.ProfileId)?.Name ?? b.ProfileId}",
                        $"{b.Parts.Count} part{(b.Parts.Count == 1 ? "" : "s")}"), b.IsActive)),
                LibraryItemKind.Profile => library.SearchProfiles(query).Select(p => new LibraryItemRow(Kind, p.Id, p.Name,
                    Join($"{Number(p.FaceWidthMm)} mm", p.Series, p.Manufacturer, $"{Money(p.CostPerMetre)}/m",
                        string.Join("/", p.Roles.Select(r => r.ToString().ToLowerInvariant()))), p.IsActive)),
                LibraryItemKind.Glass => library.SearchGlass(query).Select(g => new LibraryItemRow(Kind, g.Id, g.Name,
                    Join($"{Number(g.ThicknessMm)} mm", g.Category, g.Manufacturer, $"{Money(g.CostPerSquareMetre)}/m²"), g.IsActive)),
                _ => library.SearchMaterials(query).Select(m => new LibraryItemRow(Kind, m.Id, m.Name,
                    Join(m.Category.ToString(), m.Manufacturer, $"{Money(m.CostPerUnit)}/{m.Unit.ToString().ToLowerInvariant()}"), m.IsActive))
            };

            Items.Clear();
            if (_ownItems is { } own)
            {
                string? only = _selectedListTab?.Section;
                rows = rows.Select(r => r with { Section = own.SectionOf(r.Id) })
                    .Where(r => only is null || r.Section == only)
                    .OrderBy(r => own.OrderOf(r.Section!)).ToList();
            }
            foreach (var row in rows)
                Items.Add(row);
            int total = Kind switch
            {
                LibraryItemKind.Profile => library.Profiles.Count,
                LibraryItemKind.Glass => library.Glass.Count,
                LibraryItemKind.System => library.Systems.Count,
                LibraryItemKind.Bundle => library.Bundles.Count,
                _ => library.Materials.Count
            };
            ResultSummary = $"{Items.Count} of {total} {KindChoices.First(k => k.Kind == Kind).Name.ToLowerInvariant()}";
            SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId);
        }
        finally
        {
            _refreshing = false;
        }
        Editor = SelectedItem is null ? (Editor is { IsNew: true } ? Editor : null) : EditorFor(SelectedItem);
    }

    private void New()
    {
        _newItemTab = _selectedListTab?.Tab;                            // a new item goes in the tab shown
        var library = _library.Current;
        Editor = Kind switch
        {
            LibraryItemKind.System => new SystemEditorViewModel(new ProductSystem(), library, isNew: true),
            LibraryItemKind.Bundle => new BundleEditorViewModel(new Bundle(), library, isNew: true),
            _ => LibraryItemEditorViewModel.ForNew(Kind, library)
        };
    }

    private void RefreshChoices(IProductLibrary library)
    {
        IEnumerable<string?> manufacturers = Kind switch
        {
            LibraryItemKind.Profile => library.Profiles.Select(p => p.Manufacturer),
            LibraryItemKind.Glass => library.Glass.Select(g => g.Manufacturer),
            LibraryItemKind.Material => library.Materials.Select(m => m.Manufacturer),
            _ => Array.Empty<string?>()
        };
        IEnumerable<string?> groups = Kind switch
        {
            LibraryItemKind.Profile => library.Profiles.Select(p => p.Series),
            LibraryItemKind.Glass => library.Glass.Select(g => g.Category),
            LibraryItemKind.Material => Enum.GetNames<MaterialCategory>(),
            _ => Array.Empty<string?>()
        };
        Replace(Manufacturers, manufacturers, ManufacturerFilter, v => _manufacturerFilter = v, nameof(ManufacturerFilter));
        Replace(Groups, groups, GroupFilter, v => _groupFilter = v, nameof(GroupFilter));
    }

    /// <summary>Fills a filter list with "(all)" plus the distinct values in ordinal order; keeps the choice if still offered.</summary>
    private void Replace(ObservableCollection<string> target, IEnumerable<string?> values, string current, Action<string> set, string property)
    {
        var wanted = new[] { All }.Concat(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)).ToList();
        // Change only what differs, and never take "(all)" out: emptying the list would clear the drop-down's choice
        // (it then showed nothing after a search or a change of tab).
        if (!target.SequenceEqual(wanted))
        {
            if (target.Count == 0 || target[0] != All) target.Insert(0, All);
            while (target.Count > 1) target.RemoveAt(target.Count - 1);
            foreach (var v in wanted.Skip(1)) target.Add(v);
        }
        set(target.Contains(current) ? current : All);
        OnPropertyChanged(property);
    }

    private ILibraryEditor? EditorFor(LibraryItemRow row)
    {
        var library = _library.Current;
        ILibraryEditor? editor = row.Kind switch
        {
            LibraryItemKind.Profile => library.FindProfile(row.Id) is { } p ? LibraryItemEditorViewModel.For(p, library: library) : null,
            LibraryItemKind.Glass => library.FindGlass(row.Id) is { } g ? LibraryItemEditorViewModel.For(g, library: library) : null,
            LibraryItemKind.System => library.FindSystem(row.Id) is { } x ? new SystemEditorViewModel(x, library) : null,
            LibraryItemKind.Bundle => library.FindBundle(row.Id) is { } b ? new BundleEditorViewModel(b, library) : null,
            _ => library.FindMaterial(row.Id) is { } m ? LibraryItemEditorViewModel.For(m, library: library) : null
        };
        if (editor is LibraryItemEditorViewModel item) item.CanEditDetails = !PricesOnly;
        return editor;
    }

    private string DescribeUsage(LibraryItemRow row)
    {
        try
        {
            var parts = new List<string>();
            if (row.Kind is LibraryItemKind.Profile or LibraryItemKind.Glass or LibraryItemKind.System && _projects is not null)
            {
                var saved = _projects.FindUsing(row.Kind, row.Id);
                if (saved.Count > 0) parts.Add($"Used by {saved.Count} saved project(s): {string.Join(", ", saved.Select(p => p.Name))}.");
            }
            var blockers = _library.FindBlockers(row.Kind, row.Id, _openProject());
            parts.AddRange(blockers.Where(b => !b.StartsWith("Saved project", StringComparison.Ordinal)));
            return parts.Count == 0 ? "Not used by any saved project, the open project, a system, a bundle or another product." : string.Join(" ", parts);
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
                case ProductSystem x when editor.IsNew: _library.Add(x); break;
                case ProductSystem x: _library.Update(x); break;
                case Bundle b when editor.IsNew: _library.Add(b); break;
                case Bundle b: _library.Update(b); break;
            }
            string id = editor.Id.Trim();
            if (editor.IsNew && CanEditTabs && _ownItems!.IsOwn(id))
                _ownItems.Assign(id, _newItemTab);
            Refresh(select: id);
            if (SelectedItem?.Id != id)                         // saved but filtered out: show it anyway
            {
                SearchText = null;
                ShowInactive = true;
                _selectedListTab = null;
                Refresh(select: id);
            }
            Succeed(editor.IsNew ? $"Added '{id}'." : $"Saved '{id}'. Designs using it are recalculated.");
        });
    }

    private void Delete()
    {
        if (SelectedItem is not { } row)
            return;
        if (_dialogs is not null && !_dialogs.Confirm("Delete", $"Delete '{row.Name}' ({row.Id}) from the library permanently?"))
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
