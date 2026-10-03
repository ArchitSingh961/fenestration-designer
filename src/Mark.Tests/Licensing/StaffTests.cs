using Mark.Designer.ViewModels;
using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>Milestone 14: staff logins added by the account owner, their features, the users limit, and the admin's view.</summary>
public class StaffTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private static readonly string[] Cutter = { Features.CuttingPlans };

    private StaffRequest Owner(SignInResponse signIn, string machine = "PC-1") => new(signIn.DeviceToken, machine);

    private StaffList AddStaff(SignInResponse owner, string userId = "amit", IReadOnlyList<string>? permissions = null, string machine = "PC-1",
        bool disabled = false)
        => _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, machine,
            new StaffEdit(Guid.Empty, "Amit Kumar", userId, "cutter1", permissions ?? Cutter, disabled)));

    [Fact]
    public void OwnerLicence_HasEveryAccountFeature_AndTheUsersLimit()
    {
        _server.CreateCompany(_server.Edit() with { MaxUsers = 4 });

        var licence = _server.Read(_server.SignIn().Licence);

        Assert.Equal(UserRoles.Owner, licence.Role);
        Assert.False(licence.IsStaff);
        Assert.Null(licence.Permissions);
        Assert.Equal(4, licence.MaxUsers);
    }

    [Fact]
    public void StaffLogin_SignsIn_WithOnlyTheFeaturesItWasGiven()
    {
        _server.CreateCompany();
        var owner = _server.SignIn();
        AddStaff(owner, permissions: new[] { Features.CuttingPlans, Features.Invoices });

        var licence = _server.Read(_server.SignIn("amit", "cutter1", "PC-2").Licence);
        var status = LicenceEvaluator.Evaluate(licence, _server.Clock.Now);

        Assert.True(licence.IsStaff);
        Assert.Equal("Amit Kumar", licence.UserName);
        Assert.Equal("Shree Windows", licence.CompanyName);
        Assert.True(status.Allows(Features.CuttingPlans));
        Assert.False(status.Allows(Features.Quotes));             // core for the account, but not given to this login
        Assert.False(status.Allows(Features.Invoices));           // given, but not in the account's package
        Assert.True(status.IsWithheld(Features.Quotes));
        Assert.False(status.IsWithheld(Features.Invoices));
        Assert.True(status.CompanyFeatures.Contains(Features.PriceStructure));
    }

    [Fact]
    public void ChangedFeatures_ReachTheStaffComputer_AtItsNextCheckIn()
    {
        _server.CreateCompany();
        var owner = _server.SignIn();
        var staff = AddStaff(owner).Staff.Single();
        var amit = _server.SignIn("amit", "cutter1", "PC-2");

        _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, "PC-1",
            new StaffEdit(staff.Id, "Amit Kumar", "amit", null, new[] { Features.Quotes, Features.Drawing })));
        var licence = _server.Read(_server.Service.CheckIn(new CheckInRequest(amit.DeviceToken, "PC-2")).Licence);

        Assert.Equal(new[] { Features.Quotes, Features.Drawing }, licence.Permissions);
        Assert.Equal(TestClock.Start, _server.Service.ClientStaff(Owner(owner)).Staff.Single().LastSignInUtc);
    }

    [Fact]
    public void TwoLoginsOnOnePc_KeepTheirOwnSignIn_AndCountAsOneComputer()
    {
        _server.CreateCompany(_server.Edit(maxComputers: 1) with { MaxUsers = 2 });
        var owner = _server.SignIn(machine: "PC-1");
        AddStaff(owner);

        var amit = _server.SignIn("amit", "cutter1", "PC-1");             // the same computer: no second place needed

        _server.Service.CheckIn(new CheckInRequest(owner.DeviceToken, "PC-1"));   // the owner is still signed in
        _server.Service.CheckIn(new CheckInRequest(amit.DeviceToken, "PC-1"));
        Assert.Equal(1, _server.Service.Companies().Single().ComputersUsed);
        Assert.Equal(ErrorCodes.ComputerLimit, Assert.Throws<ApiException>(() => _server.SignIn("amit", "cutter1", "PC-2")).Code);
    }

    [Fact]
    public void OnlyTheAccountOwner_ManagesStaff()
    {
        _server.CreateCompany();
        var owner = _server.SignIn();
        AddStaff(owner);
        var amit = _server.SignIn("amit", "cutter1", "PC-2");

        var ex = Assert.Throws<ApiException>(() => _server.Service.ClientStaff(new StaffRequest(amit.DeviceToken, "PC-2")));
        Assert.Equal(ErrorCodes.Forbidden, ex.Code);
    }

    [Fact]
    public void UsersLimit_CountsTheOwner_AndNotTurnedOffLogins()
    {
        _server.CreateCompany(_server.Edit() with { MaxUsers = 2 });
        var owner = _server.SignIn();
        var first = AddStaff(owner, "amit").Staff.Single();

        var ex = Assert.Throws<ApiException>(() => AddStaff(owner, "neha"));
        Assert.Equal(ErrorCodes.UserLimit, ex.Code);

        AddStaff(owner, "neha", disabled: true);                  // a turned-off login takes no place
        _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, "PC-1",
            new StaffEdit(first.Id, first.Name, first.UserId, null, first.Permissions, Disabled: true)));
        var neha = _server.Service.ClientStaff(Owner(owner)).Staff.Single(s => s.UserId == "neha");
        var list = _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, "PC-1",
            new StaffEdit(neha.Id, neha.Name, neha.UserId, null, neha.Permissions, Disabled: false)));

        Assert.Equal(2, list.UsersInUse);
        Assert.Equal(2, list.MaxUsers);
    }

    [Fact]
    public void TurnedOffLogin_CannotSignIn_AndIsSignedOut()
    {
        _server.CreateCompany();
        var owner = _server.SignIn();
        var staff = AddStaff(owner).Staff.Single();
        var amit = _server.SignIn("amit", "cutter1", "PC-2");

        _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, "PC-1",
            new StaffEdit(staff.Id, staff.Name, staff.UserId, null, staff.Permissions, Disabled: true)));

        var checkIn = Assert.Throws<ApiException>(() => _server.Service.CheckIn(new CheckInRequest(amit.DeviceToken, "PC-2")));
        Assert.Equal(ErrorCodes.SignedOut, checkIn.Code);
        var signIn = Assert.Throws<ApiException>(() => _server.SignIn("amit", "cutter1", "PC-2"));
        Assert.Equal(ErrorCodes.LoginDisabled, signIn.Code);
    }

    [Fact]
    public void RemovedLogin_IsSignedOut()
    {
        _server.CreateCompany();
        var owner = _server.SignIn();
        var staff = AddStaff(owner).Staff.Single();
        var amit = _server.SignIn("amit", "cutter1", "PC-2");

        var list = _server.Service.ClientDeleteStaff(new DeleteStaffRequest(owner.DeviceToken, "PC-1", staff.Id));

        Assert.Empty(list.Staff);
        Assert.Equal(ErrorCodes.SignedOut,
            Assert.Throws<ApiException>(() => _server.Service.CheckIn(new CheckInRequest(amit.DeviceToken, "PC-2"))).Code);
        Assert.Equal(ErrorCodes.BadCredentials, Assert.Throws<ApiException>(() => _server.SignIn("amit", "cutter1")).Code);
    }

    [Fact]
    public void UserIds_AreUniqueAcrossCompaniesAndStaff()
    {
        var shree = _server.CreateCompany();
        _server.CreateCompany(_server.Edit("Om Aluminium", "omal"));
        var owner = _server.SignIn();

        Assert.Equal(ErrorCodes.Conflict, Assert.Throws<ApiException>(() => AddStaff(owner, "OMAL")).Code);
        AddStaff(owner, "amit");
        // The account owner cannot take a staff member's User ID either.
        var edit = _server.Edit(userId: "amit", password: null);
        Assert.Equal(ErrorCodes.Conflict, Assert.Throws<ApiException>(() => _server.Service.UpdateCompany(shree.Id, edit)).Code);
    }

    [Fact]
    public void NewStaff_NeedsAPassword_AndSomethingToUse()
    {
        _server.CreateCompany();
        var owner = _server.SignIn();

        Assert.Throws<ApiException>(() => _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, "PC-1",
            new StaffEdit(Guid.Empty, "Amit", "amit", null, Cutter))));
        Assert.Throws<ApiException>(() => _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, "PC-1",
            new StaffEdit(Guid.Empty, "Amit", "amit", "cutter1", Array.Empty<string>()))));
        Assert.Throws<ApiException>(() => _server.Service.ClientSaveStaff(new SaveStaffRequest(owner.DeviceToken, "PC-1",
            new StaffEdit(Guid.Empty, "Amit", "amit", "cutter1", new[] { "no.such.feature" }))));
    }

    [Fact]
    public void SuspendedAccount_CannotAddStaff()
    {
        var company = _server.CreateCompany();
        var owner = _server.SignIn();
        _server.Service.SetSuspended(company.Id, true);

        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<ApiException>(() => AddStaff(owner)).Code);
    }

    [Fact]
    public void Admin_SeesStaff_RemovesThem_AndCannotAllowFewerUsersThanInUse()
    {
        var company = _server.CreateCompany(_server.Edit() with { MaxUsers = 3 });
        var owner = _server.SignIn();
        AddStaff(owner, "amit");
        AddStaff(owner, "neha");

        var detail = _server.Service.Company(company.Id);
        Assert.Equal(3, detail.MaxUsers);
        Assert.Equal(new[] { "amit", "neha" }, detail.Staff!.Select(s => s.UserId).Order());
        Assert.Equal(3, _server.Service.Companies().Single().UsersInUse);

        Assert.Throws<ApiException>(() => _server.Service.UpdateCompany(company.Id, _server.Edit(password: null) with { MaxUsers = 2 }));
        detail = _server.Service.RemoveStaff(company.Id, detail.Staff!.First().Id);
        Assert.Single(detail.Staff!);
        Assert.Equal(2, _server.Service.UpdateCompany(company.Id, _server.Edit(password: null) with { MaxUsers = 2 }).MaxUsers);
        // Not saying how many users keeps the number.
        Assert.Equal(2, _server.Service.UpdateCompany(company.Id, _server.Edit(password: null)).MaxUsers);
    }

    [Fact]
    public void NewAccount_AllowsAsManyUsersAsComputers_UnlessSaid()
    {
        Assert.Equal(2, _server.CreateCompany().MaxUsers);
    }

    [Fact]
    public async Task LicenceManager_ManagesStaff_ForTheOwnerOnly()
    {
        _server.CreateCompany();
        var api = new DirectApi(_server.Service);
        var owner = new LicenceManager(new MemoryStateStore(), _server.Verifier, "PC-1", "PC-1", _ => api, () => _server.Clock.Now);
        await owner.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        Assert.True(owner.CanManageStaff);
        var (list, error) = await owner.SaveStaffAsync(new StaffEdit(Guid.Empty, "Amit", "amit", "cutter1", Cutter));
        Assert.Null(error);
        Assert.Single(list!.Staff);

        var staff = new LicenceManager(new MemoryStateStore(), _server.Verifier, "PC-2", "PC-2", _ => api, () => _server.Clock.Now);
        await staff.SignInAsync(LicenceDefaults.ServerUrl, "amit", "cutter1", true);
        Assert.False(staff.CanManageStaff);
        Assert.True(staff.Status!.Allows(Features.CuttingPlans));
        Assert.False(staff.Status.Allows(Features.Quotes));
        Assert.NotNull((await staff.StaffAsync()).Error);

        (list, error) = await owner.DeleteStaffAsync(list.Staff[0].Id);
        Assert.Null(error);
        Assert.Empty(list!.Staff);
    }
    // ── The Staff page in MARK ──────────────────────────────────────

    private async Task<StaffViewModel> StaffPage(Func<string, string, bool>? confirm = null)
    {
        var api = new DirectApi(_server.Service);
        var owner = new LicenceManager(new MemoryStateStore(), _server.Verifier, "PC-1", "PC-1", _ => api, () => _server.Clock.Now);
        await owner.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);
        var page = new StaffViewModel(owner, confirm);
        await page.LoadAsync();
        return page;
    }

    [Fact]
    public async Task StaffPage_AddsALogin_WithAQuickChoice()
    {
        _server.CreateCompany(_server.Edit() with { MaxUsers = 3 });
        var page = await StaffPage();
        Assert.Equal("1 of 3 logins in use (you and 0 staff)", page.UsersText);

        page.AddCommand.Execute(null);
        var editor = page.Editor!;
        editor.Name = "Amit Kumar";
        editor.UserId = "amit";
        editor.Password = "cutter1";
        page.PresetCommand.Execute(page.Presets.Single(p => p.Name == "Production"));
        await page.SaveAsync();

        Assert.Null(page.Editor);
        Assert.False(page.MessageIsError);
        var row = page.Staff.Single();
        Assert.Equal("Production", row.AreasText);
        Assert.Equal(new[] { Features.CuttingPlans }, row.Info.Permissions);   // production orders are not in the package
        Assert.Equal("2 of 3 logins in use (you and 1 staff)", page.UsersText);
    }

    [Fact]
    public async Task StaffPage_OffersOnlyTheAccountsFeatures_AndChecksTheForm()
    {
        _server.CreateCompany();
        var page = await StaffPage();

        page.AddCommand.Execute(null);

        Assert.DoesNotContain(page.Editor!.Choices, c => c.Id == Features.Invoices);   // not in Professional
        Assert.Contains(page.Editor.Choices, c => c.Id == Features.CuttingPlans);
        await page.SaveAsync();
        Assert.True(page.MessageIsError);
        Assert.Equal("Enter the person's name.", page.Message);
        Assert.Empty(page.Staff);
    }

    [Fact]
    public async Task StaffPage_ChangesAndRemovesALogin()
    {
        _server.CreateCompany();
        AddStaff(_server.SignIn("shree", "secret1", "PC-9"), machine: "PC-9");
        bool asked = false;
        var page = await StaffPage((_, _) => asked = true);

        page.EditCommand.Execute(page.Staff.Single());
        page.Editor!.Choices.Single(c => c.Id == Features.Quotes).IsChecked = true;
        await page.SaveAsync();
        Assert.Equal("Sales, Production", page.Staff.Single().AreasText);

        page.EditCommand.Execute(page.Staff.Single());
        await page.DeleteAsync();

        Assert.True(asked);
        Assert.Empty(page.Staff);
    }

    [Fact]
    public async Task StaffPage_WhenAllLoginsAreInUse_ANewLoginStartsTurnedOff()
    {
        _server.CreateCompany(_server.Edit() with { MaxUsers = 1 });
        var page = await StaffPage();

        page.AddCommand.Execute(null);

        Assert.True(page.IsFull);
        Assert.True(page.Editor!.IsDisabled);
    }
    // ── MARK Owner ──────────────────────────────────────────────────

    [Fact]
    public void OwnerEditor_SetsTheUsers_AndShowsTheStaff()
    {
        var company = _server.CreateCompany(_server.Edit() with { MaxUsers = 3 });
        AddStaff(_server.SignIn());
        var detail = _server.Service.Company(company.Id);
        var editor = new Mark.Owner.ViewModels.CompanyEditorViewModel(detail, _server.Service.Packages(), _server.Service.CompanyTypes());

        Assert.Equal("3", editor.MaxUsersText);
        Assert.Equal("2 of 3 logins in use: the account owner and 1 staff.", editor.UsersInUseText);
        Assert.Equal("Amit Kumar (amit)", editor.Staff.Single().Name);
        Assert.Equal("Production", editor.Staff.Single().Detail);

        editor.MaxUsersText = "5";
        Assert.Equal(5, editor.ToEdit(out _)!.MaxUsers);
        editor.MaxUsersText = "0";
        Assert.Null(editor.ToEdit(out string? error));
        Assert.Equal("Enter the number of users (1 to 1000).", error);
    }
}
