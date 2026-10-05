using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Core.Serialization;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Calculation;

/// <summary>
/// The cost sheet: formulas (#VALUES and @cost lines), "percentage of", subtotals, the design's extra cost, the sheet
/// of one window from profile cost to unit price, and the checks when a price structure is saved.
/// </summary>
public class CostSheetTests
{
    private static readonly DesignRules Rules = new();
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

    private static readonly string[] Lines = { "Profile Cost", "Glass Cost", "Sub Total Including Labour", "Sub Total" };

    private static decimal Eval(string formula)
        => CostFormula.Evaluate(formula, Lines,
            v => v switch { "PROFILECOST" => 1000m, "AREASQFT" => 10m, _ => 0m },
            h => h switch { "Profile Cost" => 1000m, "Glass Cost" => 400m, "Sub Total Including Labour" => 5000m, "Sub Total" => 1m, _ => 0m });

    [Theory]
    [InlineData("#PROFILECOST", 1000)]
    [InlineData("@Profile Cost.value + @Glass Cost.value", 1400)]
    [InlineData("@profile cost + @GLASS COST * 2", 1800)]                  // names are not case-sensitive; × before +
    [InlineData("(@Profile Cost + @Glass Cost) * 2", 2800)]
    [InlineData("@Sub Total Including Labour - @Sub Total", 4999)]          // the longest name is read first
    [InlineData("#AREASQFT * 75.5", 755)]
    [InlineData("-@Glass Cost / 4", -100)]
    [InlineData("@Glass Cost / 0", 0)]                                      // nothing to divide by: 0, not a crash
    public void Formulas_WorkOut(string formula, decimal expected) => Assert.Equal(expected, Eval(formula));

    [Theory]
    [InlineData("#PROFILCOST", "#PROFILCOST")]
    [InlineData("@Profit", "@Profit")]
    [InlineData("(@Profile Cost", "bracket")]
    [InlineData("@Profile Cost +", "ends too early")]
    [InlineData("@Profile Cost ? 2", "not understood")]
    [InlineData("", "empty")]
    public void BadFormulas_SayWhatIsWrong(string formula, string says)
        => Assert.Contains(says, CostFormula.Check(formula, Lines));

    // ── The engine ──────────────────────────────────────────────────

    private static MainViewModel Quote(string template = "div-v2")
    {
        var vm = new MainViewModel(Sample);
        Assert.Null(vm.ApplyDesign(DesignTemplates.All.Single(t => t.Id == template), "SYS-AL-SL60"));
        return vm;
    }

    [Fact]
    public void TheNewDefault_PricesExactlyLikeTheOldOne()
    {
        // The default now reads like a cost sheet (subtotals, "percentage of", extra cost) but adds up to the same price.
        var old = new PriceStructure
        {
            Heads =
            {
                new CostHead { Name = "Profile wastage", Basis = CostBasis.PercentOfProfiles, Rate = 10 },
                new CostHead { Name = "Glass wastage", Basis = CostBasis.PercentOfGlass, Rate = 5 },
                new CostHead { Name = "Powder coating", Basis = CostBasis.PerMetreOfProfile, Rate = 60 },
                new CostHead { Name = "Fabrication labour", Basis = CostBasis.PerSquareMetreOfWindow, Rate = 750 },
                new CostHead { Name = "Installation labour", Basis = CostBasis.PerSquareMetreOfWindow, Rate = 500 },
                new CostHead { Name = "Overheads and margin", Basis = CostBasis.PercentOfRunningTotal, Rate = 20 }
            },
            Charges = PriceStructure.Default().Charges,
            TaxPercent = 18,
            Rates = PriceStructure.Default().Rates
        };
        foreach (string template in new[] { "div-v2", "sld-2", "cas-left" })
        {
            var vm = Quote(template);
            var calc = vm.Calculation.Result;
            var before = PricingEngine.Price(vm.Project, calc, old);
            var now = PricingEngine.Price(vm.Project, calc, PriceStructure.Default());
            Assert.Equal(before.GrandTotal, now.GrandTotal);
            Assert.Equal(before.Designs[0].UnitPrice, now.Designs[0].UnitPrice);
        }
    }

    [Fact]
    public void TheSheet_GoesFromTheMaterialLinesToTheUnitPrice()
    {
        var vm = Quote("sld-2");
        var design = PricingEngine.Price(vm.Project, vm.Calculation.Result, PriceStructure.Default()).Designs.Single();
        var sheet = design.Sheet;

        Assert.Equal(CostFormula.MaterialLines.Select(m => m.Name), sheet.Take(6).Select(l => l.Name));
        Assert.Equal(design.MaterialCost + design.RatedCost, sheet.Take(6).Sum(l => l.Amount));      // nothing lost or doubled
        Assert.Equal("Unit Price", sheet[^1].Name);
        Assert.Equal(design.UnitPrice, sheet[^1].Amount);

        // A subtotal is everything above it, and is not added again.
        var raw = sheet.Single(l => l.Name == "Total Raw Material Cost");
        int at = sheet.ToList().IndexOf(raw);
        Assert.Equal(sheet.Take(at).Where(l => l.Kind != CostSheetLineKind.Subtotal).Sum(l => l.Amount), raw.Amount);
        var labour = sheet.Single(l => l.Name == "Sub Total Including Labour");
        var profit = sheet.Single(l => l.Name == "Profit");
        Assert.Equal(Math.Round(labour.Amount * 0.20m, 2, MidpointRounding.AwayFromZero), profit.Amount);
        Assert.Equal(labour.Amount + profit.Amount, sheet.Single(l => l.Name == "Basic Value").Amount);
        Assert.DoesNotContain(design.Heads, h => h.Name == "Total Raw Material Cost");
    }

    [Fact]
    public void TheDesignsExtraCost_IsAddedToEachWindow()
    {
        var vm = Quote();
        var frame = vm.Project.Frames.Single();
        var before = PricingEngine.Price(vm.Project, vm.Calculation.Result, PriceStructure.Default()).Designs.Single();

        var info = frame.Design.Copy();
        info.ExtraCost = 250;
        Assert.True(FrameEditor.TrySetDesignInfo(frame, info).Success);
        var after = PricingEngine.Price(vm.Project, vm.Calculation.Result, PriceStructure.Default()).Designs.Single();

        Assert.Equal(250, after.Sheet.Single(l => l.Name == "Extra Cost").Amount);
        Assert.Equal(before.UnitBasicPrice + 250 * 1.20m, after.UnitBasicPrice);         // with the profit on top

        info.ExtraCost = -1;
        Assert.False(FrameEditor.TrySetDesignInfo(frame, info).Success);
    }

    [Fact]
    public void ACustomFormula_CanUseLinesAboveAndWindowValues()
    {
        var vm = Quote();
        var pricing = new PriceStructure
        {
            Heads =
            {
                new CostHead { Name = "Double profile", Basis = CostBasis.Formula, Formula = "@Profile Cost * 2" },
                new CostHead { Name = "Per sq ft", Basis = CostBasis.Formula, Formula = "#AREASQFT * 100" },
                new CostHead { Name = "Half of it", Basis = CostBasis.PercentOf, Rate = 50, Formula = "@Double profile.value" }
            }
        };
        Assert.Null(PricingEditor.Validate(pricing));
        var design = PricingEngine.Price(vm.Project, vm.Calculation.Result, pricing).Designs.Single();
        decimal profile = design.Sheet.Single(l => l.Name == "Profile Cost").Amount;
        var calc = vm.Calculation.Result.Frames.Single();

        Assert.Equal(profile * 2, design.Heads[0].Amount);
        Assert.Equal(Math.Round((decimal)calc.AreaM2 * CostFormula.SquareFeetPerSquareMetre * 100, 2, MidpointRounding.AwayFromZero), design.Heads[1].Amount);
        Assert.Equal(profile, design.Heads[2].Amount);
    }

    [Theory]
    [InlineData("Profile Cost", CostBasis.PerWindow, "", "already on the cost sheet")]
    [InlineData("Profit", CostBasis.PercentOf, "", "percentage of")]
    [InlineData("Profit", CostBasis.Formula, "", "formula")]
    [InlineData("Profit", CostBasis.PercentOf, "@Later", "not a cost line above it")]
    [InlineData("Profit", CostBasis.Formula, "#NOPE", "#NOPE")]
    public void BadCostLines_AreRefused(string name, CostBasis basis, string formula, string says)
    {
        var pricing = new PriceStructure
        {
            Heads =
            {
                new CostHead { Name = name, Basis = basis, Rate = 10, Formula = formula },
                new CostHead { Name = "Later", Basis = CostBasis.PerWindow, Rate = 1 }
            }
        };
        Assert.Contains(says, PricingEditor.Validate(pricing), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoLinesWithOneName_AreRefused()
    {
        var pricing = new PriceStructure
        {
            Heads =
            {
                new CostHead { Name = "Labour", Basis = CostBasis.PerWindow, Rate = 1 },
                new CostHead { Name = "labour ", Basis = CostBasis.PerWindow, Rate = 2 }
            }
        };
        Assert.Contains("two cost lines", PricingEditor.Validate(pricing));
    }

    [Fact]
    public void Formulas_AreSavedAndRead()
    {
        var pricing = PriceStructure.Default();
        var back = PricingSerializer.Deserialize(PricingSerializer.Serialize(pricing));
        Assert.Equal(pricing.Heads.Select(h => (h.Name, h.Basis, h.Formula)), back.Heads.Select(h => (h.Name, h.Basis, h.Formula)));
        Assert.Null(PricingEditor.Validate(back));
    }

    [Fact]
    public void ThePricingTab_ShowsTheSheetOfTheChosenDesign()
    {
        var vm = Quote("sld-2");
        vm.Pricing.Load(vm.Project.Pricing);
        Assert.Single(vm.Pricing.SheetDesigns);
        Assert.NotNull(vm.Pricing.SelectedSheetDesign);
        Assert.Equal("Profile Cost", vm.Pricing.Sheet[0].Name);
        Assert.Equal("Material", vm.Pricing.Sheet[0].Type);
        Assert.Equal("Unit Price", vm.Pricing.Sheet[^1].Name);

        // A formula row: the rate box goes, the formula box comes.
        vm.Pricing.AddHeadCommand.Execute(null);
        var row = vm.Pricing.Heads[^1];
        Assert.True(row.UsesRate);
        row.Basis = CostBasis.Formula;
        Assert.False(row.UsesRate);
        Assert.True(row.UsesFormula);
        row.Formula = "@Profile Cost / 10";
        var (built, error) = vm.Pricing.TryBuild();
        Assert.Null(error);
        Assert.Equal("@Profile Cost / 10", built!.Heads[^1].Formula);
        Assert.Contains(vm.Pricing.Sheet, l => l.Name == row.Name && l.Formula == "@Profile Cost / 10");

        row.Formula = "@Nothing";
        Assert.Contains("not a cost line above it", vm.Pricing.TryBuild().Error);
    }
}
