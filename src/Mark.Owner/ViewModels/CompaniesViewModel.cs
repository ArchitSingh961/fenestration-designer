using System.Collections.ObjectModel;
using System.Windows.Input;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>A company in the list: name, type, products, package, computers in use, validity and state.</summary>
public sealed class CompanyRow
{
    public CompanyRow(CompanySummary summary, DateTime nowUtc)
    {
        Summary = summary;
        StateText = OwnerText.StateOf(summary.Suspended, summary.ValidUntilUtc, nowUtc);
        IsWarning = StateText.StartsWith("Ends", StringComparison.Ordinal);
        IsBad = StateText is "Suspended" or "Expired";
    }

    public CompanySummary Summary { get; }

    public Guid Id => Summary.Id;

    public string Name => Summary.Name;

    public string Initials => string.Concat(Summary.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    public string TypeText => Summary.CompanyType ?? "";

    public string ProductsText => string.Join(" + ", Summary.Products.Where(p => !p.Suspended).Select(p => p.Product.DisplayName()));

    public string PackageText => Summary.PackageName ?? "—";

    public string ComputersText => $"{Summary.ComputersUsed} / {Summary.MaxComputers}";

    public string ValidUntilText => LicenceDates.Format(Summary.ValidUntilUtc);

    public string LastCheckInText => OwnerText.When(Summary.LastCheckInUtc);

    public string UserIdText => Summary.OwnerUserId;

    public string StateText { get; }

    public bool IsWarning { get; }

    public bool IsBad { get; }
}

/// <summary>
/// The Companies page of MARK Owner: every client company at a glance, search, and the account editor (new account,
/// change, suspend or reactivate, free a computer, delete).
/// </summary>
public sealed class CompaniesViewModel : OwnerPage
{
    private readonly Func<IReadOnlyList<PackageInfo>> _packages;
    private readonly Func<IReadOnlyList<CompanyTypeInfo>> _types;
    private readonly Func<Mark.Core.Library.ProductLibrary?> _catalogue;
    private readonly Func<DateTime> _utcNow;
    private List<CompanyRow> _all = new();

    public CompaniesViewModel(OwnerApiClient api, IOwnerDialogs dialogs, Action sessionEnded,
        Func<IReadOnlyList<PackageInfo>> packages, Func<IReadOnlyList<CompanyTypeInfo>> types, Func<DateTime>? utcNow = null,
        Func<Mark.Core.Library.ProductLibrary?>? catalogue = null)
        : base(api, dialogs, sessionEnded)
    {
        _packages = packages;
        _types = types;
        _catalogue = catalogue ?? (() => null);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        NewAccountCommand = new RelayCommand(NewAccount);
        RefreshCommand = new AsyncCommand(LoadAsync);
        SaveCommand = new AsyncCommand(SaveAsync, () => Editor is not null);
        CancelCommand = new RelayCommand(() => Editor = null, () => Editor is not null);
        ToggleSuspendCommand = new AsyncCommand(ToggleSuspendAsync, () => Editor is { IsNew: false });
        DeleteCommand = new AsyncCommand(DeleteAsync, () => Editor is { IsNew: false });
        FreeComputerCommand = new RelayCommand(async p => await FreeComputerAsync(p as ComputerInfo));
        RemoveStaffCommand = new RelayCommand(async p => await RemoveStaffAsync(p as OwnerStaffRow));
        ChooseLogoCommand = new RelayCommand(ChooseLogo, () => Editor is not null);
        RemoveLogoCommand = new RelayCommand(() => Editor!.LogoBase64 = null, () => Editor?.HasLogo ?? false);
    }

    public ObservableCollection<CompanyRow> Companies { get; } = new();

    public ICommand NewAccountCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ToggleSuspendCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand FreeComputerCommand { get; }
    public ICommand RemoveStaffCommand { get; }
    public ICommand ChooseLogoCommand { get; }
    public ICommand RemoveLogoCommand { get; }

    /// <summary>"12 companies · 9 active · 1 suspended · 2 expired".</summary>
    public string SummaryText { get; private set; } = "";

    private string _search = "";
    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
                ApplyFilter();
        }
    }

    private CompanyRow? _selected;
    public CompanyRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value) || value is null) return;
            _ = OpenAsync(value.Id);
        }
    }

    private CompanyEditorViewModel? _editor;
    /// <summary>The account being created or changed (null: none open).</summary>
    public CompanyEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (!SetProperty(ref _editor, value)) return;
            OnPropertyChanged(nameof(HasEditor));
            OnPropertyChanged(nameof(SuspendText));
            foreach (var command in new[] { SaveCommand, ToggleSuspendCommand, DeleteCommand })
                ((AsyncCommand)command).RaiseCanExecuteChanged();
            foreach (var command in new[] { CancelCommand, ChooseLogoCommand, RemoveLogoCommand })
                ((RelayCommand)command).RaiseCanExecuteChanged();
        }
    }

    public bool HasEditor => _editor is not null;

    public string SuspendText => Editor?.IsSuspended == true ? "Reactivate" : "Suspend";

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var companies = await Api.CompaniesAsync();
            var now = _utcNow();
            _all = companies.Select(c => new CompanyRow(c, now)).ToList();
            int active = _all.Count(c => c.StateText == "Active" || c.IsWarning);
            int suspended = _all.Count(c => c.StateText == "Suspended");
            int expired = _all.Count(c => c.StateText == "Expired");
            SummaryText = $"{_all.Count} compan{(_all.Count == 1 ? "y" : "ies")} · {active} active · {suspended} suspended · {expired} expired";
            OnPropertyChanged(nameof(SummaryText));
            ApplyFilter();
        });
    }

    private void ApplyFilter()
    {
        string text = Search.Trim();
        var keep = _selected?.Id;
        Companies.Clear();
        foreach (var row in _all.Where(r => text.Length == 0
                                            || r.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                                            || r.UserIdText.Contains(text, StringComparison.OrdinalIgnoreCase)
                                            || r.TypeText.Contains(text, StringComparison.OrdinalIgnoreCase)))
            Companies.Add(row);
        _selected = Companies.FirstOrDefault(c => c.Id == keep);
        OnPropertyChanged(nameof(Selected));
    }

    private void NewAccount()
    {
        _selected = null;
        OnPropertyChanged(nameof(Selected));
        Show(null);
        Editor = new CompanyEditorViewModel(null, _packages(), _types(), catalogue: _catalogue());
    }

    public async Task OpenAsync(Guid id)
    {
        await RunAsync(async () =>
        {
            var detail = await Api.CompanyAsync(id);
            Editor = new CompanyEditorViewModel(detail, _packages(), _types(), catalogue: _catalogue());
            Show(null);
        });
    }

    public async Task SaveAsync()
    {
        if (Editor is not { } editor) return;
        if (editor.ToEdit(out string? error) is not { } edit)
        {
            Show(error, true);
            return;
        }

        CompanyDetail? saved = null;
        bool ok = await RunAsync(async () =>
        {
            saved = editor.IsNew
                ? await Api.CreateCompanyAsync(edit)
                : await Api.UpdateCompanyAsync(editor.Existing!.Id, edit);
        });
        if (!ok || saved is null) return;

        await LoadAsync();
        _selected = Companies.FirstOrDefault(c => c.Id == saved.Id);
        OnPropertyChanged(nameof(Selected));
        Editor = new CompanyEditorViewModel(saved, _packages(), _types(), catalogue: _catalogue());
        Show(editor.IsNew
            ? $"Created the account of {saved.Name}. They sign in to MARK with the User ID \"{saved.OwnerUserId}\" and the password you set."
            : $"Saved {saved.Name}. Their computers get the changes at the next check-in.");
    }

    private async Task ToggleSuspendAsync()
    {
        if (Editor?.Existing is not { } company) return;
        bool suspend = !company.Suspended;
        if (suspend && !Dialogs.Confirm("Suspend account",
                $"Suspend {company.Name}? MARK becomes read-only on their computers at the next check-in (within a few hours " +
                "when they are online). You can reactivate it at any time."))
            return;
        CompanyDetail? updated = null;
        if (await RunAsync(async () => updated = await Api.SetSuspendedAsync(company.Id, suspend),
                suspend ? $"Suspended {company.Name}." : $"Reactivated {company.Name}."))
        {
            string message = Message ?? "";
            await LoadAsync();
            Editor = new CompanyEditorViewModel(updated!, _packages(), _types(), catalogue: _catalogue());
            Show(message);
        }
    }

    private async Task DeleteAsync()
    {
        if (Editor?.Existing is not { } company) return;
        if (!Dialogs.Confirm("Delete account",
                $"Delete the account of {company.Name} permanently? Its User ID stops working, its computers are signed out and " +
                "its unused licence keys are cancelled. Their quotes stay on their own computers."))
            return;
        if (await RunAsync(() => Api.DeleteCompanyAsync(company.Id)))
        {
            Editor = null;
            await LoadAsync();
            Show($"Deleted the account of {company.Name}.");
        }
    }

    private async Task FreeComputerAsync(ComputerInfo? computer)
    {
        if (computer is null || Editor?.Existing is not { } company) return;
        if (!Dialogs.Confirm("Free computer",
                $"Sign out \"{computer.Name}\"? It stops counting towards the {company.MaxComputers} computer(s) allowed, and MARK " +
                "there must sign in again."))
            return;
        CompanyDetail? updated = null;
        if (await RunAsync(async () => updated = await Api.FreeComputerAsync(company.Id, computer.Id), $"Freed \"{computer.Name}\"."))
        {
            string message = Message ?? "";
            await LoadAsync();
            Editor = new CompanyEditorViewModel(updated!, _packages(), _types(), catalogue: _catalogue());
            Show(message);
        }
    }

    /// <summary>Removes a staff login of the company (its account owner adds them in MARK).</summary>
    private async Task RemoveStaffAsync(OwnerStaffRow? row)
    {
        if (row is null || Editor?.Existing is not { } company) return;
        if (!Dialogs.Confirm("Remove staff login",
                $"Remove the login of {row.Info.Name} ({row.Info.UserId}) from {company.Name}? MARK on their computers is signed out at its " +
                "next check-in."))
            return;
        CompanyDetail? updated = null;
        if (await RunAsync(async () => updated = await Api.RemoveStaffAsync(company.Id, row.Info.Id), $"Removed the login of {row.Info.Name}."))
        {
            string message = Message ?? "";
            await LoadAsync();
            Editor = new CompanyEditorViewModel(updated!, _packages(), _types(), catalogue: _catalogue());
            Show(message);
        }
    }

    private void ChooseLogo()
    {
        if (Editor is null || Dialogs.ChooseImageFile() is not { } path) return;
        Show(Editor.LoadLogo(path), true);
        ((RelayCommand)RemoveLogoCommand).RaiseCanExecuteChanged();
    }
}
