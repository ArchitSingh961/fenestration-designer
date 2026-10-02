using System.IO;
using System.Text.Json;
using Mark.Calculation;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Calculation;

/// <summary>
/// M7 on top of M6: the plan consumes the calculation's profile lines, follows material changes and undo, is
/// cached by <see cref="CalculationService"/>, and its rules and stock lengths come from data files.
/// </summary>
public class CuttingPlanIntegrationTests
{
    /// <summary>1200 × 1500 frame (60 mm members) with a centred mullion: frame 1500, 1500, 1200, 1200 (mitred), mullion 1380.</summary>
    private static (Project Project, Frame Frame) FrameWithMullion()
    {
        var (project, frame) = SingleFrame();
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, null, Rules);
        return (project, frame);
    }

    [Fact]
    public void ThePlan_ConsumesTheM6ProfileLines()
    {
        var (project, frame) = FrameWithMullion();
        var library = Create();
        var rules = new CalculationRules();
        var result = new CalculationEngine().Calculate(project, library, rules);

        var plan = new CuttingOptimizer().Optimize(result, library, rules);

        Assert.True(plan.IsComplete);
        Assert.Equal(new[] { Frame60, Mullion60 }, plan.Profiles.Select(p => p.DefinitionId));
        Assert.Equal(result.Profiles.Sum(p => p.CutLengthMm), plan.TotalCutMm);                    // 5400 + 1380
        foreach (var profile in plan.Profiles)                                                     // agrees with the M6 cut list
            Assert.Equal(result.CutList.Where(c => c.DefinitionId == profile.DefinitionId).Sum(c => c.CutLengthMm * c.Quantity),
                profile.TotalCutMm);

        var frameBar = Assert.Single(plan.FindProfile(Frame60)!.Bars);
        Assert.Equal(new[] { 1500.0, 1500, 1200, 1200 }, frameBar.Cuts.Select(c => c.CutLengthMm));
        Assert.Equal(frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame).Select(p => p.Id).Order(),
            frameBar.Cuts.Select(c => c.ProfileId).Order());                                       // linked to the drawing
        Assert.All(frameBar.Cuts, c => Assert.Equal((45.0, 45.0), (c.StartCutAngle, c.EndCutAngle)));
        Assert.Equal((6000.0, 600.0), (frameBar.StockLengthMm, frameBar.RemainingMm));
        Assert.Equal(900.00m, frameBar.Cost);                                                      // 6 m × 150 (library)
    }

    [Fact]
    public void Service_PlansLazily_CachesAndReplansAfterInvalidate()
    {
        var (project, _) = FrameWithMullion();
        var service = new CalculationService(() => project, Create());
        Assert.Equal(0, service.OptimizationCount);

        var first = service.CuttingPlan;
        Assert.Same(first, service.CuttingPlan);
        Assert.Equal((1, 1), (service.CalculationCount, service.OptimizationCount));

        service.Invalidate();
        Assert.Equal(1, service.OptimizationCount);                                                // nothing done until read
        var second = service.CuttingPlan;
        Assert.NotSame(first, second);
        Assert.Equal((2, 2), (service.CalculationCount, service.OptimizationCount));
    }

    [Fact]
    public void Service_UsesTheConfiguredCuttingRules()
    {
        var (project, _) = SingleFrame();
        var cutting = new CuttingRules { KerfMm = 3, TrimAllowanceMm = 5, MinUsableOffcutMm = 300 };
        var service = new CalculationService(() => project, Create(), new CalculationRules { Cutting = cutting });

        var bar = Assert.Single(service.CuttingPlan.FindProfile(Frame60)!.Bars);

        Assert.Equal(cutting, service.CuttingPlan.Rules);
        Assert.Equal((5.0, 12.0, 583.0), (bar.TrimMm, bar.KerfMm, bar.RemainingMm));             // 6000 − 5 − 5400 − 4 × 3
    }

    [Fact]
    public void ChangeProfile_ThePlanFollows_AndUndoRestoresIt()
    {
        var (project, frame) = SingleFrame();
        var library = Create();
        var history = new CommandHistory();
        var service = new CalculationService(() => project, library);
        history.HistoryChanged += service.Invalidate;
        var before = service.CuttingPlan;
        Assert.Equal(Frame60, Assert.Single(before.Profiles).DefinitionId);

        history.Execute(AssignProfileCommand.ForOuterFrame(frame, Frame50, library, Rules));

        var after = Assert.Single(service.CuttingPlan.Profiles);
        Assert.Equal((Frame50, "50mm Frame"), (after.DefinitionId, after.Name));
        Assert.Equal(600.00m, after.StockCost);                                                    // one 6000 bar × 100/m

        history.Undo();
        Assert.NotSame(before, service.CuttingPlan);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(service.CuttingPlan));   // the same plan again
    }

    [Fact]
    public void ChangeMullion_ToALongerStockProfile_UsesItsStockLength()
    {
        var (project, frame) = FrameWithMullion();
        var library = Create();
        var mullion = frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion).Id;
        FrameEditor.AssignProfile(frame, new[] { mullion }, Mullion80, library, Rules);

        var plan = new CuttingOptimizer().Optimize(new CalculationEngine().Calculate(project, library, new CalculationRules()),
            library, new CalculationRules());

        var bar = Assert.Single(plan.FindProfile(Mullion80)!.Bars);
        Assert.Equal(6500.0, bar.StockLengthMm);                                                   // Mullion80's library stock
        Assert.Equal(1390.0, bar.CutLengthMm);                                                     // 1380 + 2 × 5 allowance (M6)
    }

    [Fact]
    public void UnpricedProfiles_AreReportedByThePlanToo()
    {
        var (project, _) = SingleFrame();
        var service = new CalculationService(() => project, ProductLibrary.Empty);

        Assert.False(service.CuttingPlan.IsComplete);
        Assert.Equal(4, service.CuttingPlan.Issues.Count);
        Assert.Empty(service.CuttingPlan.Profiles);
    }

    // ── Data files ──────────────────────────────────────────────────

    [Fact]
    public void Rules_JsonRoundTrip()
    {
        var rules = new CalculationRules
        {
            FrameJoint = FrameJointType.Butt,
            GlassEdgeClearanceMm = 2,
            Cutting = new CuttingRules { KerfMm = 3.5, TrimAllowanceMm = 10, MinUsableOffcutMm = 250 }
        };

        var copy = CalculationRulesSerializer.Deserialize(CalculationRulesSerializer.Serialize(rules));

        Assert.Equal(rules, copy);
    }

    [Fact]
    public void Rules_MissingValuesKeepTheirDefaults_AndCommentsAreAllowed()
    {
        var rules = CalculationRulesSerializer.Deserialize("""
            { // only the kerf
              "cutting": { "kerfMm": 4 } }
            """);

        Assert.Equal(new CalculationRules { Cutting = new CuttingRules { KerfMm = 4 } }, rules);
    }

    [Theory]
    [InlineData("""{ "cutting": { "kerfMm": -3 } }""")]
    [InlineData("""{ "cutting": { "minUsableOffcutMm": 99999 } }""")]
    [InlineData("""{ "frameJoint": "glued" }""")]
    [InlineData("""{ "cutting": null }""")]
    [InlineData("{ not json")]
    public void Rules_Invalid_AreRejected(string json)
    {
        Assert.Throws<InvalidOperationException>(() => CalculationRulesSerializer.Deserialize(json));
    }

    [Fact]
    public void ShippedRules_LoadWithTheWorkshopValues()
    {
        var rules = CalculationRulesSerializer.Load(Path.Combine(TestPaths.RepositoryRoot, "src", "Mark.App", "Settings",
            "calculation-rules.json"));

        Assert.Equal(new CuttingRules { KerfMm = 3, TrimAllowanceMm = 5, MinUsableOffcutMm = 300 }, rules.Cutting);
    }

    [Fact]
    public void ShippedLibrary_OffersMultipleStockLengths()
    {
        var library = LibrarySerializer.Load(Path.Combine(TestPaths.RepositoryRoot, "src", "Mark.App", "Library", "library.json"));

        Assert.Equal(new[] { 6000.0, 6500, 7000 }, library.FindProfile("PRF-FRM-60")!.AvailableStockLengthsMm());
        Assert.Equal(new[] { 5800.0 }, library.FindProfile("PRF-UPVC-FRM-70")!.AvailableStockLengthsMm());
    }

    [Fact]
    public void Library_StockLengths_RoundTripThroughJson()
    {
        var library = new ProductLibrary(profiles: new[]
        {
            new ProfileDefinition { Id = "P", Name = "P", Roles = new[] { ProfileType.Frame }, FaceWidthMm = 50,
                StockLengthMm = 6000, StockLengthsMm = new double[] { 7000, 6500 } }
        });

        var copy = LibrarySerializer.Deserialize(LibrarySerializer.Serialize(library));

        Assert.Equal(new[] { 7000.0, 6500 }, copy.FindProfile("P")!.StockLengthsMm);
        Assert.Equal(new[] { 6000.0, 6500, 7000 }, copy.FindProfile("P")!.AvailableStockLengthsMm());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-6000)]
    [InlineData(double.NaN)]
    [InlineData(40_000)]
    public void Library_InvalidStockLengths_AreRejected(double length)
    {
        Assert.Throws<LibraryValidationException>(() => new ProductLibrary(profiles: new[]
        {
            new ProfileDefinition { Id = "P", Name = "P", Roles = new[] { ProfileType.Frame }, FaceWidthMm = 50,
                StockLengthsMm = new[] { 6000, length } }
        }));
    }
}
