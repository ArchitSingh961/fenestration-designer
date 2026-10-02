using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;
using Fenestration.Core.Serialization;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Design;

/// <summary>
/// Milestone 9: openings (sashes), design templates, design information and the outside view.
/// Default rules: 60 mm frame/mullion/transom, 250 mm minimum sash opening. A 1200 × 1500 frame has the opening
/// (60, 60)–(1140, 1440), i.e. 1080 × 1380 of glass.
/// </summary>
public class OpeningTests
{
    private static readonly DesignRules Rules = new();

    private static Frame NewFrame(double width = 1200, double height = 1500) => FrameEditor.CreateFrame(0, 0, width, height, Rules);

    private static DesignTemplate Template(string id) => DesignTemplates.Find(id) ?? throw new InvalidOperationException(id);

    /// <summary>The frame's full saved state (in a project with a fixed Id), to compare before/after.</summary>
    private static string Json(Frame frame) => ProjectSerializer.Serialize(new Project { Id = Guid.Empty, Frames = { frame } });

    private static List<GlassPanel> Panels(Frame frame)
        => frame.GlassPanels.OrderBy(g => g.Boundary.Top).ThenBy(g => g.Boundary.Left).ToList();

    // ── Opening types ───────────────────────────────────────────────

    [Fact]
    public void NewGlass_IsFixed_WithoutMesh()
    {
        var glass = Assert.Single(NewFrame().GlassPanels);
        Assert.Equal(OpeningType.Fixed, glass.Opening);
        Assert.False(glass.HasMesh);
    }

    [Fact]
    public void SetOpening_ChangesOnlyTheChosenPanels_AndKeepsTheirIds()
    {
        var frame = NewFrame();
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var (left, right) = (Panels(frame)[0], Panels(frame)[1]);

        FrameEditor.SetOpening(frame, new[] { right.Id }, OpeningType.SideHungRight, mesh: true, Rules);

        var after = Panels(frame);
        Assert.Equal(left.Id, after[0].Id);
        Assert.Equal(right.Id, after[1].Id);
        Assert.Equal(OpeningType.Fixed, after[0].Opening);
        Assert.Equal(OpeningType.SideHungRight, after[1].Opening);
        Assert.True(after[1].HasMesh);
    }

    [Fact]
    public void SetOpening_WithNullMesh_KeepsTheMesh()
    {
        var frame = NewFrame();
        var glass = frame.GlassPanels[0];
        FrameEditor.SetOpening(frame, new[] { glass.Id }, OpeningType.SideHungLeft, mesh: true, Rules);
        FrameEditor.SetOpening(frame, new[] { glass.Id }, OpeningType.TopHung, mesh: null, Rules);

        Assert.Equal(OpeningType.TopHung, frame.GlassPanels[0].Opening);
        Assert.True(frame.GlassPanels[0].HasMesh);
    }

    [Fact]
    public void SetOpening_OnTooSmallAnOpening_IsRejected_AndNothingChanges()
    {
        // 400 wide frame: two openings of (280 - 60) / 2 = 110 mm each, below the 250 mm sash minimum.
        var frame = NewFrame(400, 1500);
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 200, Rules);
        var before = Json(frame);

        var result = FrameEditor.TrySetOpening(frame, new[] { frame.GlassPanels[0].Id }, OpeningType.SideHungLeft, null, Rules);

        Assert.False(result.Success);
        Assert.Contains("too small", result.Error);
        Assert.Equal(before, Json(frame));
    }

    [Fact]
    public void AnEditThatWouldShrinkASashBelowTheMinimum_IsRejected()
    {
        var frame = NewFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        FrameEditor.SetOpening(frame, new[] { Panels(frame)[0].Id }, OpeningType.SideHungLeft, null, Rules);

        // Moving the mullion to 250 leaves the left sash 250 - 60 - 30 = 160 mm wide.
        var result = FrameEditor.TryMoveDivisions(frame, new[] { new DivisionMove(mullion, 250) }, Rules);

        Assert.False(result.Success);
        Assert.Contains("opening 1", result.Error);
        Assert.Equal(600, Members.DivisionPosition(frame.Profiles.Single(p => p.Id == mullion)));
    }

    [Fact]
    public void SplittingASash_KeepsItsTypeOnTheHalfThatKeepsThePanel_TheNewHalfIsFixed()
    {
        var frame = NewFrame();
        var glass = frame.GlassPanels[0];
        FrameEditor.SetOpening(frame, new[] { glass.Id }, OpeningType.SideHungLeft, mesh: true, Rules);

        FrameEditor.AddDivision(frame, MemberAxis.Horizontal, glass.Id, 750, Rules);

        var kept = frame.GlassPanels.Single(g => g.Id == glass.Id);
        var created = frame.GlassPanels.Single(g => g.Id != glass.Id);
        Assert.Equal(OpeningType.SideHungLeft, kept.Opening);
        Assert.True(kept.HasMesh);
        Assert.Equal(OpeningType.Fixed, created.Opening);
        Assert.False(created.HasMesh);
    }

    [Fact]
    public void OpeningsSurviveResize_Undo_AndSaveLoad()
    {
        var project = new Project();
        var frame = NewFrame();
        project.Frames.Add(frame);
        var history = new CommandHistory();
        history.Execute(new SetOpeningCommand(frame, new[] { frame.GlassPanels[0].Id }, OpeningType.TiltTurnRight, true, Rules));
        history.Execute(new ResizeFrameCommand(frame, 1400, 1600, Rules));

        Assert.Equal(OpeningType.TiltTurnRight, frame.GlassPanels[0].Opening);
        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)).Frames[0].GlassPanels[0];
        Assert.Equal(OpeningType.TiltTurnRight, loaded.Opening);
        Assert.True(loaded.HasMesh);

        history.Undo();
        history.Undo();
        Assert.Equal(OpeningType.Fixed, frame.GlassPanels[0].Opening);
        Assert.False(frame.GlassPanels[0].HasMesh);
        history.Redo();
        Assert.Equal(OpeningType.TiltTurnRight, frame.GlassPanels[0].Opening);
    }

    [Fact]
    public void OldProjectFiles_WithoutOpenings_LoadAsFixed()
    {
        string json = ProjectSerializer.Serialize(new Project { Frames = { NewFrame() } })
            .Replace("\"opening\": \"fixed\",", "")
            .Replace("\"hasMesh\": false,", "");
        Assert.DoesNotContain("opening", json, StringComparison.OrdinalIgnoreCase);

        var glass = ProjectSerializer.Deserialize(json).Frames[0].GlassPanels[0];
        Assert.Equal(OpeningType.Fixed, glass.Opening);
    }

    // ── Templates ───────────────────────────────────────────────────

    [Fact]
    public void Library_HasUniqueIds_AndEveryCategoryHasDesigns()
    {
        Assert.Equal(DesignTemplates.All.Count, DesignTemplates.All.Select(t => t.Id).Distinct().Count());
        foreach (var category in DesignTemplates.Categories)
            Assert.NotEmpty(DesignTemplates.InCategory(category));
        Assert.All(DesignTemplates.All, t => Assert.Contains(t.Category, DesignTemplates.Categories));
    }

    [Fact]
    public void EveryTemplate_AppliesToItsSuggestedSize()
    {
        foreach (var template in DesignTemplates.All)
        {
            var (w, h) = template.SuggestedSize;
            var frame = NewFrame(w, h);
            var result = FrameEditor.TryApplyTemplate(frame, template, null, Rules);
            Assert.True(result.Success, $"{template.Id}: {result.Error}");
            Assert.True(FrameLayout.Compute(frame, Rules).IsValid, template.Id);
        }
    }

    [Fact]
    public void TwoTrackSliding_OnTheWholeFrame_GivesTwoEqualSlidingPanels()
    {
        var frame = NewFrame(1500, 1500);

        FrameEditor.ApplyTemplate(frame, Template("sld-2"), null, Rules);

        var mullion = Assert.Single(frame.Profiles, p => p.ProfileType == ProfileType.Mullion);
        Assert.Equal(750, Members.DivisionPosition(mullion), 6);
        var panels = Panels(frame);
        Assert.Equal(2, panels.Count);
        Assert.Equal(panels[0].Boundary.Width, panels[1].Boundary.Width, 6);
        Assert.Equal(OpeningType.SlidingRight, panels[0].Opening);
        Assert.Equal(OpeningType.SlidingLeft, panels[1].Opening);
    }

    [Fact]
    public void ThreeColumns_HaveEqualGlass_EvenWhenTheMullionIsThickerThanTheFrame()
    {
        var rules = new DesignRules { FrameThicknessMm = 50, MullionThicknessMm = 80 };
        var frame = FrameEditor.CreateFrame(0, 0, 1500, 1200, rules);

        FrameEditor.ApplyTemplate(frame, Template("div-v3"), null, rules);

        var widths = Panels(frame).Select(g => g.Boundary.Width).ToList();
        Assert.Equal(3, widths.Count);
        Assert.All(widths, w => Assert.Equal((1400 - 160) / 3.0, w, 6));
    }

    [Fact]
    public void ApplyingToTheWholeFrame_ReplacesTheDesign_ButKeepsGlassTypeAndSize()
    {
        var frame = NewFrame();
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 400, Rules);
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 800, Rules);
        foreach (var g in frame.GlassPanels) g.GlassDefinitionId = "GLS-8-TGH";

        FrameEditor.ApplyTemplate(frame, Template("cas-left"), null, Rules);

        Assert.DoesNotContain(frame.Profiles, Members.IsDivision);
        var glass = Assert.Single(frame.GlassPanels);
        Assert.Equal(OpeningType.SideHungLeft, glass.Opening);
        Assert.Equal("GLS-8-TGH", glass.GlassDefinitionId);
        Assert.Equal((1200.0, 1500.0), (frame.Width, frame.Height));
    }

    [Fact]
    public void ApplyingToOneOpening_SplitsOnlyThatOpening()
    {
        var frame = NewFrame(2000, 1500);
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 1000, Rules);
        var right = Panels(frame)[1];

        FrameEditor.ApplyTemplate(frame, Template("cas-french"), right.Id, Rules);

        var panels = Panels(frame);
        Assert.Equal(3, panels.Count);
        Assert.Equal(OpeningType.Fixed, panels[0].Opening);
        Assert.Equal(OpeningType.SideHungLeft, panels[1].Opening);
        Assert.Equal(OpeningType.SideHungRight, panels[2].Opening);
        Assert.Equal(2, frame.Profiles.Count(p => p.ProfileType == ProfileType.Mullion));
        // The new mullion runs between the old mullion's and the frame's centrelines only (it splits that opening).
        var added = frame.Profiles.Where(p => p.ProfileType == ProfileType.Mullion).OrderBy(Members.DivisionPosition).Last();
        Assert.Equal(30, Members.SpanOf(added, MemberAxis.Vertical).Start, 6);
        Assert.Equal(1470, Members.SpanOf(added, MemberAxis.Vertical).End, 6);
    }

    [Fact]
    public void FanlightTemplate_BuildsATransomOverTwoSashes_WithWeightedHeights()
    {
        var frame = NewFrame(1200, 1600);

        FrameEditor.ApplyTemplate(frame, Template("cas-fanlight"), null, Rules);

        var panels = Panels(frame);
        Assert.Equal(3, panels.Count);
        Assert.Equal(OpeningType.TopHung, panels[0].Opening);
        Assert.Equal(panels[0].Boundary.Height * 3, panels[1].Boundary.Height, 6);
        Assert.Equal(OpeningType.SideHungLeft, panels[1].Opening);
        Assert.Equal(OpeningType.SideHungRight, panels[2].Opening);
    }

    [Fact]
    public void ATemplateThatDoesNotFit_IsRejected_AndTheFrameIsUnchanged()
    {
        var frame = NewFrame(800, 1200);
        var before = Json(frame);

        var result = FrameEditor.TryApplyTemplate(frame, Template("sld-6"), null, Rules);

        Assert.False(result.Success);
        Assert.StartsWith("Cannot apply", result.Error);
        Assert.Equal(before, Json(frame));
    }

    [Fact]
    public void MeshTemplates_ChangeOnlyTheMesh_AndKeepTheLayout()
    {
        var frame = NewFrame(1500, 1500);
        FrameEditor.ApplyTemplate(frame, Template("sld-2"), null, Rules);
        var ids = frame.GlassPanels.Select(g => g.Id).ToList();

        FrameEditor.ApplyTemplate(frame, Template("mesh-add"), null, Rules);
        Assert.All(frame.GlassPanels, g => Assert.True(g.HasMesh));
        Assert.Equal(ids, frame.GlassPanels.Select(g => g.Id));
        Assert.Equal(OpeningType.SlidingRight, Panels(frame)[0].Opening);

        FrameEditor.ApplyTemplate(frame, Template("mesh-remove"), Panels(frame)[0].Id, Rules);
        Assert.False(Panels(frame)[0].HasMesh);
        Assert.True(Panels(frame)[1].HasMesh);
    }

    [Fact]
    public void ApplyTemplateCommand_IsOneUndoStep()
    {
        var frame = NewFrame(1800, 1500);
        var history = new CommandHistory();
        var before = Json(frame);

        history.Execute(new ApplyTemplateCommand(frame, Template("cas-3"), null, Rules));
        Assert.Equal(3, frame.GlassPanels.Count);
        history.Undo();

        Assert.Equal(before, Json(frame));
        history.Redo();
        Assert.Equal(3, frame.GlassPanels.Count);
    }

    // ── Sash geometry ───────────────────────────────────────────────

    [Fact]
    public void SideHungSash_HasItsHandleOppositeTheHinges_AtMidHeight()
    {
        var frame = NewFrame();
        FrameEditor.SetOpening(frame, new[] { frame.GlassPanels[0].Id }, OpeningType.SideHungLeft, null, Rules);

        var sash = OpeningGeometry.SashOf(frame, frame.GlassPanels[0], Rules)!.Value;

        Near(new Rectangle2D(60, 60, 1080, 1380), sash.Outer);
        Near(new Rectangle2D(115, 115, 970, 1270), sash.Glass);
        Assert.Equal(HandleSide.Right, sash.Side);
        Assert.Equal(1140 - 27.5, sash.Handle.X, 6);
        Assert.Equal(750, sash.HandleHeightMm!.Value, 6);
    }

    [Fact]
    public void FixedGlass_HasNoSash()
        => Assert.Null(OpeningGeometry.SashOf(NewFrame(), NewFrame().GlassPanels[0], Rules));

    [Fact]
    public void InterlockingSlidingPanels_MeetOnTheMullionCentreline()
    {
        var frame = NewFrame(1500, 1500);
        FrameEditor.ApplyTemplate(frame, Template("sld-2"), null, Rules);
        var panels = Panels(frame);

        var left = OpeningGeometry.SashOf(frame, panels[0], Rules)!.Value;
        var right = OpeningGeometry.SashOf(frame, panels[1], Rules)!.Value;

        Assert.Equal(750, left.Outer.Right, 6);
        Assert.Equal(750, right.Outer.Left, 6);
        Assert.Equal(60, left.Outer.Left, 6);
        Assert.Equal(HandleSide.Left, left.Side);
        Assert.Equal(HandleSide.Right, right.Side);
    }

    [Fact]
    public void SlidingNextToFixed_DoesNotExtendOverTheMullion()
    {
        var frame = NewFrame(1500, 1500);
        FrameEditor.ApplyTemplate(frame, Template("mono-fixed-left"), null, Rules);
        var slider = Panels(frame)[1];

        var sash = OpeningGeometry.SashOf(frame, slider, Rules)!.Value;

        Near(slider.Boundary, sash.Outer);
    }

    // ── Outside view ────────────────────────────────────────────────

    [Fact]
    public void Mirror_FlipsGeometryAndHinges_KeepsIdsAndOrder_AndLeavesTheModelAlone()
    {
        var frame = NewFrame(2000, 1500);
        FrameEditor.ApplyTemplate(frame, Template("cas-fixed-left"), null, Rules);
        var original = Json(frame);

        var mirrored = OpeningGeometry.Mirror(frame);

        Assert.Equal(frame.GlassPanels.Select(g => g.Id), mirrored.GlassPanels.Select(g => g.Id));
        // Inside: fixed on the left, casement hinged right on the right. Outside: casement on the left, hinged left.
        Assert.Equal(OpeningType.SideHungLeft, mirrored.GlassPanels[1].Opening);
        Assert.True(mirrored.GlassPanels[1].Boundary.Right < 1000);
        Assert.True(FrameLayout.Compute(mirrored, Rules).IsValid);
        Assert.Equal(original, Json(frame));
    }

    [Fact]
    public void Mirrored_IsAnInvolution()
    {
        foreach (var type in Enum.GetValues<OpeningType>())
            Assert.Equal(type, type.Mirrored().Mirrored());
    }

    // ── Design information ──────────────────────────────────────────

    [Fact]
    public void CreateFrameCommand_GivesTheNextFreeReference()
    {
        var project = new Project();
        var first = CreateFrameCommand.Create(project, 0, 0, 1200, 1500, Rules);
        first.Execute();
        var second = CreateFrameCommand.Create(project, 2000, 0, 1200, 1500, Rules);
        second.Execute();
        first.Undo();
        var third = CreateFrameCommand.Create(project, 4000, 0, 1200, 1500, Rules);

        Assert.Equal("W1", first.Frame.Design.Reference);
        Assert.Equal("W2", second.Frame.Design.Reference);
        Assert.Equal("W1", third.Frame.Design.Reference);
    }

    [Fact]
    public void SetDesignInfo_TrimsAndStores_AndIsUndoable()
    {
        var frame = NewFrame();
        var history = new CommandHistory();

        history.Execute(new SetDesignInfoCommand(frame, new DesignInfo
        {
            Reference = "  W7 ", Quantity = 4, Location = " Bedroom ", FloorDistanceMm = 900
        }));

        Assert.Equal("W7", frame.Design.Reference);
        Assert.Equal(4, frame.Design.Quantity);
        Assert.Equal("Bedroom", frame.Design.Location);
        Assert.Equal(900, frame.Design.FloorDistanceMm);
        history.Undo();
        Assert.Equal("", frame.Design.Reference);
        Assert.Equal(1, frame.Design.Quantity);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-3, null)]
    [InlineData(1, -10.0)]
    [InlineData(1, double.NaN)]
    public void SetDesignInfo_RejectsInvalidValues(int quantity, double? floor)
    {
        var frame = NewFrame();
        var result = FrameEditor.TrySetDesignInfo(frame, new DesignInfo { Reference = "W1", Quantity = quantity, FloorDistanceMm = floor });

        Assert.False(result.Success);
        Assert.Equal("", frame.Design.Reference);
    }

    [Fact]
    public void DesignInfo_RoundTripsThroughTheProjectFile_AndOldFilesGetDefaults()
    {
        var frame = NewFrame();
        FrameEditor.SetDesignInfo(frame, new DesignInfo { Reference = "D2", Quantity = 3, Name = "Patio", FloorDistanceMm = 0 });
        var loaded = ProjectSerializer.Deserialize(Json(frame)).Frames[0];
        Assert.Equal("D2", loaded.Design.Reference);
        Assert.Equal(3, loaded.Design.Quantity);
        Assert.Equal(0, loaded.Design.FloorDistanceMm);

        var old = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(new Project { Frames = { NewFrame() } })
            .Replace("\"design\":", "\"ignoredDesign\":"));
        Assert.Equal(1, old.Frames[0].Design.Quantity);
        Assert.Null(old.Frames[0].Design.FloorDistanceMm);
    }
}
