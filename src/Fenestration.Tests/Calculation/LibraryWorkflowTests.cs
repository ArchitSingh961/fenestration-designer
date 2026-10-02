using Fenestration.Calculation;
using Fenestration.Core.Library;
using Fenestration.Core.Models;
using Fenestration.Designer.ViewModels;
using Fenestration.Tests.Library;
using Xunit;
using static Fenestration.Tests.Library.TestLibrary;

namespace Fenestration.Tests.Calculation;

/// <summary>
/// The M6 definition-of-done workflow through the real view models (no window), using the properties-panel
/// pickers the way a user does: create frame → select glass → select profile → calculate → change glass →
/// recalculate → change profile → recalculate → verify BOM and cost → undo.
/// </summary>
public class LibraryWorkflowTests
{
    private static MainViewModel CreateApp(ProductLibrary? library = null)
    {
        var vm = new MainViewModel(library ?? Create());
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

    /// <summary>Types in the picker's search box and picks the single remaining match.</summary>
    private static string? Pick(LibraryPickerViewModel? picker, string search, string expectedId)
    {
        Assert.NotNull(picker);
        picker!.SearchText = search;
        var option = Assert.Single(picker.Options, o => o.Id == expectedId);
        picker.SelectedOption = option;
        return picker.ErrorMessage;
    }

    [Fact]
    public void DefinitionOfDone_Workflow()
    {
        var vm = CreateApp();

        // Create a frame: it is priced straight away from the library defaults.
        var frame = CreateFrame(vm);
        Assert.Equal("FRAME", vm.Properties.Header);
        Assert.Equal("60mm Frame (default)", vm.Properties.ProfilePicker!.CurrentText);
        Assert.Equal("Glass", vm.Properties.GlassPicker!.Label);
        Assert.Equal("6mm Clear (default)", vm.Properties.GlassPicker.CurrentText);
        Assert.Equal("Total 2,308.40 INR", vm.CostText);                 // 810 profiles + 1490.40 glass + 8 blocks
        var glassId = frame.GlassPanels[0].Id;

        // Select glass: pick 6mm Clear explicitly in the glass panel's properties.
        vm.Select(glassId);
        Assert.Equal("GLASS", vm.Properties.Header);
        Assert.Null(Pick(vm.Properties.GlassPicker, "clear", Clear6));
        Assert.Equal(Clear6, frame.GlassPanels[0].GlassDefinitionId);
        Assert.Equal("6mm Clear", vm.Properties.GlassPicker!.CurrentText);

        // Select profile: pick 60mm Frame explicitly for the frame.
        vm.Select(frame.Id);
        Assert.Null(Pick(vm.Properties.ProfilePicker, "60", Frame60));
        Assert.All(frame.Profiles, p => Assert.Equal(Frame60, p.ProfileDefinitionId));

        // Calculate.
        var first = vm.Calculation.Result;
        Assert.Equal(2308.40m, first.Cost.Total);
        Assert.True(first.IsComplete);

        // Change glass 6mm Clear → 8mm Toughened: same panel, recalculated.
        vm.Select(glassId);
        Assert.Contains(vm.Properties.CalculationItems, i => i.Name == "Cost" && i.Value == "1,490.40");
        Assert.Null(Pick(vm.Properties.GlassPicker, "8 tough", Toughened8));
        Assert.Equal(glassId, Assert.Single(frame.GlassPanels).Id);
        Assert.True(vm.IsSelected(glassId));
        Assert.Equal("8mm Toughened", vm.Properties.GlassPicker!.CurrentText);
        Assert.Contains(vm.Properties.Items, i => i.Name == "Thickness" && i.Value == "8");
        Assert.Contains(vm.Properties.CalculationItems, i => i.Name == "Cost" && i.Value == "2,980.80");
        Assert.Equal(3840.00m, vm.Calculation.Result.Cost.Total);       // 810 + 2980.80 + 49.20 gasket
        Assert.Equal("Total 3,840.00 INR", vm.CostText);

        // Change profile 60mm Frame → 50mm Frame: glass grows, recalculated.
        vm.Select(frame.Id);
        Assert.Null(Pick(vm.Properties.ProfilePicker, "50mm", Frame50));
        Assert.Equal("50mm Frame", vm.Properties.ProfilePicker!.CurrentText);
        Assert.Equal((1100.0, 1400.0), (frame.GlassPanels[0].Boundary.Width, frame.GlassPanels[0].Boundary.Height));

        // Verify BOM and cost.
        var result = vm.Calculation.Result;
        Assert.Equal(new CostSummary(540.00m, 3080.00m, 304.00m), result.Cost);
        Assert.Equal(3924.00m, result.Cost.Total);
        Assert.Equal(new[]
        {
            new BomRow("Profile", "50mm Frame", "4 pcs, 5.4 m", "540.00"),
            new BomRow("Glass", "8mm Toughened", "1 pcs × 1100 × 1400 mm", "3,080.00"),
            new BomRow("Hardware", "Corner cleat", "4 pcs", "200.00"),
            new BomRow("Gasket", "Glazing gasket", "10.4 m", "104.00")
        }, vm.BomRows);
        Assert.Equal("Total 3,924.00 INR", vm.CostText);
        Assert.Null(vm.CalculationStatus);

        // Undo both material changes: back to the first calculation.
        Assert.Equal("Change profile to 50mm Frame", vm.CommandHistory.UndoDescription);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(Clear6, frame.GlassPanels[0].GlassDefinitionId);
        Assert.Equal(first.Bom, vm.Calculation.Result.Bom);
        Assert.Equal("Total 2,308.40 INR", vm.CostText);
    }

    [Fact]
    public void MullionSelected_OffersOnlyMullionProfiles_AndNoGlassPicker()
    {
        var vm = CreateApp();
        CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);

        Assert.Equal("MULLION", vm.Properties.Header);
        Assert.Null(vm.Properties.GlassPicker);
        var picker = vm.Properties.ProfilePicker!;
        Assert.Equal(new[] { Mullion60, Mullion80 }, picker.Options.Select(o => o.Id));
        Assert.Equal("60mm Mullion (default)", picker.CurrentText);
        Assert.Contains(vm.Properties.CalculationItems, i => i.Name == "Cut length" && i.Value == "1380");

        Assert.Null(Pick(picker, "80", Mullion80));
        Assert.Contains(vm.Properties.Items, i => i.Name == "Thickness" && i.Value == "80");
        Assert.Contains(vm.Properties.CalculationItems, i => i.Name == "Cut length" && i.Value == "1390");
    }

    [Fact]
    public void PickerFailure_ShowsTheReason_AndChangesNothing()
    {
        var vm = CreateApp();
        var frame = CreateFrame(vm, "300", "300");
        int undo = vm.CommandHistory.UndoCount;

        string? error = Pick(vm.Properties.ProfilePicker, "200", Frame200);

        Assert.NotNull(error);
        Assert.StartsWith("Cannot change the profile: ", error);
        Assert.All(frame.Profiles, p => Assert.Null(p.ProfileDefinitionId));
        Assert.Equal(undo, vm.CommandHistory.UndoCount);
        Assert.Null(vm.Properties.ProfilePicker!.SelectedOption);       // the assignment still in effect (default)
    }

    [Fact]
    public void AssignUnknownId_ReturnsAnError_AndRecordsNothing()
    {
        var vm = CreateApp();
        var frame = CreateFrame(vm);
        vm.Select(frame.GlassPanels[0].Id);
        int undo = vm.CommandHistory.UndoCount;

        Assert.Contains("'NOPE' is not in the library", vm.AssignGlass("NOPE"));
        Assert.Equal("Select a frame, mullion or transom first.", vm.AssignProfile(Frame50));   // glass is selected
        vm.Select(frame.Id);
        Assert.Contains("'NOPE' is not in the library", vm.AssignProfile("NOPE"));
        Assert.Equal(undo, vm.CommandHistory.UndoCount);
        Assert.Null(frame.GlassPanels[0].GlassDefinitionId);
    }

    [Fact]
    public void AssignWithoutASuitableSelection_IsRefused()
    {
        var vm = CreateApp();
        var first = CreateFrame(vm);
        var second = CreateFrame(vm);
        vm.ClearSelection();
        Assert.Equal("Select a glass panel or a frame first.", vm.AssignGlass(Toughened8));

        // Only glass selected (in two frames): there is no profile to change.
        vm.Select(first.GlassPanels[0].Id);
        vm.Select(second.GlassPanels[0].Id, addToSelection: true);
        Assert.Equal("Select a frame, mullion or transom first.", vm.AssignProfile(Frame50));
        Assert.Equal(2, vm.CommandHistory.UndoCount);                   // the two frames only
    }

    [Fact]
    public void FrameGlassPicker_ShowsMixed_AndAssignsAllPanes()
    {
        var vm = CreateApp();
        var frame = CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);
        vm.Select(frame.GlassPanels[0].Id);
        Pick(vm.Properties.GlassPicker, "tough", Toughened8);

        vm.Select(frame.Id);
        Assert.Equal("All glass", vm.Properties.GlassPicker!.Label);
        Assert.Equal("Mixed", vm.Properties.GlassPicker.CurrentText);

        Assert.Null(Pick(vm.Properties.GlassPicker, "lam", Laminated10));
        Assert.All(frame.GlassPanels, g => Assert.Equal(Laminated10, g.GlassDefinitionId));
        Assert.Equal("10mm Laminated", vm.Properties.GlassPicker!.CurrentText);
    }

    [Fact]
    public void Picker_Search_NarrowsTheOptions()
    {
        var vm = CreateApp();
        var frame = CreateFrame(vm);
        vm.Select(frame.GlassPanels[0].Id);
        var picker = vm.Properties.GlassPicker!;

        Assert.Equal(3, picker.Options.Count);
        Assert.Equal("3 matches", picker.ResultSummary);
        picker.SearchText = "toughened";
        var only = Assert.Single(picker.Options);
        Assert.Equal(Toughened8, only.Id);
        Assert.Equal("8 mm · Toughened · 2,000.00/m²", only.Detail);
        Assert.Equal("1 match", picker.ResultSummary);
        picker.SearchText = "nothing like this";
        Assert.Empty(picker.Options);
        Assert.Equal(0, frame.GlassPanels.Count(g => g.GlassDefinitionId is not null));   // searching assigns nothing
    }

    [Fact]
    public void NewFrames_AreDrawnWithTheLibraryDefaultFaceWidths()
    {
        var library = Create(new LibraryDefaults { FrameProfileId = Frame50, MullionProfileId = Mullion80, GlassId = Toughened8 });
        var vm = CreateApp(library);
        var frame = CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);

        Assert.All(frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame), p => Assert.Equal(50, p.Thickness));
        Assert.Equal(80, frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion).Thickness);
        Assert.All(frame.GlassPanels, g => Assert.Equal(8, g.Thickness));
        Assert.Empty(vm.Calculation.Result.Issues);                     // drawing and library agree
    }

    [Fact]
    public void WithoutALibrary_TheDesignerStillWorks_AndSaysWhatCannotBePriced()
    {
        var vm = new MainViewModel();
        var frame = CreateFrame(vm);

        Assert.Equal("None assigned", vm.Properties.ProfilePicker!.CurrentText);
        Assert.Empty(vm.Properties.ProfilePicker.Options);
        Assert.Equal(4, frame.Profiles.Count);
        Assert.NotNull(vm.CalculationStatus);
        Assert.StartsWith("5 items could not be priced.", vm.CalculationStatus);
        Assert.Empty(vm.BomRows);
    }

    [Fact]
    public void NewProject_ClearsTheBom()
    {
        var vm = CreateApp();
        CreateFrame(vm);
        Assert.NotEmpty(vm.BomRows);

        vm.NewProjectCommand.Execute(null);
        Assert.Empty(vm.BomRows);
        Assert.Equal("", vm.CostText);
    }
}
