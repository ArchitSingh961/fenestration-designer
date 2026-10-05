using System.IO;
using Mark.Core.Library;
using Mark.Designer.ViewModels;
using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Tests.Data;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>Tabs of a company's own items ("50 Series"): made by the owner, kept with the items, shown in MARK.</summary>
public class OwnItemTabsTests : IDisposable
{
    private readonly TestServer _server = new();
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

    public void Dispose() => _server.Dispose();

    /// <summary>Two own profiles of a 50 series and one of a 60 series.</summary>
    private static CompanyItems Own(IReadOnlyList<OwnItemTab>? tabs = null)
    {
        var frame = Sample.FindProfile("PRF-FRM-50")!;
        return new CompanyItems
        {
            Profiles = new[]
            {
                frame with { Id = "SOZ-50-FRM", Name = "Sozluk 50 frame" },
                frame with { Id = "SOZ-50-SASH", Name = "Sozluk 50 sash" },
                frame with { Id = "SOZ-60-FRM", Name = "Sozluk 60 frame" }
            },
            Tabs = tabs ?? Array.Empty<OwnItemTab>()
        };
    }

    // ── Core ────────────────────────────────────────────────────────

    [Fact]
    public void Tabs_AreCleanedUp_AndKeptWithTheItems()
    {
        var items = Own().WithTabs(new[]
        {
            new OwnItemTab(" 50 Series ", new[] { "SOZ-50-FRM", "NOT-THERE", "SOZ-50-SASH" }),
            new OwnItemTab("50 series", new[] { "SOZ-50-FRM", "SOZ-60-FRM" }),     // same name: merged; an id only once
            new OwnItemTab("", new[] { "SOZ-60-FRM" }),                           // no name: dropped
            new OwnItemTab("Later", Array.Empty<string>())                        // empty: kept
        });

        Assert.Equal(new[] { "50 Series", "Later" }, items.Tabs.Select(t => t.Name));
        Assert.Equal(new[] { "SOZ-50-FRM", "SOZ-50-SASH", "SOZ-60-FRM" }, items.Tabs[0].Ids);
        var back = CompanyItems.Deserialize(CompanyItems.Serialize(items));
        Assert.Equal(items.Tabs.Select(t => (t.Name, string.Join(",", t.Ids))), back.Tabs.Select(t => (t.Name, string.Join(",", t.Ids))));
        Assert.Empty(CompanyItems.Deserialize("""{"profiles":[]}""").Tabs);                 // items saved before tabs
        Assert.Contains("2 tabs", items.SummaryText);
    }

    // ── Library Manager ─────────────────────────────────────────────

    private static (LibraryManagerViewModel Manager, OwnItemsSection Section, FakeDialogs Dialogs) Manager(TempDatabase temp, bool canEdit = true)
    {
        var store = temp.Open();
        store.Library.Import(CompanyItems.Combine(Sample, Own()));
        var catalogueIds = CompanyItems.Split(Sample, ProductLibrary.Empty).AllIds.ToHashSet();
        var section = new OwnItemsSection("Sozluk", id => !catalogueIds.Contains(id), canEditTabs: canEdit);
        var dialogs = new FakeDialogs();
        return (new LibraryManagerViewModel(store.Library, dialogs: dialogs, ownItems: section), section, dialogs);
    }

    [Fact]
    public void TheOwner_MakesTabs_AndPutsItemsInThem()
    {
        using var temp = new TempDatabase();
        var (manager, section, dialogs) = Manager(temp);
        Assert.Equal(new[] { "All", "Catalogue", "Own items" }, manager.ListTabs.Select(t => t.Name));

        dialogs.PromptAnswer = "50 Series";
        manager.AddTabCommand.Execute(null);
        dialogs.PromptAnswer = "60 Series";
        manager.AddTabCommand.Execute(null);
        Assert.Equal(new[] { "All", "Catalogue", "50 Series", "60 Series", "Other own items" }, manager.ListTabs.Select(t => t.Name));
        Assert.Equal("60 Series", manager.SelectedListTab!.Name);                      // the new tab is shown, empty
        Assert.Empty(manager.Items);
        dialogs.PromptAnswer = "50 series";
        manager.AddTabCommand.Execute(null);
        Assert.True(manager.MessageIsError);                                           // already there

        manager.SelectedListTab = manager.ListTabs[0];
        foreach (var (id, tab) in new[] { ("SOZ-50-FRM", "50 Series"), ("SOZ-50-SASH", "50 Series"), ("SOZ-60-FRM", "60 Series") })
        {
            manager.SelectedItem = manager.Items.Single(i => i.Id == id);
            Assert.True(manager.ShowsItemTab);
            Assert.Equal("(no tab)", manager.ItemTab);
            manager.ItemTab = tab;
        }

        // The list, in sections: the catalogue, then each tab.
        var sections = manager.Items.Select(i => i.Section).Distinct().ToList();
        Assert.Equal(new[] { "Catalogue", "Sozluk — 50 Series", "Sozluk — 60 Series" }, sections);
        manager.SelectedListTab = manager.ListTabs.Single(t => t.Name == "50 Series");
        Assert.Equal(new[] { "SOZ-50-FRM", "SOZ-50-SASH" }, manager.Items.Select(i => i.Id));

        // Catalogue items get no tab.
        manager.SelectedListTab = manager.ListTabs.Single(t => t.Name == "Catalogue");
        manager.SelectedItem = manager.Items.First();
        Assert.False(manager.ShowsItemTab);

        Assert.Equal(new[] { ("50 Series", "SOZ-50-FRM,SOZ-50-SASH"), ("60 Series", "SOZ-60-FRM") },
            section.ToTabs().Select(t => (t.Name, string.Join(",", t.Ids))));
    }

    [Fact]
    public void Tabs_CanBeRenamedAndRemoved_TheirItemsStay()
    {
        using var temp = new TempDatabase();
        var (manager, section, dialogs) = Manager(temp);
        dialogs.PromptAnswer = "50 Serie";
        manager.AddTabCommand.Execute(null);
        manager.SelectedListTab = manager.ListTabs[0];
        manager.SelectedItem = manager.Items.Single(i => i.Id == "SOZ-50-FRM");
        manager.ItemTab = "50 Serie";

        manager.SelectedListTab = manager.ListTabs.Single(t => t.Name == "50 Serie");
        dialogs.PromptAnswer = "50 Series";
        manager.RenameTabCommand.Execute(null);
        Assert.Equal("50 Series", section.TabOf("SOZ-50-FRM"));
        Assert.Equal("50 Series", manager.SelectedListTab!.Name);
        Assert.Equal(new[] { "SOZ-50-FRM" }, manager.Items.Select(i => i.Id));

        manager.RemoveTabCommand.Execute(null);
        Assert.Empty(section.TabNames);
        Assert.Null(section.TabOf("SOZ-50-FRM"));
        Assert.Contains(manager.Items, i => i.Id == "SOZ-50-FRM" && i.Section == "Sozluk — own items");
    }

    [Fact]
    public void A_NewItem_GoesInTheTabShown()
    {
        using var temp = new TempDatabase();
        var (manager, section, dialogs) = Manager(temp);
        dialogs.PromptAnswer = "50 Series";
        manager.AddTabCommand.Execute(null);

        manager.NewCommand.Execute(null);
        Assert.True(manager.ShowsItemTab);
        Assert.Equal("50 Series", manager.ItemTab);
        var editor = manager.ItemEditor!;
        editor.Id = "SOZ-50-MUL";
        editor.Name = "Sozluk 50 mullion";
        editor.RoleMullion = true;
        editor.FaceWidth = "50";
        editor.CostPerMetre = "400";
        editor.StockLength = "6000";
        editor.ForCasement = true;
        manager.SaveCommand.Execute(null);

        Assert.False(manager.MessageIsError, manager.Message);
        Assert.Equal("50 Series", section.TabOf("SOZ-50-MUL"));
        Assert.Equal("SOZ-50-MUL", manager.SelectedItem!.Id);
    }

    [Fact]
    public void In_Mark_TheTabsAreShown_ButCannotBeChanged()
    {
        using var temp = new TempDatabase();
        var store = temp.Open();
        store.Library.Import(CompanyItems.Combine(Sample, Own()));
        var label = new OwnItemsLabel("Sozluk", new[] { "SOZ-50-FRM", "SOZ-50-SASH", "SOZ-60-FRM" },
            new[] { new OwnItemTab("50 Series", new[] { "SOZ-50-FRM", "SOZ-50-SASH" }) });

        var manager = new LibraryManagerViewModel(store.Library, pricesOnly: true, ownItems: OwnItemsSection.Of(label));

        Assert.Equal(new[] { "All", "Catalogue", "50 Series", "Other own items" }, manager.ListTabs.Select(t => t.Name));
        Assert.False(manager.CanEditTabs);
        Assert.False(manager.AddTabCommand.CanExecute(null));
        Assert.Equal("Sozluk — 50 Series", manager.Items.Single(i => i.Id == "SOZ-50-FRM").Section);
        Assert.Equal("Sozluk — own items", manager.Items.Single(i => i.Id == "SOZ-60-FRM").Section);
        manager.SelectedItem = manager.Items.Single(i => i.Id == "SOZ-50-FRM");
        Assert.False(manager.ShowsItemTab);
    }

    // ── Server ──────────────────────────────────────────────────────

    [Fact]
    public void TheServer_KeepsTheTabs_AndSendsThemWithTheCompanysCatalogue()
    {
        _server.Service.PublishCatalogue(LibrarySerializer.Serialize(Sample));
        var company = _server.CreateCompany(_server.Edit("Sozluk", "sozluk"));
        var tabs = new[]
        {
            new OwnItemTab("50 Series", new[] { "SOZ-50-FRM", "SOZ-50-SASH", "GONE" }),
            new OwnItemTab("Empty", Array.Empty<string>())
        };

        _server.Service.SaveCompanyItems(company.Id, CompanyItems.Serialize(Own(tabs)));

        var stored = CompanyItems.Deserialize(_server.Service.CompanyItems(company.Id).ItemsJson);
        Assert.Equal(new[] { "SOZ-50-FRM", "SOZ-50-SASH" }, stored.Tabs[0].Ids);                 // unknown ids dropped
        Assert.Equal("Empty", stored.Tabs[1].Name);                                              // kept for the owner
        var signIn = _server.SignIn("sozluk");
        string json = _server.Service.ClientCatalogue(new CatalogueRequest(signIn.DeviceToken, "PC-1")).LibraryJson!;
        var label = LibrarySerializer.ReadOwnItems(json)!;
        Assert.Equal(new[] { "50 Series" }, label.Tabs!.Select(t => t.Name));                   // the company sees only filled tabs
        Assert.Equal("50 Series", label.TabOf("SOZ-50-SASH"));
        Assert.Null(label.TabOf("SOZ-60-FRM"));
    }
}

/// <summary>The owner sorts a company's own items into tabs in MARK Owner, over HTTP against a real licence server.</summary>
public class OwnItemTabsOwnerTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mark-tests", "tabs-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private string _url = "";

    public async Task InitializeAsync()
    {
        var options = new ServerOptions { DataFolder = _folder, Urls = "http://127.0.0.1:0" };
        _app = LicenceServerApp.Create(options);
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

    private sealed class Host : Mark.Owner.ViewModels.ICatalogueEditorHost
    {
        public FakeDialogs Fake { get; } = new();
        public IDialogService Dialogs => Fake;
        public Action<LibraryManagerViewModel> Script { get; set; } = _ => { };
        public void ShowLibraryManager(LibraryManagerViewModel manager) => Script(manager);
    }

    private sealed class NoDialogs : Mark.Owner.ViewModels.IOwnerDialogs
    {
        public bool Confirm(string title, string message) => true;
        public string? ChooseImageFile() => null;
        public void CopyText(string text) { }
    }

    [Fact]
    public async Task MakingATab_AndMovingAnItem_IsSaved_EvenWithNoOtherChange()
    {
        using var owner = new OwnerApiClient(_url);
        await owner.SetUpAsync(new AdminSetupRequest("owner", "admin-pass", "Archit"));
        var sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);
        await owner.PublishCatalogueAsync(LibrarySerializer.Serialize(sample));
        var packages = await owner.PackagesAsync();
        var types = await owner.CompanyTypesAsync();
        var company = await owner.CreateCompanyAsync(new CompanyEdit("Sozluk", null, types.First().Id, "Vikram", "sozluk", "secret1",
            new[] { new ProductLicence(Product.Upvc, DateTime.UtcNow.AddYears(1)) }, packages.First().Id, DateTime.UtcNow.AddYears(1), 2,
            Array.Empty<AddOn>(), Array.Empty<string>(), null));
        var frame = sample.FindProfile("PRF-FRM-50")!;
        await owner.SaveCompanyItemsAsync(company.Id, CompanyItems.Serialize(new CompanyItems
        {
            Profiles = new[] { frame with { Id = "SOZ-50-FRM", Name = "Sozluk 50 frame" } }
        }));

        var host = new Host();
        var page = new Mark.Owner.ViewModels.CatalogueViewModel(owner, new NoDialogs(), () => { }, host,
            Path.Combine(_folder, "work"), null);
        await page.LoadAsync();
        host.Script = manager =>
        {
            host.Fake.PromptAnswer = "50 Series";
            manager.AddTabCommand.Execute(null);
            manager.SelectedListTab = manager.ListTabs[0];
            manager.SelectedItem = manager.Items.Single(i => i.Id == "SOZ-50-FRM");
            manager.ItemTab = "50 Series";
        };

        await page.EditOwnItemsAsync(page.OwnItemRows.Single(r => r.Name == "Sozluk"));

        Assert.False(page.MessageIsError, page.Message);
        var saved = CompanyItems.Deserialize((await owner.CompanyItemsAsync(company.Id)).ItemsJson);
        Assert.Equal("50 Series", saved.Tabs.Single().Name);
        Assert.Equal(new[] { "SOZ-50-FRM" }, saved.Tabs.Single().Ids);

        // Opened again: the tab is there, with its item.
        host.Script = manager =>
        {
            Assert.Contains(manager.ListTabs, t => t.Name == "50 Series");
            Assert.Equal("Sozluk — 50 Series", manager.Items.Single(i => i.Id == "SOZ-50-FRM").Section);
        };
        await page.EditOwnItemsAsync(page.OwnItemRows.Single(r => r.Name == "Sozluk"));
        Assert.Contains("not changed", page.Message);
    }
}
