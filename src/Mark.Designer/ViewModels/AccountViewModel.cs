using System.Windows.Input;
using Mark.Licensing;
using Mark.Licensing.Client;

namespace Mark.Designer.ViewModels;

/// <summary>A product line on the account page: "uPVC — valid until 3 Oct 2027".</summary>
public sealed record AccountProductRow(string Name, string Detail, bool IsValid);

/// <summary>A feature on the account page: included (with its date), not in the package, or coming later.</summary>
public sealed record AccountFeatureRow(string Name, string Description, string State, bool IsIncluded);

public sealed record AccountFeatureGroup(string Area, IReadOnlyList<AccountFeatureRow> Features);

/// <summary>
/// The Account page of MARK: the company, the signed-in user, products and validity, the package with every feature
/// (included or locked), licence keys, "Check now" and sign-out.
/// </summary>
public sealed class AccountViewModel : ViewModelBase
{
    private readonly LicenceManager _manager;
    private readonly Func<DateTime> _utcNow;

    /// <param name="signOut">Signs out and returns to the sign-in page (the app decides how, e.g. asking about unsaved
    /// changes first).</param>
    public AccountViewModel(LicenceManager manager, Func<Task> signOut, Func<DateTime>? utcNow = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        CheckNowCommand = new AsyncCommand(CheckNowAsync);
        RedeemKeyCommand = new AsyncCommand(RedeemKeyAsync, () => !string.IsNullOrWhiteSpace(KeyText));
        SignOutCommand = new AsyncCommand(signOut ?? throw new ArgumentNullException(nameof(signOut)));
        manager.StatusChanged += Refresh;
        Refresh();
    }

    public ICommand CheckNowCommand { get; }
    public ICommand RedeemKeyCommand { get; }
    public ICommand SignOutCommand { get; }

    public string CompanyName { get; private set; } = "";
    public string CompanyType { get; private set; } = "";
    public string UserText { get; private set; } = "";
    public string PackageName { get; private set; } = "";
    public string ValidUntilText { get; private set; } = "";
    public string ComputersText { get; private set; } = "";
    public string LastCheckText { get; private set; } = "";
    public string ServerText { get; private set; } = "";

    /// <summary>"Active", "Read-only" or "Signed out".</summary>
    public string StateText { get; private set; } = "";
    public bool IsReadOnly { get; private set; }
    public string? Notice { get; private set; }
    public bool HasNotice => Notice is not null;

    public IReadOnlyList<AccountProductRow> Products { get; private set; } = Array.Empty<AccountProductRow>();
    public IReadOnlyList<AccountFeatureGroup> FeatureGroups { get; private set; } = Array.Empty<AccountFeatureGroup>();

    private string _keyText = "";
    /// <summary>A licence key from the MARK supplier, e.g. MARK-7KQ2M-X9TPA-3HRWD-ZC4NE.</summary>
    public string KeyText
    {
        get => _keyText;
        set
        {
            if (SetProperty(ref _keyText, value))
                ((AsyncCommand)RedeemKeyCommand).RaiseCanExecuteChanged();
        }
    }

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
    public bool MessageIsError
    {
        get => _messageIsError;
        private set => SetProperty(ref _messageIsError, value);
    }

    public async Task CheckNowAsync()
    {
        var result = await _manager.CheckInAsync();
        Show(result.Message, result.Outcome != CheckInOutcome.Updated);
    }

    public async Task RedeemKeyAsync()
    {
        var (ok, message) = await _manager.RedeemKeyAsync(KeyText);
        if (ok) KeyText = "";
        Show(message, !ok);
    }

    private void Show(string message, bool isError)
    {
        MessageIsError = isError;
        Message = message;
    }

    /// <summary>Reads the licence again (after a check-in, a key, or a change of status).</summary>
    public void Refresh()
    {
        var status = _manager.Status;
        var licence = status?.Licence;
        var now = _utcNow();
        CompanyName = licence?.CompanyName ?? "";
        CompanyType = licence?.CompanyType ?? "";
        UserText = licence is null ? "" : $"{licence.UserName} ({licence.UserId})";
        PackageName = licence?.PackageName is { Length: > 0 } package ? package : "—";
        ValidUntilText = licence is null ? "" : LicenceDates.Format(licence.ValidUntilUtc);
        ComputersText = licence is null ? "" : $"Up to {licence.MaxComputers} computer{(licence.MaxComputers == 1 ? "" : "s")} · {_manager.MachineName}";
        LastCheckText = licence is null ? "" : $"Licence checked on {licence.IssuedUtc.ToLocalTime():d MMM yyyy, HH:mm}";
        ServerText = _manager.ServerUrl.TrimEnd('/');
        IsReadOnly = status?.IsReadOnly ?? true;
        StateText = status is null ? "Signed out" : status.IsReadOnly ? "Read-only" : "Active";
        Notice = status?.Reason ?? status?.Warning;

        Products = Licensing.Products.All.Select(product =>
        {
            var grant = licence?.Products.FirstOrDefault(p => p.Product == product);
            if (grant is null) return new AccountProductRow(product.DisplayName(), "Not included", false);
            bool valid = grant.ValidUntilUtc >= now;
            return new AccountProductRow(product.DisplayName(),
                (valid ? "Valid until " : "Ended on ") + LicenceDates.Format(grant.ValidUntilUtc), valid);
        }).ToList();

        var grants = licence?.Features.ToDictionary(f => f.FeatureId) ?? new Dictionary<string, FeatureGrant>();
        FeatureGroups = FeatureCatalog.Areas
            .Select(area => new AccountFeatureGroup(area, FeatureCatalog.All.Where(f => f.Area == area).Select(f =>
            {
                bool included = grants.TryGetValue(f.Id, out var grant) && (status?.Allows(f.Id) ?? false);
                string state = !f.IsBuilt ? (grants.ContainsKey(f.Id) ? "Included — coming in a later version" : "Coming in a later version")
                    : included ? (grant!.ValidUntilUtc.Date == licence!.ValidUntilUtc.Date ? "Included" : $"Included until {LicenceDates.Format(grant.ValidUntilUtc)}")
                    : grant is not null ? $"Ended on {LicenceDates.Format(grant.ValidUntilUtc)}"
                    : "Not in your package";
                return new AccountFeatureRow(f.Name, f.Description, state, included && f.IsBuilt);
            }).ToList()))
            .ToList();

        OnPropertyChanged(string.Empty);
    }
}
