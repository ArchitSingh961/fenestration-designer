using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Design;

/// <summary>A new design asks which system it is made in: brand, then system, Confirm (× keeps the default).</summary>
public class SystemPickerTests
{
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

    private static MainViewModel Designer()
    {
        var vm = new MainViewModel(Sample);
        vm.NewFrameWidthText = "1200";
        vm.NewFrameHeightText = "1500";
        return vm;
    }

    [Fact]
    public void A_NewDesign_AsksForTheSystem_ByBrand()
    {
        var vm = Designer();

        vm.CreateFrame();

        var picker = vm.SystemPicker;
        Assert.True(picker.IsOpen);
        Assert.Equal(new[] { "Sample Extrusions", "Sample Polymers" }, picker.Brands);
        Assert.Equal("Sample Extrusions", picker.SelectedBrand);                              // the frame's (default) system first
        Assert.Equal(vm.Project.Frames[0].SystemId, picker.SelectedSystem!.Id);
        Assert.Contains("Aluminium", picker.DetailText);

        picker.SelectedBrand = "Sample Extrusions";
        Assert.Equal(2, picker.Systems.Count);
        picker.SelectedSystem = picker.Systems.Single(s => s.Id == "SYS-AL-SL60");
        picker.ConfirmCommand.Execute(null);

        Assert.False(picker.IsOpen);
        Assert.Equal("SYS-AL-SL60", vm.Project.Frames[0].SystemId);
        Assert.Equal("SYS-AL-SL60", picker.LastSystemId);

        vm.CreateFrame();                                                                    // the next one offers the last choice
        Assert.Equal("SYS-AL-SL60", vm.SystemPicker.SelectedSystem!.Id);
    }

    [Fact]
    public void Closing_KeepsTheDefaultSystem()
    {
        var vm = Designer();
        vm.CreateFrame();
        string? before = vm.Project.Frames[0].SystemId;

        vm.SystemPicker.CloseCommand.Execute(null);

        Assert.False(vm.SystemPicker.IsOpen);
        Assert.Equal(before, vm.Project.Frames[0].SystemId);
    }

    [Fact]
    public void Confirming_IsOneUndoStep()
    {
        var vm = Designer();
        vm.CreateFrame();
        string? before = vm.Project.Frames[0].SystemId;
        vm.SystemPicker.SelectedBrand = "Sample Polymers";                                   // another system than the default
        vm.SystemPicker.ConfirmCommand.Execute(null);
        Assert.Equal("SYS-UPVC-62C", vm.Project.Frames.Single().SystemId);

        vm.CommandHistory.Undo();

        Assert.Equal(before, vm.Project.Frames.Single().SystemId);                           // the frame stays, in its first system
    }

    [Fact]
    public void A_DesignDroppedOnTheCanvas_AlsoAsks_ButNotOneMadeInItsOwnSystem()
    {
        var vm = Designer();
        var casement = DesignTemplates.All.First(t => t.Openings.Any(o => o is { } x && Mark.Core.Models.OpeningTypes.IsHinged(x)));

        Assert.Null(vm.ApplyDesign(casement));
        Assert.True(vm.SystemPicker.IsOpen);

        var other = Designer();
        var sliding = DesignTemplates.ForSystem(Sample.FindSystem("SYS-AL-SL60")!, Sample).First();
        Assert.True(((Mark.Designer.Interaction.IViewportDropTarget)other).Drop(new Mark.Core.Geometry.Point2D(0, 0), $"{sliding.Id}@SYS-AL-SL60"));
        Assert.False(other.SystemPicker.IsOpen);
    }

    [Fact]
    public void WithOneSystem_ThereIsNothingToAsk()
    {
        var single = new ProductLibrary(Sample.Profiles, Sample.Glass, Sample.Materials, Sample.Defaults, Sample.Currency,
            Sample.Systems.Select(s => s.Id == Sample.Defaults.SystemId ? s : s with { IsActive = false }), Sample.Bundles);   // one active system
        var vm = new MainViewModel(single) { NewFrameWidthText = "1200", NewFrameHeightText = "1500" };

        vm.CreateFrame();

        Assert.False(vm.SystemPicker.IsOpen);
    }
}
