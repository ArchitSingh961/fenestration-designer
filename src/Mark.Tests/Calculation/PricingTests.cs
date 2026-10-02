using Mark.Calculation;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Core.Serialization;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Calculation;

/// <summary>
/// Milestone 11: quantities in the calculation, sash and mesh bars, the price structure and the pricing engine, the
/// default pricing in the database and the Pricing tab.
/// </summary>
public class PricingTests : IDisposable
{
    private static readonly DesignRules Rules = new();
    private static readonly CalculationEngine Engine = new();
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The test library plus a 50 mm sash profile (100/m, bite 10) and a 20 mm mesh profile (40/m).</summary>
    private static ProductLibrary WithSashes()
    {
        var basic = Create();
        return new ProductLibrary(
            basic.Profiles.Concat(new[]
            {
                new ProfileDefinition
                {
                    Id = "SASH-50", Name = "50mm Sash", Roles = new[] { ProfileType.Sash }, FaceWidthMm = 50,
                    WeightKgPerMetre = 1.0, CostPerMetre = 100m, StockLengthMm = 6000, GlazingBiteMm = 10
                },
                new ProfileDefinition
                {
                    Id = "MESH-20", Name = "20mm Mesh", Roles = new[] { ProfileType.MeshSash }, FaceWidthMm = 20,
                    CostPerMetre = 40m, StockLengthMm = 6000
                }
            }),
            basic.Glass, basic.Materials, basic.Defaults, basic.Currency);
    }

    private static Project OneWindow(OpeningType opening = OpeningType.Fixed, bool mesh = false, int quantity = 1,
        double width = 1200, double height = 1500)
    {
        var project = new Project { Pricing = new PriceStructure { Name = "Test", TaxName = "GST" } };
        var frame = FrameEditor.CreateFrame(0, 0, width, height, Rules);
        if (opening != OpeningType.Fixed || mesh)
            FrameEditor.SetOpening(frame, frame.GlassPanels.Select(g => g.Id).ToList(), opening, mesh, Rules);
        frame.Design.Quantity = quantity;
        project.Frames.Add(frame);
        return project;
    }

    private static CalculationResult Calculate(Project project, ProductLibrary? library = null)
        => Engine.Calculate(project, library ?? WithSashes(), new CalculationRules());

    // ── Quantities ──────────────────────────────────────────────────

    [Fact]
    public void AQuantity_MultipliesTheBomCutListAndTotal_ButNotTheLines()
    {
        var one = Calculate(OneWindow());
        var three = Calculate(OneWindow(quantity: 3));

        Assert.Equal(one.Frames[0].Cost, three.Frames[0].Cost);               // per window
        Assert.Equal(3, three.Frames[0].Quantity);
        Assert.Equal(one.Cost.Total * 3, three.Cost.Total);
        Assert.Equal(one.WeightKg * 3, three.WeightKg, 6);
        Assert.Equal(one.Bom.Sum(b => b.Cost) * 3, three.Bom.Sum(b => b.Cost));
        Assert.Equal(one.Bom.Single(b => b.Category == BomCategory.Glass).Quantity * 3,
            three.Bom.Single(b => b.Category == BomCategory.Glass).Quantity);
        Assert.Equal(one.CutList.Sum(c => c.Quantity) * 3, three.CutList.Sum(c => c.Quantity));
    }

    // ── Sashes and mesh ─────────────────────────────────────────────

    [Fact]
    public void ACasement_AddsFourMitredSashBars_AndItsGlassSitsInTheSash()
    {
        var result = Calculate(OneWindow(OpeningType.SideHungLeft));
        var panel = result.Glass.Single();

        var bars = result.SashBarsOf(panel.GlassPanelId).ToList();
        Assert.Equal(4, bars.Count);
        Assert.All(bars, b => Assert.Equal((ProfileType.Sash, "SASH-50", 45.0), (b.Role, b.DefinitionId, b.StartCutAngle)));
        // The opening is 1080 × 1380; the sash fills it.
        Assert.Equal(new[] { 1080.0, 1080.0, 1380.0, 1380.0 }, bars.Select(b => b.CutLengthMm).OrderBy(l => l));
        // Glass: sash glass (1080 − 2 × 50) + 2 × 10 bite = 1000 wide; (1380 − 100) + 20 = 1300 high.
        Assert.Equal((1000.0, 1300.0), (panel.WidthMm, panel.HeightMm));
        var opening = Assert.Single(result.Openings);
        Assert.True(opening.HasSash);
        Assert.Equal(OpeningType.SideHungLeft, opening.Opening);
    }

    [Fact]
    public void AMeshShutter_AddsMeshBars_AndAMeshArea()
    {
        var result = Calculate(OneWindow(OpeningType.SideHungLeft, mesh: true));

        var bars = result.SashBarsOf(result.Glass.Single().GlassPanelId).ToList();
        Assert.Equal(4, bars.Count(b => b.Role == ProfileType.MeshSash));
        // Mesh inside the 20 mm mesh frame: (1080 − 40) × (1380 − 40).
        Assert.Equal(1.04 * 1.34, result.Openings.Single().MeshAreaM2, 4);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void WithoutASashProfile_TheSashIsReported_NotSilentlyFree()
    {
        var result = Calculate(OneWindow(OpeningType.SideHungLeft), Create());

        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, i => i.Message.Contains("no sash profile"));
        Assert.All(result.SashBarsOf(result.Glass.Single().GlassPanelId), b => Assert.False(b.IsResolved));
    }

    [Fact]
    public void FixedGlass_HasNoSashBars()
    {
        var result = Calculate(OneWindow());
        Assert.Empty(result.SashBarsOf(result.Glass.Single().GlassPanelId));
        Assert.False(result.Openings.Single().HasSash);
    }

    // ── Pricing engine ──────────────────────────────────────────────

    [Fact]
    public void Heads_AreAppliedInOrder_AndTheRunningTotalSeesTheOnesAbove()
    {
        var project = OneWindow();
        project.Pricing.Heads.Add(new CostHead { Name = "Wastage", Basis = CostBasis.PercentOfProfiles, Rate = 10 });
        project.Pricing.Heads.Add(new CostHead { Name = "Labour", Basis = CostBasis.PerWindow, Rate = 500 });
        project.Pricing.Heads.Add(new CostHead { Name = "Margin", Basis = CostBasis.PercentOfRunningTotal, Rate = 20 });
        var result = Calculate(project);
        var cost = result.Frames[0].Cost;

        var design = PricingEngine.Price(project, result, project.Pricing).Designs.Single();

        decimal wastage = Math.Round(cost.Profiles * 0.10m, 2, MidpointRounding.AwayFromZero);
        decimal margin = Math.Round((cost.Total + wastage + 500) * 0.20m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(new[] { wastage, 500m, margin }, design.Heads.Select(h => h.Amount));
        Assert.Equal(cost.Total + wastage + 500 + margin, design.UnitBasicPrice);
    }

    [Fact]
    public void RatedCosts_PriceHardwarePerSash_MeshPerSquareMetre_AndReinforcementPerMetre()
    {
        var project = OneWindow(OpeningType.TiltTurnLeft, mesh: true);
        project.Pricing.Rates = new PriceRates { TiltTurnHardware = 3000, MeshPerSquareMetre = 100, ReinforcementPerMetre = 10 };
        var result = Calculate(project);

        var design = PricingEngine.Price(project, result, project.Pricing).Designs.Single();

        decimal mesh = Math.Round((decimal)result.Openings.Single().MeshAreaM2 * 100, 2, MidpointRounding.AwayFromZero);
        decimal reinforcement = Math.Round((decimal)result.Frames[0].ProfileMetres * 10, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(3000 + mesh + reinforcement, design.RatedCost);
    }

    [Fact]
    public void TheSummary_GoesFromBasicValue_ThroughDiscountChargesAndTax_ToTheGrandTotal()
    {
        var project = OneWindow(quantity: 2);
        project.Pricing.DiscountPercent = 10;
        project.Pricing.Charges.Add(new CostHead { Name = "Transport", Basis = CostBasis.FixedPerQuote, Rate = 1000 });
        project.Pricing.Charges.Add(new CostHead { Name = "Loading", Basis = CostBasis.PerWindow, Rate = 100 });
        project.Pricing.TaxPercent = 18;
        var result = Calculate(project);

        var price = PricingEngine.Price(project, result, project.Pricing);

        decimal basic = result.Frames[0].Cost.Total * 2;
        decimal discount = Math.Round(basic * 0.10m, 2, MidpointRounding.AwayFromZero);
        decimal total = basic - discount + 1000 + 200;
        Assert.Equal(basic, price.BasicValue);
        Assert.Equal(-discount, price.Discount);
        Assert.Equal(basic - discount, price.SubTotal);
        Assert.Equal(total, price.Total);
        Assert.Equal(Math.Round(total * 0.18m, 2, MidpointRounding.AwayFromZero), price.Tax);
        Assert.Equal(price.Total + price.Tax, price.GrandTotal);
        Assert.Equal(new[] { "Basic value", "Discount 10 %", "Sub-total", "Transport", "Loading", "Total", "GST 18 %", "Grand total" },
            price.Summary.Select(l => l.Name));
        var design = price.Designs.Single();
        Assert.Equal(Math.Round(design.UnitBasicPrice * 0.9m, 2, MidpointRounding.AwayFromZero), design.UnitPrice);
    }

    [Fact]
    public void AnEmptyQuote_HasNoCharges()
    {
        var project = new Project { Pricing = PriceStructure.Default() };
        var price = PricingEngine.Price(project, Calculate(project), project.Pricing);
        Assert.Equal(0, price.GrandTotal);
        Assert.DoesNotContain(price.Summary, l => l.Kind == PriceSummaryKind.Charge);
    }

    // ── Validation, command, persistence ────────────────────────────

    [Theory]
    [InlineData("", 10, 18, "name")]
    [InlineData("Retail", 120, 18, "discount")]
    [InlineData("Retail", 10, -1, "tax")]
    public void InvalidPricing_IsRejected(string name, decimal discount, decimal tax, string expected)
    {
        var pricing = PriceStructure.Default();
        pricing.Name = name;
        pricing.DiscountPercent = discount;
        pricing.TaxPercent = tax;
        Assert.Contains(expected, PricingEditor.Validate(pricing), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFixedPerQuoteAmount_BelongsUnderCharges_NotPerWindowHeads()
    {
        var pricing = PriceStructure.Default();
        pricing.Heads.Add(new CostHead { Name = "Transport", Basis = CostBasis.FixedPerQuote, Rate = 500 });
        Assert.Contains("charges", PricingEditor.Validate(pricing));

        pricing = PriceStructure.Default();
        pricing.Charges.Add(new CostHead { Name = "Margin", Basis = CostBasis.PercentOfMaterials, Rate = 5 });
        Assert.Contains("charge", PricingEditor.Validate(pricing));
    }

    [Fact]
    public void SetPricingCommand_IsUndoable_AndStoresACopy()
    {
        var project = new Project();
        var history = new CommandHistory();
        var pricing = PriceStructure.Default();
        pricing.DiscountPercent = 7;

        history.Execute(new SetPricingCommand(project, pricing));
        pricing.DiscountPercent = 50;                       // the caller's object is not the quote's
        Assert.Equal(7, project.Pricing.DiscountPercent);

        history.Undo();
        Assert.Equal(0, project.Pricing.DiscountPercent);
    }

    [Fact]
    public void Pricing_RoundTripsThroughTheProjectFile_AndOldFilesGetTheDefault()
    {
        var project = new Project { Pricing = PriceStructure.Default() };
        project.Pricing.Heads[0].Rate = 12.5m;
        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        Assert.Equal(PricingSerializer.Serialize(project.Pricing), PricingSerializer.Serialize(loaded.Pricing));

        var old = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(new Project()).Replace("\"pricing\":", "\"unused\":"));
        Assert.Equal(PricingSerializer.Serialize(PriceStructure.Default()), PricingSerializer.Serialize(old.Pricing));
    }

    [Fact]
    public void TheDefaultPricing_IsSavedInTheDatabase_AndNewQuotesStartWithIt()
    {
        var store = _temp.Open();
        Assert.Equal("Retail", store.Settings.LoadDefaultPricing().Name);   // nothing saved yet: the built-in one
        var mine = PriceStructure.Default();
        mine.Name = "Builder projects";
        mine.DiscountPercent = 12;
        store.Settings.SaveDefaultPricing(mine);

        var vm = new MainViewModel(_temp.Open(), null, new FakeDialogs());
        Assert.Equal(("Builder projects", 12m), (vm.Project.Pricing.Name, vm.Project.Pricing.DiscountPercent));
        vm.NewQuote();
        Assert.Equal("Builder projects", vm.Project.Pricing.Name);
    }

    // ── Pricing tab ─────────────────────────────────────────────────

    private (MainViewModel Vm, LocalStore Store) Designer()
    {
        var store = _temp.Open();
        store.Library.Import(WithSashes());
        var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Villa" });
        vm.CreateFrame();
        vm.Section = QuoteSection.Pricing;
        return (vm, store);
    }

    [Fact]
    public void ThePricingTab_PreviewsChanges_AndApplyStoresThemAsOneUndoStep()
    {
        var (vm, _) = Designer();
        decimal before = vm.Price.GrandTotal;
        Assert.False(vm.Pricing.HasChanges);
        Assert.Contains(vm.Pricing.Summary, r => r.Name == "Grand total");

        vm.Pricing.DiscountText = "10";
        Assert.True(vm.Pricing.HasChanges);
        Assert.Contains("Preview", vm.Pricing.PreviewNote);
        Assert.Equal(before, vm.Price.GrandTotal);                     // not applied yet
        Assert.Contains(vm.Pricing.Summary, r => r.Name == "Discount 10 %");

        vm.Pricing.ApplyCommand.Execute(null);
        Assert.False(vm.Pricing.HasChanges);
        Assert.Equal(10, vm.Project.Pricing.DiscountPercent);
        Assert.True(vm.Price.GrandTotal < before);

        vm.UndoCommand.Execute(null);
        Assert.Equal(0, vm.Project.Pricing.DiscountPercent);
        Assert.Equal("0", vm.Pricing.DiscountText);                     // the form follows undo
    }

    [Fact]
    public void CostLines_CanBeAddedMovedAndRemoved()
    {
        var (vm, _) = Designer();
        int count = vm.Pricing.Heads.Count;

        vm.Pricing.AddHeadCommand.Execute(null);
        var added = vm.Pricing.Heads[^1];
        added.Name = "Packing";
        added.Basis = CostBasis.PerWindow;
        added.RateText = "250";
        added.MoveUpCommand.Execute(null);
        Assert.Same(added, vm.Pricing.Heads[^2]);
        vm.Pricing.ApplyCommand.Execute(null);

        Assert.Equal(count + 1, vm.Project.Pricing.Heads.Count);
        var packing = vm.Project.Pricing.Heads[^2];
        Assert.Equal(("Packing", CostBasis.PerWindow, 250m), (packing.Name, packing.Basis, packing.Rate));

        vm.Pricing.Heads[^2].RemoveCommand.Execute(null);
        vm.Pricing.ApplyCommand.Execute(null);
        Assert.Equal(count, vm.Project.Pricing.Heads.Count);
    }

    [Fact]
    public void ABadRate_IsReported_AndNothingIsApplied()
    {
        var (vm, _) = Designer();
        vm.Pricing.Heads[0].RateText = "ten";

        vm.Pricing.ApplyCommand.Execute(null);

        Assert.True(vm.Pricing.MessageIsError);
        Assert.Contains("number", vm.Pricing.Message);
        Assert.Contains("saved pricing", vm.Pricing.PreviewNote);
        Assert.Equal(PricingSerializer.Serialize(PriceStructure.Default()), PricingSerializer.Serialize(vm.Project.Pricing));
    }

    [Fact]
    public void SaveAsMyDefault_AndUseMyDefault()
    {
        var (vm, store) = Designer();
        vm.Pricing.Name = "Builders";
        vm.Pricing.SaveAsDefaultCommand.Execute(null);
        Assert.False(vm.Pricing.MessageIsError);
        Assert.Equal("Builders", store.Settings.LoadDefaultPricing().Name);

        vm.Pricing.RevertCommand.Execute(null);
        Assert.Equal("Retail", vm.Pricing.Name);
        vm.Pricing.UseDefaultCommand.Execute(null);
        Assert.Equal("Builders", vm.Pricing.Name);
        Assert.True(vm.Pricing.HasChanges);
    }

    [Fact]
    public void TheQuoteValue_IsTheGrandTotal_AndSavedForTheQuoteList()
    {
        var (vm, store) = Designer();
        vm.SaveProjectCommand.Execute(null);

        Assert.Equal(vm.Price.GrandTotal, store.Projects.List().Single().Value);
        Assert.Contains(vm.Price.GrandTotal.ToString("N2", System.Globalization.CultureInfo.InvariantCulture), vm.QuoteTotalText);
    }
}
