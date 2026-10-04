using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>
/// What the signed-in login may use, for the whole of MARK: features of the company's package (others are shown locked),
/// for a staff login only those its account owner gave it (others are hidden), the read-only state (expired, suspended,
/// offline too long) and the company name and logo for the header. Without a licence (tests, or a designer started
/// without sign-in) everything is allowed.
/// </summary>
public sealed class AccessViewModel : ViewModelBase
{
    private LicenceStatus? _status;

    /// <summary>Raised after <see cref="Apply"/>, so commands can re-check what they may do.</summary>
    public event Action? Changed;

    public LicenceStatus? Status => _status;

    public void Apply(LicenceStatus? status)
    {
        _status = status;
        Logo = DecodeLogo(status?.Licence.LogoBase64);
        OnPropertyChanged(string.Empty);                          // every property may have changed
        Changed?.Invoke();
    }

    public bool IsLicensed => _status is not null;

    /// <summary>The library follows the owner's catalogue: the company changes only its own prices.</summary>
    public bool IsCatalogueManaged => _status?.Licence.CatalogueHash is not null;

    /// <summary>Quotes can be opened, viewed and exported, but not saved, deleted or used as default.</summary>
    public bool IsReadOnly => _status?.IsReadOnly ?? false;

    /// <summary>Why MARK is read-only, or a warning (licence ending, offline for days), or null.</summary>
    public string? Notice => _status?.Reason ?? _status?.Warning;

    public bool HasNotice => Notice is not null;

    public string CompanyName => _status?.Licence.CompanyName ?? "";

    public string UserName => _status?.Licence.UserName ?? "";

    public string UserId => _status?.Licence.UserId ?? "";

    /// <summary>A staff login added by the account owner (sees only what it was given).</summary>
    public bool IsStaff => _status?.Licence.IsStaff ?? false;

    /// <summary>The account owner, signed in with a licence: may add and change staff logins.</summary>
    public bool CanManageStaff => _status is { Licence.IsStaff: false };

    /// <summary>"Account owner" or "Staff".</summary>
    public string RoleText => IsStaff ? "Staff" : "Account owner";

    public ImageSource? Logo { get; private set; }

    public bool HasLogo => Logo is not null;

    public bool Allows(string featureId) => _status is null || _status.Allows(featureId);

    /// <summary>The company has the feature but this staff login was not given it: it is hidden rather than shown locked.</summary>
    public bool IsWithheld(string featureId) => _status is not null && (_status.IsWithheld(featureId) || (IsStaff && !_status.Allows(featureId)));

    /// <summary>True for the account owner; for a staff login, true when its account owner gave it the feature.</summary>
    public bool IsGiven(string featureId) => !IsStaff || (_status!.Licence.Permissions?.Contains(featureId) ?? false);

    /// <summary>A staff login with none of quotes, drawing or price structure can open and view quotes, but not change them.</summary>
    public bool CanEditQuotes => Allows(Features.Quotes) || Allows(Features.Drawing) || Allows(Features.PriceStructure);

    /// <summary>Quote values and costs: for logins that sell, cost or price (not, e.g., a cutter).</summary>
    public bool CanSeeQuoteValues => Allows(Features.Quotes) || Allows(Features.Costing) || Allows(Features.PriceStructure);

    /// <summary>
    /// The Library Manager: with the feature, or, when the library follows the owner's catalogue, for setting the
    /// company's own prices (the account owner, or staff who see costs).
    /// </summary>
    public bool CanUseLibrary => CanManageLibrary || (IsCatalogueManaged && (!IsStaff || Allows(Features.Costing)));

    public bool CanUseDrawing => Allows(Features.Drawing);
    public bool CanMakeQuotation => Allows(Features.QuotationPdf);
    public bool CanSeeSalesCharts => Allows(Features.SalesCharts);
    public string SalesChartsLock => Lock(Features.SalesCharts);
    public bool CanUseOpenings => Allows(Features.Openings);
    public bool CanUseDesignLibrary => Allows(Features.DesignLibrary);
    public bool CanUseProjectFiles => Allows(Features.ProjectFiles);
    public bool CanSeeCosting => Allows(Features.Costing);
    public bool CanUsePriceStructure => Allows(Features.PriceStructure);
    public bool CanManageLibrary => Allows(Features.LibraryManager);
    public bool CanSeeCuttingPlans => Allows(Features.CuttingPlans);

    public string OpeningsLock => Lock(Features.Openings);
    public string DesignLibraryLock => Lock(Features.DesignLibrary);
    public string CostingLock => Lock(Features.Costing);
    public string PriceStructureLock => Lock(Features.PriceStructure);
    public string CuttingPlansLock => Lock(Features.CuttingPlans);
    public string LibraryLock => Lock(Features.LibraryManager);

    /// <summary>Why a feature cannot be used by this login: not in the package, or not given to this staff login.</summary>
    public string Lock(string featureId) => IsWithheld(featureId) ? WithheldMessage(featureId) : LockedMessage(featureId);

    /// <summary>"Cutting plans is not included in your MARK package. …"</summary>
    public static string LockedMessage(string featureId)
        => $"{FeatureCatalog.NameOf(featureId)} is not included in your MARK package. Contact your MARK supplier to add it.";

    /// <summary>"Cutting plans is not part of your login. …"</summary>
    public static string WithheldMessage(string featureId)
        => $"{FeatureCatalog.NameOf(featureId)} is not part of your login. Ask the owner of your MARK account to give it to you.";

    /// <summary>Why a change cannot be saved now, or null when it can.</summary>
    public string? ReadOnlyMessage => IsReadOnly ? $"MARK is read-only, so changes cannot be saved. {_status!.Reason}"
        : !CanEditQuotes ? "Your login can open and view quotes, but not change them. Ask the owner of your MARK account if you need to."
        : null;

    private static ImageSource? DecodeLogo(string? base64)
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
            return null;                                          // a logo that cannot be shown is left out
        }
    }
}
