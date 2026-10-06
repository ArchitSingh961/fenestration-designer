using Mark.Core.Quotes;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Finishing;

/// <summary>The dashboard's Get started checklist: what a new company sets up first, ticked off as it is done.</summary>
public class GetStartedTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TheChecklist_FollowsWhatIsSetUp_AndCanBeHidden()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Villa" });

        vm.Page = AppPage.Dashboard;
        Assert.True(vm.ShowSetup);
        Assert.Equal(new[] { "Company details", "Your prices", "Your pricing", "Your first quote" }, vm.SetupSteps.Select(s => s.Title));
        Assert.False(vm.SetupSteps.Single(s => s.Title == "Company details").IsDone);

        // The step's button goes where it is set up.
        vm.SetupSteps.Single(s => s.Title == "Company details").Action.Execute(null);
        Assert.Equal(AppView.QuotationSetup, vm.CurrentView);

        store.Settings.SaveQuotationSettings(new QuotationSettings
        {
            CompanyName = "Test Windows", Address = "1 Main Road, Jaipur", Phone = "98290 00000", Gstin = "27AAPFU0939F1ZV"
        });
        vm.CreateFrame();
        Assert.Null(vm.SaveProject());
        vm.Page = AppPage.Dashboard;
        Assert.True(vm.SetupSteps.Single(s => s.Title == "Company details").IsDone);
        Assert.True(vm.SetupSteps.Single(s => s.Title == "Your first quote").IsDone);
        Assert.Contains("of 4 done", vm.SetupProgressText);

        vm.HideSetupCommand.Execute(null);
        Assert.False(vm.ShowSetup);
        vm.Page = AppPage.Quotes;
        vm.Page = AppPage.Dashboard;
        Assert.False(vm.ShowSetup);                                                       // stays hidden
    }
}
