using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mark.Core.Library;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>A product line in the account editor: included or not, valid until, suspended.</summary>
public sealed class ProductEditRow : ViewModelBase
{
    private readonly Func<DateTime> _today;

    public ProductEditRow(Product product, Func<DateTime> today)
    {
        Product = product;
        _today = today;
        ExtendCommand = new RelayCommand(p =>
        {
            if (p is not ValidityChoice choice) return;
            var from = ValidUntil is { } until && until > _today() ? until : _today();
            ValidUntil = choice.From(from);
            IsIncluded = true;
        });
    }

    public Product Product { get; }

    public string Name => Product.DisplayName();

    private bool _isIncluded;
    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    private DateTime? _validUntil;
    /// <summary>The last local day it works.</summary>
    public DateTime? ValidUntil
    {
        get => _validUntil;
        set => SetProperty(ref _validUntil, value?.Date);
    }

    private bool _isSuspended;
    public bool IsSuspended
    {
        get => _isSuspended;
        set => SetProperty(ref _isSuspended, value);
    }

    /// <summary>Extends by a <see cref="ValidityChoice"/> from the later of today and the current end.</summary>
    public ICommand ExtendCommand { get; }
}

/// <summary>
/// A feature in the account editor. In the package: ticked (untick to remove it for this company). Not in the
/// package: tick to give it as an add-on with its own end date. Core features cannot be changed.
/// </summary>
public sealed class FeatureEditRow : ViewModelBase
{
    public FeatureEditRow(Feature feature)
    {
        Feature = feature;
    }

    public Feature Feature { get; }

    public string Name => Feature.Name;

    public string Description => Feature.IsBuilt ? Feature.Description : $"{Feature.Description} (coming in a later version)";

    public bool CanChange => !Feature.IsCore;

    private bool _inPackage;
    public bool InPackage
    {
        get => _inPackage;
        set
        {
            if (SetProperty(ref _inPackage, value))
                OnStateChanged();
        }
    }

    private bool _isIncluded;
    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (SetProperty(ref _isIncluded, value))
                OnStateChanged();
        }
    }

    private DateTime? _validUntil;
    /// <summary>The add-on's last local day (only for add-ons).</summary>
    public DateTime? ValidUntil
    {
        get => _validUntil;
        set => SetProperty(ref _validUntil, value?.Date);
    }

    public bool IsAddOn => IsIncluded && !InPackage;

    public bool IsRemoved => InPackage && !IsIncluded;

    /// <summary>"Always included", "In package", "Removed", "Add-on" or "".</summary>
    public string Tag => Feature.IsCore ? "Always included"
        : InPackage ? (IsIncluded ? "In package" : "Removed")
        : IsIncluded ? "Add-on" : "";

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsAddOn));
        OnPropertyChanged(nameof(IsRemoved));
        OnPropertyChanged(nameof(Tag));
    }
}

public sealed record FeatureEditGroup(string Area, IReadOnlyList<FeatureEditRow> Features);

/// <summary>
/// A company account being created or changed in MARK Owner: name and logo, company type, the owner's User ID and
/// password, products with their validity, package and account validity, features (removed or added on), the number
/// of computers and notes. <see cref="ToEdit"/> turns it into the request for the server.
/// </summary>
/// <summary>A staff login of the company, as the admin sees it: "Amit Kumar (amit)" · "Production".</summary>
public sealed record OwnerStaffRow(StaffInfo Info, string Name, string Detail);

public sealed class CompanyEditorViewModel : ViewModelBase
{
    private readonly Func<DateTime> _today;
    private readonly List<FeatureEditRow> _features;

    /// <param name="existing">The account to change, or null for a new one.</param>
    /// <param name="today">Today's local date (tests pass a fixed one).</param>
    /// <param name="catalogue">The published master catalogue (null: none yet; the company keeps its own library).</param>
    public CompanyEditorViewModel(CompanyDetail? existing, IReadOnlyList<PackageInfo> packages, IReadOnlyList<CompanyTypeInfo> types,
        Func<DateTime>? today = null, ProductLibrary? catalogue = null)
    {
        _today = today ?? (() => DateTime.Today);
        _catalogue = catalogue;
        if (catalogue is not null)
            CatalogueChoice = new CatalogueChoiceViewModel(catalogue, existing?.Catalogue);
        Packages = packages;
        CompanyTypes = types;
        Existing = existing;
        Products = Licensing.Products.All.Select(p => new ProductEditRow(p, _today)).ToList();
        _features = FeatureCatalog.All.Select(f => new FeatureEditRow(f)).ToList();
        FeatureGroups = FeatureCatalog.Areas
            .Select(area => new FeatureEditGroup(area, _features.Where(f => f.Feature.Area == area).ToList()))
            .ToList();
        ExtendCommand = new RelayCommand(p =>
        {
            if (p is not ValidityChoice choice) return;
            var from = ValidUntil is { } until && until > _today() ? until : _today();
            ValidUntil = choice.From(from);
        });

        if (existing is null)
        {
            _maxComputersText = "1";
            _maxUsersText = "3";
            ApplyType(types.FirstOrDefault());
            _companyType = types.FirstOrDefault();
        }
        else
        {
            Load(existing);
        }
    }

    public CompanyDetail? Existing { get; }

    private readonly ProductLibrary? _catalogue;

    private CatalogueChoiceViewModel? _catalogueChoice;
    /// <summary>The systems and items the company gets from the catalogue; null when no catalogue is published.</summary>
    public CatalogueChoiceViewModel? CatalogueChoice
    {
        get => _catalogueChoice;
        private set
        {
            if (SetProperty(ref _catalogueChoice, value))
                OnPropertyChanged(nameof(HasCatalogue));
        }
    }

    public bool HasCatalogue => _catalogueChoice is not null;

    public bool IsNew => Existing is null;

    public string Title => IsNew ? "New account" : Existing!.Name;

    public IReadOnlyList<PackageInfo> Packages { get; }

    public IReadOnlyList<CompanyTypeInfo> CompanyTypes { get; }

    public IReadOnlyList<ValidityChoice> ValidityChoices => ValidityChoice.All;

    public IReadOnlyList<ProductEditRow> Products { get; }

    public IReadOnlyList<FeatureEditGroup> FeatureGroups { get; }

    public IReadOnlyList<ComputerInfo> Computers => Existing?.Computers ?? Array.Empty<ComputerInfo>();

    public bool HasComputers => Computers.Count > 0;

    /// <summary>The company's staff logins (added by its account owner in MARK).</summary>
    public IReadOnlyList<OwnerStaffRow> Staff => (Existing?.Staff ?? Array.Empty<StaffInfo>())
        .Select(s => new OwnerStaffRow(s, $"{s.Name} ({s.UserId})",
            Mark.Designer.ViewModels.StaffViewModel.AreasOf(s.Permissions) + (s.Disabled ? " · turned off" : "")))
        .ToList();

    public bool HasStaff => Existing?.Staff is { Count: > 0 };

    /// <summary>"2 of 3 logins in use: the account owner and 1 staff."</summary>
    public string UsersInUseText
    {
        get
        {
            if (Existing is null) return "";
            int staff = Existing.Staff?.Count(s => !s.Disabled) ?? 0;
            return $"{staff + 1} of {Existing.MaxUsers} logins in use: the account owner and {staff} staff.";
        }
    }

    public bool IsSuspended => Existing?.Suspended ?? false;

    /// <summary>Extends the account by a <see cref="ValidityChoice"/> from the later of today and the current end.</summary>
    public ICommand ExtendCommand { get; }

    // ── Company ─────────────────────────────────────────────────────

    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string? _logoBase64;
    public string? LogoBase64
    {
        get => _logoBase64;
        set
        {
            if (!SetProperty(ref _logoBase64, value)) return;
            Logo = DecodeImage(value);
            OnPropertyChanged(nameof(Logo));
            OnPropertyChanged(nameof(HasLogo));
        }
    }

    public ImageSource? Logo { get; private set; }

    public bool HasLogo => Logo is not null;

    private CompanyTypeInfo? _companyType;
    /// <summary>The kind of company. Choosing one for a new account fills in its products, package and validity.</summary>
    public CompanyTypeInfo? CompanyType
    {
        get => _companyType;
        set
        {
            if (!SetProperty(ref _companyType, value)) return;
            if (IsNew) ApplyType(value);
        }
    }

    private string _notes = "";
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    // ── Sign-in ─────────────────────────────────────────────────────

    private string _ownerName = "";
    public string OwnerName
    {
        get => _ownerName;
        set => SetProperty(ref _ownerName, value);
    }

    private string _ownerUserId = "";
    public string OwnerUserId
    {
        get => _ownerUserId;
        set => SetProperty(ref _ownerUserId, value);
    }

    private string _password = "";
    /// <summary>The password for a new account; for an existing one a new password (empty: unchanged).</summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string PasswordLabel => IsNew ? "Password *" : "New password (leave empty to keep it)";

    // ── Package and limits ──────────────────────────────────────────

    private PackageInfo? _package;
    public PackageInfo? Package
    {
        get => _package;
        set
        {
            if (SetProperty(ref _package, value))
                UpdatePackageFeatures(keepRemovals: false);
        }
    }

    private DateTime? _validUntil;
    /// <summary>The account's (package's) last local day.</summary>
    public DateTime? ValidUntil
    {
        get => _validUntil;
        set => SetProperty(ref _validUntil, value?.Date);
    }

    private string _maxComputersText = "1";
    public string MaxComputersText
    {
        get => _maxComputersText;
        set => SetProperty(ref _maxComputersText, value);
    }

    private string _maxUsersText = "1";
    /// <summary>How many people may have a login: the account owner and the staff they add.</summary>
    public string MaxUsersText
    {
        get => _maxUsersText;
        set => SetProperty(ref _maxUsersText, value);
    }

    // ── Loading and saving ──────────────────────────────────────────

    private void ApplyType(CompanyTypeInfo? type)
    {
        if (type is null) return;
        var until = _today().AddDays(type.ValidityDays);
        foreach (var row in Products)
        {
            row.IsIncluded = type.Products.Contains(row.Product);
            row.ValidUntil = row.IsIncluded ? until : null;
            row.IsSuspended = false;
        }
        ValidUntil = until;
        Package = Packages.FirstOrDefault(p => p.Id == type.PackageId) ?? Package ?? Packages.FirstOrDefault();
        if (_catalogue is not null)
            CatalogueChoice = new CatalogueChoiceViewModel(_catalogue, type.Catalogue);
    }

    private void Load(CompanyDetail c)
    {
        _name = c.Name;
        LogoBase64 = c.LogoBase64;
        _companyType = CompanyTypes.FirstOrDefault(t => t.Id == c.CompanyTypeId);
        _notes = c.Notes ?? "";
        _ownerName = c.OwnerName;
        _ownerUserId = c.OwnerUserId;
        _validUntil = LicenceDates.LocalDate(c.ValidUntilUtc);
        _maxComputersText = c.MaxComputers.ToString();
        _maxUsersText = c.MaxUsers.ToString();
        foreach (var row in Products)
        {
            var product = c.Products.FirstOrDefault(p => p.Product == row.Product);
            row.IsIncluded = product is not null;
            row.ValidUntil = product is null ? null : LicenceDates.LocalDate(product.ValidUntilUtc);
            row.IsSuspended = product?.Suspended ?? false;
        }

        _package = Packages.FirstOrDefault(p => p.Id == c.PackageId);
        UpdatePackageFeatures(keepRemovals: false);
        foreach (var row in _features)
        {
            if (c.RemovedFeatures.Contains(row.Feature.Id) && row.InPackage && row.CanChange)
                row.IsIncluded = false;
            if (c.AddOns.FirstOrDefault(a => a.FeatureId == row.Feature.Id) is { } addOn && !row.InPackage)
            {
                row.IsIncluded = true;
                row.ValidUntil = LicenceDates.LocalDate(addOn.ValidUntilUtc);
            }
        }
    }

    /// <summary>
    /// After the package changed: its features are ticked; add-ons not in it stay add-ons; add-ons now in the package
    /// become package features.
    /// </summary>
    private void UpdatePackageFeatures(bool keepRemovals)
    {
        var inPackage = new HashSet<string>(FeatureCatalog.CoreIds.Concat(_package?.Features ?? Array.Empty<string>()));
        foreach (var row in _features)
        {
            bool wasAddOn = row.IsAddOn;
            bool wasRemoved = row.IsRemoved;
            row.InPackage = inPackage.Contains(row.Feature.Id);
            if (row.InPackage) row.IsIncluded = !(keepRemovals && wasRemoved) || !row.CanChange;
            else row.IsIncluded = wasAddOn;
            if (row.InPackage) row.ValidUntil = null;
        }
    }

    /// <summary>The request for the server, or null with the reason something is missing.</summary>
    public CompanyEdit? ToEdit(out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(Name)) error = "Enter the company name.";
        else if (string.IsNullOrWhiteSpace(OwnerUserId)) error = "Enter the User ID the company signs in with.";
        else if (IsNew && string.IsNullOrEmpty(Password)) error = "Enter the password the company signs in with.";
        else if (!string.IsNullOrEmpty(Password) && PasswordHasher.Check(Password) is { } weak) error = weak;
        else if (!Products.Any(p => p.IsIncluded)) error = "Choose at least one product (uPVC or Aluminium).";
        else if (Products.FirstOrDefault(p => p.IsIncluded && p.ValidUntil is null) is { } undated) error = $"Choose until when {undated.Name} is valid.";
        else if (Package is null) error = "Choose a package.";
        else if (ValidUntil is null) error = "Choose until when the account is valid.";
        else if (!int.TryParse(MaxComputersText, out int max) || max is < 1 or > 1000) error = "Enter the number of computers (1 to 1000).";
        else if (!int.TryParse(MaxUsersText, out int users) || users is < 1 or > 1000) error = "Enter the number of users (1 to 1000).";
        if (error is not null) return null;

        var accountUntil = LicenceDates.EndOfLocalDayUtc(ValidUntil!.Value);
        return new CompanyEdit(
            Name.Trim(),
            LogoBase64,
            CompanyType?.Id,
            OwnerName.Trim(),
            OwnerUserId.Trim(),
            string.IsNullOrEmpty(Password) ? null : Password,
            Products.Where(p => p.IsIncluded)
                .Select(p => new ProductLicence(p.Product, LicenceDates.EndOfLocalDayUtc(p.ValidUntil!.Value), p.IsSuspended))
                .ToList(),
            Package!.Id,
            accountUntil,
            int.Parse(MaxComputersText),
            _features.Where(f => f.IsAddOn)
                .Select(f => new AddOn(f.Feature.Id, f.ValidUntil is { } until ? LicenceDates.EndOfLocalDayUtc(until) : accountUntil))
                .ToList(),
            _features.Where(f => f.IsRemoved && f.CanChange).Select(f => f.Feature.Id).ToList(),
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            CatalogueChoice?.ToCatalogue() ?? Existing?.Catalogue,
            int.Parse(MaxUsersText));
    }

    /// <summary>Reads an image file for the logo; returns an error message, or null.</summary>
    public string? LoadLogo(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 300 * 1024)
                return $"The logo is too large ({bytes.Length / 1024} KB). Use an image of at most 300 KB.";
            string base64 = Convert.ToBase64String(bytes);
            if (DecodeImage(base64) is null) return "The file is not an image MARK can show. Use a PNG or JPEG file.";
            LogoBase64 = base64;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The logo could not be read: {ex.Message}";
        }
    }

    internal static ImageSource? DecodeImage(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(Convert.FromBase64String(base64));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException or InvalidOperationException
                                       or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
