using Mark.Calculation;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Calculation;

/// <summary>
/// The Products tab: what the designs use, and the bar lengths of the quote (one profile, the ticked or all), which the
/// cutting plan, the production papers and the stock needs use.
/// </summary>
public class ProductsTabTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private MainViewModel Designer()
    {
        var store = _temp.Open();
        store.Library.Import(Create());
        var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Villa" });
        vm.CreateFrame();
        vm.Section = QuoteSection.Products;
        return vm;
    }

    private static IEnumerable<double> BarsOf(MainViewModel vm, string profileId)
        => vm.Calculation.CuttingPlan.Profiles.Single(p => p.DefinitionId == profileId).Stock.Select(s => s.StockLengthMm);

    [Fact]
    public void TheProfilesPage_ListsWhatIsUsed_AndALengthIsForThisQuote()
    {
        var vm = Designer();
        Assert.Equal(AppView.Products, vm.CurrentView);
        var row = vm.Products.Rows.Single(r => r.Id == Frame60);
        Assert.Equal(("AL-6001", 6000d, "library"), (row.Code, row.LibraryLength!.Value, row.SourceText));
        Assert.Contains("6000", row.BarsText);

        row.LengthText = "5000";

        Assert.Equal(5000, vm.Project.Products.BarLengths[Frame60]);
        Assert.All(BarsOf(vm, Frame60), length => Assert.Equal(5000, length));
        Assert.Equal(6000, vm.Library.FindProfile(Frame60)!.StockLengthMm);           // the library keeps its length
        var again = vm.Products.Rows.Single(r => r.Id == Frame60);
        Assert.Equal(("5000", "this quote"), (again.LengthText, again.SourceText));

        vm.UndoCommand.Execute(null);                                                  // one step
        Assert.Empty(vm.Project.Products.BarLengths);
        Assert.All(BarsOf(vm, Frame60), length => Assert.Equal(6000, length));
    }

    [Fact]
    public void ALengthForAll_AndBackToTheLibrary_AndSavedWithTheQuote()
    {
        var vm = Designer();
        vm.Products.BulkLength = "5800";
        vm.Products.SetLengthCommand.Execute(null);                                    // nothing ticked: every profile
        Assert.All(vm.Products.Rows, r => Assert.Equal(5800, vm.Project.Products.BarLengths[r.Id]));

        Assert.Null(vm.SaveProject());
        var id = vm.Project.Id;
        vm.NewProjectCommand.Execute(null);
        Assert.Null(vm.OpenProject(id));
        Assert.Equal(5800, vm.Project.Products.BarLengths[Frame60]);

        vm.Section = QuoteSection.Products;
        vm.Products.Rows.Single(r => r.Id == Frame60).IsSelected = true;               // only this one
        vm.Products.UseLibraryLengthsCommand.Execute(null);
        Assert.False(vm.Project.Products.BarLengths.ContainsKey(Frame60));
        Assert.Contains("back on the library", vm.Products.Message);
    }

    [Fact]
    public void ABadLength_IsRefused_AndOtherPagesHaveNone()
    {
        var vm = Designer();
        var row = vm.Products.Rows.Single(r => r.Id == Frame60);
        row.LengthText = "12";
        Assert.True(vm.Products.MessageIsError);
        Assert.Contains("between", vm.Products.Message);
        Assert.Empty(vm.Project.Products.BarLengths);

        vm.Products.Page = ProductsPage.Glass;
        Assert.False(vm.Products.HasLengths);
        Assert.Contains(vm.Products.Rows, r => r.Id == Clear6 && !r.HasLength);
    }

    [Fact]
    public void StockNeeds_BuyTheQuotesBars()
    {
        var vm = Designer();
        vm.Products.Rows.Single(r => r.Id == Frame60).LengthText = "3000";
        var needs = StockNeeds.For(vm.Project, vm.Library, new CalculationRules());
        var frame = needs.Single(n => n.Key.ItemId == Frame60);
        int barsAt6000 = StockNeeds.For(new Project { Frames = vm.Project.Frames }, vm.Library, new CalculationRules())
            .Single(n => n.Key.ItemId == Frame60).Quantity is var q ? (int)q : 0;
        Assert.True(frame.Quantity > barsAt6000);                                       // shorter bars: more of them
    }
}
