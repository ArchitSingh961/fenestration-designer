using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Designer.Views;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Design;

/// <summary>
/// A new design asks for its design ref., quantity, location, brand and glass; the design itself decides the system
/// (sliding designs in a sliding system, casement designs in a casement system). Apply sets them in one undo step,
/// Cancel keeps the design as it was made.
/// </summary>
public class NewDesignPanelTests
{
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

    private static MainViewModel Designer()
    {
        var vm = new MainViewModel(Sample);
        vm.NewFrameWidthText = "1200";
        vm.NewFrameHeightText = "1500";
        return vm;
    }

    private static DesignTemplate AllSliding
        => DesignTemplates.All.First(t => !t.KeepsLayout && t.Openings.Any(o => o is { } x && x.IsSliding())
                                          && t.Openings.All(o => o is OpeningType.Fixed || o is { } x && x.IsSliding()));

    private static DesignTemplate Casement
        => DesignTemplates.All.First(t => !t.KeepsLayout && t.Openings.Any(o => o is { } x && x.IsHinged()));

    [Fact]
    public void A_PlainNewFrame_AsksItsSizeAndType_AndAppliesThemInOneStep()
    {
        var vm = Designer();
        vm.CreateFrame();
        var panel = vm.NewDesignPanel;
        Assert.True(panel.AsksType);
        Assert.Equal(("1200", "1500"), (panel.WidthText, panel.HeightText));

        panel.Reference = "W1";
        panel.WidthText = "1800";
        panel.SelectedType = NewDesignViewModel.Types.Single(t => t.TemplateId == "sld-2");
        Assert.Contains(vm.Library.Systems, x => x.Id == panel.SystemId);                    // the type picks a sliding system
        panel.ConfirmCommand.Execute(null);

        var frame = vm.Project.Frames.Single();
        Assert.False(panel.IsOpen);
        Assert.Equal((1800d, 1500d), (frame.Width, frame.Height));
        Assert.Equal(2, frame.GlassPanels.Count(g => g.Opening.IsSliding()));
        Assert.Equal("W1", frame.Design.Reference);

        vm.UndoCommand.Execute(null);                                                        // size, type and details: one step
        Assert.Equal(1200, frame.Width);
        Assert.Single(frame.GlassPanels);

        panel.WidthText = "wide";
        vm.NewDesignPanel.Open(frame);
        panel.WidthText = "wide";
        panel.ConfirmCommand.Execute(null);
        Assert.Contains("width and height", panel.Message);
    }

    [Fact]
    public void A_NewDesign_AsksForTheBrand_NotTheSystem()
    {
        var vm = Designer();

        vm.CreateFrame();

        var panel = vm.NewDesignPanel;
        Assert.True(panel.IsOpen);
        Assert.True(panel.AsksBrand);
        Assert.Equal(new[] { "Sample Extrusions", "Sample Polymers" }, panel.Brands);
        Assert.Equal("Sample Extrusions", panel.SelectedBrand);                               // the brand of the default system
        Assert.Equal(vm.Project.Frames[0].SystemId, panel.SystemId);

        panel.SelectedBrand = "Sample Polymers";
        Assert.Equal("SYS-UPVC-62C", panel.SystemId);                                       // the brand's system for this design
        var upvc = Sample.FindSystem("SYS-UPVC-62C")!;
        Assert.All(panel.GlassOptions, o => Assert.True(upvc.AcceptsGlass(Sample.FindGlass(o.Id)!.ThicknessMm)));   // the glass follows
        panel.ConfirmCommand.Execute(null);

        Assert.False(panel.IsOpen);
        Assert.Equal("SYS-UPVC-62C", vm.Project.Frames[0].SystemId);
        Assert.Equal("Sample Polymers", panel.LastBrand);

        vm.CreateFrame();                                                                    // the next one offers the last brand
        Assert.Equal("Sample Polymers", vm.NewDesignPanel.SelectedBrand);
    }

    [Fact]
    public void A_SlidingDesign_GoesInTheBrandsSlidingSystem_OnItsOwn()
    {
        var vm = Designer();
        vm.CreateFrame();
        vm.NewDesignPanel.ConfirmCommand.Execute(null);
        var frame = vm.Project.Frames.Single();
        string? casement = frame.SystemId;
        Assert.Equal("SYS-AL-S60C", casement);

        Assert.Null(vm.ApplyDesign(AllSliding));

        Assert.Equal("SYS-AL-SL60", frame.SystemId);                                         // same brand and material, sliding
        Assert.Contains(frame.GlassPanels, g => g.Opening.IsSliding());

        Assert.Null(vm.ApplyDesign(Casement));
        Assert.Equal("SYS-AL-S60C", frame.SystemId);                                         // and back for a casement design

        vm.CommandHistory.Undo();
        Assert.Equal("SYS-AL-SL60", frame.SystemId);                                         // one undo step each
    }

    [Fact]
    public void SystemMatch_KeepsASystemThatMakesTheDesign()
    {
        Assert.Equal("SYS-AL-S60C", SystemMatch.For(Sample, new OpeningType?[] { OpeningType.Fixed }, "SYS-AL-S60C"));
        Assert.Equal("SYS-AL-SL60", SystemMatch.For(Sample, new OpeningType?[] { OpeningType.Fixed }, "SYS-AL-SL60"));
        Assert.Equal("SYS-AL-SL60", SystemMatch.For(Sample, AllSliding.Openings, "SYS-AL-S60C"));
        Assert.Equal("SYS-AL-S60C", SystemMatch.For(Sample, Casement.Openings, "SYS-AL-SL60"));
        Assert.Equal("SYS-UPVC-62C", SystemMatch.For(Sample, Casement.Openings, "SYS-AL-S60C", brand: "Sample Polymers"));
    }

    [Fact]
    public void A_NewDesign_AsksForItsRef_Quantity_Location_AndGlass()
    {
        var vm = Designer();
        vm.CreateFrame();
        var panel = vm.NewDesignPanel;
        var frame = vm.Project.Frames.Single();
        Assert.Equal(frame.Design.Reference, panel.Reference);                               // W1, as made
        Assert.Equal("1", panel.QuantityText);
        Assert.True(panel.AsksGlass);
        Assert.NotEmpty(panel.GlassOptions);
        var system = Sample.FindSystem(panel.SystemId)!;
        Assert.All(panel.GlassOptions, o => Assert.True(system.AcceptsGlass(Sample.FindGlass(o.Id)!.ThicknessMm)));   // what fits the system

        panel.Reference = "LR-1";
        panel.QuantityText = "3";
        panel.Location = "Living room";
        var glass = panel.GlassOptions.Last();
        panel.SelectedGlass = glass;
        panel.ConfirmCommand.Execute(null);

        Assert.False(panel.IsOpen);
        Assert.Equal("LR-1", frame.Design.Reference);
        Assert.Equal(3, frame.Design.Quantity);
        Assert.Equal("Living room", frame.Design.Location);
        Assert.All(frame.GlassPanels, g => Assert.Equal(glass.Id, g.GlassDefinitionId));
        Assert.Equal(glass.Id, panel.LastGlassId);
    }

    [Fact]
    public void A_WrongQuantity_IsShown_AndNothingChanges()
    {
        var vm = Designer();
        vm.CreateFrame();
        var panel = vm.NewDesignPanel;
        panel.QuantityText = "two";

        panel.ConfirmCommand.Execute(null);

        Assert.True(panel.IsOpen);
        Assert.Contains("quantity", panel.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, vm.Project.Frames.Single().Design.Quantity);
    }

    [Fact]
    public void Cancel_KeepsTheDesignAsItWasMade()
    {
        var vm = Designer();
        vm.CreateFrame();
        string? before = vm.Project.Frames[0].SystemId;
        vm.NewDesignPanel.Reference = "Changed";
        vm.NewDesignPanel.SelectedBrand = "Sample Polymers";

        vm.NewDesignPanel.CloseCommand.Execute(null);

        Assert.False(vm.NewDesignPanel.IsOpen);
        Assert.Equal(before, vm.Project.Frames[0].SystemId);
        Assert.Equal("W1", vm.Project.Frames[0].Design.Reference);
    }

    [Fact]
    public void Applying_IsOneUndoStep()
    {
        var vm = Designer();
        vm.CreateFrame();
        var frame = vm.Project.Frames.Single();
        string? before = frame.SystemId;
        vm.NewDesignPanel.SelectedBrand = "Sample Polymers";                                 // another system than the default
        vm.NewDesignPanel.QuantityText = "4";
        vm.NewDesignPanel.ConfirmCommand.Execute(null);
        Assert.Equal("SYS-UPVC-62C", frame.SystemId);
        Assert.Equal(4, frame.Design.Quantity);

        vm.CommandHistory.Undo();

        Assert.Single(vm.Project.Frames);                                                    // the frame stays, as it was made
        Assert.Equal(before, frame.SystemId);
        Assert.Equal(1, frame.Design.Quantity);
    }

    [Fact]
    public void A_DesignDroppedOnTheCanvas_AlsoAsks_ButNotTheBrandOfOneMadeInItsOwnSystem()
    {
        var vm = Designer();

        Assert.Null(vm.ApplyDesign(AllSliding));                                             // no frame yet: made with the design
        Assert.True(vm.NewDesignPanel.IsOpen);
        Assert.True(vm.NewDesignPanel.AsksBrand);
        Assert.Equal("SYS-AL-SL60", vm.Project.Frames.Single().SystemId);                    // the design decided

        var other = Designer();
        var sliding = DesignTemplates.ForSystem(Sample.FindSystem("SYS-AL-SL60")!, Sample).First();
        Assert.True(((Mark.Designer.Interaction.IViewportDropTarget)other).Drop(new Mark.Core.Geometry.Point2D(0, 0), $"{sliding.Id}@SYS-AL-SL60"));
        Assert.True(other.NewDesignPanel.IsOpen);
        Assert.False(other.NewDesignPanel.AsksBrand);
        other.NewDesignPanel.ConfirmCommand.Execute(null);
        Assert.Equal("SYS-AL-SL60", other.Project.Frames.Single().SystemId);                 // still in its own system
    }

    [Fact]
    public void WithOneBrand_NothingIsAskedAboutTheSystem()
    {
        var single = new ProductLibrary(Sample.Profiles, Sample.Glass, Sample.Materials, Sample.Defaults, Sample.Currency,
            Sample.Systems.Select(s => s.Id == Sample.Defaults.SystemId ? s : s with { IsActive = false }), Sample.Bundles);   // one active system
        var vm = new MainViewModel(single) { NewFrameWidthText = "1200", NewFrameHeightText = "1500" };

        vm.CreateFrame();

        Assert.True(vm.NewDesignPanel.IsOpen);
        Assert.False(vm.NewDesignPanel.AsksBrand);
    }

    [Fact]
    public void DropDownSearch_MatchesEveryWord_InTheItemsText()
    {
        var option = new LibraryOption("GL-DGU-24", "(6+12+6)24mm DGU", "24 mm · Double glazed");

        Assert.Contains("24mm DGU", SearchCombo.TextOf(option));
        Assert.Contains("GL-DGU-24", SearchCombo.TextOf(option));                            // the code too
        Assert.Contains("Double glazed", SearchCombo.TextOf(option));
        Assert.Equal("Casement", SearchCombo.TextOf("Casement"));
    }
}
