using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Systems;

/// <summary>
/// Items ticked in a system (interlock, guide rail, cleats, rollers…) go with every design in it, where their role or
/// kind says, without building bundles by hand; choosing a system's frame fills the rest in.
/// </summary>
public class SystemKitTests
{
    private const string System31 = "SYS-31";

    private static ProfileDefinition Profile(string id, string name, params ProfileType[] roles) => new()
    {
        Id = id, Name = name, Series = "31mm Sliding", Roles = roles, FaceWidthMm = 40, CostPerMetre = 100m, StockLengthMm = 6000
    };

    /// <summary>The test library plus a 31mm sliding series: frame, sash, interlock, guide rail, a cleat and rollers.</summary>
    private static ProductLibrary Sliding(IReadOnlyList<KitItem>? items = null)
    {
        var basic = Create();
        var profiles = new[]
        {
            Profile("SL-FRM", "2 Track Outer Frame", ProfileType.Frame, ProfileType.Track),
            Profile("SL-SASH", "SGU Shutter", ProfileType.Sash),
            Profile("SL-MESH", "Mesh Shutter", ProfileType.MeshSash),
            Profile("SL-INT", "Interlock", ProfileType.Interlock),
            Profile("SL-RAIL", "Guide Rail", ProfileType.Track),
            Profile("SL-CLEAT", "Sliding Corner Cleat", ProfileType.Generic)
        };
        var roller = new MaterialDefinition
        {
            Id = "SL-ROLLER", Name = "Double wheel roller", Category = MaterialCategory.Hardware, Unit = MaterialUnit.Piece, CostPerUnit = 50m,
            UsedWith = new UsedWith { SystemIds = new[] { System31 } }
        };
        var system = new ProductSystem
        {
            Id = System31, Name = "31mm Sliding", FrameProfileId = "SL-FRM", SashProfileId = "SL-SASH", MeshSashProfileId = "SL-MESH",
            Items = items ?? Array.Empty<KitItem>()
        };
        return new ProductLibrary(basic.Profiles.Concat(profiles), basic.Glass, basic.Materials.Append(roller), basic.Defaults,
            basic.Currency, new[] { system });
    }

    private static Frame SlidingWindow(IProductLibrary library)
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1800, 1500, Rules);
        FrameEditor.ApplyTemplate(frame, DesignTemplates.Find("sld-2")!, null, Rules);
        FrameEditor.SetSystem(frame, System31, library, Rules);
        return frame;
    }

    [Fact]
    public void TickedItems_GoWhereTheirRoleSays()
    {
        var items = new[] { "SL-INT", "SL-RAIL", "SL-ROLLER" }
            .Select(id => SystemKit.DefaultFor(id, Sliding())).Append(new KitItem { ItemId = "SL-CLEAT", Use = KitUse.SashCorners, LengthMm = 50 }).ToList();
        Assert.Equal((KitUse.EachSlidingSash, 1d), (items[0].Use, items[0].Quantity));            // interlock: per sliding sash
        Assert.Equal(KitUse.BottomFrameBar, items[1].Use);                                          // guide rail: bottom bar
        Assert.Equal((KitUse.EachSlidingSash, 2d), (items[2].Use, items[2].Quantity));            // rollers: two per sash

        var library = Sliding(items);
        var project = new Project();
        project.Frames.Add(SlidingWindow(library));
        var result = new CalculationEngine().Calculate(project, library, new CalculationRules());

        Assert.Equal(2, result.Profiles.Count(p => p.DefinitionId == "SL-INT"));                   // one per sash, cut to its height
        Assert.All(result.Profiles.Where(p => p.DefinitionId == "SL-INT"), p => Assert.True(p.CutLengthMm > 1000));
        var rail = Assert.Single(result.Profiles, p => p.DefinitionId == "SL-RAIL");               // the bottom bar only
        Assert.True(rail.CutLengthMm > 1500);
        Assert.Equal(8, result.Profiles.Count(p => p.DefinitionId == "SL-CLEAT"));                 // 4 corners × 2 sashes …
        Assert.All(result.Profiles.Where(p => p.DefinitionId == "SL-CLEAT"), p => Assert.Equal(50, p.CutLengthMm)); // … 50 mm each
        Assert.Equal(4, result.Materials.Where(m => m.MaterialId == "SL-ROLLER").Sum(m => m.Quantity));
        // Rollers are hardware: they stand in for the per-sash hardware rate.
        Assert.All(result.Openings.Where(o => o.HasSash), o => Assert.True(o.HasHardwareSet));
    }

    [Fact]
    public void AnInterlockAlone_DoesNotStandInForTheHardwareRate()
    {
        var library = Sliding(new[] { new KitItem { ItemId = "SL-INT", Use = KitUse.EachSlidingSash } });
        var project = new Project();
        project.Frames.Add(SlidingWindow(library));
        var result = new CalculationEngine().Calculate(project, library, new CalculationRules());
        Assert.All(result.Openings.Where(o => o.HasSash), o => Assert.False(o.HasHardwareSet));
    }

    [Fact]
    public void ChoosingTheFrame_TicksItsSeries_AndFillsTheSash()
    {
        var library = Sliding();
        var bare = new ProductSystem { Id = System31, Name = "31mm Sliding" };
        var editor = new SystemEditorViewModel(bare, library);
        Assert.DoesNotContain(editor.KitRows, r => r.IsTicked);

        editor.FrameProfile = editor.FrameChoices.Single(c => c.Id == "SL-FRM");

        Assert.Equal("SL-SASH", editor.SashProfile.Id);
        Assert.Equal("SL-MESH", editor.MeshProfile.Id);
        var ticked = editor.KitRows.Where(r => r.IsTicked).Select(r => r.Id).OrderBy(id => id).ToList();
        Assert.Equal(new[] { "SL-CLEAT", "SL-INT", "SL-RAIL", "SL-ROLLER" }, ticked);              // not the sash or mesh again
        Assert.Contains("Ticked 4", editor.KitNote);

        var built = (ProductSystem)editor.Build(out string? error)!;
        Assert.Null(error);
        Assert.Equal(KitUse.SashCorners, built.Items.Single(i => i.ItemId == "SL-CLEAT").Use);   // "corner cleat"
        _ = new ProductLibrary(library.Profiles, library.Glass, library.Materials, library.Defaults, library.Currency,
            new[] { built });                                                                          // valid: no exception
    }

    [Fact]
    public void AnItemThatIsNotInTheLibrary_IsRefused()
    {
        var error = Assert.Throws<LibraryValidationException>(() => Sliding(new[] { new KitItem { ItemId = "NOPE" } }));
        Assert.Contains("NOPE", error.Message);
    }

    [Fact]
    public void ChoosingASystemsFrame_PutsTheWindowInTheSystem()
    {
        using var db = new TempDatabase();
        var store = db.Open();
        store.Library.Import(Sliding(new[] { new KitItem { ItemId = "SL-INT", Use = KitUse.EachSlidingSash } }));
        var vm = new MainViewModel(store, null, new FakeDialogs());
        vm.CreateFrame();
        var frame = vm.Project.Frames[0];
        vm.Select(frame.Id);

        Assert.Null(vm.AssignProfile("SL-FRM"));

        Assert.Equal(System31, frame.SystemId);
        Assert.Contains("31mm Sliding", vm.Notice);
        vm.UndoCommand.Execute(null);                                                                // one step
        Assert.NotEqual(System31, frame.SystemId);
    }
}
