using Mark.Core.Library;
using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>A company's own items: products the owner made for one company only, delivered with its catalogue.</summary>
public class CompanyItemsTests : IDisposable
{
    private const string Upvc = "SYS-UPVC-62C";

    private readonly TestServer _server = new();
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);
    private static readonly string SampleJson = LibrarySerializer.Serialize(Sample);

    public void Dispose() => _server.Dispose();

    /// <summary>Sozluk's own 62 mm system: its own frame (with the catalogue's steel), the catalogue's sash and mullion,
    /// and its own handle in its own hardware set.</summary>
    private static CompanyItems Sozluk()
    {
        var frame = Sample.FindProfile("PRF-UPVC-FRM-62")! with { Id = "PRF-SOZ-FRM", Name = "Sozluk frame 62" };
        var handle = Sample.FindMaterial("MAT-HANDLE")! with { Id = "MAT-SOZ-HANDLE", Name = "Sozluk brass handle" };
        var system = Sample.FindSystem(Upvc)! with { Id = "SYS-SOZ-62", Name = "Sozluk 62 Casement", FrameProfileId = frame.Id };
        var hardware = Sample.FindBundle("BND-UPVC-CASE-HW")!;
        var set = hardware with
        {
            Id = "BND-SOZ-HW", Name = "Sozluk hardware", SystemId = system.Id,
            Parts = hardware.Parts.Select(p => p.ItemId == "MAT-HANDLE" ? p with { ItemId = handle.Id } : p).ToList()
        };
        return new CompanyItems
        {
            Profiles = new[] { frame }, Materials = new[] { handle }, Systems = new[] { system }, Bundles = new[] { set },
            DefaultSystemId = system.Id
        };
    }

    private CompanyDetail Company(string name, string userId, CompanyCatalogue? catalogue = null)
        => _server.CreateCompany(_server.Edit(name, userId) with { Catalogue = catalogue });

    private ProductLibrary CatalogueOf(string userId)
    {
        var signIn = _server.SignIn(userId);
        return LibrarySerializer.Deserialize(_server.Service.ClientCatalogue(new CatalogueRequest(signIn.DeviceToken, "PC-1")).LibraryJson);
    }

    // ── Core ────────────────────────────────────────────────────────

    [Fact]
    public void Combine_AddsTheItemsToTheCatalogue_AndSplitGetsThemBack()
    {
        var own = Sozluk();

        var combined = CompanyItems.Combine(Sample, own);
        var back = CompanyItems.Split(combined, Sample);

        Assert.Equal(Sample.Systems.Count + 1, combined.Systems.Count);
        Assert.Equal("SYS-SOZ-62", combined.Defaults.SystemId);
        Assert.Equal(CompanyItems.Serialize(own), CompanyItems.Serialize(back));
        Assert.Equal(0, CompanyItems.CatalogueChanges(combined, Sample));
        Assert.Equal("1 system · 1 bundle · 1 profile · 1 hardware or accessory", own.SummaryText);
    }

    [Fact]
    public void Combine_RefusesIdsTheCatalogueUses_AndItemsThatAreMissing()
    {
        var clash = new CompanyItems { Materials = new[] { Sample.FindMaterial("MAT-HANDLE")! } };
        var missing = Sozluk() with { Profiles = Array.Empty<ProfileDefinition>() };    // the system's frame is gone

        Assert.Contains("MAT-HANDLE", Assert.Throws<InvalidOperationException>(() => CompanyItems.Combine(Sample, clash)).Message);
        Assert.Throws<LibraryValidationException>(() => CompanyItems.Combine(Sample, missing));
    }

    [Fact]
    public void Split_LeavesOutChangesToCatalogueItems_AndCountsThem()
    {
        var combined = CompanyItems.Combine(Sample, Sozluk());
        var edited = new ProductLibrary(
            combined.Profiles.Select(p => p.Id == "PRF-UPVC-SASH-62" ? p with { Name = "Renamed" } : p), combined.Glass, combined.Materials,
            combined.Defaults, combined.Currency, combined.Systems, combined.Bundles);

        Assert.Equal(1, CompanyItems.CatalogueChanges(edited, Sample));
        Assert.DoesNotContain(CompanyItems.Split(edited, Sample).Profiles, p => p.Id == "PRF-UPVC-SASH-62");
    }

    // ── Server ──────────────────────────────────────────────────────

    [Fact]
    public void OnlyThatCompany_GetsItsOwnItems_WithWhatTheyUseFromTheCatalogue()
    {
        _server.Service.PublishCatalogue(SampleJson);
        var sozluk = Company("Sozluk", "sozluk");
        Company("Shree Windows", "shree", new CompanyCatalogue(new[] { Upvc }, Array.Empty<string>()));

        _server.Service.SaveCompanyItems(sozluk.Id, CompanyItems.Serialize(Sozluk()));

        var own = CatalogueOf("sozluk");
        Assert.Equal(new[] { "SYS-SOZ-62" }, own.Systems.Select(x => x.Id));             // nothing ticked: only its own system
        Assert.Equal("SYS-SOZ-62", own.Defaults.SystemId);
        Assert.NotNull(own.FindProfile("PRF-SOZ-FRM"));
        Assert.NotNull(own.FindProfile("PRF-UPVC-SASH-62"));                             // from the catalogue, used by its system
        Assert.NotNull(own.FindProfile("PRF-RI-FRM-62"));                                // the steel its own frame uses
        Assert.NotNull(own.FindBundle("BND-SOZ-HW"));
        Assert.NotNull(own.FindMaterial("MAT-SOZ-HANDLE"));
        Assert.Equal("1 system · 1 bundle · 1 profile · 1 hardware or accessory", _server.Service.Company(sozluk.Id).OwnItemsSummary);

        var other = CatalogueOf("shree");
        Assert.Null(other.FindSystem("SYS-SOZ-62"));
        Assert.Null(other.FindMaterial("MAT-SOZ-HANDLE"));
    }

    [Fact]
    public void OwnItems_ComeOnTopOfTheTickedCatalogue_AndChangeTheLicence()
    {
        _server.Service.PublishCatalogue(SampleJson);
        var company = Company("Sozluk", "sozluk", new CompanyCatalogue(new[] { Upvc }, Array.Empty<string>()));
        string? before = _server.Read(_server.SignIn("sozluk").Licence).CatalogueHash;

        _server.Service.SaveCompanyItems(company.Id, CompanyItems.Serialize(Sozluk()));

        Assert.NotEqual(before, _server.Read(_server.SignIn("sozluk").Licence).CatalogueHash);
        Assert.Equal(new[] { Upvc, "SYS-SOZ-62" }, CatalogueOf("sozluk").Systems.Select(x => x.Id));
    }

    [Fact]
    public void SavingOwnItems_ThatDoNotFit_IsRefused()
    {
        _server.Service.PublishCatalogue(SampleJson);
        var company = Company("Sozluk", "sozluk");
        var clash = new CompanyItems { Materials = new[] { Sample.FindMaterial("MAT-HANDLE")! } };

        Assert.Throws<ApiException>(() => _server.Service.SaveCompanyItems(company.Id, CompanyItems.Serialize(clash)));
        Assert.Throws<ApiException>(() => _server.Service.SaveCompanyItems(company.Id, "{ not json"));
        Assert.Null(_server.Service.CompanyItems(company.Id).ItemsJson);
    }

    [Fact]
    public void ACatalogueThatWouldBreakACompanysOwnItems_IsNotPublished()
    {
        _server.Service.PublishCatalogue(SampleJson);
        var company = Company("Sozluk", "sozluk");
        _server.Service.SaveCompanyItems(company.Id, CompanyItems.Serialize(Sozluk()));
        // A catalogue that also has an item called MAT-SOZ-HANDLE.
        var clashing = new ProductLibrary(Sample.Profiles, Sample.Glass,
            Sample.Materials.Append(Sample.FindMaterial("MAT-HANDLE")! with { Id = "MAT-SOZ-HANDLE" }), Sample.Defaults, Sample.Currency,
            Sample.Systems, Sample.Bundles);

        var ex = Assert.Throws<ApiException>(() => _server.Service.PublishCatalogue(LibrarySerializer.Serialize(clashing)));

        Assert.Contains("Sozluk's own items", ex.Message);
        Assert.Equal(1, _server.Service.Catalogue().Version);
    }

    [Fact]
    public void EmptyOwnItems_AreRemoved()
    {
        _server.Service.PublishCatalogue(SampleJson);
        var company = Company("Sozluk", "sozluk");
        _server.Service.SaveCompanyItems(company.Id, CompanyItems.Serialize(Sozluk()));

        _server.Service.SaveCompanyItems(company.Id, CompanyItems.Serialize(CompanyItems.Empty));

        Assert.Null(_server.Service.CompanyItems(company.Id).ItemsJson);
        Assert.Null(_server.Read(_server.SignIn("sozluk").Licence).CatalogueHash);
        Assert.Equal("None", _server.Service.Company(company.Id).OwnItemsSummary);
    }
}
