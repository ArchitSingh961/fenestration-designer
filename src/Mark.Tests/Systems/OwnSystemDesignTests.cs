using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Systems;

/// <summary>A company's own systems as ready-made designs in the design library, made in that system.</summary>
public class OwnSystemDesignTests
{
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

    [Fact]
    public void ASystem_GetsTheDesignsItsHardwareCovers()
    {
        var sliding = DesignTemplates.ForSystem(Sample.FindSystem("SYS-AL-SL60")!, Sample);
        var casement = DesignTemplates.ForSystem(Sample.FindSystem("SYS-UPVC-62C")!, Sample);
        var noSash = DesignTemplates.ForSystem(Sample.FindSystem("SYS-UPVC-62C")! with { SashProfileId = null }, Sample);

        Assert.NotEmpty(sliding);
        Assert.All(sliding, t => Assert.All(t.Openings, o => Assert.True(o is OpeningType.Fixed || o!.Value.IsSliding())));
        Assert.Contains(sliding, t => t.Openings.Any(o => o!.Value.IsSliding()));
        Assert.Contains(casement, t => t.Openings.Any(o => o!.Value.IsHinged()));
        Assert.DoesNotContain(casement, t => t.Openings.Any(o => o!.Value.IsSliding()));
        Assert.All(noSash, t => Assert.True(t.IsDividerOnly));
    }

    [Fact]
    public void TheLibrary_ShowsTheCompanysSystems_AsACategoryNamedAfterIt()
    {
        var library = new DesignLibraryViewModel(new DesignRules(), (_, _) => null);
        var system = Sample.FindSystem("SYS-UPVC-62C")!;

        library.SetCompanyDesigns("Sozluk", new[] { new SystemDesigns(system, DesignTemplates.ForSystem(system, Sample)) });

        Assert.Equal("Sozluk", library.Categories.Last().Name);
        library.SelectedCategory = "Sozluk";
        var section = library.Sections.Single();
        Assert.Equal(system.Name, section.Title);
        Assert.All(section.Items, i => Assert.EndsWith("@SYS-UPVC-62C", i.DragId));

        library.SetCompanyDesigns("Sozluk", Array.Empty<SystemDesigns>());
        Assert.DoesNotContain(library.Categories, c => c.Name == "Sozluk");
        Assert.Equal(DesignLibraryViewModel.FrameCategory, library.SelectedCategory);
    }

    [Fact]
    public void ADesignInAnOwnSystem_MakesTheFrameInThatSystem_InOneStep()
    {
        var vm = new MainViewModel(Sample);
        var template = DesignTemplates.ForSystem(Sample.FindSystem("SYS-UPVC-62C")!, Sample)
            .First(t => t.Openings.Any(o => o!.Value.IsHinged()));

        Assert.Null(vm.ApplyDesign(template, "SYS-UPVC-62C"));

        var frame = vm.Project.Frames.Single();
        Assert.Equal("SYS-UPVC-62C", frame.SystemId);
        Assert.Contains(frame.GlassPanels, g => g.Opening.IsHinged());
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Project.Frames);
    }

    [Fact]
    public void DroppingADesignInAnOwnSystem_UsesThatSystem()
    {
        var vm = new MainViewModel(Sample);
        var template = DesignTemplates.ForSystem(Sample.FindSystem("SYS-AL-SL60")!, Sample).First();
        var target = (Mark.Designer.Interaction.IViewportDropTarget)vm;

        Assert.True(target.Drop(new Mark.Core.Geometry.Point2D(0, 0), $"{template.Id}@SYS-AL-SL60"));

        Assert.Equal("SYS-AL-SL60", vm.Project.Frames.Single().SystemId);
    }
}
