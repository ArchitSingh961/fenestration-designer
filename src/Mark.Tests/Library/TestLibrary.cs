using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Tests.Library;

/// <summary>
/// A small library with round numbers so expected quantities and costs can be worked out by hand.
/// Defaults: 60 mm frame, 60 mm mullion/transom, 6 mm clear glass (the same face widths as the generic DesignRules).
/// </summary>
internal static class TestLibrary
{
    public const string Frame50 = "FRM-50";
    public const string Frame60 = "FRM-60";
    public const string Frame200 = "FRM-200";
    public const string Mullion60 = "MUL-60";
    public const string Mullion80 = "MUL-80";
    public const string Clear6 = "CLR-6";
    public const string Toughened8 = "TGH-8";
    public const string Laminated10 = "LAM-10";
    public const string Gasket = "GSK";
    public const string Block = "BLK";
    public const string Cleat = "CLEAT";

    /// <summary>Frame 60, mullion 60, glass 6, as DesignRules defaults (so frames drawn with them match the library).</summary>
    public static readonly DesignRules Rules = new();

    public static MaterialDefinition GasketMaterial => new()
        { Id = Gasket, Name = "Glazing gasket", Category = MaterialCategory.Gasket, Unit = MaterialUnit.Metre, CostPerUnit = 10m };

    public static ProductLibrary Create(LibraryDefaults? defaults = null) => new(
        profiles: new[]
        {
            new ProfileDefinition
            {
                Id = Frame50, Name = "50mm Frame", Series = "Series 50", Roles = new[] { ProfileType.Frame },
                FaceWidthMm = 50, WeightKgPerMetre = 1.0, CostPerMetre = 100m, StockLengthMm = 6000,
                Materials = new[]
                {
                    new MaterialUsage { MaterialId = Cleat, Basis = UsageBasis.PerPiece, Quantity = 1 },
                    new MaterialUsage { MaterialId = Gasket, Basis = UsageBasis.PerMetre, Quantity = 1 }
                }
            },
            new ProfileDefinition
            {
                Id = Frame60, Name = "60mm Frame", Code = "AL-6001", Series = "Series 60", Roles = new[] { ProfileType.Frame },
                FaceWidthMm = 60, WeightKgPerMetre = 1.5, CostPerMetre = 150m, StockLengthMm = 6000
            },
            new ProfileDefinition
            {
                Id = Frame200, Name = "200mm Frame", Roles = new[] { ProfileType.Frame }, FaceWidthMm = 200, CostPerMetre = 1m
            },
            new ProfileDefinition
            {
                Id = Mullion60, Name = "60mm Mullion", Series = "Series 60", Roles = new[] { ProfileType.Mullion, ProfileType.Transom },
                FaceWidthMm = 60, WeightKgPerMetre = 1.2, CostPerMetre = 120m, StockLengthMm = 6000
            },
            new ProfileDefinition
            {
                Id = Mullion80, Name = "80mm Mullion", Series = "Series 60", Roles = new[] { ProfileType.Mullion, ProfileType.Transom },
                FaceWidthMm = 80, WeightKgPerMetre = 2.0, CostPerMetre = 200m, CutAllowancePerEndMm = 5, StockLengthMm = 6500
            }
        },
        glass: new[]
        {
            new GlassDefinition
            {
                Id = Clear6, Name = "6mm Clear", Category = "Float", ThicknessMm = 6, CostPerSquareMetre = 1000m,
                WeightKgPerSquareMetre = 15,
                Materials = new[] { new MaterialUsage { MaterialId = Block, Basis = UsageBasis.PerPiece, Quantity = 4 } }
            },
            new GlassDefinition
            {
                Id = Toughened8, Name = "8mm Toughened", Category = "Toughened", ThicknessMm = 8, CostPerSquareMetre = 2000m,
                WeightKgPerSquareMetre = 20,
                Materials = new[] { new MaterialUsage { MaterialId = Gasket, Basis = UsageBasis.PerMetre, Quantity = 1 } }
            },
            new GlassDefinition
            {
                Id = Laminated10, Name = "10mm Laminated", Category = "Laminated", ThicknessMm = 10, CostPerSquareMetre = 3000m,
                MinChargeableAreaM2 = 1.0
            }
        },
        materials: new[]
        {
            GasketMaterial,
            new MaterialDefinition { Id = Block, Name = "Setting block", Category = MaterialCategory.Accessory, Unit = MaterialUnit.Piece, CostPerUnit = 2m },
            new MaterialDefinition { Id = Cleat, Name = "Corner cleat", Category = MaterialCategory.Hardware, Unit = MaterialUnit.Piece, CostPerUnit = 50m }
        },
        defaults: defaults ?? new LibraryDefaults
        {
            FrameProfileId = Frame60, MullionProfileId = Mullion60, TransomProfileId = Mullion60, GlassId = Clear6
        },
        currency: "INR");

    /// <summary>A project with one 1200 × 1500 frame (60 mm members, one 1080 × 1380 glass) and no explicit references.</summary>
    public static (Project Project, Frame Frame) SingleFrame(double width = 1200, double height = 1500)
    {
        var frame = FrameEditor.CreateFrame(0, 0, width, height, Rules);
        var project = new Project { Name = "Test" };
        project.Frames.Add(frame);
        return (project, frame);
    }
}
