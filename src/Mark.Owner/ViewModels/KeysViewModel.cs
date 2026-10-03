using System.Collections.ObjectModel;
using System.Windows.Input;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>What a key can give, as offered in the generator.</summary>
public sealed record KeyGift(string Name, KeyTarget Target, Product? Product)
{
    public static IReadOnlyList<KeyGift> All { get; } = new[]
    {
        new KeyGift("Account validity (renewal)", KeyTarget.Account, null),
        new KeyGift("uPVC", KeyTarget.Product, Licensing.Product.Upvc),
        new KeyGift("Aluminium", KeyTarget.Product, Licensing.Product.Aluminium),
        new KeyGift("A feature (add-on)", KeyTarget.Feature, null)
    };

    public override string ToString() => Name;
}

/// <summary>A company to generate keys for; <see cref="Id"/> null = any company.</summary>
public sealed record KeyCompanyChoice(Guid? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A licence key in the list.</summary>
public sealed class KeyRow
{
    public KeyRow(LicenceKeyInfo key)
    {
        Key = key;
    }

    public LicenceKeyInfo Key { get; }

    public string KeyText => Key.Key;

    public string ForText => Key.CompanyName ?? "Any company";

    public string GivesText => Key.Target switch
    {
        KeyTarget.Account => "Account validity",
        KeyTarget.Product => Key.Product?.DisplayName() ?? "Product",
        _ => FeatureCatalog.NameOf(Key.FeatureId ?? "")
    };

    public string ValidUntilText => LicenceDates.Format(Key.ValidUntilUtc);

    public string StateText => Key.State switch
    {
        KeyState.Used => $"Used by {Key.UsedByCompany} on {OwnerText.When(Key.UsedUtc)}",
        KeyState.Revoked => "Cancelled",
        KeyState.Expired => "Expired",
        _ => "Not used yet"
    };

    public bool CanRevoke => Key.State == KeyState.Unused;

    public bool IsUnused => Key.State == KeyState.Unused;

    public string? Note => Key.Note;
}

/// <summary>
/// The Licence keys page: generate keys (for one company or any; account renewal, a product or a feature; valid for a
/// period counted from today) and the list of all keys with their state.
/// </summary>
public sealed class KeysViewModel : OwnerPage
{
    private readonly Func<DateTime> _today;

    public KeysViewModel(OwnerApiClient api, IOwnerDialogs dialogs, Action sessionEnded, Func<DateTime>? today = null)
        : base(api, dialogs, sessionEnded)
    {
        _today = today ?? (() => DateTime.Today);
        _gift = KeyGift.All[0];
        _validity = ValidityChoice.All.First(v => v.Months == 12);
        _company = CompanyChoices[0];
        GenerateCommand = new AsyncCommand(GenerateAsync);
        RefreshCommand = new AsyncCommand(LoadAsync);
        RevokeCommand = new RelayCommand(async p => await RevokeAsync(p as KeyRow));
        CopyCommand = new RelayCommand(p =>
        {
            if (p is KeyRow row) Dialogs.CopyText(row.KeyText);
            else if (p is string text) Dialogs.CopyText(text);
            Show("Copied.");
        });
    }

    public ObservableCollection<KeyRow> Keys { get; } = new();

    public ObservableCollection<KeyCompanyChoice> CompanyChoices { get; } = new() { new KeyCompanyChoice(null, "Any company") };

    public IReadOnlyList<KeyGift> Gifts => KeyGift.All;

    public IReadOnlyList<Feature> FeatureChoices { get; } = FeatureCatalog.All.Where(f => !f.IsCore).ToList();

    public IReadOnlyList<ValidityChoice> ValidityChoices => ValidityChoice.All;

    public ICommand GenerateCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RevokeCommand { get; }
    public ICommand CopyCommand { get; }

    private KeyCompanyChoice _company;
    public KeyCompanyChoice Company
    {
        get => _company;
        set => SetProperty(ref _company, value ?? CompanyChoices[0]);
    }

    private KeyGift _gift;
    public KeyGift Gift
    {
        get => _gift;
        set
        {
            if (SetProperty(ref _gift, value))
                OnPropertyChanged(nameof(IsFeatureGift));
        }
    }

    public bool IsFeatureGift => Gift.Target == KeyTarget.Feature;

    private Feature? _feature;
    public Feature? Feature
    {
        get => _feature;
        set => SetProperty(ref _feature, value);
    }

    private ValidityChoice _validity;
    /// <summary>How long what the key gives lasts, counted from today (the day the key is generated).</summary>
    public ValidityChoice Validity
    {
        get => _validity;
        set
        {
            if (SetProperty(ref _validity, value))
                OnPropertyChanged(nameof(ValidUntilText));
        }
    }

    public string ValidUntilText => $"Valid until {Validity.From(_today()):d MMM yyyy}";

    private string _countText = "1";
    public string CountText
    {
        get => _countText;
        set => SetProperty(ref _countText, value);
    }

    private string _note = "";
    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    /// <summary>The keys just generated, one per line (to copy and send).</summary>
    private string? _newKeysText;
    public string? NewKeysText
    {
        get => _newKeysText;
        private set
        {
            if (SetProperty(ref _newKeysText, value))
                OnPropertyChanged(nameof(HasNewKeys));
        }
    }

    public bool HasNewKeys => _newKeysText is not null;

    /// <summary>Companies to choose from (set by the shell after loading them).</summary>
    public void SetCompanies(IEnumerable<CompanySummary> companies)
    {
        var keep = Company.Id;
        while (CompanyChoices.Count > 1) CompanyChoices.RemoveAt(1);
        foreach (var company in companies.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            CompanyChoices.Add(new KeyCompanyChoice(company.Id, company.Name));
        Company = CompanyChoices.FirstOrDefault(c => c.Id == keep) ?? CompanyChoices[0];
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var keys = await Api.KeysAsync();
            Keys.Clear();
            foreach (var key in keys) Keys.Add(new KeyRow(key));
        });
    }

    public async Task GenerateAsync()
    {
        if (!int.TryParse(CountText, out int count) || count is < 1 or > 100)
        {
            Show("Enter how many keys to generate (1 to 100).", true);
            return;
        }
        if (IsFeatureGift && Feature is null)
        {
            Show("Choose the feature the key gives.", true);
            return;
        }

        var request = new GenerateKeysRequest(Company.Id, Gift.Target, Gift.Product, IsFeatureGift ? Feature!.Id : null,
            LicenceDates.EndOfLocalDayUtc(Validity.From(_today())), count, string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
        List<LicenceKeyInfo>? generated = null;
        if (await RunAsync(async () => generated = await Api.GenerateKeysAsync(request)))
        {
            NewKeysText = string.Join(Environment.NewLine, generated!.Select(k => k.Key));
            await LoadAsync();
            Show($"Generated {generated!.Count} key{(generated.Count == 1 ? "" : "s")}. Copy and send them to the company: " +
                 "they enter a key on MARK's Account page.");
        }
    }

    private async Task RevokeAsync(KeyRow? row)
    {
        if (row is null || !row.CanRevoke) return;
        if (!Dialogs.Confirm("Cancel key", $"Cancel the key {row.KeyText}? It can no longer be used.")) return;
        if (await RunAsync(() => Api.RevokeKeyAsync(row.Key.Id), $"Cancelled {row.KeyText}."))
        {
            string? message = Message;
            await LoadAsync();
            Show(message);
        }
    }
}
