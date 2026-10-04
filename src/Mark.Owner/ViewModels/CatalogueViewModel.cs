using System.IO;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>A system of the catalogue in the overview.</summary>
public sealed record CatalogueSystemRow(string Name, string Material, string Detail, IReadOnlyList<string> Bundles);

/// <summary>Opens the shared Library Manager on a library (MARK Owner's own dialogs).</summary>
public interface ICatalogueEditorHost
{
    void ShowLibraryManager(LibraryManagerViewModel manager);

    IDialogService Dialogs { get; }
}

/// <summary>
/// The Catalogue page of MARK Owner: the master catalogue every company's products come from (profiles, glass, hardware,
/// systems with their bundles). Edit it with the Library Manager (on a working copy), then publish it; companies get their
/// part at their next check-in. A new server can start from the sample catalogue or a library file.
/// </summary>
/// <summary>A company on the Catalogue page with its own items: "Sozluk" · "1 system · 2 profiles".</summary>
public sealed record OwnItemsRow(Guid CompanyId, string Name, string Summary);

public sealed class CatalogueViewModel : OwnerPage
{
    private readonly ICatalogueEditorHost _host;
    private readonly string _workFolder;
    private readonly string? _samplePath;

    public CatalogueViewModel(OwnerApiClient api, IOwnerDialogs dialogs, Action sessionEnded, ICatalogueEditorHost host,
        string workFolder, string? samplePath)
        : base(api, dialogs, sessionEnded)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _workFolder = workFolder;
        _samplePath = samplePath;
        EditCommand = new AsyncCommand(EditAsync);
        ImportCommand = new AsyncCommand(ImportAsync);
        ExportCommand = new RelayCommand(Export, () => Library is not null);
        UseSampleCommand = new AsyncCommand(UseSampleAsync, () => _samplePath is not null && File.Exists(_samplePath));
        RefreshCommand = new AsyncCommand(LoadAsync);
        EditOwnItemsCommand = new RelayCommand(async p => { if (p is OwnItemsRow row) await EditOwnItemsAsync(row); });
    }

    public ICommand EditOwnItemsCommand { get; }

    /// <summary>Every company with its own items (products for it only), for "Edit own items…".</summary>
    public System.Collections.ObjectModel.ObservableCollection<OwnItemsRow> OwnItemRows { get; } = new();

    public bool HasCompanies => OwnItemRows.Count > 0;

    public ICommand EditCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand UseSampleCommand { get; }
    public ICommand RefreshCommand { get; }

    /// <summary>The published catalogue, or null when none is published yet.</summary>
    public ProductLibrary? Library { get; private set; }

    public bool HasCatalogue => Library is not null;

    public string VersionText { get; private set; } = "";

    public string CountsText { get; private set; } = "";

    public IReadOnlyList<CatalogueSystemRow> SystemRows { get; private set; } = Array.Empty<CatalogueSystemRow>();

    /// <summary>Told after every load (company and type editors offer the catalogue's systems and items).</summary>
    public event Action<ProductLibrary?>? Loaded;

    public override async Task LoadAsync()
    {
        await RunAsync(async () => Display(await Api.CatalogueAsync()));
        await LoadCompaniesAsync();
    }

    private async Task LoadCompaniesAsync()
    {
        List<CompanySummary>? companies = null;
        if (!await RunAsync(async () => companies = await Api.CompaniesAsync())) return;
        OwnItemRows.Clear();
        foreach (var c in companies!)
            OwnItemRows.Add(new OwnItemsRow(c.Id, c.Name, c.OwnItemsSummary is null or "None" ? "No own items" : c.OwnItemsSummary));
        OnPropertyChanged(nameof(HasCompanies));
    }

    /// <summary>
    /// Edits a company's own items (products only it gets) with the Library Manager, on a copy of the catalogue with
    /// them added and shown apart under the company's name: what is new there becomes the company's; changes to catalogue
    /// items are not kept. Saved at once.
    /// </summary>
    public async Task EditOwnItemsAsync(OwnItemsRow company)
    {
        var master = Library ?? ProductLibrary.Empty;
        CompanyItemsInfo? info = null;
        if (!await RunAsync(async () => info = await Api.CompanyItemsAsync(company.CompanyId))) return;

        ProductLibrary edited;
        try
        {
            var own = CompanyItems.Deserialize(info!.ItemsJson);
            // Everything not in the catalogue is the company's, including what is added in the Library Manager.
            var catalogueIds = CompanyItems.Split(master, ProductLibrary.Empty).AllIds.ToHashSet(StringComparer.Ordinal);
            var section = new OwnItemsSection(new OwnItemsLabel(company.Name, Array.Empty<string>()).SectionTitle, id => !catalogueIds.Contains(id));
            if (new LibraryWorkingCopy(_host, _workFolder).Edit(CompanyItems.Combine(master, own), section) is not { } changed)
            {
                Show($"The own items of {company.Name} were not changed.");
                return;
            }
            edited = changed;
        }
        catch (InvalidOperationException ex)
        {
            Show($"The own items of {company.Name} could not be opened: {ex.Message}", true);
            return;
        }

        var items = CompanyItems.Split(edited, master);
        int catalogueChanges = CompanyItems.CatalogueChanges(edited, master);
        string note = catalogueChanges == 0 ? ""
            : $" Changes to {catalogueChanges} item{(catalogueChanges == 1 ? "" : "s")} of the catalogue were not kept: use Edit catalogue for those.";
        if (await RunAsync(() => Api.SaveCompanyItemsAsync(company.CompanyId, CompanyItems.Serialize(items))))
        {
            await LoadCompaniesAsync();
            Show(items.IsEmpty
                ? $"{company.Name} has no own items now.{note}"
                : $"Saved the own items of {company.Name} ({items.SummaryText}). Only {company.Name} gets them, at its next check-in.{note}");
        }
    }

    private void Display(CatalogueInfo info)
    {
        Library = info.LibraryJson is { Length: > 0 } json ? LibrarySerializer.Deserialize(json) : null;
        VersionText = info.Version == 0 ? "No catalogue published yet."
            : $"Version {info.Version} · published {OwnerText.When(info.PublishedUtc)}";
        CountsText = Library is null ? ""
            : $"{Library.Systems.Count} systems · {Library.Bundles.Count} bundles · {Library.Profiles.Count} profiles · " +
              $"{Library.Glass.Count} glass · {Library.Materials.Count} hardware and accessories";
        SystemRows = Library?.Systems.Select(x => new CatalogueSystemRow(x.Name, EditorText.MaterialName(x.Material),
            string.Join(" · ", new[]
            {
                EditorText.UseName(x.Use), Library.FindProfile(x.FrameProfileId)?.Name,
                x.GlassRangeText.Length > 0 ? $"glass {x.GlassRangeText}" : null, x.IsActive ? null : "retired"
            }.Where(t => !string.IsNullOrEmpty(t))),
            Library.Bundles.Where(b => b.SystemId == x.Id).Select(b => b.Name).ToList())).ToList() ?? new List<CatalogueSystemRow>();
        OnPropertyChanged(string.Empty);
        ((RelayCommand)ExportCommand).RaiseCanExecuteChanged();
        Loaded?.Invoke(Library);
    }

    /// <summary>
    /// Opens the Library Manager on a working copy of the catalogue. When it is closed with changes, they are published
    /// (after asking).
    /// </summary>
    public async Task EditAsync()
    {
        Directory.CreateDirectory(_workFolder);
        string path = Path.Combine(_workFolder, $"catalogue-{Guid.NewGuid():N}.db");
        try
        {
            var store = LocalStore.Open(path);
            if (Library is not null) store.Library.Import(Library);
            string before = LibrarySerializer.Serialize(store.Library.Current);
            _host.ShowLibraryManager(new LibraryManagerViewModel(store.Library, dialogs: _host.Dialogs));
            string after = LibrarySerializer.Serialize(store.Library.Current);
            if (after == before)
            {
                Show("The catalogue was not changed.");
                return;
            }
            if (!Dialogs.Confirm("Publish catalogue",
                    "Publish your changes to the catalogue? Companies get them at their next check-in. Their own prices are kept."))
            {
                Show("Your changes were not published.");
                return;
            }
            await PublishAsync(after, "Published the catalogue.");
        }
        catch (Exception ex) when (ex is DataStoreException or InvalidOperationException or IOException)
        {
            Show($"The catalogue could not be opened for editing: {ex.Message}", true);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryDelete(path);
        }
    }

    private async Task ImportAsync()
    {
        if (_host.Dialogs.ChooseOpenFile("Import catalogue (library file)", "Library files (*.json)|*.json|All files (*.*)|*.*") is not { } path)
            return;
        await PublishFileAsync(path);
    }

    private async Task UseSampleAsync()
    {
        if (_samplePath is null) return;
        await PublishFileAsync(_samplePath);
    }

    private async Task PublishFileAsync(string path)
    {
        string json;
        try
        {
            json = LibrarySerializer.Serialize(LibrarySerializer.Load(path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Show($"The file is not a library MARK can read: {ex.Message}", true);
            return;
        }
        if (Library is not null && !Dialogs.Confirm("Replace catalogue",
                $"Replace the whole catalogue with {Path.GetFileName(path)}? Companies get the new one at their next check-in."))
            return;
        await PublishAsync(json, $"Published {Path.GetFileName(path)} as the catalogue.");
    }

    private async Task PublishAsync(string json, string success)
    {
        CatalogueInfo? info = null;
        if (await RunAsync(async () => info = await Api.PublishCatalogueAsync(json)))
        {
            Display(info!);
            Show($"{success} Companies get their part at their next check-in.");
        }
    }

    private void Export()
    {
        if (Library is null) return;
        if (_host.Dialogs.ChooseSaveFile("Export catalogue", "Library files (*.json)|*.json", "catalogue.json") is not { } path) return;
        try
        {
            File.WriteAllText(path, LibrarySerializer.Serialize(Library));
            Show($"Exported the catalogue to {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Show(ex.Message, true);
        }
    }

    private static void TryDelete(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (IOException)
            {
                // A leftover working copy in the Owner's own folder is harmless.
            }
        }
    }
}
