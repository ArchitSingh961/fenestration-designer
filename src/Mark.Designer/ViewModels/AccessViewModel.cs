using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>
/// What the signed-in company may use, for the whole of MARK: features of its package (others are shown locked), the
/// read-only state (expired, suspended, offline too long) and the company name and logo for the header. Without a
/// licence (tests, or a designer started without sign-in) everything is allowed.
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

    public ImageSource? Logo { get; private set; }

    public bool HasLogo => Logo is not null;

    public bool Allows(string featureId) => _status is null || _status.Allows(featureId);

    public bool CanUseOpenings => Allows(Features.Openings);
    public bool CanUseDesignLibrary => Allows(Features.DesignLibrary);
    public bool CanUseProjectFiles => Allows(Features.ProjectFiles);
    public bool CanSeeCosting => Allows(Features.Costing);
    public bool CanUsePriceStructure => Allows(Features.PriceStructure);
    public bool CanManageLibrary => Allows(Features.LibraryManager);
    public bool CanSeeCuttingPlans => Allows(Features.CuttingPlans);

    public string OpeningsLock => LockedMessage(Features.Openings);
    public string DesignLibraryLock => LockedMessage(Features.DesignLibrary);
    public string CostingLock => LockedMessage(Features.Costing);
    public string PriceStructureLock => LockedMessage(Features.PriceStructure);
    public string CuttingPlansLock => LockedMessage(Features.CuttingPlans);

    /// <summary>"Cutting plans is not included in your MARK package. …"</summary>
    public static string LockedMessage(string featureId)
        => $"{FeatureCatalog.NameOf(featureId)} is not included in your MARK package. Contact your MARK supplier to add it.";

    /// <summary>Why a change cannot be saved now, or null when it can.</summary>
    public string? ReadOnlyMessage => IsReadOnly ? $"MARK is read-only, so changes cannot be saved. {_status!.Reason}" : null;

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
