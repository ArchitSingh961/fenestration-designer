using Mark.Calculation;
using Mark.Designer.ViewModels;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Calculation;

/// <summary>The cutting plan in the designer: shown from the calculation layer's plan, refreshed on every change.</summary>
public class CuttingPlanViewModelTests
{
    private static MainViewModel CreateApp(CalculationRules? rules = null, Core.Library.ProductLibrary? library = null)
    {
        var vm = new MainViewModel(library ?? Create(), rules);
        vm.Canvas.SetViewportSize(1000, 800);
        return vm;
    }

    private static void CreateFrame(MainViewModel vm, string width = "1200", string height = "1500")
    {
        vm.NewFrameWidthText = width;
        vm.NewFrameHeightText = height;
        vm.CreateFrameCommand.Execute(null);
    }

    [Fact]
    public void NewFrame_ShowsItsCuttingPlan()
    {
        var vm = CreateApp();
        CreateFrame(vm);

        var cutting = vm.Cutting;
        Assert.Equal("1 bar · 6,000 mm · utilisation 100.0% · waste 0.0%", cutting.SummaryText);
        Assert.Equal("Bars 900.00 INR · remnants 90.00 · net 810.00", cutting.CostText);   // net = the M6 profile cost
        Assert.Equal("Kerf 0 mm · trim 0 mm · remnant ≥ 0 mm", cutting.RulesText);
        var profile = Assert.Single(cutting.Profiles);
        Assert.Equal(new CuttingProfileRow("60mm Frame", "1 × 6,000 mm · utilisation 100.0% · 900.00 INR", profile.Bars), profile);
        Assert.Equal(new CuttingBarRow("6,000 mm", "1,500 · 1,500 · 1,200 · 1,200", "remnant 600 mm · waste 0 mm"),
            Assert.Single(profile.Bars));
        Assert.Null(cutting.StatusText);
    }

    [Fact]
    public void ConfiguredRules_AreUsedAndShown()
    {
        var rules = new CalculationRules { Cutting = new CuttingRules { KerfMm = 3, TrimAllowanceMm = 5, MinUsableOffcutMm = 300 } };
        var vm = CreateApp(rules);
        CreateFrame(vm);

        Assert.Equal("Kerf 3 mm · trim 5 mm · remnant ≥ 300 mm", vm.Cutting.RulesText);
        Assert.Equal("remnant 583 mm · waste 17 mm", Assert.Single(vm.Cutting.Profiles[0].Bars).Leftover);   // 5 trim + 4 × 3 kerf
    }

    [Fact]
    public void IdenticalBars_AreListedOnceWithACount()
    {
        var vm = CreateApp();
        for (int i = 0; i < 3; i++)
            CreateFrame(vm, "1000", "2000");                               // 6 × 2000 + 6 × 1000

        Assert.Equal(new[]
        {
            new CuttingBarRow("2 × 6,000 mm", "2,000 · 2,000 · 2,000", "waste 0 mm"),
            new CuttingBarRow("6,000 mm", "1,000 · 1,000 · 1,000 · 1,000 · 1,000 · 1,000", "waste 0 mm")
        }, vm.Cutting.Profiles[0].Bars);
        Assert.StartsWith("3 bars", vm.Cutting.SummaryText);
    }

    [Fact]
    public void ChangingTheProfile_UpdatesThePlan_AndUndoRestoresIt()
    {
        var vm = CreateApp();
        CreateFrame(vm);
        string before = vm.Cutting.Profiles[0].Summary;

        Assert.Null(vm.AssignProfile(Frame50));
        Assert.Equal("50mm Frame", Assert.Single(vm.Cutting.Profiles).Name);
        Assert.Equal("1 × 6,000 mm · utilisation 100.0% · 600.00 INR", vm.Cutting.Profiles[0].Summary);

        vm.UndoCommand.Execute(null);
        Assert.Equal(("60mm Frame", before), (vm.Cutting.Profiles[0].Name, vm.Cutting.Profiles[0].Summary));
    }

    [Fact]
    public void NewProject_ClearsThePlan()
    {
        var vm = CreateApp();
        CreateFrame(vm);
        vm.NewProjectCommand.Execute(null);

        Assert.Empty(vm.Cutting.Profiles);
        Assert.Equal(("", ""), (vm.Cutting.SummaryText, vm.Cutting.CostText));
    }

    [Fact]
    public void WithoutALibrary_SaysWhatCannotBePlanned()
    {
        var vm = CreateApp(library: Core.Library.ProductLibrary.Empty);
        CreateFrame(vm);

        Assert.StartsWith("4 pieces could not be planned.", vm.Cutting.StatusText);
        Assert.Empty(vm.Cutting.Profiles);
    }
}
