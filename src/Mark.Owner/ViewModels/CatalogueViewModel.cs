using System.IO;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>A system of the catalogue in the overview.</summary>
public sealed record CatalogueSystemRow(string Name, string Material, string Detail, IReadOnlyList<string> Bundles);

/// <summary>An item of the catalogue in the overview: "31011", "2 Track Outer Frame", "frame, track · 0.968 kg/m".</summary>
public sealed record CatalogueItemRow(string Code, string Name, string Detail);

/// <summary>Items of the catalogue shown together: the profiles of one series, the glass, the hardware.</summary>
public sealed record CatalogueItemGroup(string Title, string CountText, IReadOnlyList<CatalogueItemRow> Rows);

/// <summary>Where an imported library file goes.</summary>
public enum ImportDestination
{
    /// <summary>Its items are added to the catalogue every company can be given (existing ids are kept).</summary>
    AddToCatalogue,

    /// <summary>It becomes the whole catalogue.</summary>
    ReplaceCatalogue,

    /// <summary>Its items become one company's own items.</summary>
    CompanyOwnItems
}

/// <summary>The owner's answer to "where does this file go?".</summary>
/// <param name="CompanyId">The company, for <see cref="ImportDestination.CompanyOwnItems"/>.</param>
/// <param name="GiveSystemsTo">Companies to tick the file's systems for (catalogue destinations).</param>
public sealed record ImportChoice(ImportDestination Destination, Guid? CompanyId = null, IReadOnlyList<Guid>? GiveSystemsTo = null);

/// <summary>Opens the shared Library Manager on a library (MARK Owner's own dialogs).</summary>
public interface ICatalogueEditorHost
{
    void ShowLibraryManager(LibraryManagerViewModel manager);

    IDialogService Dialogs { get; }

    /// <summary>Asks where a library file goes: the catalogue (added to it or replacing it) or one company's own items. Null: cancelled.</summary>
    ImportChoice? ChooseImport(string fileName, IProductLibrary file, bool hasCatalogue, IReadOnlyList<OwnItemsRow> companies) => null;
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

    /// <summary>The catalogue's items: profiles by series, then glass, then hardware and accessories.</summary>
    public IReadOnlyList<CatalogueItemGroup> ItemGroups { get; private set; } = Array.Empty<CatalogueItemGroup>();

    /// <summary>The overview of a library's items, for the Catalogue page.</summary>
    public static IReadOnlyList<CatalogueItemGroup> ItemGroupsOf(IProductLibrary library)
    {
        static string Number(double value) => value.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
        static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        static string RoleName(ProfileType role) => System.Text.RegularExpressions.Regex.Replace(role.ToString(), "(?<!^)([A-Z])", " $1").ToLowerInvariant();
        var groups = library.Profiles
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Series) ? "Other profiles" : p.Series!.Trim())
            .OrderBy(g => g.Key == "Other profiles").ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CatalogueItemGroup(g.Key, Plural(g.Count(), "profile", "profiles"), g.Select(p => new CatalogueItemRow(p.Code ?? "", p.Name,
                string.Join(" · ", new[]
                {
                    string.Join(", ", p.Roles.Select(RoleName)),
                    p.Manufacturer,
                    p.FaceWidthMm > 0 ? $"{Number(p.FaceWidthMm)} × {Number(p.DepthMm)} mm" : null,
                    p.WeightKgPerMetre > 0 ? $"{Number(p.WeightKgPerMetre)} kg/m" : null
                }.Where(t => !string.IsNullOrWhiteSpace(t))))).ToList()))
            .ToList();
        if (library.Glass.Count > 0)
            groups.Add(new CatalogueItemGroup("Glass", Plural(library.Glass.Count, "glass", "glass"), library.Glass
                .Select(g => new CatalogueItemRow("", g.Name, $"{Number(g.ThicknessMm)} mm · {g.Category}")).ToList()));
        if (library.Materials.Count > 0)
            groups.Add(new CatalogueItemGroup("Hardware and accessories", Plural(library.Materials.Count, "item", "items"), library.Materials
                .Select(m => new CatalogueItemRow("", m.Name, $"{m.Category} · per {m.Unit}".ToLowerInvariant())).ToList()));
        return groups;
    }

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
    /// them added and shown apart under the company's name, in the tabs made for them ("50 Series"): what is new there
    /// becomes the company's; changes to catalogue items are not kept. Saved at once.
    /// </summary>
    public async Task EditOwnItemsAsync(OwnItemsRow company)
    {
        var master = Library ?? ProductLibrary.Empty;
        CompanyItemsInfo? info = null;
        if (!await RunAsync(async () => info = await Api.CompanyItemsAsync(company.CompanyId))) return;

        ProductLibrary edited;
        OwnItemsSection section;
        try
        {
            var own = CompanyItems.Deserialize(info!.ItemsJson);
            // Everything not in the catalogue is the company's, including what is added in the Library Manager.
            var catalogueIds = CompanyItems.Split(master, ProductLibrary.Empty).AllIds.ToHashSet(StringComparer.Ordinal);
            section = new OwnItemsSection(company.Name, id => !catalogueIds.Contains(id), own.Tabs, canEditTabs: true);
            var combined = CompanyItems.Combine(master, own);
            var changed = new LibraryWorkingCopy(_host, _workFolder).Edit(combined, section);
            if (changed is null && SameTabs(section.ToTabs(), own.Tabs))
            {
                Show($"The own items of {company.Name} were not changed.");
                return;
            }
            edited = changed ?? combined;
        }
        catch (InvalidOperationException ex)
        {
            Show($"The own items of {company.Name} could not be opened: {ex.Message}", true);
            return;
        }

        var items = CompanyItems.Split(edited, master).WithTabs(section.ToTabs());
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

    private static bool SameTabs(IReadOnlyList<OwnItemTab> a, IReadOnlyList<OwnItemTab> b)
        => a.Count == b.Count && a.Zip(b).All(p => p.First.Name == p.Second.Name && p.First.Ids.SequenceEqual(p.Second.Ids));

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
        ItemGroups = Library is null ? Array.Empty<CatalogueItemGroup>() : ItemGroupsOf(Library);
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
        if (_host.Dialogs.ChooseOpenFile("Import a library file", "Library files (*.json)|*.json|All files (*.*)|*.*") is not { } path)
            return;
        ProductLibrary file;
        try
        {
            file = LibrarySerializer.Load(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Show($"The file is not a library MARK can read: {ex.Message}", true);
            return;
        }
        string name = Path.GetFileName(path);
        if (_host.ChooseImport(name, file, Library is not null, OwnItemRows.ToList()) is not { } choice) return;
        await ImportAsync(file, name, choice);
    }

    /// <summary>
    /// Imports a library file where the owner chose: added to the catalogue (ids already used are kept as they are),
    /// as the whole catalogue, or as one company's own items (its profiles in tabs by series). For the catalogue, the
    /// file's systems can be ticked at once for some companies, so they get them at their next check-in.
    /// </summary>
    public async Task ImportAsync(ProductLibrary file, string fileName, ImportChoice choice)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Destination == ImportDestination.CompanyOwnItems)
        {
            if (OwnItemRows.FirstOrDefault(r => r.CompanyId == choice.CompanyId) is not { } company)
            {
                Show("Choose the company the items are for.", true);
                return;
            }
            await ImportOwnItemsAsync(file, fileName, company);
            return;
        }

        string json, success;
        if (choice.Destination == ImportDestination.ReplaceCatalogue || Library is null)
        {
            json = LibrarySerializer.Serialize(file);
            success = $"Published {fileName} as the catalogue.";
        }
        else
        {
            LibraryMergeResult merged;
            try
            {
                merged = LibraryMerge.Add(Library, file);
            }
            catch (InvalidOperationException ex)
            {
                Show($"{fileName} cannot be added to the catalogue: {ex.Message}", true);
                return;
            }
            if (merged.Added.Count == 0)
            {
                Show($"Nothing was added: everything in {fileName} is already in the catalogue.");
                return;
            }
            json = LibrarySerializer.Serialize(merged.Library);
            success = $"Added {merged.AddedText(file)} from {fileName} to the catalogue." + SkippedText(merged.Skipped);
        }
        if (!await PublishAsync(json, success)) return;
        if (choice.GiveSystemsTo is { Count: > 0 } companies && file.Systems.Count > 0)
            await GiveSystemsAsync(file, companies, success);
    }

    /// <summary>Ticks the file's systems in the companies' accounts (what else they have stays).</summary>
    private async Task GiveSystemsAsync(ProductLibrary file, IReadOnlyList<Guid> companies, string success)
    {
        var systems = file.Systems.Where(x => Library?.FindSystem(x.Id) is not null).ToList();
        var given = new List<string>();
        var notes = new List<string>();
        foreach (var id in companies)
        {
            CompanyDetail? detail = null;
            if (!await RunAsync(async () => detail = await Api.CompanyAsync(id))) return;
            var current = detail!.Catalogue ?? CompanyCatalogue.Empty;
            var catalogue = current with { SystemIds = current.SystemIds.Union(systems.Select(x => x.Id)).ToList() };
            var edit = new CompanyEdit(detail.Name, detail.LogoBase64, detail.CompanyTypeId, detail.OwnerName, detail.OwnerUserId, null,
                detail.Products, detail.PackageId, detail.ValidUntilUtc, detail.MaxComputers, detail.AddOns, detail.RemovedFeatures,
                detail.Notes, catalogue, detail.MaxUsers);
            if (!await RunAsync(() => Api.UpdateCompanyAsync(id, edit))) return;
            given.Add(detail.Name);
            var missing = systems.Select(x => LicenceProductName(x.Material)).Distinct()
                .Where(p => detail.Products.All(l => l.Product.ToString() != p)).ToList();
            if (missing.Count > 0)
                notes.Add($"{detail.Name} has no {string.Join(" or ", missing)} licence, so it will not receive those systems until one is added in its account.");
        }
        Show($"{success} Ticked its systems for {string.Join(", ", given)}; they get them at their next check-in." +
             (notes.Count > 0 ? " " + string.Join(" ", notes) : ""), notes.Count > 0);
    }

    private static string LicenceProductName(SystemMaterial material) => material == SystemMaterial.Upvc ? "Upvc" : "Aluminium";

    /// <summary>Adds the file's items to a company's own items, its profiles in a tab per series.</summary>
    private async Task ImportOwnItemsAsync(ProductLibrary file, string fileName, OwnItemsRow company)
    {
        var master = Library ?? ProductLibrary.Empty;
        CompanyItemsInfo? info = null;
        if (!await RunAsync(async () => info = await Api.CompanyItemsAsync(company.CompanyId))) return;
        CompanyItems items;
        LibraryMergeResult merged;
        try
        {
            var own = CompanyItems.Deserialize(info!.ItemsJson);
            merged = LibraryMerge.Add(CompanyItems.Combine(master, own), file);
            if (merged.Added.Count == 0)
            {
                Show($"Nothing was added: everything in {fileName} is already in the catalogue or in the own items of {company.Name}.");
                return;
            }
            var added = merged.Added.ToHashSet(StringComparer.Ordinal);
            var tabs = own.Tabs.ToList();
            foreach (var series in file.Profiles.Where(p => added.Contains(p.Id) && !string.IsNullOrWhiteSpace(p.Series)).GroupBy(p => p.Series!.Trim()))
            {
                int at = tabs.FindIndex(t => string.Equals(t.Name, series.Key, StringComparison.OrdinalIgnoreCase));
                var ids = series.Select(p => p.Id).ToList();
                if (at >= 0) tabs[at] = tabs[at] with { Ids = tabs[at].Ids.Concat(ids).ToList() };
                else tabs.Add(new OwnItemTab(series.Key, ids));
            }
            items = CompanyItems.Split(merged.Library, master).WithTabs(tabs);
        }
        catch (InvalidOperationException ex)
        {
            Show($"{fileName} cannot be added to the own items of {company.Name}: {ex.Message}", true);
            return;
        }
        if (await RunAsync(() => Api.SaveCompanyItemsAsync(company.CompanyId, CompanyItems.Serialize(items))))
        {
            await LoadCompaniesAsync();
            Show($"Added {merged.AddedText(file)} from {fileName} to the own items of {company.Name} ({items.SummaryText}). " +
                 $"Only {company.Name} gets them, at its next check-in.{SkippedText(merged.Skipped)}");
        }
    }

    private static string SkippedText(IReadOnlyList<string> skipped) => skipped.Count == 0 ? ""
        : $" {skipped.Count} already there {(skipped.Count == 1 ? "was" : "were")} kept as {(skipped.Count == 1 ? "it was" : "they were")}: " +
          string.Join(", ", skipped.Take(8)) + (skipped.Count > 8 ? ", …" : "") + ".";

    /// <summary>"2 systems · 45 profiles · 0 glass".</summary>
    public static string ContentText(IProductLibrary library)
    {
        var parts = new List<string>();
        void Add(int n, string one, string many) { if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}"); }
        Add(library.Systems.Count, "system", "systems");
        Add(library.Bundles.Count, "bundle", "bundles");
        Add(library.Profiles.Count, "profile", "profiles");
        Add(library.Glass.Count, "glass", "glass");
        Add(library.Materials.Count, "hardware or accessory", "hardware and accessories");
        return parts.Count == 0 ? "nothing" : string.Join(" · ", parts);
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

    private async Task<bool> PublishAsync(string json, string success)
    {
        CatalogueInfo? info = null;
        if (!await RunAsync(async () => info = await Api.PublishCatalogueAsync(json))) return false;
        Display(info!);
        Show($"{success} Companies get their part at their next check-in.");
        return true;
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
