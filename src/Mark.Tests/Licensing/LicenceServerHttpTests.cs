using System.IO;
using System.Net.Http;
using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>
/// The whole flow over HTTP against a real licence server on a free local port: MARK Owner sets up the admin and
/// creates an account; MARK signs in, checks in and uses a key; the owner suspends and frees the computer.
/// </summary>
public class LicenceServerHttpTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mark-tests", "http-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private string _url = "";
    private LicenceVerifier _verifier = null!;

    public async Task InitializeAsync()
    {
        var options = new ServerOptions { DataFolder = _folder, Urls = "http://127.0.0.1:0" };
        _app = LicenceServerApp.Create(options);
        await _app.StartAsync();
        _url = _app.Urls.First();
        using var signer = LicenceSigner.FromPem(File.ReadAllText(options.KeyPath));
        _verifier = new LicenceVerifier(signer.PublicKey);
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

    [Fact]
    public async Task OwnerCreatesAnAccount_MarkSignsInAndFollowsTheOwnersChanges()
    {
        // MARK Owner: first run on the server's computer creates the admin.
        using var owner = new OwnerApiClient(_url);
        var status = await owner.StatusAsync();
        Assert.False(status.HasAdmin);
        Assert.True(status.CanSetUpHere);
        Assert.False(status.PublicKeyMatchesMark);                 // a test server has its own key
        await owner.SetUpAsync(new AdminSetupRequest("owner", "admin-pass", "Archit"));

        var packages = await owner.PackagesAsync();
        var types = await owner.CompanyTypesAsync();
        var created = await owner.CreateCompanyAsync(new CompanyEdit("Shree Windows", null, types.First(t => t.Name == "uPVC fabricator").Id,
            "Ravi", "shree", "secret1", new[] { new ProductLicence(Product.Upvc, DateTime.UtcNow.AddYears(1)) },
            packages.First(p => p.Name == "Basic").Id, DateTime.UtcNow.AddYears(1), 2, Array.Empty<AddOn>(), Array.Empty<string>(), null));
        Assert.Single(await owner.CompaniesAsync());

        // MARK signs in.
        var store = new MemoryStateStore();
        using var client = new LicenceApiClient(_url);
        var mark = new LicenceManager(store, _verifier, "PC-1", "Office PC", _ => client);
        Assert.Null(await mark.SignInAsync(_url, "shree", "secret1", true));
        Assert.Equal(LicenceMode.Full, mark.Status!.Mode);
        Assert.False(mark.Status.Allows(Features.CuttingPlans));
        Assert.Equal("Office PC", (await owner.CompanyAsync(created.Id)).Computers.Single().Name);

        // A key for cutting plans.
        var key = (await owner.GenerateKeysAsync(new GenerateKeysRequest(created.Id, KeyTarget.Feature, null, Features.CuttingPlans,
            DateTime.UtcNow.AddMonths(3), 1, null))).Single();
        var (ok, message) = await mark.RedeemKeyAsync(key.Key);
        Assert.True(ok, message);
        Assert.True(mark.Status.Allows(Features.CuttingPlans));

        // Suspended, then the computer freed.
        await owner.SetSuspendedAsync(created.Id, true);
        Assert.Equal(CheckInOutcome.Updated, (await mark.CheckInAsync()).Outcome);
        Assert.True(mark.Status.IsReadOnly);

        var computer = (await owner.CompanyAsync(created.Id)).Computers.Single();
        await owner.FreeComputerAsync(created.Id, computer.Id);
        Assert.Equal(CheckInOutcome.SignedOut, (await mark.CheckInAsync()).Outcome);
    }

    [Fact]
    public async Task AdminCalls_NeedASignedInAdmin()
    {
        using var stranger = new OwnerApiClient(_url);

        var error = await Assert.ThrowsAsync<LicenceServerException>(() => stranger.CompaniesAsync());

        Assert.Equal(ErrorCodes.Unauthorized, error.Code);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.Status);
    }

    [Fact]
    public async Task Errors_ComeBackAsMessages()
    {
        using var owner = new OwnerApiClient(_url);
        await owner.SetUpAsync(new AdminSetupRequest("owner", "admin-pass", "Archit"));

        var error = await Assert.ThrowsAsync<LicenceServerException>(() => owner.SignInAsync(new AdminSignInRequest("owner", "wrong")));
        Assert.Equal(ErrorCodes.BadCredentials, error.Code);
        Assert.Equal("The User ID or password is not correct.", error.Message);

        var invalid = await Assert.ThrowsAsync<LicenceServerException>(() => owner.SavePackageAsync(new PackageInfo(Guid.Empty, "", null, Array.Empty<string>())));
        Assert.Equal(ErrorCodes.Invalid, invalid.Code);
    }

    [Fact]
    public async Task UnreachableServer_IsAConnectionFailure()
    {
        using var client = new LicenceApiClient("http://127.0.0.1:1");

        var error = await Assert.ThrowsAsync<LicenceServerException>(() =>
            client.CheckInAsync(new CheckInRequest("token", "PC-1")));

        Assert.True(error.IsConnectionFailure);
    }

    [Fact]
    public void ServerAddresses_AreNormalised()
    {
        Assert.Equal("http://localhost:5180/", JsonApi.NormaliseUrl(" localhost:5180 "));
        Assert.Equal("https://licence.example.com/", JsonApi.NormaliseUrl("https://licence.example.com/"));
        Assert.Throws<ArgumentException>(() => JsonApi.NormaliseUrl(""));
        Assert.Throws<ArgumentException>(() => JsonApi.NormaliseUrl("ftp://x"));
    }
    [Fact]
    public async Task TheAccountPage_OpensInPlaceOfTheList_AndBackLetsTheSameCompanyOpenAgain()
    {
        using var owner = new OwnerApiClient(_url);
        await owner.SetUpAsync(new AdminSetupRequest("owner", "admin-pass", "Archit"));
        var packages = await owner.PackagesAsync();
        var types = await owner.CompanyTypesAsync();
        await owner.CreateCompanyAsync(new CompanyEdit("Shree Windows", null, types.First().Id, "Ravi", "shree", "secret1",
            new[] { new ProductLicence(Product.Upvc, DateTime.UtcNow.AddYears(1)) }, packages.First().Id, DateTime.UtcNow.AddYears(1), 2,
            Array.Empty<AddOn>(), Array.Empty<string>(), null));
        var page = new Mark.Owner.ViewModels.CompaniesViewModel(owner, new NoDialogs(), () => { }, () => packages, () => types);
        await page.LoadAsync();

        page.Selected = page.Companies.Single();
        await Until(() => page.HasEditor);
        Assert.Equal("Shree Windows", page.Editor!.HeaderName);
        Assert.Equal("SW", page.Editor.Initials);
        Assert.StartsWith("User ID shree", page.Editor.SubtitleText);

        page.CancelCommand.Execute(null);                                   // ← All companies
        Assert.False(page.HasEditor);
        Assert.Null(page.Selected);

        page.Selected = page.Companies.Single();                            // the same one again
        await Until(() => page.HasEditor);

        page.NewAccountCommand.Execute(null);
        Assert.Equal("New account", page.Editor!.HeaderName);
        Assert.Equal("+", page.Editor.Initials);
        page.Editor.Name = "Om Glass House";
        Assert.Equal("Om Glass House", page.Editor.HeaderName);
        Assert.Equal("OG", page.Editor.Initials);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition());
    }

    private sealed class NoDialogs : Mark.Owner.ViewModels.IOwnerDialogs
    {
        public bool Confirm(string title, string message) => true;
        public string? ChooseImageFile() => null;
        public void CopyText(string text) { }
    }
}
