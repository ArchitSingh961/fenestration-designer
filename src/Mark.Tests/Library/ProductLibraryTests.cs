using System.IO;
using Mark.Core.Library;
using Mark.Core.Models;
using Xunit;

namespace Mark.Tests.Library;

public class ProductLibraryTests
{
    // ── Lookup ──────────────────────────────────────────────────────

    [Fact]
    public void Find_ReturnsDefinitionsById_AndNullForUnknownOrNull()
    {
        var library = TestLibrary.Create();
        Assert.Equal("8mm Toughened", library.FindGlass(TestLibrary.Toughened8)?.Name);
        Assert.Equal(60, library.FindProfile(TestLibrary.Frame60)?.FaceWidthMm);
        Assert.Equal(MaterialUnit.Metre, library.FindMaterial(TestLibrary.Gasket)?.Unit);
        Assert.Null(library.FindGlass("NOPE"));
        Assert.Null(library.FindProfile(null));
        Assert.Null(library.FindGlass(TestLibrary.Frame60));   // ids are looked up per kind
    }

    [Fact]
    public void Ids_AreCaseSensitive()
    {
        Assert.Null(TestLibrary.Create().FindGlass(TestLibrary.Clear6.ToLowerInvariant()));
    }

    [Fact]
    public void Defaults_ResolvePerRole()
    {
        var library = TestLibrary.Create();
        Assert.Equal(TestLibrary.Frame60, library.DefaultProfileFor(ProfileType.Frame)?.Id);
        Assert.Equal(TestLibrary.Mullion60, library.DefaultProfileFor(ProfileType.Mullion)?.Id);
        Assert.Equal(TestLibrary.Mullion60, library.DefaultProfileFor(ProfileType.Transom)?.Id);
        Assert.Null(library.DefaultProfileFor(ProfileType.Sash));
        Assert.Equal(TestLibrary.Clear6, library.DefaultGlass?.Id);
        Assert.Null(ProductLibrary.Empty.DefaultGlass);
    }

    // ── Search ──────────────────────────────────────────────────────

    [Fact]
    public void Search_WithoutText_ReturnsAllInLibraryOrder()
    {
        var library = TestLibrary.Create();
        Assert.Equal(new[] { TestLibrary.Clear6, TestLibrary.Toughened8, TestLibrary.Laminated10 },
            library.SearchGlass(new LibraryQuery()).Select(g => g.Id));
    }

    [Theory]
    [InlineData("tough", TestLibrary.Toughened8)]
    [InlineData("TOUGHENED 8", TestLibrary.Toughened8)]
    [InlineData("8 tough", TestLibrary.Toughened8)]
    [InlineData("float", TestLibrary.Clear6)]
    [InlineData("LAM-10", TestLibrary.Laminated10)]
    public void SearchGlass_MatchesEveryWordInNameIdOrCategory(string text, string expectedId)
    {
        var hit = Assert.Single(TestLibrary.Create().SearchGlass(new LibraryQuery(text)));
        Assert.Equal(expectedId, hit.Id);
    }

    [Fact]
    public void SearchGlass_NoMatch_IsEmpty()
    {
        Assert.Empty(TestLibrary.Create().SearchGlass(new LibraryQuery("triple glazed")));
    }

    [Fact]
    public void SearchProfiles_FiltersByRole()
    {
        var library = TestLibrary.Create();
        Assert.Equal(new[] { TestLibrary.Mullion60, TestLibrary.Mullion80 },
            library.SearchProfiles(new LibraryQuery(Role: ProfileType.Transom)).Select(p => p.Id));
        Assert.Equal(new[] { TestLibrary.Frame50, TestLibrary.Frame60, TestLibrary.Frame200 },
            library.SearchProfiles(new LibraryQuery(Role: ProfileType.Frame)).Select(p => p.Id));
    }

    [Fact]
    public void SearchProfiles_MatchesCodeAndSeries_AndHonoursLimit()
    {
        var library = TestLibrary.Create();
        Assert.Equal(TestLibrary.Frame60, Assert.Single(library.SearchProfiles(new LibraryQuery("al-6001"))).Id);
        Assert.Equal(3, library.SearchProfiles(new LibraryQuery("series 60")).Count);
        Assert.Equal(2, library.SearchProfiles(new LibraryQuery("series 60", Limit: 2)).Count);
        Assert.Equal(new[] { TestLibrary.Frame60 },
            library.SearchProfiles(new LibraryQuery("series 60", ProfileType.Frame)).Select(p => p.Id));
    }

    // ── Validation (invalid properties are rejected at the boundary) ─

    private static LibraryValidationException Invalid(Func<ProductLibrary> create)
        => Assert.Throws<LibraryValidationException>(create);

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var ex = Invalid(() => new ProductLibrary(glass: new[]
        {
            new GlassDefinition { Id = "G", Name = "A", ThicknessMm = 6 },
            new GlassDefinition { Id = "G", Name = "B", ThicknessMm = 8 }
        }));
        Assert.Contains(ex.Errors, e => e.Contains("'G' is used more than once"));
    }

    [Fact]
    public void MissingIdOrName_IsRejected()
    {
        var ex = Invalid(() => new ProductLibrary(glass: new[] { new GlassDefinition { Id = " ", Name = "", ThicknessMm = 6 } }));
        Assert.Contains(ex.Errors, e => e.Contains("has no id"));
        Assert.Contains(ex.Errors, e => e.Contains("has no name"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-6)]
    [InlineData(double.NaN)]
    [InlineData(600)]
    public void GlassThickness_OutOfRange_IsRejected(double thickness)
    {
        Invalid(() => new ProductLibrary(glass: new[] { new GlassDefinition { Id = "G", Name = "G", ThicknessMm = thickness } }));
    }

    [Fact]
    public void NegativePricesAndWeights_AreRejected()
    {
        var ex = Invalid(() => new ProductLibrary(
            profiles: new[] { new ProfileDefinition { Id = "P", Name = "P", Roles = new[] { ProfileType.Frame }, FaceWidthMm = 60, CostPerMetre = -1m, WeightKgPerMetre = -2 } },
            glass: new[] { new GlassDefinition { Id = "G", Name = "G", ThicknessMm = 6, CostPerSquareMetre = -5m, WeightKgPerSquareMetre = -1 } },
            materials: new[] { new MaterialDefinition { Id = "M", Name = "M", CostPerUnit = -0.01m } }));
        Assert.Contains(ex.Errors, e => e.Contains("'P': the cost per metre"));
        Assert.Contains(ex.Errors, e => e.Contains("'P': the weight per metre"));
        Assert.Contains(ex.Errors, e => e.Contains("'G': the cost per m²"));
        Assert.Contains(ex.Errors, e => e.Contains("'G': the weight per m²"));
        Assert.Contains(ex.Errors, e => e.Contains("'M': the cost per unit"));
    }

    [Fact]
    public void Profile_WithoutRoleOrFaceWidth_IsRejected()
    {
        var ex = Invalid(() => new ProductLibrary(profiles: new[] { new ProfileDefinition { Id = "P", Name = "P" } }));
        Assert.Contains(ex.Errors, e => e.Contains("at least one role"));
        Assert.Contains(ex.Errors, e => e.Contains("face width"));
    }

    [Fact]
    public void Usage_OfUnknownMaterial_IsRejected()
    {
        var ex = Invalid(() => new ProductLibrary(glass: new[]
        {
            new GlassDefinition
            {
                Id = "G", Name = "G", ThicknessMm = 6,
                Materials = new[] { new MaterialUsage { MaterialId = "GHOST", Quantity = 1 } }
            }
        }));
        Assert.Contains(ex.Errors, e => e.Contains("'GHOST', which is not in the library"));
    }

    [Fact]
    public void Profile_PerSquareMetreUsage_IsRejected()
    {
        var ex = Invalid(() => new ProductLibrary(
            profiles: new[]
            {
                new ProfileDefinition
                {
                    Id = "P", Name = "P", Roles = new[] { ProfileType.Frame }, FaceWidthMm = 60,
                    Materials = new[] { new MaterialUsage { MaterialId = "M", Basis = UsageBasis.PerSquareMetre, Quantity = 1 } }
                }
            },
            materials: new[] { new MaterialDefinition { Id = "M", Name = "M" } }));
        Assert.Contains(ex.Errors, e => e.Contains("not a valid basis"));
    }

    [Fact]
    public void Defaults_MustExistAndFitTheirRole()
    {
        var ex = Invalid(() => TestLibrary.Create(new LibraryDefaults { FrameProfileId = TestLibrary.Mullion60, GlassId = "NOPE" }));
        Assert.Contains(ex.Errors, e => e.Contains("cannot be used as a frame"));
        Assert.Contains(ex.Errors, e => e.Contains("default glass 'NOPE' is not in the library"));
    }

    [Fact]
    public void EmptyLibrary_IsValid()
    {
        Assert.Empty(ProductLibrary.Empty.Glass);
        Assert.Empty(ProductLibrary.Empty.SearchProfiles(new LibraryQuery("anything")));
    }

    // ── JSON ────────────────────────────────────────────────────────

    [Fact]
    public void Json_RoundTrip_PreservesTheLibrary()
    {
        var original = TestLibrary.Create();
        var copy = LibrarySerializer.Deserialize(LibrarySerializer.Serialize(original));

        Assert.Equal(original.Currency, copy.Currency);
        Assert.Equal(original.Defaults, copy.Defaults);
        Assert.Equal(original.Glass.Select(g => (g.Id, g.Name, g.ThicknessMm, g.CostPerSquareMetre, g.MinChargeableAreaM2)),
            copy.Glass.Select(g => (g.Id, g.Name, g.ThicknessMm, g.CostPerSquareMetre, g.MinChargeableAreaM2)));
        Assert.Equal(original.Profiles.Select(p => (p.Id, p.FaceWidthMm, p.CostPerMetre, p.CutAllowancePerEndMm, string.Join(",", p.Roles))),
            copy.Profiles.Select(p => (p.Id, p.FaceWidthMm, p.CostPerMetre, p.CutAllowancePerEndMm, string.Join(",", p.Roles))));
        Assert.Equal(original.FindGlass(TestLibrary.Clear6)!.Materials, copy.FindGlass(TestLibrary.Clear6)!.Materials);
        Assert.Equal(original.Materials.Select(m => (m.Id, m.Name, m.Category, m.Unit, m.CostPerUnit)),
            copy.Materials.Select(m => (m.Id, m.Name, m.Category, m.Unit, m.CostPerUnit)));
    }

    [Fact]
    public void Json_MissingCollections_AreTreatedAsEmpty()
    {
        var library = LibrarySerializer.Deserialize("""
            { "version": 1, "glass": [ { "id": "G", "name": "G", "thicknessMm": 6, "materials": null } ] }
            """);
        Assert.Empty(library.FindGlass("G")!.Materials);
        Assert.Empty(library.Profiles);
    }

    [Fact]
    public void Json_Invalid_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => LibrarySerializer.Deserialize("{ not json"));
        Assert.Throws<InvalidOperationException>(() => LibrarySerializer.Deserialize("""{ "version": 99 }"""));
        Assert.Throws<LibraryValidationException>(() => LibrarySerializer.Deserialize(
            """{ "version": 1, "glass": [ { "id": "G", "name": "G", "thicknessMm": -1 } ] }"""));
    }

    /// <summary>The sample library shipped with the app must load and be internally consistent.</summary>
    [Fact]
    public void ShippedLibrary_LoadsAndHasDefaults()
    {
        var library = LibrarySerializer.Load(Path.Combine(TestPaths.RepositoryRoot, "src", "Mark.App", "Library", "library.json"));

        Assert.NotEmpty(library.Glass);
        Assert.NotEmpty(library.Profiles);
        Assert.NotNull(library.DefaultGlass);
        foreach (var role in new[] { ProfileType.Frame, ProfileType.Mullion, ProfileType.Transom })
            Assert.NotNull(library.DefaultProfileFor(role));
    }
}
