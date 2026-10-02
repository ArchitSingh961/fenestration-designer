using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Data;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Data;

/// <summary>Library persistence and management: CRUD, ids, validation, search/filter, retire, delete rules, import.</summary>
public class LibraryServiceTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A store whose library is the small hand-checkable test library.</summary>
    private LocalStore Store()
    {
        var store = _temp.Open();
        store.Library.Import(Create());
        return store;
    }

    private static ProfileDefinition NewProfile(string id = "PRF-NEW", string manufacturer = "Acme") => new()
    {
        Id = id, Name = "New Frame", Manufacturer = manufacturer, Series = "Series 70", Roles = new[] { ProfileType.Frame },
        FaceWidthMm = 70, CostPerMetre = 123.45m, StockLengthMm = 6000, StockLengthsMm = new double[] { 6500 },
        Materials = new[] { new MaterialUsage { MaterialId = Gasket, Basis = UsageBasis.PerMetre, Quantity = 2 } },
        Properties = new Dictionary<string, string> { ["colour"] = "RAL 9016" }
    };

    // ── CRUD ────────────────────────────────────────────────────────

    [Fact]
    public void Import_IntoAnEmptyLibrary_StoresEverything_InLibraryOrder()
    {
        Store();
        var reopened = _temp.Open();

        Assert.Equal(LibrarySerializer.Serialize(Create()), LibrarySerializer.Serialize(reopened.Library.Current));
        Assert.Equal(new[] { Frame50, Frame60, Frame200, Mullion60, Mullion80 }, reopened.Library.Current.Profiles.Select(p => p.Id));
        Assert.Equal("INR", reopened.Library.Current.Currency);
        Assert.Equal(Clear6, reopened.Library.Current.Defaults.GlassId);
    }

    [Fact]
    public void Add_StoresTheProduct_ExactlyAndAppendsIt()
    {
        var store = Store();
        var profile = NewProfile();
        ProductLibrary? raised = null;
        store.Library.Changed += l => raised = l;

        store.Library.Add(profile);

        var reloaded = _temp.Open().Library.Current;
        var stored = reloaded.FindProfile("PRF-NEW")!;
        Assert.Equal("PRF-NEW", reloaded.Profiles[^1].Id);
        Assert.Equal(123.45m, stored.CostPerMetre);
        Assert.Equal(new double[] { 6500 }, stored.StockLengthsMm);
        Assert.Equal(new[] { ProfileType.Frame }, stored.Roles);
        Assert.Equal(Gasket, Assert.Single(stored.Materials).MaterialId);
        Assert.Equal("RAL 9016", stored.Properties["colour"]);
        Assert.Same(store.Library.Current, raised);
    }

    [Fact]
    public void Update_ChangesTheProduct_AndKeepsItsPlaceAndId()
    {
        var store = Store();
        var changed = store.Library.Current.FindGlass(Clear6)! with { Name = "6mm Clear Float", CostPerSquareMetre = 1100.50m };

        store.Library.Update(changed);

        var reloaded = _temp.Open().Library.Current;
        Assert.Equal(Clear6, reloaded.Glass[0].Id);
        Assert.Equal(("6mm Clear Float", 1100.50m), (reloaded.Glass[0].Name, reloaded.Glass[0].CostPerSquareMetre));
    }

    [Fact]
    public void Delete_AnUnreferencedProduct_RemovesIt()
    {
        var store = Store();
        store.Library.Delete(LibraryItemKind.Glass, Laminated10);

        Assert.Null(_temp.Open().Library.Current.FindGlass(Laminated10));
    }

    [Fact]
    public void Materials_CanBeAddedAndEdited()
    {
        var store = Store();
        store.Library.Add(new MaterialDefinition { Id = "HW-HANDLE", Name = "Handle", Category = MaterialCategory.Hardware, CostPerUnit = 450m });
        store.Library.Update(store.Library.Current.FindMaterial("HW-HANDLE")! with { CostPerUnit = 475.25m });

        Assert.Equal(475.25m, _temp.Open().Library.Current.FindMaterial("HW-HANDLE")!.CostPerUnit);
    }

    // ── Ids and validation ──────────────────────────────────────────

    [Theory]
    [InlineData(Frame60)]        // same kind
    [InlineData(Clear6)]         // ids are unique across kinds
    [InlineData(Gasket)]
    public void Add_WithAnIdAlreadyInUse_IsRejected_AndChangesNothing(string id)
    {
        var store = Store();
        string before = store.Library.Export();

        var ex = Assert.Throws<LibraryOperationException>(() => store.Library.Add(NewProfile(id)));

        Assert.Contains("already used", ex.Message);
        Assert.Equal(before, _temp.Open().Library.Export());
    }

    [Fact]
    public void Update_OfAnUnknownId_IsRejected()
    {
        var store = Store();
        Assert.Throws<LibraryOperationException>(() => store.Library.Update(NewProfile("NOPE")));
    }

    [Fact]
    public void AnInvalidProduct_IsRejected_AndNothingIsWritten()
    {
        var store = Store();
        string before = store.Library.Export();

        Assert.Throws<LibraryValidationException>(() => store.Library.Add(NewProfile() with { FaceWidthMm = 0 }));
        Assert.Throws<LibraryValidationException>(() => store.Library.Add(NewProfile() with
        {
            Materials = new[] { new MaterialUsage { MaterialId = "NO-SUCH-MATERIAL", Quantity = 1 } }
        }));
        Assert.Throws<LibraryValidationException>(() => store.Library.Update(store.Library.Current.FindGlass(Clear6)! with { CostPerSquareMetre = -1 }));

        Assert.Equal(before, _temp.Open().Library.Export());
    }

    // ── Search and filter ───────────────────────────────────────────

    [Fact]
    public void Search_ByNameAndId_AndFilters()
    {
        var store = Store();
        store.Library.Add(NewProfile("PRF-ACME", manufacturer: "Acme"));
        var library = store.Library.Current;

        Assert.Equal(new[] { Frame60 }, library.SearchProfiles(new LibraryQuery("60mm frame")).Select(p => p.Id));
        Assert.Equal(new[] { Mullion80 }, library.SearchProfiles(new LibraryQuery("MUL-80")).Select(p => p.Id));
        Assert.Equal(new[] { "PRF-ACME" }, library.SearchProfiles(new LibraryQuery { Manufacturer = "acme" }).Select(p => p.Id));
        Assert.Equal(new[] { Mullion60, Mullion80 }, library.SearchProfiles(new LibraryQuery { Group = "Series 60" })
            .Where(p => p.Supports(ProfileType.Mullion)).Select(p => p.Id));
        Assert.Equal(new[] { Toughened8 }, library.SearchGlass(new LibraryQuery { Group = "toughened" }).Select(g => g.Id));
        Assert.Equal(new[] { Cleat }, library.SearchMaterials(new LibraryQuery { MaterialCategory = MaterialCategory.Hardware }).Select(m => m.Id));
        Assert.Equal(new[] { Gasket }, library.SearchMaterials(new LibraryQuery("gasket")).Select(m => m.Id));
    }

    // ── Retire (deactivate) ─────────────────────────────────────────

    [Fact]
    public void Retire_HidesTheProductFromSearches_ButItStillResolves()
    {
        var store = Store();
        store.Library.SetActive(LibraryItemKind.Glass, Toughened8, false);
        var library = _temp.Open().Library.Current;

        Assert.DoesNotContain(library.SearchGlass(new LibraryQuery()), g => g.Id == Toughened8);
        Assert.Contains(library.SearchGlass(new LibraryQuery { IncludeInactive = true }), g => g.Id == Toughened8);
        Assert.False(library.FindGlass(Toughened8)!.IsActive);       // references still resolve and price

        store.Library.SetActive(LibraryItemKind.Glass, Toughened8, true);
        Assert.True(_temp.Open().Library.Current.FindGlass(Toughened8)!.IsActive);
    }

    // ── Delete rules ────────────────────────────────────────────────

    [Fact]
    public void Delete_IsRefused_ForALibraryDefault()
    {
        var store = Store();
        var ex = Assert.Throws<LibraryOperationException>(() => store.Library.Delete(LibraryItemKind.Glass, Clear6));
        Assert.Contains(ex.Reasons, r => r.Contains("default glass"));
        Assert.NotNull(_temp.Open().Library.Current.FindGlass(Clear6));
    }

    [Fact]
    public void Delete_IsRefused_ForAMaterialUsedByAProduct()
    {
        var store = Store();
        var ex = Assert.Throws<LibraryOperationException>(() => store.Library.Delete(LibraryItemKind.Material, Gasket));
        Assert.Contains(ex.Reasons, r => r.Contains("8mm Toughened"));
    }

    [Fact]
    public void Delete_IsRefused_WhileASavedProjectUsesTheProduct()
    {
        var store = Store();
        var (project, frame) = SingleFrame();
        project.Name = "Kitchen";
        frame.GlassPanels[0].GlassDefinitionId = Laminated10;
        store.Projects.Save(project);

        var ex = Assert.Throws<LibraryOperationException>(() => store.Library.Delete(LibraryItemKind.Glass, Laminated10));
        Assert.Contains(ex.Reasons, r => r.Contains("Kitchen"));

        store.Projects.Delete(project.Id);
        store.Library.Delete(LibraryItemKind.Glass, Laminated10);           // no longer referenced
    }

    [Fact]
    public void Delete_IsRefused_WhileTheOpenProjectUsesTheProduct()
    {
        var store = Store();
        var (project, frame) = SingleFrame();
        frame.Profiles[0].ProfileDefinitionId = Frame200;                   // not saved yet

        var ex = Assert.Throws<LibraryOperationException>(() => store.Library.Delete(LibraryItemKind.Profile, Frame200, project));
        Assert.Contains(ex.Reasons, r => r.Contains("open project"));
        store.Library.Delete(LibraryItemKind.Profile, Frame200);            // another open project would not block
    }

    // ── Import ──────────────────────────────────────────────────────

    [Fact]
    public void Import_IntoANonEmptyLibrary_SkipsExistingIds_NeverOverwrites_AndKeepsSettings()
    {
        var store = Store();
        store.Library.Update(store.Library.Current.FindGlass(Clear6)! with { CostPerSquareMetre = 999m });
        var incoming = new ProductLibrary(
            profiles: Create().Profiles.Append(NewProfile("PRF-IMPORTED")),
            glass: Create().Glass, materials: Create().Materials,
            defaults: new LibraryDefaults { GlassId = Laminated10 }, currency: "EUR");

        var result = store.Library.Import(incoming);

        Assert.Equal(new[] { "PRF-IMPORTED" }, result.Added);
        Assert.Contains(Clear6, result.Skipped);
        Assert.False(result.SettingsImported);
        var library = _temp.Open().Library.Current;
        Assert.Equal(999m, library.FindGlass(Clear6)!.CostPerSquareMetre);  // not overwritten
        Assert.Equal(("INR", Clear6), (library.Currency, library.Defaults.GlassId));
    }

    [Fact]
    public void Import_ThatWouldLeaveTheLibraryInvalid_ChangesNothing()
    {
        var store = Store();
        string before = store.Library.Export();

        // Valid on its own, but its material "TGH-8" is skipped (that id is already a glass type here), so the merged
        // library would have a profile using a material that does not exist.
        var incoming = new ProductLibrary(
            profiles: new[] { NewProfile("PRF-X") with { Materials = new[] { new MaterialUsage { MaterialId = Toughened8, Quantity = 1 } } } },
            materials: new[] { new MaterialDefinition { Id = Toughened8, Name = "Clashing id" } });

        Assert.Throws<LibraryValidationException>(() => store.Library.Import(incoming));
        Assert.Equal(before, _temp.Open().Library.Export());
        Assert.Null(store.Library.Current.FindProfile("PRF-X"));
    }

    [Fact]
    public void Export_RoundTripsThroughTheLibraryFileFormat()
    {
        var store = Store();
        store.Library.Add(NewProfile());
        var exported = LibrarySerializer.Deserialize(store.Library.Export());

        Assert.Equal(store.Library.Export(), LibrarySerializer.Serialize(exported));
    }

    [Fact]
    public void Load_IsDeterministic_RegardlessOfTheOrderRowsWereWritten()
    {
        var store = Store();
        store.Library.Update(store.Library.Current.FindProfile(Frame50)! with { Name = "50mm Frame (edited)" });   // rewritten last
        var library = _temp.Open().Library.Current;

        Assert.Equal(new[] { Frame50, Frame60, Frame200, Mullion60, Mullion80 }, library.Profiles.Select(p => p.Id));
        Assert.Equal(_temp.Open().Library.Export(), _temp.Open().Library.Export());
    }
}
