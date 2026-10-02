using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Designer.ViewModels;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Calculation;

/// <summary>
/// Changing a material for many objects at once through the M5 multi-selection (Shift+click, box, Ctrl+A):
/// one picker, one undo step across frames, all-or-nothing, and a recalculated cost for the selection.
/// Each 1200 × 1500 frame with the test library's defaults costs 2,308.40 (810 profiles + 1,490.40 glass + 8 blocks).
/// </summary>
public class MultiSelectionAssignmentTests
{
    private static MainViewModel CreateApp()
    {
        var vm = new MainViewModel(Create());
        vm.Canvas.SetViewportSize(1000, 800);
        return vm;
    }

    private static Frame CreateFrame(MainViewModel vm, string width = "1200", string height = "1500")
    {
        vm.NewFrameWidthText = width;
        vm.NewFrameHeightText = height;
        vm.CreateFrameCommand.Execute(null);
        return vm.Project.Frames[^1];
    }

    private static string? Pick(LibraryPickerViewModel? picker, string search, string expectedId)
    {
        Assert.NotNull(picker);
        picker!.SearchText = search;
        picker.SelectedOption = Assert.Single(picker.Options, o => o.Id == expectedId);
        return picker.ErrorMessage;
    }

    [Fact]
    public void SelectAll_ChangeGlass_ChangesEveryPaneOfEveryFrame_AsOneUndoStep()
    {
        var vm = CreateApp();
        var first = CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);                              // first frame: two panes
        var second = CreateFrame(vm);
        var paneIds = first.GlassPanels.Concat(second.GlassPanels).Select(p => p.Id).ToList();
        // Edits rebuild a frame's objects (keeping their Ids), so always look them up again.
        List<GlassPanel> Panes() => first.GlassPanels.Concat(second.GlassPanels).ToList();
        decimal before = vm.Calculation.Result.Cost.Total;

        vm.SelectAll();
        Assert.Equal("6 OBJECTS SELECTED", vm.Properties.Header);       // 2 frames, 1 mullion, 3 panes
        Assert.Equal("All glass", vm.Properties.GlassPicker!.Label);
        Assert.Equal("6mm Clear (default)", vm.Properties.GlassPicker.CurrentText);

        int undo = vm.CommandHistory.UndoCount;
        Assert.Null(Pick(vm.Properties.GlassPicker, "tough", Toughened8));

        Assert.Equal(paneIds, Panes().Select(p => p.Id));               // the same panes: nothing deleted and recreated
        Assert.All(Panes(), p => Assert.Equal((Toughened8, 8.0), (p.GlassDefinitionId!, p.Thickness)));
        Assert.Equal(undo + 1, vm.CommandHistory.UndoCount);
        Assert.Equal("Change glass to 8mm Toughened", vm.CommandHistory.UndoDescription);
        Assert.Equal("8mm Toughened", vm.Properties.GlassPicker!.CurrentText);

        var result = vm.Calculation.Result;
        Assert.All(result.Glass, g => Assert.Equal(Toughened8, g.DefinitionId));
        Assert.Equal(Toughened8, Assert.Single(result.Bom, b => b.Category == Mark.Calculation.BomCategory.Glass
                                                               && b.Quantity == 2).ItemId);   // the two mullion halves
        Assert.True(result.Cost.Total > before);

        vm.UndoCommand.Execute(null);                                    // one step restores every frame
        Assert.All(Panes(), p => Assert.Null(p.GlassDefinitionId));
        Assert.Equal(before, vm.Calculation.Result.Cost.Total);

        vm.RedoCommand.Execute(null);
        Assert.All(Panes(), p => Assert.Equal(Toughened8, p.GlassDefinitionId));
        Assert.Equal(result.Cost, vm.Calculation.Result.Cost);
    }

    [Fact]
    public void SelectAll_ProfilePicker_OffersOnlySectionsUsableInEveryRole()
    {
        var vm = CreateApp();
        CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);
        vm.SelectAll();

        // Outer members (frame role) and a mullion: no section in the library is both.
        var picker = vm.Properties.ProfilePicker!;
        Assert.Equal("Profile", picker.Label);
        Assert.Equal("Library defaults", picker.CurrentText);           // 60mm Frame and 60mm Mullion
        Assert.Empty(picker.Options);
    }

    [Fact]
    public void MullionAndTransom_ChangeTogether_ToASectionForBothRoles()
    {
        var vm = CreateApp();
        var frame = CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);
        vm.Select(frame.GlassPanels[0].Id);
        vm.AddTransomCommand.Execute(null);
        var mullionId = frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion).Id;
        var transomId = frame.Profiles.Single(p => p.ProfileType == ProfileType.Transom).Id;

        vm.Select(mullionId);
        vm.Select(transomId, addToSelection: true);
        var picker = vm.Properties.ProfilePicker!;
        Assert.Equal("Profile", picker.Label);
        Assert.Equal("60mm Mullion (default)", picker.CurrentText);
        Assert.Equal(new[] { Mullion60, Mullion80 }, picker.Options.Select(o => o.Id));

        int undo = vm.CommandHistory.UndoCount;
        Assert.Null(Pick(picker, "80", Mullion80));

        Assert.All(frame.Profiles.Where(p => p.Id == mullionId || p.Id == transomId), p => Assert.Equal((Mullion80, 80.0), (p.ProfileDefinitionId!, p.Thickness)));
        Assert.All(frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame), p => Assert.Null(p.ProfileDefinitionId));
        Assert.Equal(undo + 1, vm.CommandHistory.UndoCount);
        var lines = vm.Calculation.Result.Profiles.Where(p => p.Role != ProfileType.Frame).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal((Mullion80, 200m), (l.DefinitionId!, l.CostPerMetre)));
    }

    [Fact]
    public void ProfileForAFrameAndAMullionOfAnotherFrame_IsAllOrNothing()
    {
        var vm = CreateApp();
        var first = CreateFrame(vm);
        var second = CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);                              // selects the new mullion of the second frame
        var mullion = second.Profiles.Single(p => p.ProfileType == ProfileType.Mullion);
        vm.Select(first.Id, addToSelection: true);
        string saved = ProjectSerializer.Serialize(vm.Project);
        int undo = vm.CommandHistory.UndoCount;

        // The first frame accepts a frame profile, the mullion does not: the first frame's change is rolled back.
        string? error = vm.AssignProfile(Frame50);

        Assert.Contains("cannot be used as a mullion", error);
        Assert.Equal(saved, ProjectSerializer.Serialize(vm.Project));
        Assert.Equal(undo, vm.CommandHistory.UndoCount);
        Assert.True(vm.IsSelected(mullion.Id));
    }

    [Fact]
    public void AFrameThatCannotTakeTheProfile_RollsBackTheOtherFrames()
    {
        var vm = CreateApp();
        CreateFrame(vm);
        CreateFrame(vm, "300", "300");
        vm.SelectAll();
        string saved = ProjectSerializer.Serialize(vm.Project);
        decimal cost = vm.Calculation.Result.Cost.Total;

        string? error = vm.AssignProfile(Frame200);                     // fits the big frame, leaves no glass in the small one

        Assert.StartsWith("Cannot change the profile: ", error);
        Assert.Equal(saved, ProjectSerializer.Serialize(vm.Project));
        Assert.Equal(cost, vm.Calculation.Result.Cost.Total);
    }

    [Fact]
    public void MultiSelection_ShowsGlassAreaAndSummedCost_CountingEachObjectOnce()
    {
        var vm = CreateApp();
        var first = CreateFrame(vm);
        var second = CreateFrame(vm);

        vm.SelectAll();                                                  // 2 frames + their 2 panes
        Assert.Contains(new PropertyItem("Frames", "2"), vm.Properties.Items);
        Assert.Contains(new PropertyItem("Glass panels", "2"), vm.Properties.Items);
        Assert.Contains(new PropertyItem("Glass area", "2.981", "m²"), vm.Properties.CalculationItems);
        Assert.Contains(new PropertyItem("Cost", "4,616.80", "INR"), vm.Properties.CalculationItems);

        vm.Select(first.Id);
        vm.Select(second.GlassPanels[0].Id, addToSelection: true);       // a frame + another frame's pane
        Assert.Contains(new PropertyItem("Cost", "3,798.80", "INR"), vm.Properties.CalculationItems); // 2,308.40 + 1,490.40
    }
}
