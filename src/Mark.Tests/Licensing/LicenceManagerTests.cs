using System.IO;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>MARK's side: signing in (online and offline), resuming, check-ins, keys, sign-out and the saved state.</summary>
public class LicenceManagerTests : IDisposable
{
    private readonly TestServer _server = new();
    private readonly MemoryStateStore _store = new();
    private readonly DirectApi _api;

    public LicenceManagerTests()
    {
        _api = new DirectApi(_server.Service);
        _server.CreateCompany();
    }

    public void Dispose() => _server.Dispose();

    private LicenceManager Manager(string machine = "PC-1", LicenceVerifier? verifier = null)
        => new(_store, verifier ?? _server.Verifier, machine, machine + " name", _ => _api, () => _server.Clock.Now);

    [Fact]
    public async Task SignIn_KeepsTheLicenceForThisComputer()
    {
        var manager = Manager();

        Assert.Null(await manager.SignInAsync("localhost:5180", "shree", "secret1", keepSignedIn: true));

        Assert.Equal(LicenceMode.Full, manager.Status!.Mode);
        Assert.Equal("Shree Windows", manager.Licence!.CompanyName);
        Assert.Equal("http://localhost:5180/", _store.State!.ServerUrl);
        Assert.Equal("shree", _store.State.UserId);
        Assert.NotNull(_store.State.DeviceToken);
        Assert.True(PasswordHasher.Verify("secret1", _store.State.PasswordHash));
    }

    [Fact]
    public async Task WrongPassword_IsReported()
    {
        string? error = await Manager().SignInAsync(LicenceDefaults.ServerUrl, "shree", "nope!!", true);

        Assert.Equal("The User ID or password is not correct.", error);
        Assert.Null(_store.State);
    }

    [Fact]
    public async Task NextStart_ResumesWithoutPassword_OnlyWhenKeptSignedIn()
    {
        await Manager().SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", keepSignedIn: true);
        Assert.True(Manager().TryResume());

        await Manager().SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", keepSignedIn: false);
        Assert.False(Manager().TryResume());
    }

    [Fact]
    public async Task LicenceCopiedToAnotherComputer_IsNotUsed()
    {
        await Manager("PC-1").SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        Assert.False(Manager("PC-2").TryResume());
    }

    [Fact]
    public async Task Offline_TheSameUserCanSignInWithTheSavedPassword()
    {
        await Manager().SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", keepSignedIn: false);
        _api.Offline = true;

        Assert.NotNull(await Manager().SignInAsync(LicenceDefaults.ServerUrl, "shree", "wrong!!", false));
        var manager = Manager();
        Assert.Null(await manager.SignInAsync(LicenceDefaults.ServerUrl, "SHREE", "secret1", false));
        Assert.Equal(LicenceMode.Full, manager.Status!.Mode);
    }

    [Fact]
    public async Task Offline_FirstSignIn_SaysTheServerCannotBeReached()
    {
        _api.Offline = true;

        string? error = await Manager().SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        Assert.Contains("could not be reached", error);
    }

    [Fact]
    public async Task CheckIn_RenewsTheGracePeriod_OfflineItRunsOut()
    {
        var manager = Manager();
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        _server.Clock.AdvanceDays(6);
        Assert.Equal(CheckInOutcome.Updated, (await manager.CheckInAsync()).Outcome);
        _server.Clock.AdvanceDays(6);
        Assert.Equal(LicenceMode.Full, manager.Refresh()!.Mode);

        _api.Offline = true;
        _server.Clock.AdvanceDays(2);
        Assert.Equal(CheckInOutcome.Offline, (await manager.CheckInAsync()).Outcome);
        Assert.True(manager.Status!.IsReadOnly);

        _api.Offline = false;
        await manager.CheckInAsync();
        Assert.Equal(LicenceMode.Full, manager.Status!.Mode);
    }

    [Fact]
    public async Task Suspension_ReachesMarkAtTheNextCheckIn()
    {
        var manager = Manager();
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);
        int changes = 0;
        manager.StatusChanged += () => changes++;

        _server.Service.SetSuspended(_server.Service.Companies().Single().Id, true);
        await manager.CheckInAsync();

        Assert.True(manager.Status!.IsReadOnly);
        Assert.Contains("suspended", manager.Status.Reason);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ComputerFreedByTheOwner_MakesMarkReadOnly_AndNeedsSignInNextTime()
    {
        var manager = Manager();
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);
        var company = _server.Service.Companies().Single();
        _server.Service.FreeComputer(company.Id, _server.Service.Company(company.Id).Computers.Single().Id);

        var result = await manager.CheckInAsync();

        Assert.Equal(CheckInOutcome.SignedOut, result.Outcome);
        Assert.True(manager.Status!.IsReadOnly);
        Assert.Contains("signed out", manager.Status.Reason);
        Assert.True(manager.Refresh()!.IsReadOnly);                // stays read-only
        Assert.False(Manager().TryResume());
    }

    [Fact]
    public async Task ClockMovedBack_MakesMarkReadOnly()
    {
        var manager = Manager();
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);
        _server.Clock.AdvanceDays(3);
        manager.Refresh();

        _server.Clock.AdvanceDays(-2);

        Assert.True(Manager().TryResume());
        Assert.True(manager.Refresh()!.IsReadOnly);
    }

    [Fact]
    public async Task LicenceKey_UpdatesTheLicence()
    {
        var manager = Manager();
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);
        var key = _server.Service.GenerateKeys(new GenerateKeysRequest(null, KeyTarget.Product, Product.Aluminium, null,
            _server.Clock.Now.AddYears(1), 1, null)).Single();

        var (ok, message) = await manager.RedeemKeyAsync(key.Key);

        Assert.True(ok, message);
        Assert.Contains(Product.Aluminium, manager.Status!.Products);
        Assert.Contains("Aluminium is now valid until", message);
        Assert.False((await manager.RedeemKeyAsync(key.Key)).Ok);
    }

    [Fact]
    public async Task SignOut_FreesTheComputer_AndRemembersTheUserId()
    {
        var manager = Manager();
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        await manager.SignOutAsync();

        Assert.Null(manager.Status);
        Assert.Null(_store.State!.Licence);
        Assert.Null(_store.State.PasswordHash);
        Assert.Equal("shree", Manager().RememberedUserId);
        Assert.Empty(_server.Service.Company(_server.Service.Companies().Single().Id).Computers);
    }

    [Fact]
    public async Task LicenceFromAServerWithAnotherKey_IsNotAccepted()
    {
        using var otherKey = LicenceSigner.CreateNew();

        string? error = await Manager(verifier: new LicenceVerifier(otherKey.PublicKey))
            .SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        Assert.Contains("cannot accept", error);
        Assert.Null(_store.State);
    }

    [Fact]
    public void StateFile_IsProtected_AndUnreadableFilesMeanSignInAgain()
    {
        string path = Path.Combine(_server.Folder, "state", "licence.dat");
        var store = new FileLicenceStateStore(path, new ReversingProtector());
        store.Save(new LicenceState { UserId = "shree", ServerUrl = "http://x/" });

        Assert.DoesNotContain("shree", File.ReadAllText(path));
        Assert.Equal("shree", store.Load()!.UserId);

        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.Null(store.Load());
        Assert.Null(new FileLicenceStateStore(Path.Combine(_server.Folder, "missing.dat"), new ReversingProtector()).Load());
    }
}
