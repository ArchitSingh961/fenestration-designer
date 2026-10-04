using Mark.Core.Quotes;
using Mark.Designer.ViewModels;
using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>A company's quotation details (details, brand, bank, last page): set by the admin only, shown read-only in MARK.</summary>
public class QuotationProfileTests : IDisposable
{
    private readonly TestServer _server = new();
    private readonly TempDatabase _temp = new();

    public void Dispose()
    {
        _server.Dispose();
        _temp.Dispose();
    }

    private static readonly QuotationProfile Profile = new()
    {
        PartnerLabel = "Authorised partner", Address = "Plot 1, Industrial Area\nJaipur", Phone = "+91 90000 00000", Gstin = "08abcde1234f1z5",
        BrandName = "Sample Systems", BankAccountName = "Shree Windows", BankAccountNumber = "000111222333", BankIfsc = "smpl0000001"
    };

    [Fact]
    public void TheAdmin_SetsTheDetails_AndTheLicenceCarriesTheirFingerprint()
    {
        var company = _server.CreateCompany(_server.Edit() with { Profile = Profile });
        var signIn = _server.SignIn();
        var licence = _server.Read(signIn.Licence);

        var detail = _server.Service.Company(company.Id);
        Assert.Equal("08ABCDE1234F1Z5", detail.Profile!.Gstin);                       // cleaned up
        Assert.Equal("SMPL0000001", detail.Profile.BankIfsc);
        string json = _server.Service.ClientProfile(new CatalogueRequest(signIn.DeviceToken, "PC-1")).ProfileJson;
        Assert.Equal(CatalogueHash.Of(json), licence.ProfileHash);
    }

    [Fact]
    public void NoDetails_NoFingerprint_AndLeavingThemOutKeepsThem()
    {
        var company = _server.CreateCompany();
        Assert.Null(_server.Read(_server.SignIn().Licence).ProfileHash);

        _server.Service.UpdateCompany(company.Id, _server.Edit(password: null) with { Profile = Profile });
        _server.Service.UpdateCompany(company.Id, _server.Edit(password: null));          // no profile in the edit: kept

        Assert.Equal("Sample Systems", _server.Service.Company(company.Id).Profile!.BrandName);
        _server.Service.UpdateCompany(company.Id, _server.Edit(password: null) with { Profile = QuotationProfile.Empty });
        Assert.True(_server.Service.Company(company.Id).Profile!.IsEmpty);
        Assert.Null(_server.Read(_server.SignIn().Licence).ProfileHash);
    }

    [Fact]
    public void PicturesMustBeImages()
    {
        var bad = Profile with { BrandLogoBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }) };

        Assert.Throws<ApiException>(() => _server.CreateCompany(_server.Edit() with { Profile = bad }));
    }

    [Fact]
    public async Task Mark_DownloadsTheDetails_ShowsThemReadOnly_AndPrintsThem()
    {
        _server.CreateCompany(_server.Edit() with { Profile = Profile });
        var api = new DirectApi(_server.Service);
        var manager = new LicenceManager(new MemoryStateStore(), _server.Verifier, "PC-1", "PC-1", _ => api, () => _server.Clock.Now);
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var sync = new CatalogueSync(manager, store);

        Assert.True(sync.IsDue);
        Assert.Null(await sync.SyncAsync());
        Assert.False(sync.IsDue);

        var vm = new MainViewModel(store);
        vm.Access.Apply(manager.Status);
        var setup = vm.QuotationSetup;
        setup.Load();
        Assert.True(setup.IsManaged);
        Assert.Equal("Sample Systems", setup.BrandName);
        Assert.Equal("08ABCDE1234F1Z5", setup.Gstin);
        setup.Terms = "Only our terms.";
        setup.BrandName = "Changed in MARK";                                          // not editable, and not kept
        setup.Save();
        Assert.Equal("Only our terms.", store.Settings.LoadQuotationSettings().Terms);

        vm.CommandHistory.Execute(Mark.Core.Commands.CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        var doc = Sales.QuotationPdfTests.OnSta(vm.BuildQuotation);
        Assert.Equal("Shree Windows", doc.Company.Name);
        Assert.Equal("Authorised partner", doc.Company.PartnerLabel);
        Assert.Contains("GSTIN : 08ABCDE1234F1Z5", doc.Company.Lines);
        Assert.Equal("Sample Systems", doc.Brand!.Name);
        Assert.Contains(doc.Bank!.Rows, r => r.Value == "000111222333");
        Assert.Equal(new[] { "Only our terms." }, doc.Terms);
    }

    [Fact]
    public void WithoutAnAccount_TheCompanyFillsThemIn()
    {
        var vm = new MainViewModel(_temp.Open());

        vm.QuotationSetup.Load();

        Assert.False(vm.QuotationSetup.IsManaged);
        Assert.True(vm.QuotationSetup.CanEditDetails);
    }

    [Fact]
    public void TheAccountEditor_SendsTheDetails()
    {
        var company = _server.CreateCompany(_server.Edit() with { Profile = Profile });
        var editor = new Mark.Owner.ViewModels.CompanyEditorViewModel(_server.Service.Company(company.Id), _server.Service.Packages(),
            _server.Service.CompanyTypes());

        Assert.Equal("Sample Systems", editor.BrandName);
        editor.BankBranch = "Main road";
        Assert.Equal("Main road", editor.ToEdit(out _)!.Profile!.BankBranch);
    }
}
