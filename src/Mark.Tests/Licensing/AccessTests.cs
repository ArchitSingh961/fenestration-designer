using Mark.Core.Design;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;
using Mark.Owner.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>What MARK lets a company do under its licence, the Account page, and MARK Owner's account editor.</summary>
public class AccessTests
{
    private static readonly DateTime Now = TestClock.Start;

    private static LicenceStatus Status(IEnumerable<string> features, bool suspended = false)
    {
        var licence = new Licence
        {
            CompanyName = "Shree Windows", UserId = "shree", UserName = "Ravi", MachineId = "PC-1", IssuedUtc = Now,
            ValidUntilUtc = Now.AddYears(1), Suspended = suspended, PackageName = "Custom", MaxComputers = 2,
            Products = new[] { new ProductGrant(Product.Upvc, Now.AddYears(1)) },
            Features = FeatureCatalog.CoreIds.Concat(features).Select(f => new FeatureGrant(f, Now.AddYears(1))).ToList()
        };
        return LicenceEvaluator.Evaluate(licence, Now);
    }

    private static MainViewModel Designer(LicenceStatus status)
    {
        var vm = new MainViewModel();
        vm.Access.Apply(status);
        return vm;
    }

    // ── MARK ────────────────────────────────────────────────────────

    [Fact]
    public void WithoutALicence_EverythingIsAllowed()
    {
        var access = new AccessViewModel();

        Assert.True(access.CanUseOpenings && access.CanSeeCosting && access.CanUsePriceStructure && access.CanSeeCuttingPlans);
        Assert.False(access.IsReadOnly);
        Assert.Null(access.ReadOnlyMessage);
    }

    [Fact]
    public void AccessFollowsTheLicence()
    {
        var access = new AccessViewModel();
        int changes = 0;
        access.Changed += () => changes++;

        access.Apply(Status(new[] { Features.Openings, Features.Costing }));

        Assert.Equal(1, changes);
        Assert.True(access.CanUseOpenings);
        Assert.True(access.CanSeeCosting);
        Assert.False(access.CanUsePriceStructure);
        Assert.False(access.CanSeeCuttingPlans);
        Assert.Equal("Shree Windows", access.CompanyName);
        Assert.Contains("Cutting plans is not included", access.CuttingPlansLock);
    }

    [Fact]
    public void WithoutOpenings_OpeningsCannotBeSet_ButDividersCan()
    {
        var vm = Designer(Status(new[] { Features.DesignLibrary }));
        Assert.Null(vm.ApplyDesign(DesignTemplates.All.First(t => t.IsDividerOnly && !t.KeepsLayout)));
        var openable = DesignTemplates.All.First(t => !t.IsDividerOnly);

        Assert.Equal(vm.Access.OpeningsLock, vm.ApplyDesign(openable));
        var panel = vm.Project.Frames[0].GlassPanels[0];
        Assert.Equal(vm.Access.OpeningsLock, vm.SetOpening(new[] { panel }, OpeningType.SideHungLeft, null));
        Assert.Null(vm.SetOpening(new[] { panel }, OpeningType.Fixed, null));
    }

    [Fact]
    public void WithoutTheDesignLibrary_DesignsCannotBeApplied()
    {
        var vm = Designer(Status(new[] { Features.Openings }));

        Assert.Equal(vm.Access.DesignLibraryLock, vm.ApplyDesign(DesignTemplates.All[0]));
        Assert.Empty(vm.Project.Frames);
    }

    [Fact]
    public void ReadOnlyLicence_BlocksSavingAndDeleting()
    {
        using var db = new TempDatabase();
        var vm = new MainViewModel(db.Open(), dialogs: new FakeDialogs { PromptAnswer = "Job" });
        vm.CreateFrame();
        Assert.Null(vm.SaveProject());
        Assert.True(vm.SaveProjectCommand.CanExecute(null));

        vm.Access.Apply(Status(FeatureCatalog.All.Select(f => f.Id), suspended: true));

        Assert.False(vm.SaveProjectCommand.CanExecute(null));
        Assert.False(vm.OpenLibraryManagerCommand.CanExecute(null));
        Assert.Contains("read-only", vm.SaveProject());
        Assert.Contains("read-only", vm.SaveProjectAs("Copy"));
        Assert.True(vm.ExportProjectFileCommand.CanExecute(null));   // old quotes can still be exported
    }

    [Fact]
    public void LockedFeatures_DisableTheirCommands()
    {
        using var db = new TempDatabase();
        var vm = new MainViewModel(db.Open());

        vm.Access.Apply(Status(new[] { Features.Openings }));

        Assert.False(vm.OpenLibraryManagerCommand.CanExecute(null));
        Assert.False(vm.ImportProjectFileCommand.CanExecute(null));
        Assert.False(vm.ExportProjectFileCommand.CanExecute(null));
        Assert.True(vm.SaveProjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task AccountPage_ShowsTheLicence()
    {
        using var server = new TestServer();
        server.CreateCompany(server.Edit(package: "Basic", addOns: new[] { new AddOn(Features.CuttingPlans, server.Clock.Now.AddMonths(2)) }));
        var manager = new LicenceManager(new MemoryStateStore(), server.Verifier, "PC-1", "Office PC", _ => new DirectApi(server.Service),
            () => server.Clock.Now);
        await manager.SignInAsync(LicenceDefaults.ServerUrl, "shree", "secret1", true);

        var account = new AccountViewModel(manager, () => Task.CompletedTask, () => server.Clock.Now);

        Assert.Equal("Shree Windows", account.CompanyName);
        Assert.Equal("Basic", account.PackageName);
        Assert.Equal("Active", account.StateText);
        Assert.Equal("Ravi Shah (shree) · account owner", account.UserText);
        Assert.True(account.Products.Single(p => p.Name == "uPVC").IsValid);
        Assert.Equal("Not included", account.Products.Single(p => p.Name == "Aluminium").Detail);
        var features = account.FeatureGroups.SelectMany(g => g.Features).ToDictionary(f => f.Name);
        Assert.True(features["Openings"].IsIncluded);
        Assert.Equal("Not in your package", features["Price structure"].State);
        Assert.StartsWith("Included until", features["Cutting plans"].State);
        Assert.Equal("Coming in a later version", features["Inventory"].State);

        account.KeyText = "MARK-AAAAA-BBBBB-CCCCC-DDDDD";
        await account.RedeemKeyAsync();
        Assert.True(account.MessageIsError);
        Assert.Contains("not valid", account.Message);
    }

    // ── MARK Owner: account editor ──────────────────────────────────

    private static readonly IReadOnlyList<PackageInfo> Packages = new[]
    {
        new PackageInfo(Guid.NewGuid(), "Basic", null, StarterPackages.All[0].Features),
        new PackageInfo(Guid.NewGuid(), "Complete", null, StarterPackages.All[2].Features)
    };

    private static readonly IReadOnlyList<CompanyTypeInfo> Types = new[]
    {
        new CompanyTypeInfo(Guid.NewGuid(), "uPVC fabricator", new[] { Product.Upvc }, Packages[0].Id, 365),
        new CompanyTypeInfo(Guid.NewGuid(), "Trial", new[] { Product.Upvc, Product.Aluminium }, Packages[1].Id, 14)
    };

    private static readonly DateTime Today = new(2026, 10, 3);

    [Fact]
    public void NewAccount_StartsFromTheCompanyType()
    {
        var editor = new CompanyEditorViewModel(null, Packages, Types, () => Today);
        Assert.Equal("uPVC fabricator", editor.CompanyType!.Name);
        Assert.Equal("Basic", editor.Package!.Name);

        editor.CompanyType = Types[1];

        Assert.Equal("Complete", editor.Package!.Name);
        Assert.Equal(Today.AddDays(14), editor.ValidUntil);
        Assert.All(editor.Products, p =>
        {
            Assert.True(p.IsIncluded);
            Assert.Equal(Today.AddDays(14), p.ValidUntil);
        });
    }

    [Fact]
    public void AccountEditor_MakesTheRequest_WithRemovalsAndAddOns()
    {
        var editor = new CompanyEditorViewModel(null, Packages, Types, () => Today)
        {
            Name = "Shree Windows",
            OwnerUserId = "shree",
            Password = "secret1",
            MaxComputersText = "3"
        };
        var features = editor.FeatureGroups.SelectMany(g => g.Features).ToDictionary(f => f.Feature.Id);
        features[Features.DesignLibrary].IsIncluded = false;                         // removed from Basic
        features[Features.CuttingPlans].IsIncluded = true;                           // add-on
        features[Features.CuttingPlans].ValidUntil = Today.AddMonths(1);
        Assert.Equal("Removed", features[Features.DesignLibrary].Tag);
        Assert.Equal("Add-on", features[Features.CuttingPlans].Tag);
        Assert.False(features[Features.Quotes].CanChange);

        var edit = editor.ToEdit(out string? error);

        Assert.Null(error);
        Assert.Equal(new[] { Features.DesignLibrary }, edit!.RemovedFeatures);
        Assert.Equal(Features.CuttingPlans, edit.AddOns.Single().FeatureId);
        Assert.Equal(LicenceDates.EndOfLocalDayUtc(Today.AddMonths(1)), edit.AddOns.Single().ValidUntilUtc);
        Assert.Equal(LicenceDates.EndOfLocalDayUtc(Today.AddDays(365)), edit.ValidUntilUtc);
        Assert.Equal(3, edit.MaxComputers);
        Assert.Equal(Product.Upvc, edit.Products.Single().Product);
    }

    [Fact]
    public void AccountEditor_SaysWhatIsMissing()
    {
        var editor = new CompanyEditorViewModel(null, Packages, Types, () => Today) { Name = "X", OwnerUserId = "xco" };
        Assert.Null(editor.ToEdit(out string? error));
        Assert.Equal("Enter the password the company signs in with.", error);

        editor.Password = "secret1";
        foreach (var product in editor.Products) product.IsIncluded = false;
        Assert.Null(editor.ToEdit(out error));
        Assert.Contains("at least one product", error);
    }

    [Fact]
    public void ChangingThePackage_KeepsAddOnsThatAreNotInIt()
    {
        var editor = new CompanyEditorViewModel(null, Packages, Types, () => Today);
        var features = editor.FeatureGroups.SelectMany(g => g.Features).ToDictionary(f => f.Feature.Id);
        features[Features.CuttingPlans].IsIncluded = true;

        editor.Package = Packages[1];                                                // Complete has it
        Assert.True(features[Features.CuttingPlans].InPackage);
        Assert.False(features[Features.CuttingPlans].IsAddOn);

        editor.Package = Packages[0];
        Assert.False(features[Features.CuttingPlans].IsIncluded);
    }

    [Fact]
    public void ExtendingValidity_CountsFromTheLaterOfTodayAndTheCurrentEnd()
    {
        var editor = new CompanyEditorViewModel(null, Packages, Types, () => Today);
        var year = ValidityChoice.All.Single(v => v.Name == "1 year");

        editor.ValidUntil = Today.AddMonths(2);
        editor.ExtendCommand.Execute(year);
        Assert.Equal(Today.AddMonths(14), editor.ValidUntil);

        editor.ValidUntil = Today.AddDays(-30);
        editor.ExtendCommand.Execute(year);
        Assert.Equal(Today.AddYears(1), editor.ValidUntil);

        var aluminium = editor.Products.Single(p => p.Product == Product.Aluminium);
        aluminium.ExtendCommand.Execute(ValidityChoice.All.Single(v => v.Name == "3 months"));
        Assert.True(aluminium.IsIncluded);
        Assert.Equal(Today.AddMonths(3), aluminium.ValidUntil);
    }

    [Fact]
    public void OwnerText_DescribesTheState()
    {
        Assert.Equal("Suspended", OwnerText.StateOf(true, Now.AddYears(1), Now));
        Assert.Equal("Expired", OwnerText.StateOf(false, Now.AddDays(-1), Now));
        Assert.Equal("Ends in 3 days", OwnerText.StateOf(false, Now.AddDays(3), Now));
        Assert.Equal("Active", OwnerText.StateOf(false, Now.AddDays(30), Now));
    }
}
