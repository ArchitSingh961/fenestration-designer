using System.IO;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Owner.ViewModels;
using Mark.Tests.Data;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>
/// Importing a library file in MARK Owner: added to the catalogue, replacing it, or as one company's own items; the
/// catalogue page lists the items by series. Over HTTP against a real licence server.
/// </summary>
public class CatalogueImportTests : IAsyncLifetime
{
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mark-tests", "import-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private string _url = "";

    public async Task InitializeAsync()
    {
        _app = LicenceServerApp.Create(new ServerOptions { DataFolder = _folder, Urls = "http://127.0.0.1:0" });
        await _app.StartAsync();
        _url = _app.Urls.First();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A maker's file: a 31mm sliding system with its frame and shutter, and a casement frame of another series.</summary>
    private static ProductLibrary MakersFile()
    {
        var frame = Sample.FindProfile("PRF-SL-FRM-2T")!;
        var sash = Sample.FindProfile("PRF-SL-SASH-45")!;
        var profiles = new[]
        {
            frame with { Id = "SZ-31011", Code = "31011", Name = "2 Track Outer Frame", Manufacturer = "Sözlük", Series = "31mm Sliding", UsedWith = null, Materials = Array.Empty<MaterialUsage>(),
                         Roles = new[] { ProfileType.Frame, ProfileType.Track } },
            sash with { Id = "SZ-31013", Code = "31013", Name = "SGU Shutter", Manufacturer = "Sözlük", Series = "31mm Sliding", UsedWith = null, Materials = Array.Empty<MaterialUsage>() },
            frame with { Id = "SZ-41011", Code = "41011", Name = "Outer Frame", Manufacturer = "Sözlük", Series = "41mm Casement", UsedWith = null, Materials = Array.Empty<MaterialUsage>() },
        };
        var system = new ProductSystem
        {
            Id = "SYS-SZ-31SL", Name = "31mm Sliding – Aluminium", Material = SystemMaterial.Aluminium,
            FrameProfileId = "SZ-31011", SashProfileId = "SZ-31013"
        };
        return new ProductLibrary(profiles, systems: new[] { system });
    }

    private async Task<(OwnerApiClient Owner, CatalogueViewModel Page, CompanyDetail Aluminium, CompanyDetail Upvc)> SetUpAsync()
    {
        var owner = new OwnerApiClient(_url);
        await owner.SetUpAsync(new AdminSetupRequest("owner", "admin-pass", "Archit"));
        await owner.PublishCatalogueAsync(LibrarySerializer.Serialize(Sample));
        var package = (await owner.PackagesAsync()).First().Id;
        var type = (await owner.CompanyTypesAsync()).First().Id;
        CompanyDetail Create(string name, string user, Product product) => owner.CreateCompanyAsync(new CompanyEdit(name, null, type, "Vikram", user,
            "secret1", new[] { new ProductLicence(product, DateTime.UtcNow.AddYears(1)) }, package, DateTime.UtcNow.AddYears(1), 2,
            Array.Empty<AddOn>(), Array.Empty<string>(), null, new CompanyCatalogue(new[] { "SYS-AL-S60C" }, Array.Empty<string>()))).GetAwaiter().GetResult();
        var aluminium = Create("Sozluk", "sozluk", Product.Aluminium);
        var upvc = Create("mte", "mte", Product.Upvc);
        var page = new CatalogueViewModel(owner, new NoDialogs(), () => { }, new Host(), Path.Combine(_folder, "work"), null);
        await page.LoadAsync();
        return (owner, page, aluminium, upvc);
    }

    [Fact]
    public async Task AddedToTheCatalogue_KeepsWhatIsThere_AndGivesTheSystemToTheCompaniesTicked()
    {
        var (owner, page, aluminium, upvc) = await SetUpAsync();

        await page.ImportAsync(MakersFile(), "sozluk.json",
            new ImportChoice(ImportDestination.AddToCatalogue, null, new[] { aluminium.Id, upvc.Id }));

        var catalogue = LibrarySerializer.Deserialize((await owner.CatalogueAsync()).LibraryJson!);
        Assert.Equal(Sample.Profiles.Count + 3, catalogue.Profiles.Count);                      // the sample stays
        Assert.NotNull(catalogue.FindSystem("SYS-AL-S60C"));
        Assert.NotNull(catalogue.FindSystem("SYS-SZ-31SL"));
        Assert.Equal(Sample.Defaults.SystemId, catalogue.Defaults.SystemId);
        Assert.Equal(new[] { "SYS-AL-S60C", "SYS-SZ-31SL" }, (await owner.CompanyAsync(aluminium.Id)).Catalogue!.SystemIds);   // added to the ticks
        Assert.Contains("SYS-SZ-31SL", (await owner.CompanyAsync(upvc.Id)).Catalogue!.SystemIds);
        Assert.Contains("mte has no Aluminium licence", page.Message);                          // told why it will not get it

        await page.ImportAsync(MakersFile(), "sozluk.json", new ImportChoice(ImportDestination.AddToCatalogue));
        Assert.Contains("already in the catalogue", page.Message);                              // nothing twice
    }

    [Fact]
    public async Task AsACompanysOwnItems_OnlyThatCompanyGetsThem_InATabPerSeries()
    {
        var (owner, page, aluminium, _) = await SetUpAsync();
        var sozluk = page.OwnItemRows.Single(r => r.Name == "Sozluk");

        await page.ImportAsync(MakersFile(), "sozluk.json", new ImportChoice(ImportDestination.CompanyOwnItems, sozluk.CompanyId));

        Assert.False(page.MessageIsError, page.Message);
        var own = CompanyItems.Deserialize((await owner.CompanyItemsAsync(aluminium.Id)).ItemsJson);
        Assert.Equal(new[] { "SZ-31011", "SZ-31013", "SZ-41011" }, own.Profiles.Select(p => p.Id));
        Assert.Equal("SYS-SZ-31SL", own.Systems.Single().Id);
        Assert.Equal(new[] { "31mm Sliding", "41mm Casement" }, own.Tabs.Select(t => t.Name));
        Assert.Equal(new[] { "SZ-31011", "SZ-31013" }, own.Tabs[0].Ids);
        var catalogue = LibrarySerializer.Deserialize((await owner.CatalogueAsync()).LibraryJson!);
        Assert.Null(catalogue.FindProfile("SZ-31011"));                                          // not in the universal catalogue
        Assert.Contains("3 profiles", page.OwnItemRows.Single(r => r.Name == "Sozluk").Summary);
    }

    [Fact]
    public async Task Replacing_MakesTheFileTheWholeCatalogue_AndThePageListsItsItemsBySeries()
    {
        var (owner, page, _, _) = await SetUpAsync();

        await page.ImportAsync(MakersFile(), "sozluk.json", new ImportChoice(ImportDestination.ReplaceCatalogue));

        var catalogue = LibrarySerializer.Deserialize((await owner.CatalogueAsync()).LibraryJson!);
        Assert.Equal(3, catalogue.Profiles.Count);
        Assert.Equal(new[] { "31mm Sliding", "41mm Casement" }, page.ItemGroups.Select(g => g.Title));
        Assert.Equal("2 profiles", page.ItemGroups[0].CountText);
        var row = page.ItemGroups[0].Rows[0];
        Assert.Equal(("31011", "2 Track Outer Frame"), (row.Code, row.Name));
        Assert.Contains("frame, track", row.Detail);
    }

    [Fact]
    public void Merging_SkipsIdsAlreadyUsed_AndKeepsTheTargetsDefaults()
    {
        var merged = LibraryMerge.Add(Sample, MakersFile());
        Assert.Equal(4, merged.Added.Count);
        Assert.Empty(merged.Skipped);
        Assert.Equal(Sample.Defaults, merged.Library.Defaults);

        var again = LibraryMerge.Add(merged.Library, MakersFile());
        Assert.Empty(again.Added);
        Assert.Equal(4, again.Skipped.Count);
        Assert.Equal("1 system · 3 profiles", merged.AddedText(MakersFile()));
    }

    private sealed class Host : ICatalogueEditorHost
    {
        public IDialogService Dialogs => new FakeDialogs();
        public Action<LibraryManagerViewModel> Script { get; set; } = _ => { };
        public void ShowLibraryManager(LibraryManagerViewModel manager) => Script(manager);
    }

    [Fact]
    public async Task AddGlass_OpensANewGlass_AndPublishesIt()
    {
        var (owner, page, _, _) = await SetUpAsync();
        await page.ImportAsync(MakersFile(), "sozluk.json", new ImportChoice(ImportDestination.ReplaceCatalogue));
        Assert.True(page.HasNoGlass);                                                         // the page says so
        var host = new Host();
        var withHost = new CatalogueViewModel(owner, new NoDialogs(), () => { }, host, Path.Combine(_folder, "work2"), null);
        await withHost.LoadAsync();
        host.Script = manager =>
        {
            Assert.Equal(Mark.Data.LibraryItemKind.Glass, manager.Kind);
            var glass = Assert.IsType<LibraryItemEditorViewModel>(manager.Editor);
            Assert.True(glass.IsNew);
            glass.Id = "SZ-GLS-5";
            glass.Name = "5mm Clear";
            glass.Thickness = "5";
            glass.CostPerSquareMetre = "650";
            glass.ForCasement = glass.ForSliding = true;
            manager.SaveCommand.Execute(null);
        };

        withHost.AddItemCommand.Execute("Glass");
        await WaitAsync(() => withHost.Library?.Glass.Count == 1);

        Assert.False(withHost.HasNoGlass);
        Assert.Equal("5mm Clear", LibrarySerializer.Deserialize((await owner.CatalogueAsync()).LibraryJson!).Glass.Single().Name);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition());
    }

    private sealed class NoDialogs : IOwnerDialogs
    {
        public bool Confirm(string title, string message) => true;
        public string? ChooseImageFile() => null;
        public void CopyText(string text) { }
    }
}
