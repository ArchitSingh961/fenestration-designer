using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Calculation;

/// <summary>
/// Sliding sashes side by side meet with their interlock at the division between them: that division is not a mullion
/// to cut or price. A division next to a fixed (or hinged) panel stays a real mullion.
/// </summary>
public class SlidingMeetingLineTests
{
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

    private static MainViewModel Window(string templateId)
    {
        var vm = new MainViewModel(Sample);
        var template = DesignTemplates.All.Single(t => t.Id == templateId);
        Assert.Null(vm.ApplyDesign(template, "SYS-AL-SL60"));
        return vm;
    }

    [Fact]
    public void TwoSlidingPanels_HaveNoMullion_OnlyTheirInterlock()
    {
        var vm = Window("sld-2");
        var frame = vm.Project.Frames.Single();
        var division = frame.Profiles.Single(Members.IsDivision);
        Assert.True(OpeningGeometry.IsMeetingLine(frame, division));

        var result = vm.Calculation.Result;
        Assert.DoesNotContain(result.Profiles, p => p.ProfileId == division.Id);            // not cut, not priced
        Assert.Contains(result.Profiles, p => p.DefinitionId == "PRF-SL-INTERLOCK");        // the interlock is
    }

    [Fact]
    public void ADivision_NextToAFixedPanel_IsARealMullion()
    {
        var vm = Window("sld-3-fixed");
        var frame = vm.Project.Frames.Single();
        var divisions = frame.Profiles.Where(Members.IsDivision).ToList();
        Assert.NotEmpty(divisions);
        Assert.All(divisions, d => Assert.False(OpeningGeometry.IsMeetingLine(frame, d)));
        Assert.All(divisions, d => Assert.Contains(vm.Calculation.Result.Profiles, p => p.ProfileId == d.Id));
    }
}

/// <summary>The opening drop-down of several openings that open differently says so instead of staying blank.</summary>
public class MixedOpeningsTextTests
{
    [Fact]
    public void DifferentOpenings_ShowWhatTheyAre()
    {
        var panels = new[]
        {
            new GlassPanel { Opening = OpeningType.SlidingRight },
            new GlassPanel { Opening = OpeningType.SlidingLeft }
        };
        var editor = new OpeningEditorViewModel("Openings (2)", panels, (_, _) => null);

        Assert.True(editor.IsMixed);
        Assert.StartsWith("Mixed: ", editor.MixedText);
        editor.SelectedOption = OpeningEditorViewModel.AllOptions.First(o => o.Type == OpeningType.Fixed);
        Assert.False(editor.IsMixed);

        var same = new OpeningEditorViewModel("Opening", new[] { new GlassPanel { Opening = OpeningType.Fixed } }, (_, _) => null);
        Assert.False(same.IsMixed);
    }
}
