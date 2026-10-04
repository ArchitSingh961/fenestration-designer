using Mark.Core.Library;
using Mark.Designer.Rendering;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Library;

/// <summary>How a glass is drawn: its look is part of the library, saved, edited and used for the fill.</summary>
public class GlassLookTests
{
    private static readonly GlassLook Frosted = new() { Pattern = GlassPattern.Frosted, Color = "#F2F4F5" };

    [Fact]
    public void ALook_IsSavedWithTheGlass_AndSurvivesTheLibraryFile()
    {
        using var temp = new TempDatabase();
        var store = temp.Open(TempDatabase.ShippedLibraryPath);
        var glass = store.Library.Current.FindGlass("GLS-TGH-8")! with { Look = Frosted };

        store.Library.Update(glass);

        Assert.Equal(Frosted, temp.Open().Library.Current.FindGlass("GLS-TGH-8")!.Look);
        var copy = LibrarySerializer.Deserialize(LibrarySerializer.Serialize(store.Library.Current));
        Assert.Equal(Frosted, copy.FindGlass("GLS-TGH-8")!.Look);
        Assert.Equal(GlassPattern.Frosted, copy.FindGlass("GLS-FRS-5")!.Look!.Pattern);     // the sample's frosted glass
    }

    [Fact]
    public void AColour_MustBeHex()
    {
        var bad = new GlassDefinition { Id = "G1", Name = "Odd", ThicknessMm = 6, Look = new GlassLook { Color = "bronze" } };

        Assert.Throws<LibraryValidationException>(() => new ProductLibrary(glass: new[] { bad }));
    }

    [Fact]
    public void TheEditor_SetsTheLook_AndPlainClearGlassHasNone()
    {
        var editor = LibraryItemEditorViewModel.For(new GlassDefinition { Id = "G1", Name = "Glass", ThicknessMm = 6, CostPerSquareMetre = 100 });
        Assert.Equal(GlassPattern.Clear, editor.GlassPattern);

        editor.GlassPattern = GlassPattern.Tinted;
        editor.GlassColour = "Bronze";
        var tinted = (GlassDefinition)editor.Build(out string? error)!;
        Assert.Null(error);
        Assert.Equal(new GlassLook { Pattern = GlassPattern.Tinted, Color = "#B08B5E" }, tinted.Look);
        Assert.Equal("Tinted · Bronze", tinted.Look!.Description);

        var back = LibraryItemEditorViewModel.For(tinted);
        back.GlassPattern = GlassPattern.Clear;
        back.GlassColour = "Clear blue";
        Assert.Null(((GlassDefinition)back.Build(out _)!).Look);
    }

    [Fact]
    public void EachLook_HasItsOwnFill()
    {
        var clear = GlassBrushes.For(null);
        var frosted = GlassBrushes.For(Frosted);
        var bronze = GlassBrushes.For(new GlassLook { Pattern = GlassPattern.Tinted, Color = "#B08B5E" });

        Assert.Same(DesignTheme.GlassFill, clear);
        Assert.IsType<System.Windows.Media.DrawingBrush>(frosted);
        Assert.NotSame(frosted, bronze);
        Assert.Same(frosted, GlassBrushes.For(Frosted));                       // cached
        Assert.NotSame(frosted, GlassBrushes.For(Frosted, selected: true));
    }
}
