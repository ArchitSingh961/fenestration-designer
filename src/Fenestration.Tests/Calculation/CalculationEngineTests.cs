using Fenestration.Calculation;
using Fenestration.Core.Design;
using Fenestration.Core.Library;
using Fenestration.Core.Models;
using Fenestration.Core.Serialization;
using Fenestration.Tests.Library;
using Xunit;
using static Fenestration.Tests.Library.TestLibrary;

namespace Fenestration.Tests.Calculation;

/// <summary>
/// Calculation engine on a 1200 × 1500 frame drawn with 60 mm members: glass opening 1080 × 1380 mm
/// (X 60–1140, Y 60–1440). Expected values are worked out by hand in the comments.
/// </summary>
public class CalculationEngineTests
{
    private static readonly CalculationEngine Engine = new();
    private static readonly CalculationRules DefaultRules = new();

    private static CalculationResult Calc(Project project, IProductLibrary? library = null, CalculationRules? rules = null)
        => Engine.Calculate(project, library ?? Create(), rules ?? DefaultRules);

    private static Guid AddMullion(Frame frame, double x = 600) => FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, x, Rules);

    // ── Glass ───────────────────────────────────────────────────────

    [Fact]
    public void Glass_DimensionsAreTheFaceToFaceOpening_ByDefault()
    {
        var (project, frame) = SingleFrame();
        var line = Assert.Single(Calc(project).Glass);

        Assert.Equal(frame.GlassPanels[0].Id, line.GlassPanelId);
        Assert.Equal(frame.Id, line.FrameId);
        Assert.Equal(1080, line.WidthMm);
        Assert.Equal(1380, line.HeightMm);
        Assert.Equal(1, line.Quantity);
    }

    [Fact]
    public void Glass_AreaPerimeterWeightAndCost()
    {
        var (project, _) = SingleFrame();
        var line = Assert.Single(Calc(project).Glass);

        Assert.Equal(1.4904, line.AreaM2);              // 1.08 × 1.38
        Assert.Equal(4.92, line.PerimeterM);            // 2 × (1.08 + 1.38)
        Assert.Equal(22.356, line.WeightKg);            // 1.4904 × 15 kg/m²
        Assert.Equal(1490.40m, line.Cost);              // 1.4904 × 1000/m²
        Assert.True(line.IsDefault);
        Assert.Equal(Clear6, line.DefinitionId);
    }

    [Theory]
    [InlineData(Clear6, "6mm Clear", "Float", 6, "1490.40")]
    [InlineData(Toughened8, "8mm Toughened", "Toughened", 8, "2980.80")]
    [InlineData(Laminated10, "10mm Laminated", "Laminated", 10, "4471.20")]
    public void Glass_TypeThicknessAndCost_ComeFromTheLibraryDefinition(string id, string name, string category, double thickness, string cost)
    {
        var (project, frame) = SingleFrame();
        FrameEditor.AssignGlass(frame, new[] { frame.GlassPanels[0].Id }, id, Create(), Rules);
        var line = Assert.Single(Calc(project).Glass);

        Assert.Equal(id, line.DefinitionId);
        Assert.False(line.IsDefault);
        Assert.Equal(name, line.Name);
        Assert.Equal(category, line.Category);
        Assert.Equal(thickness, line.ThicknessMm);
        Assert.Equal(decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture), line.Cost);
    }

    [Fact]
    public void Glass_SmallerThanTheMinimumChargeableArea_IsChargedAsTheMinimum()
    {
        var (project, frame) = SingleFrame(400, 400);                    // opening 280 × 280 = 0.0784 m²
        FrameEditor.AssignGlass(frame, frame.GlassPanels.Select(g => g.Id).ToList(), Laminated10, Create(), Rules);
        var line = Assert.Single(Calc(project).Glass);

        Assert.Equal(0.0784, line.AreaM2);
        Assert.Equal(1.0, line.ChargeableAreaM2);
        Assert.Equal(3000.00m, line.Cost);
    }

    [Fact]
    public void Glass_SizeAddsTheGlazingBiteOfEachSurroundingProfile_AndDeductsTheClearance()
    {
        var library = new ProductLibrary(
            profiles: new[]
            {
                new ProfileDefinition { Id = "F", Name = "F", Roles = new[] { ProfileType.Frame }, FaceWidthMm = 60, GlazingBiteMm = 10 },
                new ProfileDefinition { Id = "M", Name = "M", Roles = new[] { ProfileType.Mullion }, FaceWidthMm = 60, GlazingBiteMm = 12 }
            },
            glass: new[] { new GlassDefinition { Id = "G", Name = "G", ThicknessMm = 6 } },
            defaults: new LibraryDefaults { FrameProfileId = "F", MullionProfileId = "M", GlassId = "G" });
        var (project, frame) = SingleFrame();
        AddMullion(frame);

        var glass = Calc(project, library, new CalculationRules { GlassEdgeClearanceMm = 2 }).Glass;

        // Left pane: opening 60–570 = 510 wide; + 10 (frame bite) + 12 (mullion bite) − 2 × 2 clearance.
        Assert.Equal(528, glass[0].WidthMm);
        // Height: opening 1380 + 10 + 10 − 4.
        Assert.Equal(1396, glass[0].HeightMm);
        Assert.Equal(528, glass[1].WidthMm);
    }

    [Fact]
    public void Glass_ClearanceLargerThanTheGlass_IsAnError()
    {
        var (project, _) = SingleFrame();
        var result = Calc(project, rules: new CalculationRules { GlassEdgeClearanceMm = 600 });

        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, i => i.Message.Contains("no glass left"));
        Assert.Equal(0, result.Glass[0].WidthMm);
    }

    // ── Profiles ────────────────────────────────────────────────────

    [Fact]
    public void OuterFrame_MitredMembersAreCutToTheOuterSize()
    {
        var (project, frame) = SingleFrame();
        var lines = Calc(project).Profiles;

        Assert.Equal(frame.Profiles.Select(p => p.Id), lines.Select(l => l.ProfileId));     // design order
        Assert.Equal(new[] { 1500.0, 1200.0, 1500.0, 1200.0 }, lines.Select(l => l.CutLengthMm));   // left, top, right, bottom
        Assert.All(lines, l => Assert.Equal((45.0, 45.0), (l.StartCutAngle, l.EndCutAngle)));
        Assert.All(lines, l => Assert.Equal(ProfileType.Frame, l.Role));
    }

    [Fact]
    public void OuterFrame_ButtJoint_HorizontalsFitBetweenTheVerticals()
    {
        var (project, _) = SingleFrame();
        var lines = Calc(project, rules: new CalculationRules { FrameJoint = FrameJointType.Butt }).Profiles;

        Assert.Equal(new[] { 1500.0, 1080.0, 1500.0, 1080.0 }, lines.Select(l => l.CutLengthMm));   // 1200 − 2 × 60
        Assert.All(lines, l => Assert.Equal((90.0, 90.0), (l.StartCutAngle, l.EndCutAngle)));
    }

    [Fact]
    public void Profile_WeightAndCost_ArePerMetreOfCutLength()
    {
        var (project, _) = SingleFrame();
        var lines = Calc(project).Profiles;

        Assert.Equal(2.25, lines[0].WeightKg);          // 1.5 m × 1.5 kg/m
        Assert.Equal(225.00m, lines[0].Cost);           // 1.5 m × 150/m
        Assert.Equal(180.00m, lines[1].Cost);           // 1.2 m × 150/m
        Assert.Equal(810.00m, lines.Sum(l => l.Cost));
    }

    [Fact]
    public void Mullion_IsCutFaceToFace_PlusTheAllowancePerEnd()
    {
        var (project, frame) = SingleFrame();
        var mullionId = AddMullion(frame);

        var line = Calc(project).FindProfile(mullionId)!;
        Assert.Equal(1380, line.CutLengthMm);           // frame faces at Y 60 and 1440; MUL-60 has no allowance
        Assert.Equal((90.0, 90.0), (line.StartCutAngle, line.EndCutAngle));
        Assert.Equal(165.60m, line.Cost);               // 1.38 m × 120/m

        FrameEditor.AssignProfile(frame, new[] { mullionId }, Mullion80, Create(), Rules);
        line = Calc(project).FindProfile(mullionId)!;
        Assert.Equal(1390, line.CutLengthMm);           // + 2 × 5 mm
        Assert.Equal(278.00m, line.Cost);               // 1.39 m × 200/m
    }

    [Theory]
    [InlineData(Frame60, 810, 8.1, 1080, 1380)]
    [InlineData(Frame50, 540, 5.4, 1100, 1400)]
    public void DifferentFrameProfiles_GiveDifferentCostWeightAndGlass(string id, double cost, double weight, double glassWidth, double glassHeight)
    {
        var (project, frame) = SingleFrame();
        FrameEditor.AssignProfile(frame, frame.Profiles.Select(p => p.Id).ToList(), id, Create(), Rules);
        var result = Calc(project);

        Assert.All(result.Profiles, l => Assert.Equal(id, l.DefinitionId));
        Assert.Equal((decimal)cost, result.Cost.Profiles);
        Assert.Equal(weight, result.Profiles.Sum(l => l.WeightKg), 6);
        Assert.Equal((glassWidth, glassHeight), (result.Glass[0].WidthMm, result.Glass[0].HeightMm));
    }

    // ── Materials ───────────────────────────────────────────────────

    [Fact]
    public void Materials_ComeFromTheUsageRulesOfTheDefinitions()
    {
        var (project, frame) = SingleFrame();
        var library = Create();
        FrameEditor.AssignProfile(frame, frame.Profiles.Select(p => p.Id).ToList(), Frame50, library, Rules);
        FrameEditor.AssignGlass(frame, frame.GlassPanels.Select(g => g.Id).ToList(), Toughened8, library, Rules);

        var result = Calc(project, library);
        var cleats = result.Materials.Where(m => m.MaterialId == Cleat).ToList();
        Assert.Equal(4, cleats.Count);                                   // one per frame piece
        Assert.All(cleats, m => Assert.Equal(50.00m, m.Cost));
        var gasket = result.Materials.Where(m => m.MaterialId == Gasket).ToList();
        Assert.Equal(5.4 + 5.0, gasket.Sum(m => m.Quantity), 6);         // 5.4 m of frame + 2 × (1.1 + 1.4) m of glass edge
        Assert.Equal(104.00m, gasket.Sum(m => m.Cost));                  // 10.4 m × 10
        Assert.Equal(304.00m, result.Cost.Materials);
    }

    // ── BOM, cut list, totals ───────────────────────────────────────

    [Fact]
    public void Bom_AggregatesProfilesGlassAndMaterials()
    {
        var (project, frame) = SingleFrame();
        AddMullion(frame);                                               // two panes 510 × 1380
        var result = Calc(project);

        Assert.Equal(new[]
        {
            new BomLine(BomCategory.Profile, Frame60, "60mm Frame", "4 pcs, 5.4 m", 4, "pcs", 5400, null, 8.1, 810.00m),
            new BomLine(BomCategory.Profile, Mullion60, "60mm Mullion", "1 pcs, 1.38 m", 1, "pcs", 1380, null, 1.656, 165.60m),
            new BomLine(BomCategory.Glass, Clear6, "6mm Clear", "510 × 1380 mm", 2, "pcs", null, 1.4076, 21.114, 1407.60m),
            new BomLine(BomCategory.Accessory, Block, "Setting block", "", 8, "pcs", null, null, null, 16.00m)
        }, result.Bom);
    }

    [Fact]
    public void CutList_GroupsIdenticalPieces_LongestFirst()
    {
        var (project, frame) = SingleFrame();
        AddMullion(frame);

        Assert.Equal(new[]
        {
            new CutListLine(Frame60, "60mm Frame", 1500, 45, 45, 2, 6000),
            new CutListLine(Frame60, "60mm Frame", 1200, 45, 45, 2, 6000),
            new CutListLine(Mullion60, "60mm Mullion", 1380, 90, 90, 1, 6000)
        }, Calc(project).CutList);
    }

    [Fact]
    public void Cost_IsTheSumOfProfilesGlassAndMaterials_PerFrameAndInTotal()
    {
        var (project, frame) = SingleFrame();
        AddMullion(frame);
        var second = FrameEditor.CreateFrame(2000, 0, 1200, 1500, Rules);
        project.Frames.Add(second);

        var result = Calc(project);
        var first = result.FindFrame(frame.Id)!;
        Assert.Equal(new CostSummary(975.60m, 1407.60m, 16.00m), first.Cost);
        Assert.Equal(2399.20m, first.Cost.Total);
        Assert.Equal(2308.40m, result.FindFrame(second.Id)!.Cost.Total);  // 810 + 1490.40 + 8
        Assert.Equal(2399.20m + 2308.40m, result.Cost.Total);
        Assert.Equal(result.Bom.Sum(b => b.Cost), result.Cost.Total);
        Assert.Equal("INR", result.Currency);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Weight_IsProfilesPlusGlass()
    {
        var (project, _) = SingleFrame();
        Assert.Equal(8.1 + 22.356, Calc(project).WeightKg, 6);
    }

    // ── Determinism and isolation ───────────────────────────────────

    [Fact]
    public void Calculation_IsDeterministic()
    {
        var (project, frame) = SingleFrame();
        AddMullion(frame);
        FrameEditor.AddDivision(frame, MemberAxis.Horizontal, frame.GlassPanels[1].Id, 700, Rules);

        var a = Calc(project);
        var b = Calc(project);
        Assert.Equal(a.Bom, b.Bom);
        Assert.Equal(a.CutList, b.CutList);
        Assert.Equal(a.Glass, b.Glass);
        Assert.Equal(a.Profiles, b.Profiles);
        Assert.Equal(a.Materials, b.Materials);
        Assert.Equal(a.Cost, b.Cost);
    }

    [Fact]
    public void Calculation_DoesNotModifyTheDesign()
    {
        var (project, frame) = SingleFrame();
        AddMullion(frame);
        string before = ProjectSerializer.Serialize(project);
        Calc(project, rules: new CalculationRules { GlassEdgeClearanceMm = 3, FrameJoint = FrameJointType.Butt });
        Assert.Equal(before, ProjectSerializer.Serialize(project));
    }

    [Fact]
    public void InvalidRules_AreRejected()
    {
        var (project, _) = SingleFrame();
        Assert.Throws<ArgumentOutOfRangeException>(() => Calc(project, rules: new CalculationRules { GlassEdgeClearanceMm = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calc(project, rules: new CalculationRules { MoneyDecimals = 9 }));
    }

    // ── Invalid references ──────────────────────────────────────────

    [Fact]
    public void MissingGlassReference_IsAnError_AndThePaneIsNotPriced()
    {
        var (project, frame) = SingleFrame();
        var panel = frame.GlassPanels[0];
        panel.GlassDefinitionId = "NOPE";

        var result = Calc(project);
        var line = Assert.Single(result.Glass);
        Assert.False(result.IsComplete);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(panel.Id, issue.ObjectId);
        Assert.Contains("'NOPE', which is not in the library", issue.Message);
        Assert.False(line.IsResolved);
        Assert.Equal("(missing: NOPE)", line.Name);
        Assert.Equal(0m, line.Cost);
        Assert.Equal(1080, line.WidthMm);                                 // geometry is still reported
        Assert.DoesNotContain(result.Bom, b => b.Category == BomCategory.Glass);
        Assert.Equal(810.00m, result.Cost.Total);                         // the rest is still priced
    }

    [Fact]
    public void MissingProfileReference_IsAnError()
    {
        var (project, frame) = SingleFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        frame.Profiles.Single(p => p.Id == mullion).ProfileDefinitionId = "GONE";

        var result = Calc(project);
        Assert.Contains(result.Issues, i => i.ObjectId == mullion && i.Message.Contains("'GONE', which is not in the library"));
        Assert.False(result.FindProfile(mullion)!.IsResolved);
        Assert.DoesNotContain(result.CutList, c => c.DefinitionId == "GONE");
    }

    [Fact]
    public void NoReferenceAndNoDefault_IsAnError()
    {
        var (project, _) = SingleFrame();
        var result = Calc(project, ProductLibrary.Empty);

        Assert.Equal(5, result.Issues.Count(i => i.Severity == IssueSeverity.Error));   // 4 frame members + 1 pane
        Assert.Contains(result.Issues, i => i.Message.Contains("no default frame profile"));
        Assert.Contains(result.Issues, i => i.Message.Contains("no default glass"));
        Assert.Equal(0m, result.Cost.Total);
        Assert.Empty(result.Bom);
        Assert.Equal(1080, result.Glass[0].WidthMm);
    }

    [Fact]
    public void ProfileUsedInTheWrongRole_IsAnError()
    {
        var (project, frame) = SingleFrame();
        var mullion = AddMullion(frame);
        frame.Profiles.Single(p => p.Id == mullion).ProfileDefinitionId = Frame60;   // bypassing the validated edit

        var result = Calc(project);
        Assert.Contains(result.Issues, i => i.ObjectId == mullion && i.Message == "'60mm Frame' cannot be used as a mullion.");
    }

    [Fact]
    public void DrawnSizeDifferentFromTheLibrary_IsAWarning()
    {
        var (project, frame) = SingleFrame();
        frame.GlassPanels[0].GlassDefinitionId = Toughened8;          // thickness left at 6 mm

        var result = Calc(project);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("drawn 6 mm thick but '8mm Toughened' is 8 mm", issue.Message);
        Assert.True(result.IsComplete);
        Assert.Equal(8, result.Glass[0].ThicknessMm);                 // the library is the source of truth
        Assert.Equal(2980.80m, result.Glass[0].Cost);
    }

    [Fact]
    public void EmptyProject_GivesAnEmptyResult()
    {
        var result = Calc(new Project());
        Assert.Empty(result.Bom);
        Assert.Equal(0m, result.Cost.Total);
        Assert.True(result.IsComplete);
    }
}
