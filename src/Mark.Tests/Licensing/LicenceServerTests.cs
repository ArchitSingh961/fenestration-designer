using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>The licence server's rules: admin, accounts, computers, licences, packages, company types and keys.</summary>
public class LicenceServerTests : IDisposable
{
    private readonly TestServer _server = new();

    private LicenceService Service => _server.Service;

    public void Dispose() => _server.Dispose();

    private static ApiException Refused(Action action) => Assert.Throws<ApiException>(action);

    // ── Admin ───────────────────────────────────────────────────────

    [Fact]
    public void NewServer_HasNoAdmin_AndStartsWithStarterPackagesAndTypes()
    {
        Assert.False(Service.HasAdmin());
        Assert.True(Service.Status(fromServerComputer: true).CanSetUpHere);
        Assert.False(Service.Status(fromServerComputer: false).CanSetUpHere);
        Assert.Equal(new[] { "Basic", "Complete", "Professional" }, Service.Packages().Select(p => p.Name));
        Assert.Contains(Service.CompanyTypes(), t => t.Name == "Trial" && t.ValidityDays == 14 && t.Products.Count == 2);
    }

    [Fact]
    public void AdminSetUp_OnlyOnce_ThenSignIn()
    {
        var session = Service.SetUp(new AdminSetupRequest("owner", "admin-pass", "Archit"));
        Assert.Equal("Archit", Service.Authenticate(session.Token)!.Name);

        Assert.Equal(ErrorCodes.Forbidden, Refused(() => Service.SetUp(new AdminSetupRequest("x", "admin-pass", "X"))).Code);
        Assert.Equal(ErrorCodes.BadCredentials, Refused(() => Service.SignIn(new AdminSignInRequest("owner", "wrong"))).Code);

        var again = Service.SignIn(new AdminSignInRequest("OWNER", "admin-pass"));
        Assert.NotNull(Service.Authenticate(again.Token));
        Service.SignOut(again.Token);
        Assert.Null(Service.Authenticate(again.Token));
    }

    [Fact]
    public void AdminSession_Expires()
    {
        var session = Service.SetUp(new AdminSetupRequest("owner", "admin-pass", "Archit"));

        _server.Clock.Advance(LicenceService.AdminSessionLength + TimeSpan.FromMinutes(1));

        Assert.Null(Service.Authenticate(session.Token));
    }

    [Fact]
    public void WrongPasswords_LockTheUserIdForAWhile()
    {
        _server.CreateCompany();
        for (int i = 0; i < LicenceService.MaxFailedAttempts; i++)
            Refused(() => _server.SignIn(password: "wrong!"));

        var locked = Refused(() => _server.SignIn());               // even the right password
        Assert.Equal(ErrorCodes.LockedOut, locked.Code);
        Assert.Equal(429, locked.Status);

        _server.Clock.Advance(TimeSpan.FromMinutes(LicenceService.LockMinutes + 1));
        Assert.NotNull(_server.SignIn());
    }

    // ── Accounts and licences ───────────────────────────────────────

    [Fact]
    public void NewAccount_SignsIn_AndGetsTheLicenceOfItsPackage()
    {
        var company = _server.CreateCompany();

        var response = _server.SignIn();
        var licence = _server.Read(response.Licence);

        Assert.Equal("Shree Windows", licence.CompanyName);
        Assert.Equal("uPVC fabricator", licence.CompanyType);
        Assert.Equal("shree", licence.UserId);
        Assert.Equal("Ravi Shah", licence.UserName);
        Assert.Equal("PC-1", licence.MachineId);
        Assert.Equal("Professional", licence.PackageName);
        Assert.Equal(_server.Clock.Now, licence.IssuedUtc);
        Assert.Equal(new[] { Product.Upvc }, licence.Products.Select(p => p.Product));
        Assert.Equal(_server.Package("Professional").Features.OrderBy(f => f, StringComparer.Ordinal),
            licence.Features.Select(f => f.FeatureId));
        Assert.False(string.IsNullOrEmpty(response.DeviceToken));
        Assert.Single(Service.Company(company.Id).Computers);
    }

    [Fact]
    public void RemovedFeaturesAndAddOns_AreInTheLicence()
    {
        var addOnUntil = _server.Clock.Now.AddMonths(3);
        _server.CreateCompany(_server.Edit(package: "Basic",
            addOns: new[] { new AddOn(Features.CuttingPlans, addOnUntil) },
            removed: new[] { Features.DesignLibrary }));

        var licence = _server.Read(_server.SignIn().Licence);

        var ids = licence.Features.Select(f => f.FeatureId).ToList();
        Assert.DoesNotContain(Features.DesignLibrary, ids);
        Assert.Contains(Features.Openings, ids);
        Assert.Equal(addOnUntil, licence.Features.Single(f => f.FeatureId == Features.CuttingPlans).ValidUntilUtc);
    }

    [Fact]
    public void CoreFeatures_CannotBeRemoved()
    {
        var error = Refused(() => _server.CreateCompany(_server.Edit(removed: new[] { Features.Quotes })));
        Assert.Equal(ErrorCodes.Invalid, error.Code);
    }

    [Fact]
    public void WrongUserIdOrPassword_IsRefusedTheSameWay()
    {
        _server.CreateCompany();

        Assert.Equal(ErrorCodes.BadCredentials, Refused(() => _server.SignIn(userId: "nobody")).Code);
        Assert.Equal(ErrorCodes.BadCredentials, Refused(() => _server.SignIn(password: "wrong!")).Code);
    }

    [Fact]
    public void ComputerLimit_IsEnforced_AndFreeingAComputerMakesRoom()
    {
        var company = _server.CreateCompany(_server.Edit(maxComputers: 1));
        _server.SignIn(machine: "PC-1");
        _server.SignIn(machine: "PC-1");                            // the same computer again is fine

        var refused = Refused(() => _server.SignIn(machine: "PC-2"));
        Assert.Equal(ErrorCodes.ComputerLimit, refused.Code);
        Assert.Contains("1 computer", refused.Message);

        var computer = Service.Company(company.Id).Computers.Single();
        Service.FreeComputer(company.Id, computer.Id);
        Assert.NotNull(_server.SignIn(machine: "PC-2"));
    }

    [Fact]
    public void FreedComputer_IsSignedOutAtItsNextCheckIn()
    {
        var company = _server.CreateCompany();
        var response = _server.SignIn();
        Service.FreeComputer(company.Id, Service.Company(company.Id).Computers.Single().Id);

        var error = Refused(() => Service.CheckIn(new CheckInRequest(response.DeviceToken, "PC-1")));

        Assert.Equal(ErrorCodes.SignedOut, error.Code);
    }

    [Fact]
    public void CheckIn_ReflectsSuspensionAndChanges()
    {
        var company = _server.CreateCompany();
        var response = _server.SignIn();
        _server.Clock.AdvanceDays(1);

        Service.SetSuspended(company.Id, true);
        var suspended = _server.Read(Service.CheckIn(new CheckInRequest(response.DeviceToken, "PC-1")).Licence);
        Assert.True(suspended.Suspended);
        Assert.Equal(_server.Clock.Now, suspended.IssuedUtc);
        Assert.True(LicenceEvaluator.Evaluate(suspended, _server.Clock.Now).IsReadOnly);

        Service.SetSuspended(company.Id, false);
        Service.UpdateCompany(company.Id, _server.Edit(name: "Shree Windows & Doors", password: null, package: "Complete",
            products: new[] { new ProductLicence(Product.Upvc, _server.Clock.Now.AddYears(1)), new ProductLicence(Product.Aluminium, _server.Clock.Now.AddDays(30)) }));
        var changed = _server.Read(Service.CheckIn(new CheckInRequest(response.DeviceToken, "PC-1")).Licence);
        Assert.False(changed.Suspended);
        Assert.Equal("Shree Windows & Doors", changed.CompanyName);
        Assert.Equal("Complete", changed.PackageName);
        Assert.Equal(2, changed.Products.Count);
    }

    [Fact]
    public void SuspendedProduct_IsLeftOutOfTheLicence()
    {
        _server.CreateCompany(_server.Edit(products: new[]
        {
            new ProductLicence(Product.Upvc, _server.Clock.Now.AddYears(1)),
            new ProductLicence(Product.Aluminium, _server.Clock.Now.AddYears(1), Suspended: true)
        }));

        var licence = _server.Read(_server.SignIn().Licence);

        Assert.Equal(new[] { Product.Upvc }, licence.Products.Select(p => p.Product));
    }

    [Fact]
    public void PasswordReset_ReplacesTheOldPassword()
    {
        var company = _server.CreateCompany();

        Service.UpdateCompany(company.Id, _server.Edit(password: "new-pass"));

        Refused(() => _server.SignIn(password: "secret1"));
        Assert.NotNull(_server.SignIn(password: "new-pass"));
    }

    [Fact]
    public void UserIds_AreUniqueAcrossCompanies()
    {
        _server.CreateCompany();

        var error = Refused(() => _server.CreateCompany(_server.Edit(name: "Other Co", userId: "SHREE")));

        Assert.Equal(ErrorCodes.Conflict, error.Code);
    }

    [Fact]
    public void InvalidAccounts_AreRefused()
    {
        Refused(() => _server.CreateCompany(_server.Edit(name: " ")));
        Refused(() => _server.CreateCompany(_server.Edit(userId: "a b")));
        Refused(() => _server.CreateCompany(_server.Edit(password: "123")));
        Refused(() => _server.CreateCompany(_server.Edit(products: Array.Empty<ProductLicence>())));
        Refused(() => _server.CreateCompany(_server.Edit(maxComputers: 0)));
        Refused(() => _server.CreateCompany(_server.Edit(logo: Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }))));
        Assert.Empty(Service.Companies());
    }

    [Fact]
    public void Logo_IsKeptAndSentInTheLicence()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        string logo = Convert.ToBase64String(png);
        _server.CreateCompany(_server.Edit(logo: logo));

        Assert.Equal(logo, _server.Read(_server.SignIn().Licence).LogoBase64);
        Assert.True(Service.Companies().Single().HasLogo);
    }

    [Fact]
    public void CompanyList_ShowsComputersAndLastCheckIn()
    {
        var company = _server.CreateCompany();
        var response = _server.SignIn();
        _server.Clock.AdvanceDays(2);
        Service.CheckIn(new CheckInRequest(response.DeviceToken, "PC-1"));

        var summary = Service.Companies().Single();

        Assert.Equal(company.Id, summary.Id);
        Assert.Equal("shree", summary.OwnerUserId);
        Assert.Equal("Professional", summary.PackageName);
        Assert.Equal(1, summary.ComputersUsed);
        Assert.Equal(_server.Clock.Now, summary.LastCheckInUtc);
    }

    [Fact]
    public void DeletedCompany_IsSignedOut_AndItsUserIdIsFree()
    {
        var company = _server.CreateCompany();
        var response = _server.SignIn();

        Service.DeleteCompany(company.Id);

        Assert.Equal(ErrorCodes.SignedOut, Refused(() => Service.CheckIn(new CheckInRequest(response.DeviceToken, "PC-1"))).Code);
        Assert.NotNull(_server.CreateCompany());
    }

    // ── Packages and company types ──────────────────────────────────

    [Fact]
    public void ChangedPackage_ReachesItsCompaniesAtTheNextCheckIn()
    {
        _server.CreateCompany(_server.Edit(package: "Basic"));
        var response = _server.SignIn();
        var basic = _server.Package("Basic");

        Service.SavePackage(basic with { Features = basic.Features.Append(Features.CuttingPlans).ToList() });
        var licence = _server.Read(Service.CheckIn(new CheckInRequest(response.DeviceToken, "PC-1")).Licence);

        Assert.Contains(licence.Features, f => f.FeatureId == Features.CuttingPlans);
    }

    [Fact]
    public void Packages_AlwaysHaveTheCoreFeatures_AndUsedOnesCannotBeDeleted()
    {
        var created = Service.SavePackage(new PackageInfo(Guid.Empty, "Starter", null, new[] { Features.Openings }));

        Assert.Equal(new[] { Features.Quotes, Features.Drawing, Features.Openings }, created.Features);
        Assert.Equal(ErrorCodes.Conflict, Refused(() => Service.SavePackage(new PackageInfo(Guid.Empty, "starter", null, Array.Empty<string>()))).Code);

        _server.CreateCompany(_server.Edit(package: "Starter"));
        Assert.Equal(ErrorCodes.Conflict, Refused(() => Service.DeletePackage(created.Id)).Code);
        Assert.Equal(1, Service.Packages().Single(p => p.Name == "Starter").UsedBy);
    }

    [Fact]
    public void CompanyTypes_CanBeAddedChangedAndDeleted()
    {
        var type = Service.SaveCompanyType(new CompanyTypeInfo(Guid.Empty, "Dealer", new[] { Product.Aluminium }, _server.Package("Basic").Id, 180));
        Assert.Equal(180, type.ValidityDays);

        var changed = Service.SaveCompanyType(type with { ValidityDays = 90 });
        Assert.Equal(90, changed.ValidityDays);

        Refused(() => Service.SaveCompanyType(type with { Id = Guid.Empty, Name = "Empty", Products = Array.Empty<Product>() }));
        Service.DeleteCompanyType(type.Id);
        Assert.DoesNotContain(Service.CompanyTypes(), t => t.Name == "Dealer");
    }

    // ── Licence keys ────────────────────────────────────────────────

    private LicenceKeyInfo Key(KeyTarget target, DateTime until, Guid? company = null, Product? product = null, string? feature = null)
        => Service.GenerateKeys(new GenerateKeysRequest(company, target, product, feature, until, 1, "test")).Single();

    [Fact]
    public void AccountKey_ExtendsTheAccount_AndWorksOnlyOnce()
    {
        _server.CreateCompany(_server.Edit(validUntil: _server.Clock.Now.AddDays(10)));
        var response = _server.SignIn();
        var until = _server.Clock.Now.AddYears(1);
        var key = Key(KeyTarget.Account, until);

        var redeemed = Service.RedeemKey(new RedeemKeyRequest(response.DeviceToken, "PC-1", key.Key.ToLowerInvariant()));

        Assert.Equal(until, _server.Read(redeemed.Licence).ValidUntilUtc);
        Assert.Contains("valid until", redeemed.Message);
        Assert.Equal(KeyState.Used, Service.Keys().Single().State);
        Assert.Equal("Shree Windows", Service.Keys().Single().UsedByCompany);
        Assert.Equal(ErrorCodes.KeyInvalid, Refused(() => Service.RedeemKey(new RedeemKeyRequest(response.DeviceToken, "PC-1", key.Key))).Code);
    }

    [Fact]
    public void ProductKey_AddsTheProduct_AndNeverShortens()
    {
        _server.CreateCompany();
        var response = _server.SignIn();
        var aluminium = Key(KeyTarget.Product, _server.Clock.Now.AddMonths(6), product: Product.Aluminium);
        var shortUpvc = Key(KeyTarget.Product, _server.Clock.Now.AddMonths(1), product: Product.Upvc);

        Service.RedeemKey(new RedeemKeyRequest(response.DeviceToken, "PC-1", aluminium.Key));
        var licence = _server.Read(Service.RedeemKey(new RedeemKeyRequest(response.DeviceToken, "PC-1", shortUpvc.Key)).Licence);

        Assert.Equal(2, licence.Products.Count);
        Assert.Equal(_server.Clock.Now.AddYears(1), licence.Products.Single(p => p.Product == Product.Upvc).ValidUntilUtc);
    }

    [Fact]
    public void FeatureKey_AddsAnAddOn()
    {
        var company = _server.CreateCompany(_server.Edit(package: "Basic", removed: new[] { Features.Openings }));
        var response = _server.SignIn();
        var key = Key(KeyTarget.Feature, _server.Clock.Now.AddMonths(3), feature: Features.Openings);

        var licence = _server.Read(Service.RedeemKey(new RedeemKeyRequest(response.DeviceToken, "PC-1", key.Key)).Licence);

        Assert.Contains(licence.Features, f => f.FeatureId == Features.Openings);
        Assert.Empty(Service.Company(company.Id).RemovedFeatures);
    }

    [Fact]
    public void KeysForAnotherCompany_RevokedOrExpired_AreRefused()
    {
        var other = _server.CreateCompany(_server.Edit(name: "Other Co", userId: "other"));
        _server.CreateCompany();
        var response = _server.SignIn();
        string token = response.DeviceToken;

        var forOther = Key(KeyTarget.Account, _server.Clock.Now.AddYears(1), company: other.Id);
        Assert.Contains("another company", Refused(() => Service.RedeemKey(new RedeemKeyRequest(token, "PC-1", forOther.Key))).Message);

        var revoked = Key(KeyTarget.Account, _server.Clock.Now.AddYears(1));
        Service.RevokeKey(revoked.Id);
        Assert.Contains("cancelled", Refused(() => Service.RedeemKey(new RedeemKeyRequest(token, "PC-1", revoked.Key))).Message);

        var expiring = Key(KeyTarget.Account, _server.Clock.Now.AddDays(1));
        _server.Clock.AdvanceDays(2);
        Assert.Equal(KeyState.Expired, Service.Keys().Single(k => k.Id == expiring.Id).State);
        Assert.Contains("expired", Refused(() => Service.RedeemKey(new RedeemKeyRequest(token, "PC-1", expiring.Key))).Message);

        Assert.Contains("not valid", Refused(() => Service.RedeemKey(new RedeemKeyRequest(token, "PC-1", "MARK-AAAAA-BBBBB-CCCCC-DDDDD"))).Message);
    }

    [Fact]
    public void GeneratingKeys_IsValidated()
    {
        Refused(() => Service.GenerateKeys(new GenerateKeysRequest(null, KeyTarget.Account, null, null, _server.Clock.Now.AddDays(-1), 1, null)));
        Refused(() => Service.GenerateKeys(new GenerateKeysRequest(null, KeyTarget.Product, null, null, _server.Clock.Now.AddDays(1), 1, null)));
        Refused(() => Service.GenerateKeys(new GenerateKeysRequest(null, KeyTarget.Feature, null, "nope", _server.Clock.Now.AddDays(1), 1, null)));
        Refused(() => Service.GenerateKeys(new GenerateKeysRequest(null, KeyTarget.Account, null, null, _server.Clock.Now.AddDays(1), 0, null)));

        var keys = Service.GenerateKeys(new GenerateKeysRequest(null, KeyTarget.Account, null, null, _server.Clock.Now.AddDays(1), 5, null));
        Assert.Equal(5, keys.Select(k => k.Key).Distinct().Count());
    }
}
