using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>Milestone 14: MARK in areas, what each login sees, and that a login cannot reach what it was not given.</summary>
public class AreaTests
{
    private static readonly DateTime Now = TestClock.Start;

    private static LicenceStatus Status(IEnumerable<string> features, IReadOnlyList<string>? permissions = null, bool suspended = false)
    {
        var licence = new Licence
        {
            CompanyName = "Shree Windows", UserId = permissions is null ? "shree" : "amit", UserName = permissions is null ? "Ravi" : "Amit",
            Role = permissions is null ? UserRoles.Owner : UserRoles.Staff, Permissions = permissions,
            MachineId = "PC-1", IssuedUtc = Now, ValidUntilUtc = Now.AddYears(1), Suspended = suspended, MaxComputers = 2, MaxUsers = 3,
            Products = new[] { new ProductGrant(Product.Upvc, Now.AddYears(1)) },
            Features = FeatureCatalog.CoreIds.Concat(features).Distinct().Select(f => new FeatureGrant(f, Now.AddYears(1))).ToList()
        };
        return LicenceEvaluator.Evaluate(licence, Now);
    }

    private static readonly string[] Professional =
    {
        Features.Openings, Features.DesignLibrary, Features.Costing, Features.PriceStructure, Features.LibraryManager,
        Features.CuttingPlans, Features.ProjectFiles
    };

    private static MainViewModel Designer(LicenceStatus? status)
    {
        var vm = new MainViewModel();
        vm.Access.Apply(status);
        return vm;
    }

    private static IEnumerable<AppArea> Visible(MainViewModel vm) => vm.Areas.Where(a => a.IsVisible).Select(a => a.Area);

    private static IEnumerable<string> TabTitles(MainViewModel vm) => vm.Tabs.Select(t => t.Title);

    [Fact]
    public void WithoutALicence_EveryAreaIsOpen()
    {
        var vm = Designer(null);

        Assert.Equal(Enum.GetValues<AppArea>(), Visible(vm));
        Assert.All(vm.Areas, a => Assert.False(a.IsLocked));
        vm.ShowArea(AppArea.Sales);
        Assert.Equal(new[] { "Dashboard", "Enquiries", "Quotes", "Client", "Designs", "Documents", "Quotation setup" }, TabTitles(vm));
    }

    [Fact]
    public void EachArea_OpensItsPages()
    {
        var vm = Designer(Status(Professional));

        vm.ShowArea(AppArea.Sales);
        Assert.Equal(AppPage.Dashboard, vm.Page);
        vm.ShowArea(AppArea.Design);                     // the designer was open: Design opens where it was
        Assert.Equal((AppPage.Quote, QuoteSection.Drawing), (vm.Page, vm.Section));
        vm.ShowView(AppView.Designs);
        Assert.Equal((AppArea.Design, QuoteSection.Designs), (vm.Area, vm.Section));
        vm.ShowArea(AppArea.Pricing);
        Assert.Equal((AppPage.Quote, QuoteSection.Pricing), (vm.Page, vm.Section));
        vm.ShowView(AppView.Materials);
        Assert.Equal(QuoteSection.Materials, vm.Section);
        vm.ShowArea(AppArea.Library);
        Assert.Equal(AppPage.Library, vm.Page);
        vm.ShowArea(AppArea.Production);
        Assert.Equal((AppPage.Quote, QuoteSection.Cutting), (vm.Page, vm.Section));
        vm.ShowArea(AppArea.Orders);
        Assert.Equal(AppPage.Orders, vm.Page);                                     // built (Milestone 17); locked in this package
        Assert.False(vm.Access.CanManageOrders);
        vm.ShowArea(AppArea.Purchasing);
        Assert.Equal(AppPage.PurchaseOrders, vm.Page);                             // built (Milestone 18); locked in this package
        Assert.False(vm.Access.CanUsePurchasing);
        vm.ShowArea(AppArea.Inventory);
        Assert.Equal(AppPage.Stock, vm.Page);
        vm.ShowArea(AppArea.Accounts);
        Assert.Equal(AppPage.Overview, vm.Page);
        Assert.Contains(vm.OverviewFeatures, f => f.Name == "Invoices and payments" && f.State.Contains("Coming in a later version"));
    }

    [Fact]
    public void AnArea_RemembersItsLastTab_AndTheSelectionFollows()
    {
        var vm = Designer(Status(Professional));
        vm.ShowArea(AppArea.Sales);
        vm.ShowView(AppView.Quotes);
        vm.ShowArea(AppArea.Design);

        vm.ShowArea(AppArea.Sales);

        Assert.Equal(AppPage.Quotes, vm.Page);
        Assert.Equal(AppArea.Sales, vm.Area);
        Assert.Single(vm.Areas, a => a.IsSelected);
        Assert.True(vm.Areas.Single(a => a.Area == AppArea.Sales).IsSelected);
        Assert.Equal("Quotes", vm.Tabs.Single(t => t.IsSelected).Title);
    }

    [Fact]
    public void Designs_StaysInTheAreaItWasOpenedFrom()
    {
        var vm = Designer(Status(Professional));

        vm.ShowArea(AppArea.Sales);
        vm.ShowView(AppView.Designs);
        Assert.Equal(AppArea.Sales, vm.Area);

        vm.ShowArea(AppArea.Design);
        vm.ShowView(AppView.Designs);
        Assert.Equal(AppArea.Design, vm.Area);
    }

    [Fact]
    public void ChangingThePageFromCode_MovesToItsArea()
    {
        var vm = Designer(Status(Professional));
        vm.ShowArea(AppArea.Sales);

        vm.Page = AppPage.Quote;
        vm.Section = QuoteSection.Drawing;               // e.g. "Edit design" on a design card

        Assert.Equal(AppArea.Design, vm.Area);
        Assert.Equal("Drawing", vm.Tabs.Single(t => t.IsSelected).Title);
    }

    [Fact]
    public void AccountOwner_SeesWhatIsNotInThePackage_Locked()
    {
        var vm = Designer(Status(new[] { Features.Openings, Features.Costing }));    // like Basic

        Assert.Equal(Enum.GetValues<AppArea>(), Visible(vm));
        Assert.True(vm.Areas.Single(a => a.Area == AppArea.Production).IsLocked);
        Assert.True(vm.Areas.Single(a => a.Area == AppArea.Library).IsLocked);
        vm.ShowArea(AppArea.Pricing);
        Assert.True(vm.Tabs.Single(t => t.Title == "Price").IsLocked);
        Assert.False(vm.Tabs.Single(t => t.Title == "Bill of materials").IsLocked);
        vm.ShowArea(AppArea.Production);
        Assert.Equal(AppPage.ProductionOrders, vm.Page);             // shown, with what is missing
        Assert.True(vm.Tabs.Single(t => t.Title == "Production orders").IsLocked);
        Assert.Contains("not included in your MARK package", vm.Access.ProductionOrdersLock);
        Assert.Contains("not included in your MARK package", vm.Access.CuttingPlansLock);
    }

    [Fact]
    public void Cutter_SeesOnlyProduction_AndStartsThere()
    {
        var vm = Designer(Status(Professional, permissions: new[] { Features.CuttingPlans }));

        Assert.Equal(new[] { AppArea.Production }, Visible(vm));
        Assert.Equal((AppPage.Quote, QuoteSection.Cutting), (vm.Page, vm.Section));
        Assert.Equal(new[] { "Cutting plan" }, TabTitles(vm));
        Assert.False(vm.Access.CanUseDrawing);
    }

    [Fact]
    public void Staff_CannotReachWhatTheyWereNotGiven()
    {
        var vm = Designer(Status(Professional, permissions: new[] { Features.CuttingPlans }));

        vm.ShowView(AppView.Drawing);
        Assert.Equal(QuoteSection.Cutting, vm.Section);
        vm.ShowArea(AppArea.Sales);
        Assert.Equal(QuoteSection.Cutting, vm.Section);

        vm.Page = AppPage.Dashboard;                     // straight from code: sent back to their area
        Assert.Equal((AppPage.Quote, QuoteSection.Cutting), (vm.Page, vm.Section));
        vm.Page = AppPage.Library;
        Assert.Equal(AppPage.Quote, vm.Page);
        vm.Page = AppPage.Account;                       // their own account is always there
        Assert.Equal(AppPage.Account, vm.Page);
    }

    [Fact]
    public void Staff_ViewQuotesWithoutChangingThem_WhenTheyHaveNothingToEditWith()
    {
        var cutter = Designer(Status(Professional, permissions: new[] { Features.CuttingPlans }));
        var sales = Designer(Status(Professional, permissions: new[] { Features.Quotes, Features.Drawing }));

        Assert.False(cutter.Access.CanEditQuotes);
        Assert.Contains("view quotes", cutter.Access.ReadOnlyMessage);
        Assert.False(cutter.SaveProjectCommand.CanExecute(null));
        Assert.True(sales.Access.CanEditQuotes);
        Assert.Null(sales.Access.ReadOnlyMessage);
    }

    [Fact]
    public void Staff_SeeOnlyTheTabsTheyWereGiven()
    {
        var vm = Designer(Status(Professional, permissions: new[] { Features.Quotes, Features.Drawing, Features.Costing }));

        Assert.Equal(new[] { AppArea.Sales, AppArea.Design, AppArea.Pricing }, Visible(vm));
        vm.ShowArea(AppArea.Pricing);
        Assert.Equal(new[] { "Bill of materials" }, TabTitles(vm));
        Assert.Equal(QuoteSection.Materials, vm.Section);
        Assert.Contains("not part of your login", vm.Access.PriceStructureLock);
    }

    [Fact]
    public void Staff_SeeAnAreaStillToCome_OnlyWhenGivenPartOfIt()
    {
        var vm = Designer(Status(FeatureCatalog.All.Select(f => f.Id), permissions: new[] { Features.Quotes, Features.Invoices }));

        Assert.Contains(AppArea.Accounts, Visible(vm));
        Assert.DoesNotContain(AppArea.Orders, Visible(vm));
    }

    [Fact]
    public void Staff_GetTheirAreasWhenTheLicenceChanges()
    {
        var vm = Designer(Status(Professional, permissions: new[] { Features.Quotes }));
        vm.ShowArea(AppArea.Sales);

        vm.Access.Apply(Status(Professional, permissions: new[] { Features.CuttingPlans }));

        Assert.Equal(new[] { AppArea.Production }, Visible(vm));
        Assert.Equal(QuoteSection.Cutting, vm.Section);
    }

    [Fact]
    public void LibraryPage_ListsTheSystems()
    {
        var vm = new MainViewModel(Mark.Core.Library.LibrarySerializer.Load(Data.TempDatabase.ShippedLibraryPath));

        vm.ShowArea(AppArea.Library);

        Assert.Contains(vm.LibrarySystems, s => s.Name.Contains("uPVC") || s.Detail.Contains("uPVC"));
        Assert.Contains("systems", vm.LibraryCountsText);
        Assert.Contains(vm.LibrarySystems, s => s.Detail.Contains("used for new windows"));
    }

    [Fact]
    public void StaffPage_IsOnlyForTheAccountOwner()
    {
        var staff = Designer(Status(Professional, permissions: new[] { Features.Quotes }));
        var owner = Designer(Status(Professional));

        Assert.False(staff.Access.CanManageStaff);
        Assert.True(owner.Access.CanManageStaff);
        Assert.False(owner.HasStaff);                     // no Staff page without sign-in
        owner.ShowView(AppView.Staff);
        Assert.NotEqual(AppPage.Staff, owner.Page);
    }
    [Fact]
    public void Cutter_DoesNotSeePrices()
    {
        var vm = Designer(Status(Professional, permissions: new[] { Features.CuttingPlans }));
        Mark.Core.Commands.CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, new Mark.Core.Design.DesignRules()).Execute();

        Assert.False(vm.Access.CanSeeQuoteValues);
        Assert.Equal("Qty 1", vm.QuoteTotalText);
        Assert.False(vm.Cutting.ShowsCosts);
        Assert.Equal("", vm.Cutting.CostText);
    }

    [Fact]
    public void AccountAndStaffPages_HaveNoAreaTabs()
    {
        var vm = Designer(Status(Professional));

        vm.ShowView(AppView.Account);

        Assert.Equal("Account", vm.HeaderTitle);
        Assert.False(vm.ShowsTabs);
        Assert.DoesNotContain(vm.Areas, a => a.IsSelected);
    }

    [Fact]
    public void OpenDialog_CannotDeleteForALoginThatCannotChangeQuotes()
    {
        var store = new Mark.Tests.Data.TempDatabase();
        try
        {
            var repository = store.Open().Projects;
            repository.Save(new Project { Name = "Sharma residence" });
            var vm = Designer(Status(Professional, permissions: new[] { Features.CuttingPlans }));
            var list = new ProjectListViewModel(repository, Guid.NewGuid()) { Blocked = () => vm.Access.ReadOnlyMessage };

            Assert.False(list.DeleteCommand.CanExecute(null));
        }
        finally
        {
            store.Dispose();
        }
    }
}
