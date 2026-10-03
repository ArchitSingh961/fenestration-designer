using Mark.Core.Library;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Mark.Owner.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>
/// Milestone 13: the owner's master catalogue on the licence server, what each company gets from it, and MARK following it
/// (download checked against the licence, applied with the company's own prices kept).
/// </summary>
public class CatalogueTests : IDisposable
{
    private const string Upvc = "SYS-UPVC-62C";
    private const string Sliding = "SYS-AL-SL60";

    private readonly TestServer _server = new();
    private static readonly string SampleJson = LibrarySerializer.Serialize(LibrarySerializer.Load(TempDatabase.ShippedLibraryPath));

    public void Dispose() => _server.Dispose();

    private CompanyEdit Edit(CompanyCatalogue? catalogue, params Product[] products)
        => _server.Edit(products: (products.Length == 0 ? new[] { Product.Upvc } : products)
            .Select(p => new ProductLicence(p, _server.Clock.Now.AddYears(1))).ToList()) with { Catalogue = catalogue };

    private Licence SignedInLicence() => _server.Read(_server.SignIn().Licence);

    // ── Server ──────────────────────────────────────────────────────

    [Fact]
    public void PublishingACatalogue_IsValidatedAndVersioned()
    {
        Assert.Equal(0, _server.Service.Catalogue().Version);
        Assert.Throws<LicenceServer.ApiException>(() => _server.Service.PublishCatalogue("{ not json"));

        var first = _server.Service.PublishCatalogue(SampleJson);
        var second = _server.Service.PublishCatalogue(SampleJson);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(SampleJson, _server.Service.Catalogue().LibraryJson);
    }

    [Fact]
    public void ACompanyWithoutSelection_KeepsItsOwnLibrary()
    {
        _server.Service.PublishCatalogue(SampleJson);
        _server.CreateCompany(Edit(null));

        Assert.Null(SignedInLicence().CatalogueHash);
    }

    [Fact]
    public void ACompanysLicence_CarriesTheHashOfItsCatalogue_WhichMarkCanDownload()
    {
        _server.Service.PublishCatalogue(SampleJson);
        _server.CreateCompany(Edit(new CompanyCatalogue(new[] { Upvc }, Array.Empty<string>())));
        var response = _server.SignIn();
        var licence = _server.Read(response.Licence);

        var catalogue = _server.Service.ClientCatalogue(new CatalogueRequest(response.DeviceToken, "PC-1")).LibraryJson;

        Assert.Equal(CatalogueHash.Of(catalogue), licence.CatalogueHash);
        Assert.Equal(new[] { Upvc }, LibrarySerializer.Deserialize(catalogue).Systems.Select(x => x.Id));
    }

    [Fact]
    public void OnlySystemsOfLicensedProducts_AreSent()
    {
        _server.Service.PublishCatalogue(SampleJson);
        var company = _server.CreateCompany(Edit(new CompanyCatalogue(new[] { Upvc, Sliding }, Array.Empty<string>()), Product.Upvc));
        var response = _server.SignIn();

        var onlyUpvc = LibrarySerializer.Deserialize(_server.Service.ClientCatalogue(new CatalogueRequest(response.DeviceToken, "PC-1")).LibraryJson);
        Assert.Equal(new[] { Upvc }, onlyUpvc.Systems.Select(x => x.Id));

        // Selling Aluminium as well changes the catalogue, and so the hash in the next licence.
        string before = _server.Read(response.Licence).CatalogueHash!;
        _server.Service.UpdateCompany(company.Id, Edit(null, Product.Upvc, Product.Aluminium) with { OwnerPassword = null });
        var checkedIn = _server.Read(_server.Service.CheckIn(new CheckInRequest(response.DeviceToken, "PC-1")).Licence);
        Assert.NotEqual(before, checkedIn.CatalogueHash);
        var both = LibrarySerializer.Deserialize(_server.Service.ClientCatalogue(new CatalogueRequest(response.DeviceToken, "PC-1")).LibraryJson);
        Assert.Equal(new[] { Upvc, Sliding }, both.Systems.Select(x => x.Id));
    }

    [Fact]
    public void CompanyTypes_RememberTheirCatalogue()
    {
        var type = _server.Type("Trial");
        var saved = _server.Service.SaveCompanyType(type with { Catalogue = new CompanyCatalogue(new[] { Upvc }, new[] { "GLS-TGH-8" }) });

        Assert.Equal(new[] { Upvc }, saved.Catalogue!.SystemIds);
        Assert.Equal(new[] { "GLS-TGH-8" }, _server.Service.CompanyTypes().Single(t => t.Name == "Trial").Catalogue!.ItemIds);
    }

    // ── MARK ────────────────────────────────────────────────────────

    [Fact]
    public async Task Mark_AppliesTheCatalogue_KeepsItsPrices_AndFollowsChanges()
    {
        _server.Service.PublishCatalogue(SampleJson);
        var company = _server.CreateCompany(Edit(new CompanyCatalogue(new[] { Upvc }, Array.Empty<string>())));
        var manager = new LicenceManager(new MemoryStateStore(), _server.Verifier, "PC-1", "Office", _ => new DirectApi(_server.Service),
            () => _server.Clock.Now);
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);
        using var db = new TempDatabase();
        var store = db.Open();
        var sync = new CatalogueSync(manager, store);

        Assert.True(sync.IsDue);
        Assert.Contains("updated", await sync.SyncAsync());
        Assert.False(sync.IsDue);
        Assert.Equal(new[] { Upvc }, store.Library.Current.Systems.Select(x => x.Id));
        var frame = store.Library.Current.FindProfile("PRF-UPVC-FRM-62")!;
        store.Library.Update(frame with { CostPerMetre = 311 });

        // The owner adds the sliding system; MARK gets it at the next check-in; its own price stays.
        _server.Service.UpdateCompany(company.Id, Edit(new CompanyCatalogue(new[] { Upvc, Sliding }, Array.Empty<string>()),
            Product.Upvc, Product.Aluminium) with { OwnerPassword = null });
        await manager.CheckInAsync();
        Assert.True(sync.IsDue);
        await sync.SyncAsync();

        Assert.Equal(new[] { Upvc, Sliding }, store.Library.Current.Systems.Select(x => x.Id));
        Assert.Equal(311, store.Library.Current.FindProfile("PRF-UPVC-FRM-62")!.CostPerMetre);
    }

    [Fact]
    public async Task ACatalogueThatDoesNotMatchTheLicence_IsNotUsed()
    {
        _server.Service.PublishCatalogue(SampleJson);
        _server.CreateCompany(Edit(new CompanyCatalogue(new[] { Upvc }, Array.Empty<string>())));
        var api = new TamperingApi(new DirectApi(_server.Service));
        var manager = new LicenceManager(new MemoryStateStore(), _server.Verifier, "PC-1", "Office", _ => api, () => _server.Clock.Now);
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        var (json, error) = await manager.DownloadCatalogueAsync();

        Assert.Null(json);
        Assert.Contains("does not match", error);
    }

    /// <summary>Changes the catalogue on its way to MARK.</summary>
    private sealed class TamperingApi : ILicenceApi
    {
        private readonly ILicenceApi _inner;
        public TamperingApi(ILicenceApi inner) => _inner = inner;
        public Task<SignInResponse> SignInAsync(SignInRequest r, CancellationToken c = default) => _inner.SignInAsync(r, c);
        public Task<LicenceResponse> CheckInAsync(CheckInRequest r, CancellationToken c = default) => _inner.CheckInAsync(r, c);
        public Task<RedeemKeyResponse> RedeemKeyAsync(RedeemKeyRequest r, CancellationToken c = default) => _inner.RedeemKeyAsync(r, c);
        public Task SignOutAsync(SignOutRequest r, CancellationToken c = default) => _inner.SignOutAsync(r, c);
        public Task<StaffList> StaffAsync(StaffRequest r, CancellationToken c = default) => _inner.StaffAsync(r, c);
        public Task<StaffList> SaveStaffAsync(SaveStaffRequest r, CancellationToken c = default) => _inner.SaveStaffAsync(r, c);
        public Task<StaffList> DeleteStaffAsync(DeleteStaffRequest r, CancellationToken c = default) => _inner.DeleteStaffAsync(r, c);

        public async Task<CatalogueResponse> CatalogueAsync(CatalogueRequest r, CancellationToken c = default)
        {
            var real = await _inner.CatalogueAsync(r, c);
            return new CatalogueResponse(real.LibraryJson.Replace("320.0", "1.0"));
        }
    }

    [Fact]
    public void AccessKnowsWhenTheLibraryFollowsACatalogue()
    {
        _server.Service.PublishCatalogue(SampleJson);
        _server.CreateCompany(Edit(new CompanyCatalogue(new[] { Upvc }, Array.Empty<string>())));
        var access = new AccessViewModel();

        access.Apply(LicenceEvaluator.Evaluate(SignedInLicence(), _server.Clock.Now));

        Assert.True(access.IsCatalogueManaged);
    }

    // ── MARK Owner ──────────────────────────────────────────────────

    [Fact]
    public void OwnerChoice_TicksSystemsAndExtraItems()
    {
        var master = LibrarySerializer.Deserialize(SampleJson);
        var choice = new CatalogueChoiceViewModel(master, null);
        Assert.Contains("keeps its own library", choice.SummaryText);

        choice.Systems.Single(s => s.Id == Upvc).IsChecked = true;
        var dgu = choice.ItemGroups.SelectMany(g => g.Rows).Single(r => r.Id == "GLS-DGU-24");
        Assert.True(dgu.IsCovered);                                       // comes with the uPVC system
        Assert.True(dgu.IsChecked);
        Assert.False(dgu.CanChange);
        var toughened = choice.ItemGroups.SelectMany(g => g.Rows).Single(r => r.Id == "GLS-TGH-8");
        toughened.IsChecked = true;

        var catalogue = choice.ToCatalogue();
        Assert.Equal(new[] { Upvc }, catalogue.SystemIds);
        Assert.Equal(new[] { "GLS-TGH-8" }, catalogue.ItemIds);
        Assert.StartsWith("1 system", choice.SummaryText);
    }

    [Fact]
    public void NewAccount_StartsWithItsTypesCatalogue()
    {
        var master = LibrarySerializer.Deserialize(SampleJson);
        var packages = new[] { new PackageInfo(Guid.NewGuid(), "Basic", null, StarterPackages.All[0].Features) };
        var types = new[]
        {
            new CompanyTypeInfo(Guid.NewGuid(), "uPVC fabricator", new[] { Product.Upvc }, packages[0].Id, 365,
                Catalogue: new CompanyCatalogue(new[] { Upvc }, Array.Empty<string>()))
        };

        var editor = new CompanyEditorViewModel(null, packages, types, () => new DateTime(2026, 10, 3), master);

        Assert.True(editor.CatalogueChoice!.Systems.Single(s => s.Id == Upvc).IsChecked);
        editor.Name = "X";
        editor.OwnerUserId = "xco";
        editor.Password = "secret1";
        Assert.Equal(new[] { Upvc }, editor.ToEdit(out _)!.Catalogue!.SystemIds);
    }
}
