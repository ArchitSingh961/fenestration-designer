using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Calculation;

/// <summary>
/// The Pricing tab's rate pages: a quote's own rates of profiles, glass, hardware and mesh, in place of the library's
/// prices, used by the calculation (and so by the bill of materials, the cost sheet and the quotation).
/// </summary>
public class QuoteRatesTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Project OneWindow()
    {
        var project = new Project { Pricing = new PriceStructure { Name = "Test", TaxName = "GST" } };
        project.Frames.Add(FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules));
        return project;
    }

    [Fact]
    public void AQuotesOwnRate_PricesTheItem_AndTheLibraryKeepsItsPrice()
    {
        var library = Create();
        var project = OneWindow();
        var before = new CalculationEngine().Calculate(project, library, new CalculationRules());

        project.Pricing.ItemRates[RateKey.Profile(Frame60)] = 300m;                // the library has 150 / m
        project.Pricing.ItemRates[RateKey.Glass(Clear6)] = 0m;
        var after = new CalculationEngine().Calculate(project, library, new CalculationRules());

        Assert.Equal(before.Frames[0].Cost.Profiles * 2, after.Frames[0].Cost.Profiles);
        Assert.Equal(0, after.Frames[0].Cost.Glass);
        Assert.All(after.Profiles.Where(p => p.DefinitionId == Frame60), p => Assert.Equal(300m, p.CostPerMetre));
        Assert.Equal(150m, library.FindProfile(Frame60)!.CostPerMetre);
    }

    [Fact]
    public void MeshRates_GoByTheDesignsMeshType()
    {
        var project = OneWindow();
        var frame = project.Frames[0];
        FrameEditor.SetOpening(frame, frame.GlassPanels.Select(g => g.Id).ToList(), OpeningType.SideHungLeft, true, Rules);
        frame.Design.MeshType = "SS Flymesh";
        project.Pricing.Rates.MeshPerSquareMetre = 100m;
        var calculation = new CalculationEngine().Calculate(project, Create(), new CalculationRules());
        decimal Mesh() => PricingEngine.Price(project, calculation, project.Pricing).Designs[0].Sheet.Single(l => l.Formula == "#MESHCOST").Amount;

        decimal byDefault = Mesh();
        project.Pricing.MeshRates["ss flymesh"] = 1000m;                            // any case
        Assert.True(byDefault > 0);
        Assert.Equal(byDefault * 10, Mesh());
    }

    private MainViewModel Designer()
    {
        var store = _temp.Open();
        store.Library.Import(Create());
        var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Villa" });
        vm.CreateFrame();
        vm.Section = QuoteSection.Pricing;
        return vm;
    }

    [Fact]
    public void TheProfileRatePage_ListsWhatTheDesignsUse_AndApplyGivesTheQuoteItsRate()
    {
        var vm = Designer();
        decimal before = vm.Price.GrandTotal;
        vm.Pricing.Page = PricingPage.ProfileRate;
        var row = vm.Pricing.RateRows.Single(r => r.Key == RateKey.Profile(Frame60));
        Assert.Equal(("AL-6001", "60mm Frame", 150m, "library"), (row.Code, row.Name, row.LibraryRate, row.SourceText));

        row.RateText = "300";
        Assert.True(row.IsOwnRate);
        Assert.True(vm.Pricing.HasChanges);
        Assert.Equal(before, vm.Price.GrandTotal);                                  // not applied yet …
        Assert.Contains("Preview", vm.Pricing.PreviewNote);

        vm.Pricing.ApplyCommand.Execute(null);                                      // … now it is
        Assert.Equal(300m, vm.Project.Pricing.ItemRates[RateKey.Profile(Frame60)]);
        Assert.True(vm.Price.GrandTotal > before);
        Assert.Equal(150m, vm.Library.FindProfile(Frame60)!.CostPerMetre);

        // Saved with the quote, and opened again with it.
        Assert.Null(vm.SaveProject());
        var id = vm.Project.Id;
        vm.NewProjectCommand.Execute(null);
        Assert.Null(vm.OpenProject(id));
        Assert.Equal(300m, vm.Project.Pricing.ItemRates[RateKey.Profile(Frame60)]);

        // Back to the library's price.
        vm.Section = QuoteSection.Pricing;
        vm.Pricing.Page = PricingPage.ProfileRate;
        vm.Pricing.RateRows.Single(r => r.Key == RateKey.Profile(Frame60)).IsSelected = true;
        vm.Pricing.UseLibraryRatesCommand.Execute(null);
        vm.Pricing.ApplyCommand.Execute(null);
        Assert.Empty(vm.Project.Pricing.ItemRates);
        Assert.Equal(before, vm.Price.GrandTotal);
    }

    [Fact]
    public void ABadRate_IsReported_AndTheOtherPagesListTheirItems()
    {
        var vm = Designer();
        vm.Pricing.Page = PricingPage.GlassRate;
        var glass = Assert.Single(vm.Pricing.RateRows);
        Assert.Equal(RateKey.Glass(Clear6), glass.Key);
        glass.RateText = "lots";
        vm.Pricing.ApplyCommand.Execute(null);
        Assert.True(vm.Pricing.MessageIsError);
        Assert.Contains("rate of", vm.Pricing.Message);
        Assert.Empty(vm.Project.Pricing.ItemRates);

        vm.Pricing.RateSearch = "nothing like it";
        Assert.True(vm.Pricing.HasNoRows);
        Assert.Contains("matches", vm.Pricing.NoRowsText);
    }

    [Fact]
    public void ADesignAddOn_SetsTheDesignsExtraCost_AsOneUndoStep()
    {
        var vm = Designer();
        decimal before = vm.Price.GrandTotal;
        vm.Pricing.Page = PricingPage.DesignAddOns;
        var row = Assert.Single(vm.Pricing.DesignAddOns);

        row.ExtraCostText = "500";
        Assert.Equal(500m, vm.Project.Frames[0].Design.ExtraCost);
        Assert.False(vm.Pricing.HasMessage);                                       // no error for a change that worked
        Assert.True(vm.Price.GrandTotal > before);

        vm.UndoCommand.Execute(null);
        Assert.Equal(0m, vm.Project.Frames[0].Design.ExtraCost);
    }
}
