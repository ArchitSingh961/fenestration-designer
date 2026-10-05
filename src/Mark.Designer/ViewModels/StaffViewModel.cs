using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;

namespace Mark.Designer.ViewModels;

/// <summary>A staff login in the list: "Amit Kumar (amit) · Production · Active, last signed in 3 Oct 2026".</summary>
public sealed record StaffRow(StaffInfo Info, string Name, string UserId, string AreasText, string StateText, bool IsDisabled)
{
    public override string ToString() => $"{Name} ({UserId})";
}

/// <summary>A feature the account owner can give a staff login.</summary>
public sealed class StaffFeatureChoice : ViewModelBase
{
    public StaffFeatureChoice(Feature feature, bool isChecked)
    {
        Feature = feature;
        _isChecked = isChecked;
    }

    public Feature Feature { get; }

    public string Id => Feature.Id;

    public string Name => Feature.Name;

    /// <summary>"Cutting plans — Stock bars, cut lengths…" or "… (coming in a later version)".</summary>
    public string Description => Feature.IsBuilt ? Feature.Description : $"{Feature.Description} (coming in a later version)";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>The features of one area on the staff editor.</summary>
public sealed record StaffFeatureGroup(string Area, IReadOnlyList<StaffFeatureChoice> Features);

/// <summary>A ready-made set of features for a kind of job (the account owner can change the ticks after choosing it).</summary>
public sealed record StaffPreset(string Name, string Description, IReadOnlyList<string> Features);

/// <summary>A new staff login, or the changes to one.</summary>
public sealed class StaffEditorViewModel : ViewModelBase
{
    public StaffEditorViewModel(StaffInfo? existing, IReadOnlyList<Feature> companyFeatures)
    {
        Existing = existing;
        _name = existing?.Name ?? "";
        _userId = existing?.UserId ?? "";
        _isDisabled = existing?.Disabled ?? false;
        var given = existing?.Permissions.ToHashSet() ?? new HashSet<string>();
        Groups = companyFeatures.GroupBy(f => f.Area)
            .OrderBy(g => FeatureCatalog.Areas.ToList().IndexOf(g.Key))
            .Select(g => new StaffFeatureGroup(g.Key, g.Select(f => new StaffFeatureChoice(f, given.Contains(f.Id))).ToList()))
            .ToList();
    }

    public StaffInfo? Existing { get; }

    public bool IsNew => Existing is null;

    public string Title => IsNew ? "New staff login" : $"Edit {Existing!.Name}";

    /// <summary>"Password *" for a new login, "New password (leave empty to keep)" for an existing one.</summary>
    public string PasswordLabel => IsNew ? "Password *" : "New password (leave empty to keep the current one)";

    public IReadOnlyList<StaffFeatureGroup> Groups { get; }

    public IEnumerable<StaffFeatureChoice> Choices => Groups.SelectMany(g => g.Features);

    private string _name;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _userId;
    public string UserId { get => _userId; set => SetProperty(ref _userId, value); }

    private string _password = "";
    public string Password { get => _password; set => SetProperty(ref _password, value); }

    private bool _isDisabled;
    /// <summary>Turned off: cannot sign in, and does not count towards the users allowed.</summary>
    public bool IsDisabled { get => _isDisabled; set => SetProperty(ref _isDisabled, value); }

    /// <summary>Ticks exactly the preset's features (those the company has).</summary>
    public void Apply(StaffPreset preset)
    {
        foreach (var choice in Choices)
            choice.IsChecked = preset.Features.Contains(choice.Id);
    }

    /// <summary>What is missing before saving, or null.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Enter the person's name.";
        if (string.IsNullOrWhiteSpace(UserId)) return "Enter a User ID for this person to sign in with.";
        if (IsNew && string.IsNullOrEmpty(Password)) return "Enter a password for the new login.";
        if (!string.IsNullOrEmpty(Password) && PasswordHasher.Check(Password) is { } weak) return weak;
        if (!Choices.Any(c => c.IsChecked)) return "Tick at least one thing this person may use.";
        return null;
    }

    public StaffEdit ToEdit() => new(Existing?.Id ?? Guid.Empty, Name.Trim(), UserId.Trim(),
        string.IsNullOrEmpty(Password) ? null : Password, Choices.Where(c => c.IsChecked).Select(c => c.Id).ToList(), IsDisabled);
}

/// <summary>
/// The Staff page (Milestone 14), for the account owner: staff logins with their own User ID and password and the
/// parts of MARK each may use, up to the number of users the MARK supplier allows. Changes are made on the licence
/// server and reach a staff member's MARK at its next check-in (or when they sign in).
/// </summary>
public sealed class StaffViewModel : ViewModelBase
{
    private readonly LicenceManager _manager;
    private readonly Func<string, string, bool>? _confirm;
    private StaffList? _list;

    /// <param name="confirm">Asks a yes/no question (title, message); null: no questions (tests).</param>
    public StaffViewModel(LicenceManager manager, Func<string, string, bool>? confirm = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _confirm = confirm;
        AddCommand = new RelayCommand(() => Edit(null));
        EditCommand = new RelayCommand(p => { if (p is StaffRow row) Edit(row.Info); });
        PresetCommand = new RelayCommand(p => { if (p is StaffPreset preset) Editor?.Apply(preset); });
        CancelCommand = new RelayCommand(() => Editor = null);
        SaveCommand = new AsyncCommand(SaveAsync, () => Editor is not null);
        DeleteCommand = new AsyncCommand(DeleteAsync, () => Editor is { IsNew: false });
        RefreshCommand = new AsyncCommand(LoadAsync);
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand PresetCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RefreshCommand { get; }

    public ObservableCollection<StaffRow> Staff { get; } = new();

    public bool HasStaff => Staff.Count > 0;

    /// <summary>"3 of 5 logins in use (you and 2 staff)".</summary>
    public string UsersText => _list is null ? ""
        : $"{_list.UsersInUse} of {_list.MaxUsers} login{(_list.MaxUsers == 1 ? "" : "s")} in use (you and {_list.UsersInUse - 1} staff)";

    /// <summary>Every login allowed is in use: a new login can only be added turned off.</summary>
    public bool IsFull => _list is not null && _list.UsersInUse >= _list.MaxUsers;

    /// <summary>The account's features, which staff can be given.</summary>
    public IReadOnlyList<Feature> CompanyFeatures
    {
        get
        {
            var granted = _manager.Licence?.Features.Select(f => f.FeatureId).ToHashSet() ?? new HashSet<string>();
            return FeatureCatalog.All.Where(f => granted.Contains(f.Id)).ToList();
        }
    }

    /// <summary>Ready-made sets: Sales, Design, Pricing, Production and Everything.</summary>
    public IReadOnlyList<StaffPreset> Presets { get; } = new[]
    {
        new StaffPreset("Sales", "Quotes and clients, with design and price",
            new[] { Features.Quotes, Features.Enquiries, Features.QuotationPdf, Features.SalesCharts, Features.Drawing, Features.Openings,
                Features.DesignLibrary, Features.Costing, Features.PriceStructure }),
        new StaffPreset("Design", "Drawing windows and doors",
            new[] { Features.Drawing, Features.Openings, Features.DesignLibrary, Features.ProjectFiles }),
        new StaffPreset("Pricing", "Price structure, bill of materials and prices",
            new[] { Features.Costing, Features.PriceStructure, Features.LibraryManager }),
        new StaffPreset("Production", "Cutting plans and production",
            new[] { Features.CuttingPlans, Features.ProductionOrders }),
        new StaffPreset("Orders", "Payments, dispatch, installation and sign-off",
            new[] { Features.Quotes, Features.OrderManagement }),
        new StaffPreset("Stores", "Stock, purchase orders, suppliers and goods received",
            new[] { Features.Inventory, Features.Purchasing, Features.ProductionOrders }),
        new StaffPreset("Everything", "Everything in the account",
            FeatureCatalog.All.Select(f => f.Id).ToList())
    };

    private StaffEditorViewModel? _editor;
    /// <summary>The login being added or changed, or null.</summary>
    public StaffEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (!SetProperty(ref _editor, value)) return;
            OnPropertyChanged(nameof(HasEditor));
            ((AsyncCommand)SaveCommand).RaiseCanExecuteChanged();
            ((AsyncCommand)DeleteCommand).RaiseCanExecuteChanged();
        }
    }

    public bool HasEditor => _editor is not null;

    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            SetProperty(ref _message, value);
            OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message is not null;

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>Reads the staff from the licence server (when the page is shown).</summary>
    public void Reload() => _ = LoadAsync();

    public async Task LoadAsync()
    {
        IsBusy = true;
        var (list, error) = await _manager.StaffAsync();
        IsBusy = false;
        if (list is null)
        {
            Show(error ?? "The staff logins could not be read.", true);
            return;
        }
        Use(list);
        if (MessageIsError) Message = null;
    }

    private void Edit(StaffInfo? existing)
    {
        Editor = new StaffEditorViewModel(existing, CompanyFeatures);
        Message = null;
        if (existing is null && IsFull)
        {
            Editor.IsDisabled = true;
            Show($"All {_list!.MaxUsers} logins of your account are in use, so this login is turned off until one is free. " +
                 "Ask your MARK supplier if you need more.", false);
        }
    }

    public async Task SaveAsync()
    {
        if (Editor is not { } editor) return;
        if (editor.Problem() is { } problem)
        {
            Show(problem, true);
            return;
        }
        IsBusy = true;
        var (list, error) = await _manager.SaveStaffAsync(editor.ToEdit());
        IsBusy = false;
        if (list is null)
        {
            Show(error ?? "The staff login could not be saved.", true);
            return;
        }
        Use(list);
        Editor = null;
        Show(editor.IsNew
            ? $"Added {editor.Name.Trim()}. They can now sign in to MARK with the User ID \"{editor.UserId.Trim()}\" and the password you set."
            : $"Saved {editor.Name.Trim()}. The change reaches their MARK at its next check-in (or when they sign in).", false);
    }

    public async Task DeleteAsync()
    {
        if (Editor?.Existing is not { } staff) return;
        if (_confirm is not null && !_confirm("Remove staff login",
                $"Remove the login of {staff.Name} ({staff.UserId})? MARK on their computers is signed out at its next check-in. " +
                "Quotes they made stay as they are."))
            return;
        IsBusy = true;
        var (list, error) = await _manager.DeleteStaffAsync(staff.Id);
        IsBusy = false;
        if (list is null)
        {
            Show(error ?? "The staff login could not be removed.", true);
            return;
        }
        Use(list);
        Editor = null;
        Show($"Removed the login of {staff.Name}.", false);
    }

    private void Use(StaffList list)
    {
        _list = list;
        Staff.Clear();
        foreach (var s in list.Staff)
            Staff.Add(new StaffRow(s, s.Name, s.UserId, AreasOf(s.Permissions), StateOf(s), s.Disabled));
        OnPropertyChanged(nameof(HasStaff));
        OnPropertyChanged(nameof(UsersText));
        OnPropertyChanged(nameof(IsFull));
    }

    /// <summary>"Sales, Design" — the areas with at least one feature given.</summary>
    public static string AreasOf(IReadOnlyList<string> permissions)
    {
        var areas = FeatureCatalog.Areas.Where(a => permissions.Any(p => FeatureCatalog.Find(p)?.Area == a)).ToList();
        return areas.Count == 0 ? "Nothing" : string.Join(", ", areas);
    }

    private static string StateOf(StaffInfo s)
    {
        if (s.Disabled) return "Turned off";
        string signedIn = s.LastSignInUtc is { } last
            ? "last signed in " + last.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : "not signed in yet";
        return s.Computers > 0 ? $"Active · on {s.Computers} computer{(s.Computers == 1 ? "" : "s")} · {signedIn}" : $"Active · {signedIn}";
    }

    private void Show(string message, bool isError)
    {
        MessageIsError = isError;
        Message = message;
    }
}
