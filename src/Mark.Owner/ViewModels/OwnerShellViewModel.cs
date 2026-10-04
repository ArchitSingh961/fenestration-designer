using System.IO;
using System.Windows.Input;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>The pages of MARK Owner.</summary>
public enum OwnerSection { Companies, Keys, Packages, CompanyTypes, Catalogue }

/// <summary>
/// MARK Owner after sign-in: the pages (Companies, Licence keys, Packages, Company types), the signed-in admin and
/// sign-out. Packages and company types are loaded once and shared with the account editor.
/// </summary>
public sealed class OwnerShellViewModel : ViewModelBase
{
    private readonly OwnerApiClient _api;
    private readonly Action _signedOut;
    private IReadOnlyList<PackageInfo> _packages = Array.Empty<PackageInfo>();
    private IReadOnlyList<CompanyTypeInfo> _types = Array.Empty<CompanyTypeInfo>();
    private Mark.Core.Library.ProductLibrary? _catalogue;

    /// <param name="signedOut">Called after signing out, or when the session ended: back to the sign-in page.</param>
    /// <param name="catalogueHost">Opens the Library Manager to edit the catalogue (null: the Catalogue page cannot edit).</param>
    public OwnerShellViewModel(OwnerApiClient api, IOwnerDialogs dialogs, Action signedOut, ICatalogueEditorHost? catalogueHost = null,
        string? workFolder = null, string? samplePath = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _signedOut = signedOut ?? throw new ArgumentNullException(nameof(signedOut));
        Action sessionEnded = () => _signedOut();
        Companies = new CompaniesViewModel(api, dialogs, sessionEnded, () => _packages, () => _types, catalogue: () => _catalogue);
        Keys = new KeysViewModel(api, dialogs, sessionEnded);
        Packages = new PackagesViewModel(api, dialogs, sessionEnded, packages => _packages = packages);
        CompanyTypes = new CompanyTypesViewModel(api, dialogs, sessionEnded, () => _packages, types => _types = types, () => _catalogue);
        Catalogue = new CatalogueViewModel(api, dialogs, sessionEnded, catalogueHost ?? new NoCatalogueEditor(),
            workFolder ?? Path.Combine(Path.GetTempPath(), "MARK Owner"), samplePath);
        Catalogue.Loaded += library => _catalogue = library;
        SignOutCommand = new AsyncCommand(SignOutAsync);
        ShowSectionCommand = new RelayCommand(p =>
        {
            if (p is OwnerSection section) Section = section;
            else if (p is string name && Enum.TryParse(name, out OwnerSection parsed)) Section = parsed;
        });
    }

    public CompaniesViewModel Companies { get; }
    public KeysViewModel Keys { get; }
    public PackagesViewModel Packages { get; }
    public CompanyTypesViewModel CompanyTypes { get; }
    public CatalogueViewModel Catalogue { get; }

    public ICommand SignOutCommand { get; }
    public ICommand ShowSectionCommand { get; }

    public string AdminName => _api.Session?.Name ?? "";

    public string ServerText => _api.ServerUrl.TrimEnd('/');

    public string FeatureCountText => $"{FeatureCatalog.All.Count} features in MARK";

    private OwnerSection _section = OwnerSection.Companies;
    /// <summary>The page shown; showing a page reads it from the server again.</summary>
    public OwnerSection Section
    {
        get => _section;
        set
        {
            SetProperty(ref _section, value);
            _ = LoadSectionAsync(value);
        }
    }

    /// <summary>Reads everything after sign-in.</summary>
    public async Task LoadAsync()
    {
        await Catalogue.LoadAsync();
        await Packages.LoadAsync();
        await CompanyTypes.LoadAsync();
        await Companies.LoadAsync();
    }

    private async Task LoadSectionAsync(OwnerSection section)
    {
        switch (section)
        {
            case OwnerSection.Catalogue:
                await Catalogue.LoadAsync();
                break;
            case OwnerSection.Companies:
                await Catalogue.LoadAsync();
                await Packages.LoadAsync();
                await CompanyTypes.LoadAsync();
                await Companies.LoadAsync();
                break;
            case OwnerSection.Keys:
                await Companies.LoadAsync();
                Keys.SetCompanies(Companies.Companies.Select(c => c.Summary));
                await Keys.LoadAsync();
                break;
            case OwnerSection.Packages:
                await Packages.LoadAsync();
                break;
            case OwnerSection.CompanyTypes:
                await Catalogue.LoadAsync();
                await Packages.LoadAsync();
                await CompanyTypes.LoadAsync();
                break;
        }
    }

    private async Task SignOutAsync()
    {
        try
        {
            await _api.SignOutAsync();
        }
        catch (LicenceServerException)
        {
            // Signing out locally is what matters; the server session ends by itself.
        }
        _signedOut();
    }
}

/// <summary>Stand-in when MARK Owner runs without a window to edit the catalogue in (tests).</summary>
internal sealed class NoCatalogueEditor : ICatalogueEditorHost
{
    public void ShowLibraryManager(LibraryManagerViewModel manager)
    {
    }

    public IDialogService Dialogs { get; } = new NoDialogs();

    private sealed class NoDialogs : IDialogService
    {
        public bool Confirm(string title, string message) => false;
        public string? PromptText(string title, string label, string initialText) => null;
        public Guid? ChooseProject(ProjectListViewModel projects) => null;
        public string? ChooseOpenFile(string title, string filter) => null;
        public string? ChooseSaveFile(string title, string filter, string fileName) => null;
        public void ShowLibraryManager(LibraryManagerViewModel manager)
        {
        }
    }
}
