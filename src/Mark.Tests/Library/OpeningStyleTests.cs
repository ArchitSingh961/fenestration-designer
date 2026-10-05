using Mark.Core.Commands;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Library;

/// <summary>Items are marked for casement, sliding or both; designing offers only the items for the window's openings.</summary>
public class OpeningStyleTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Styles_AreKeptAsOpeningTypes()
    {
        var casement = new UsedWith { OpeningTypes = UsedWith.StyleTypes(casement: true, sliding: false) };
        var sliding = new UsedWith { OpeningTypes = UsedWith.StyleTypes(casement: false, sliding: true) };

        Assert.True(casement.ForCasement);
        Assert.False(casement.ForSliding);
        Assert.True(sliding.ForSliding);
        Assert.False(sliding.ForCasement);
        Assert.Empty(UsedWith.StyleTypes(true, true));                                       // both = any
        Assert.True(new UsedWith().ForCasement && new UsedWith().ForSliding);                // nothing marked = both
        Assert.True(sliding.FitsOpenings(new[] { OpeningType.Fixed }));                      // fixed panes take anything
        Assert.False(sliding.FitsOpenings(new[] { OpeningType.SideHungLeft, OpeningType.Fixed }));
        Assert.True(casement.FitsOpenings(new[] { OpeningType.TiltTurnRight }));
    }

    [Fact]
    public void A_NewItem_MustBeMarked_CasementSlidingOrBoth()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var manager = new LibraryManagerViewModel(store.Library) { Kind = LibraryItemKind.Glass };
        manager.NewCommand.Execute(null);
        var editor = manager.ItemEditor!;
        Assert.False(editor.ForCasement || editor.ForSliding);
        editor.Id = "GLS-SLIDE-5";
        editor.Name = "5mm sliding glass";
        editor.Thickness = "5";
        editor.CostPerSquareMetre = "700";

        manager.SaveCommand.Execute(null);
        Assert.True(manager.MessageIsError);
        Assert.Contains("Casement, Sliding or both", manager.Message);
        Assert.Null(store.Library.Current.FindGlass("GLS-SLIDE-5"));

        editor.ForSliding = true;
        manager.SaveCommand.Execute(null);
        Assert.False(manager.MessageIsError, manager.Message);
        var saved = store.Library.Current.FindGlass("GLS-SLIDE-5")!.UsedWith!;
        Assert.True(saved.ForSliding);
        Assert.False(saved.ForCasement);

        // Opened again, it shows what it is for.
        manager.SelectedItem = manager.Items.Single(i => i.Id == "GLS-SLIDE-5");
        Assert.True(manager.ItemEditor!.ForSliding);
        Assert.False(manager.ItemEditor.ForCasement);
    }

    [Fact]
    public void ExistingItems_NotMarked_CountAsBoth_AndKeepTheirDetail()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var manager = new LibraryManagerViewModel(store.Library) { Kind = LibraryItemKind.Material };
        var handle = store.Library.Current.Materials.First();
        store.Library.Update(handle with { UsedWith = new UsedWith { OpeningTypes = new[] { OpeningType.TopHung } } });

        manager.SelectedItem = manager.Items.Single(i => i.Id == handle.Id);
        Assert.True(manager.ItemEditor!.ForCasement);
        Assert.False(manager.ItemEditor.ForSliding);
        manager.SaveCommand.Execute(null);                                                   // style unchanged: top hung kept

        Assert.Equal(new[] { OpeningType.TopHung }, store.Library.Current.FindMaterial(handle.Id)!.UsedWith!.OpeningTypes);
    }

    [Fact]
    public void Designing_OffersOnlyTheGlassForTheOpening()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var plain = store.Library.Current.Glass.First();
        store.Library.Add(plain with { Id = "GLS-ONLY-SLIDING", Name = "Only sliding",
            UsedWith = new UsedWith { OpeningTypes = UsedWith.StyleTypes(false, true) } });
        store.Library.Add(plain with { Id = "GLS-ONLY-CASEMENT", Name = "Only casement",
            UsedWith = new UsedWith { OpeningTypes = UsedWith.StyleTypes(true, false) } });
        var vm = new MainViewModel(store);
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        var frame = vm.Project.Frames.Single();
        var pane = frame.GlassPanels.Single();

        IReadOnlyList<string> Offered()
        {
            vm.Properties.ShowGlass(pane, frame.SystemId);
            return vm.Properties.GlassPicker!.Options.Select(o => o.Id).ToList();
        }

        pane.Opening = OpeningType.Fixed;
        Assert.Contains("GLS-ONLY-SLIDING", Offered());                                      // a fixed pane: anything
        Assert.Contains("GLS-ONLY-CASEMENT", Offered());
        pane.Opening = OpeningType.SideHungLeft;
        Assert.DoesNotContain("GLS-ONLY-SLIDING", Offered());
        Assert.Contains("GLS-ONLY-CASEMENT", Offered());
        pane.Opening = OpeningType.SlidingLeft;
        Assert.Contains("GLS-ONLY-SLIDING", Offered());
        Assert.DoesNotContain("GLS-ONLY-CASEMENT", Offered());
        Assert.Contains(plain.Id, Offered());                                                // not marked: both
    }
}
