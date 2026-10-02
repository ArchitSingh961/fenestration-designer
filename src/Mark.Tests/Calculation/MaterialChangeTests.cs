using Mark.Calculation;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Tests.Library;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Calculation;

/// <summary>
/// Changing glass and profiles after design: validate → update the model through an undoable command →
/// invalidate → recalculate → changed BOM and cost. Nothing has to be deleted and recreated.
/// </summary>
public class MaterialChangeTests
{
    private sealed class Session
    {
        public Session(double width = 1200, double height = 1500)
        {
            (Project, Frame) = SingleFrame(width, height);
            History.HistoryChanged += Calculation.Invalidate;   // as MainViewModel does
        }

        public Project Project { get; }
        public Frame Frame { get; }
        public Core.Library.ProductLibrary Library { get; } = Create();
        public CommandHistory History { get; } = new();

        private CalculationService? _calculation;
        public CalculationService Calculation => _calculation ??= new CalculationService(() => Project, Library);

        public Guid Glass => Frame.GlassPanels[0].Id;

        public string Saved() => ProjectSerializer.Serialize(Project);
    }

    // ── Glass ───────────────────────────────────────────────────────

    [Fact]
    public void Calculate_ChangeGlass_Recalculate_ResultChanges()
    {
        var s = new Session();
        var before = s.Calculation.Result;
        Assert.Equal(1490.40m, before.Cost.Glass);

        s.History.Execute(new AssignGlassCommand(s.Frame, new[] { s.Glass }, Toughened8, s.Library, Rules));

        Assert.True(s.Calculation.IsStale);
        var after = s.Calculation.Result;
        var line = Assert.Single(after.Glass);
        Assert.Equal(Toughened8, line.DefinitionId);
        Assert.Equal("8mm Toughened", line.Name);
        Assert.Equal(8, line.ThicknessMm);
        Assert.Equal(2980.80m, after.Cost.Glass);
        Assert.Equal(49.20m, after.Cost.Materials);                     // gasket: 4.92 m × 10 (setting blocks were 6mm Clear's)
        Assert.Contains(after.Bom, b => b.ItemId == Toughened8);
        Assert.DoesNotContain(after.Bom, b => b.ItemId == Clear6 || b.ItemId == Block);
        Assert.Equal(1490.40m, before.Cost.Glass);                      // an old result is immutable
    }

    [Fact]
    public void ChangeGlass_KeepsThePanelItsIdAndGeometry()
    {
        var s = new Session();
        var panel = s.Frame.GlassPanels[0];
        var boundary = panel.Boundary;

        s.History.Execute(new AssignGlassCommand(s.Frame, new[] { s.Glass }, Toughened8, s.Library, Rules));

        var updated = Assert.Single(s.Frame.GlassPanels);
        Assert.Equal(panel.Id, updated.Id);
        Assert.Equal(boundary, updated.Boundary);
        Assert.Equal(Toughened8, updated.GlassDefinitionId);
        Assert.Equal(8, updated.Thickness);
        Assert.Empty(s.Calculation.Result.Issues);
    }

    [Fact]
    public void ChangeGlass_Undo_Redo()
    {
        var s = new Session();
        string original = s.Saved();
        s.History.Execute(new AssignGlassCommand(s.Frame, new[] { s.Glass }, Toughened8, s.Library, Rules));
        string changed = s.Saved();

        s.History.Undo();
        Assert.Equal(original, s.Saved());
        Assert.Equal(1490.40m, s.Calculation.Result.Cost.Glass);

        s.History.Redo();
        Assert.Equal(changed, s.Saved());
        Assert.Equal(2980.80m, s.Calculation.Result.Cost.Glass);
    }

    [Fact]
    public void ChangeGlass_ToAnUnknownId_IsRejectedAndNotRecorded()
    {
        var s = new Session();
        string original = s.Saved();

        var ex = Assert.Throws<DesignValidationException>(() =>
            s.History.Execute(new AssignGlassCommand(s.Frame, new[] { s.Glass }, "NOPE", s.Library, Rules)));

        Assert.Contains("'NOPE' is not in the library", ex.Message);
        Assert.Equal(original, s.Saved());
        Assert.False(s.History.CanUndo);
    }

    [Fact]
    public void ChangeGlass_ForAPanelOfAnotherFrame_IsRejected()
    {
        var s = new Session();
        var result = FrameEditor.TryAssignGlass(s.Frame, new[] { Guid.NewGuid() }, Toughened8, s.Library, Rules);
        Assert.False(result.Success);
        Assert.Null(s.Frame.GlassPanels[0].GlassDefinitionId);
    }

    [Fact]
    public void SplittingAssignedGlass_GivesBothPanesTheSameGlassType()
    {
        var s = new Session();
        s.History.Execute(new AssignGlassCommand(s.Frame, new[] { s.Glass }, Toughened8, s.Library, Rules));

        s.History.Execute(AddDivisionCommand.Mullion(s.Frame, Rules));

        Assert.Equal(2, s.Frame.GlassPanels.Count);
        Assert.All(s.Frame.GlassPanels, g => Assert.Equal((Toughened8, 8.0), (g.GlassDefinitionId, g.Thickness)));
        Assert.All(s.Calculation.Result.Glass, l => Assert.Equal("8mm Toughened", l.Name));
    }

    // ── Profiles ────────────────────────────────────────────────────

    [Fact]
    public void Calculate_ChangeFrameProfile_Recalculate_ResultChanges()
    {
        var s = new Session();
        Assert.Equal(810.00m, s.Calculation.Result.Cost.Profiles);

        s.History.Execute(AssignProfileCommand.ForOuterFrame(s.Frame, Frame50, s.Library, Rules));

        var after = s.Calculation.Result;
        Assert.All(after.Profiles, l => Assert.Equal((Frame50, "50mm Frame"), (l.DefinitionId!, l.Name)));
        Assert.Equal(540.00m, after.Cost.Profiles);                      // 5.4 m × 100
        Assert.Equal(new[] { 1500.0, 1200.0, 1500.0, 1200.0 }, after.Profiles.Select(l => l.CutLengthMm));
        var glass = Assert.Single(after.Glass);
        Assert.Equal((1100.0, 1400.0), (glass.WidthMm, glass.HeightMm));  // narrower frame → bigger glass
        Assert.Equal(1540.00m, glass.Cost);
        Assert.Contains(after.Bom, b => b.ItemId == Cleat && b.Quantity == 4);
    }

    [Fact]
    public void ChangeFrameProfile_KeepsTheOuterSize_AndMovesTheCentrelines()
    {
        var s = new Session();
        var mullion = FrameEditor.AddDivision(s.Frame, MemberAxis.Vertical, null, 600, Rules);

        s.History.Execute(AssignProfileCommand.ForOuterFrame(s.Frame, Frame50, s.Library, Rules));

        var outer = FrameMembers.Find(s.Frame)!;
        Assert.Equal((1200.0, 1500.0), (s.Frame.Width, s.Frame.Height));
        Assert.Equal(25, outer.Left.StartPoint.X);
        Assert.Equal(1175, outer.Right.StartPoint.X);
        Assert.Equal(1475, outer.Bottom.StartPoint.Y);
        Assert.All(new[] { outer.Left, outer.Top, outer.Right, outer.Bottom }, p => Assert.Equal(50, p.Thickness));
        // The mullion still runs between the frame centrelines (its ends followed them) and keeps its own profile.
        var m = s.Frame.Profiles.Single(p => p.Id == mullion);
        Assert.Equal(new Point2D(600, 25), m.StartPoint);
        Assert.Equal(new Point2D(600, 1475), m.EndPoint);
        Assert.Null(m.ProfileDefinitionId);
        Assert.Equal(1400, s.Calculation.Result.FindProfile(mullion)!.CutLengthMm);   // faces at 50 and 1450
    }

    [Fact]
    public void ChangeMullionProfile_ChangesItsWidthCutAndCost()
    {
        var s = new Session();
        var add = AddDivisionCommand.Mullion(s.Frame, Rules, x: 600);
        s.History.Execute(add);
        var mullion = add.CreatedProfileId!.Value;
        Assert.Equal(165.60m, s.Calculation.Result.FindProfile(mullion)!.Cost);

        s.History.Execute(new AssignProfileCommand(s.Frame, new[] { mullion }, Mullion80, s.Library, Rules));

        Assert.Equal(80, s.Frame.Profiles.Single(p => p.Id == mullion).Thickness);
        var result = s.Calculation.Result;
        Assert.Equal(278.00m, result.FindProfile(mullion)!.Cost);
        Assert.All(result.Glass, g => Assert.Equal(500, g.WidthMm));      // 60–560 and 640–1140
    }

    [Fact]
    public void ChangeProfile_Undo_RestoresGeometryAndCost()
    {
        var s = new Session();
        string original = s.Saved();
        decimal total = s.Calculation.Result.Cost.Total;

        s.History.Execute(AssignProfileCommand.ForOuterFrame(s.Frame, Frame50, s.Library, Rules));
        Assert.NotEqual(total, s.Calculation.Result.Cost.Total);

        s.History.Undo();
        Assert.Equal(original, s.Saved());
        Assert.Equal(total, s.Calculation.Result.Cost.Total);
    }

    [Fact]
    public void ChangeProfile_ToTheWrongRole_IsRejected()
    {
        var s = new Session();
        string original = s.Saved();

        var ex = Assert.Throws<DesignValidationException>(() =>
            s.History.Execute(AssignProfileCommand.ForOuterFrame(s.Frame, Mullion60, s.Library, Rules)));

        Assert.Equal("'60mm Mullion' cannot be used as a frame.", ex.Message);
        Assert.Equal(original, s.Saved());
    }

    [Fact]
    public void ChangeProfile_ToAnUnknownId_IsRejected()
    {
        var s = new Session();
        var result = FrameEditor.TryAssignProfile(s.Frame, s.Frame.Profiles.Select(p => p.Id).ToList(), "NOPE", s.Library, Rules);
        Assert.False(result.Success);
        Assert.Contains("'NOPE' is not in the library", result.Error);
    }

    [Fact]
    public void ChangeProfile_ThatLeavesNoGlass_IsRejected()
    {
        var s = new Session(300, 300);
        string original = s.Saved();

        var result = FrameEditor.TryAssignProfile(s.Frame, s.Frame.Profiles.Select(p => p.Id).ToList(), Frame200, s.Library, Rules);

        Assert.False(result.Success);
        Assert.StartsWith("Cannot change the profile: ", result.Error);
        Assert.Equal(original, s.Saved());
    }

    // ── Recalculation is driven by the history ──────────────────────

    [Fact]
    public void EveryCommittedChange_InvalidatesTheCalculation_OnceRecalculatedOnRead()
    {
        var s = new Session();
        _ = s.Calculation.Result;
        int runs = s.Calculation.CalculationCount;

        s.History.Execute(new AssignGlassCommand(s.Frame, new[] { s.Glass }, Toughened8, s.Library, Rules));
        s.History.Execute(AssignProfileCommand.ForOuterFrame(s.Frame, Frame50, s.Library, Rules));
        Assert.Equal(runs, s.Calculation.CalculationCount);              // invalidation is cheap: nothing computed yet

        _ = s.Calculation.Result;
        _ = s.Calculation.Result;
        Assert.Equal(runs + 1, s.Calculation.CalculationCount);
    }
}
