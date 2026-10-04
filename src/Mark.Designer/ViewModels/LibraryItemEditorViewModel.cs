using System.Collections.ObjectModel;
using System.Globalization;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>
/// The edit form of one library product in the library manager. It only converts between text fields and a definition:
/// whether the product is valid (and fits the rest of the library) is decided by <see cref="LibraryService"/> when it is
/// saved. Fields the form does not show (e.g. extra properties) are carried over from the original unchanged.
/// Material usages are edited as one line each: <c>MATERIAL-ID basis quantity</c>, e.g. <c>MAT-GSK perMetre 2</c>.
/// </summary>
public sealed class LibraryItemEditorViewModel : ViewModelBase, ILibraryEditor
{
    private readonly object _original;

    private LibraryItemEditorViewModel(LibraryItemKind kind, object original, bool isNew, IProductLibrary? library, UsedWith? usedWith)
    {
        Kind = kind;
        _original = original;
        IsNew = isNew;
        library ??= ProductLibrary.Empty;
        UsedWithSystems = EditorText.SystemChoices(library, usedWith?.SystemIds);
        UsedWithOpenings = EditorText.OpeningChoices(usedWith?.OpeningTypes);
        ReinforcementChoices = library.Profiles.Where(p => p.Supports(ProfileType.Reinforcement))
            .Select(p => new LibraryChoice(p.Id, p.Name)).Prepend(new LibraryChoice(null, "(none)")).ToList();
        Reinforcement = ReinforcementChoices[0];
    }

    /// <summary>False when only prices may be changed (the library follows the owner's catalogue).</summary>
    public bool CanEditDetails { get; set; } = true;

    /// <summary>The systems this item is used with (none ticked = any system).</summary>
    public ObservableCollection<CheckChoice<string>> UsedWithSystems { get; }

    public bool HasSystems => UsedWithSystems.Count > 0;

    /// <summary>Hardware and accessories: the opening types they are for (none ticked = any).</summary>
    public ObservableCollection<CheckChoice<OpeningType>> UsedWithOpenings { get; }

    /// <summary>The reinforcement section inside this profile, or "(none)".</summary>
    public IReadOnlyList<LibraryChoice> ReinforcementChoices { get; }
    public LibraryChoice Reinforcement { get; set; }
    public string ReinforcementMinLength { get; set; } = "";
    public string ReinforcementDeduction { get; set; } = "";

    public LibraryItemKind Kind { get; }

    /// <summary>True for a product not yet in the library (its id can still be chosen; ids are permanent afterwards).</summary>
    public bool IsNew { get; }

    public bool IsProfile => Kind == LibraryItemKind.Profile;
    public bool IsGlass => Kind == LibraryItemKind.Glass;
    public bool IsMaterial => Kind == LibraryItemKind.Material;

    public string Title => IsNew ? $"New {Kind.ToString().ToLowerInvariant()}" : $"{Kind}: {Id}";

    public static IReadOnlyList<MaterialCategory> MaterialCategories { get; } = Enum.GetValues<MaterialCategory>();
    public static IReadOnlyList<MaterialUnit> MaterialUnits { get; } = Enum.GetValues<MaterialUnit>();

    // ── Common fields ───────────────────────────────────────────────

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string UsagesText { get; set; } = "";

    // ── Profile ─────────────────────────────────────────────────────

    /// <summary>Series / system (profiles) or category (glass).</summary>
    public string Group { get; set; } = "";
    public bool RoleFrame { get; set; }
    public bool RoleMullion { get; set; }
    public bool RoleTransom { get; set; }
    public bool RoleSash { get; set; }
    public bool RoleMeshSash { get; set; }
    public bool RoleReinforcement { get; set; }
    public bool RoleGlazingBead { get; set; }
    public bool RoleInterlock { get; set; }
    public bool RoleTrack { get; set; }
    public bool RoleCoupler { get; set; }
    public string FaceWidth { get; set; } = "";
    public string Depth { get; set; } = "";
    public string WeightPerMetre { get; set; } = "";
    public string CostPerMetre { get; set; } = "";
    public string StockLength { get; set; } = "";
    /// <summary>Further stock lengths, separated by commas or spaces, e.g. "6500, 7000".</summary>
    public string OtherStockLengths { get; set; } = "";
    public string CutAllowance { get; set; } = "";
    public string GlazingBite { get; set; } = "";

    // ── Glass ───────────────────────────────────────────────────────

    public string Thickness { get; set; } = "";
    public string CostPerSquareMetre { get; set; } = "";
    /// <summary>Blank = not given.</summary>
    public string WeightPerSquareMetre { get; set; } = "";
    public string MinChargeableArea { get; set; } = "";

    /// <summary>How the glass is drawn: clear, tinted, frosted, reflective, patterned or designer.</summary>
    public GlassPattern GlassPattern { get; set; } = GlassPattern.Clear;

    /// <summary>One of <see cref="GlassColours"/>.</summary>
    public string GlassColour { get; set; } = GlassLook.Colors[0].Name;

    public static IReadOnlyList<GlassPattern> GlassPatterns { get; } = Enum.GetValues<GlassPattern>();

    public static IReadOnlyList<string> GlassColours { get; } = GlassLook.Colors.Select(c => c.Name).ToList();

    // ── Material ────────────────────────────────────────────────────

    public MaterialCategory MaterialCategory { get; set; } = MaterialCategory.Accessory;
    public MaterialUnit MaterialUnit { get; set; } = MaterialUnit.Piece;
    public string CostPerUnit { get; set; } = "";

    // ── Factories ───────────────────────────────────────────────────

    public static LibraryItemEditorViewModel ForNew(LibraryItemKind kind, IProductLibrary? library = null) => kind switch
    {
        LibraryItemKind.Profile => For(new ProfileDefinition(), isNew: true, library),
        LibraryItemKind.Glass => For(new GlassDefinition(), isNew: true, library),
        _ => For(new MaterialDefinition(), isNew: true, library)
    };

    public static LibraryItemEditorViewModel For(ProfileDefinition p, bool isNew = false, IProductLibrary? library = null)
    {
        var editor = ForProfile(p, isNew, library);
        if (p.Reinforcement is { } rule)
        {
            editor.Reinforcement = editor.ReinforcementChoices.FirstOrDefault(c => c.Id == rule.ProfileId) ?? editor.Reinforcement;
            editor.ReinforcementMinLength = Text(rule.MinLengthMm, true);
            editor.ReinforcementDeduction = Text(rule.CutDeductionMm, true);
        }
        return editor;
    }

    private static LibraryItemEditorViewModel ForProfile(ProfileDefinition p, bool isNew, IProductLibrary? library)
        => new(LibraryItemKind.Profile, p, isNew, library, p.UsedWith)
    {
        Id = p.Id, Name = p.Name, Code = p.Code ?? "", Manufacturer = p.Manufacturer ?? "", Group = p.Series ?? "",
        IsActive = p.IsActive,
        RoleFrame = p.Supports(ProfileType.Frame), RoleMullion = p.Supports(ProfileType.Mullion), RoleTransom = p.Supports(ProfileType.Transom),
        RoleSash = p.Supports(ProfileType.Sash), RoleMeshSash = p.Supports(ProfileType.MeshSash),
        RoleReinforcement = p.Supports(ProfileType.Reinforcement), RoleGlazingBead = p.Supports(ProfileType.GlazingBead),
        RoleInterlock = p.Supports(ProfileType.Interlock), RoleTrack = p.Supports(ProfileType.Track), RoleCoupler = p.Supports(ProfileType.Coupler),
        FaceWidth = Text(p.FaceWidthMm, isNew), Depth = Text(p.DepthMm, isNew), WeightPerMetre = Text(p.WeightKgPerMetre, isNew),
        CostPerMetre = Text(p.CostPerMetre, isNew), StockLength = Text(p.StockLengthMm, isNew),
        OtherStockLengths = string.Join(", ", p.StockLengthsMm.Select(l => Text(l))),
        CutAllowance = Text(p.CutAllowancePerEndMm, isNew), GlazingBite = Text(p.GlazingBiteMm, isNew),
        UsagesText = UsageLines(p.Materials)
    };

    public static LibraryItemEditorViewModel For(GlassDefinition g, bool isNew = false, IProductLibrary? library = null)
        => new(LibraryItemKind.Glass, g, isNew, library, g.UsedWith)
    {
        Id = g.Id, Name = g.Name, Code = g.Code ?? "", Manufacturer = g.Manufacturer ?? "", Group = g.Category ?? "",
        IsActive = g.IsActive, Thickness = Text(g.ThicknessMm, isNew), CostPerSquareMetre = Text(g.CostPerSquareMetre, isNew),
        WeightPerSquareMetre = g.WeightKgPerSquareMetre is { } w ? Text(w) : "", MinChargeableArea = Text(g.MinChargeableAreaM2, isNew),
        UsagesText = UsageLines(g.Materials),
        GlassPattern = g.Look?.Pattern ?? GlassPattern.Clear,
        GlassColour = GlassLook.Colors.FirstOrDefault(c => string.Equals(c.Color, g.Look?.Color, StringComparison.OrdinalIgnoreCase)).Name
                      ?? GlassLook.Colors[0].Name
    };

    public static LibraryItemEditorViewModel For(MaterialDefinition m, bool isNew = false, IProductLibrary? library = null)
        => new(LibraryItemKind.Material, m, isNew, library, m.UsedWith)
    {
        Id = m.Id, Name = m.Name, Code = m.Code ?? "", Manufacturer = m.Manufacturer ?? "", IsActive = m.IsActive,
        MaterialCategory = m.Category, MaterialUnit = m.Unit, CostPerUnit = Text(m.CostPerUnit, isNew)
    };

    // ── Build ───────────────────────────────────────────────────────

    /// <summary>The definition the form describes, or null with <paramref name="error"/> if a field cannot be read.</summary>
    public object? Build(out string? error)
    {
        error = null;
        var errors = new List<string>();
        object? result = Kind switch
        {
            LibraryItemKind.Profile => BuildProfile(errors),
            LibraryItemKind.Glass => BuildGlass(errors),
            _ => BuildMaterial(errors)
        };
        if (errors.Count == 0)
            return result;
        error = string.Join(" ", errors);
        return null;
    }

    private ProfileDefinition BuildProfile(List<string> errors)
    {
        var roles = new List<ProfileType>();
        if (RoleFrame) roles.Add(ProfileType.Frame);
        if (RoleMullion) roles.Add(ProfileType.Mullion);
        if (RoleTransom) roles.Add(ProfileType.Transom);
        if (RoleSash) roles.Add(ProfileType.Sash);
        if (RoleMeshSash) roles.Add(ProfileType.MeshSash);
        if (RoleReinforcement) roles.Add(ProfileType.Reinforcement);
        if (RoleGlazingBead) roles.Add(ProfileType.GlazingBead);
        if (RoleInterlock) roles.Add(ProfileType.Interlock);
        if (RoleTrack) roles.Add(ProfileType.Track);
        if (RoleCoupler) roles.Add(ProfileType.Coupler);
        var original = (ProfileDefinition)_original;
        var reinforcement = Reinforcement?.Id is { } steel
            ? new ReinforcementRule
            {
                ProfileId = steel,
                MinLengthMm = Number(ReinforcementMinLength, "Reinforcement from length", errors, blankIsZero: true),
                CutDeductionMm = Number(ReinforcementDeduction, "Reinforcement cut deduction", errors, blankIsZero: true)
            }
            : null;
        return original with
        {
            Id = Id.Trim(), Name = Name.Trim(), Code = Optional(Code), Manufacturer = Optional(Manufacturer), Series = Optional(Group),
            IsActive = IsActive, Roles = roles,
            FaceWidthMm = Number(FaceWidth, "Face width", errors), DepthMm = Number(Depth, "Depth", errors, blankIsZero: true),
            WeightKgPerMetre = Number(WeightPerMetre, "Weight per metre", errors, blankIsZero: true),
            CostPerMetre = Money(CostPerMetre, "Cost per metre", errors),
            StockLengthMm = Number(StockLength, "Stock length", errors, blankIsZero: true),
            StockLengthsMm = NumberList(OtherStockLengths, "Other stock lengths", errors),
            CutAllowancePerEndMm = Number(CutAllowance, "Cut allowance", errors, blankIsZero: true),
            GlazingBiteMm = Number(GlazingBite, "Glazing bite", errors, blankIsZero: true),
            Materials = Usages(errors),
            Reinforcement = reinforcement,
            UsedWith = EditorText.UsedWith(original.UsedWith, UsedWithSystems)
        };
    }

    private GlassDefinition BuildGlass(List<string> errors) => (GlassDefinition)_original with
    {
        Id = Id.Trim(), Name = Name.Trim(), Code = Optional(Code), Manufacturer = Optional(Manufacturer), Category = Optional(Group),
        IsActive = IsActive, ThicknessMm = Number(Thickness, "Thickness", errors),
        CostPerSquareMetre = Money(CostPerSquareMetre, "Cost per m²", errors),
        WeightKgPerSquareMetre = string.IsNullOrWhiteSpace(WeightPerSquareMetre) ? null : Number(WeightPerSquareMetre, "Weight per m²", errors),
        MinChargeableAreaM2 = Number(MinChargeableArea, "Minimum chargeable area", errors, blankIsZero: true),
        Materials = Usages(errors),
        UsedWith = EditorText.UsedWith(((GlassDefinition)_original).UsedWith, UsedWithSystems),
        Look = LookOf(GlassPattern, GlassColour, ((GlassDefinition)_original).Look)
    };

    /// <summary>The look chosen in the editor (null for plain clear glass). A colour not in the list is kept.</summary>
    private static GlassLook? LookOf(GlassPattern pattern, string colourName, GlassLook? original)
    {
        var named = GlassLook.Colors.FirstOrDefault(c => c.Name == colourName);
        string? colour = named.Name is null ? original?.Color : named.Color;
        return pattern == GlassPattern.Clear && colour is null ? null : new GlassLook { Pattern = pattern, Color = colour };
    }

    private MaterialDefinition BuildMaterial(List<string> errors) => (MaterialDefinition)_original with
    {
        Id = Id.Trim(), Name = Name.Trim(), Code = Optional(Code), Manufacturer = Optional(Manufacturer), IsActive = IsActive,
        Category = MaterialCategory, Unit = MaterialUnit, CostPerUnit = Money(CostPerUnit, "Cost per unit", errors),
        UsedWith = EditorText.UsedWith(((MaterialDefinition)_original).UsedWith, UsedWithSystems, UsedWithOpenings)
    };

    private IReadOnlyList<MaterialUsage> Usages(List<string> errors)
    {
        var usages = new List<MaterialUsage>();
        foreach (var line in UsagesText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !Enum.TryParse<UsageBasis>(parts[1], ignoreCase: true, out var basis)
                                  || !PropertiesViewModel.TryParse(parts[2], out double quantity))
            {
                errors.Add($"Material line \"{line}\" must be: MATERIAL-ID perPiece|perMetre|perSquareMetre quantity.");
                continue;
            }
            usages.Add(new MaterialUsage { MaterialId = parts[0], Basis = basis, Quantity = quantity });
        }
        return usages;
    }

    // ── Text helpers ────────────────────────────────────────────────

    private static string? Optional(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static double Number(string text, string field, List<string> errors, bool blankIsZero = false)
    {
        if (blankIsZero && string.IsNullOrWhiteSpace(text))
            return 0;
        if (PropertiesViewModel.TryParse(text, out double value))
            return value;
        errors.Add($"{field} must be a number.");
        return 0;
    }

    private static decimal Money(string text, string field, List<string> errors)
    {
        text = text.Trim();
        if (text.Length == 0)
            return 0;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return value;
        errors.Add($"{field} must be an amount.");
        return 0;
    }

    private static IReadOnlyList<double> NumberList(string text, string field, List<string> errors)
    {
        var values = new List<double>();
        foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (PropertiesViewModel.TryParse(part, out double value)) values.Add(value);
            else errors.Add($"{field}: \"{part}\" is not a number.");
        }
        return values;
    }

    /// <summary>A new product starts with empty number fields rather than zeros.</summary>
    private static string Text(double value, bool blankIfZero = false)
        => blankIfZero && value == 0 ? "" : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Text(decimal value, bool blankIfZero = false)
        => blankIfZero && value == 0 ? "" : value.ToString(CultureInfo.InvariantCulture);

    private static string UsageLines(IReadOnlyList<MaterialUsage> usages)
        => string.Join(Environment.NewLine, usages.Select(u => $"{u.MaterialId} {Camel(u.Basis)} {Text(u.Quantity)}"));

    private static string Camel(UsageBasis basis) => char.ToLowerInvariant(basis.ToString()[0]) + basis.ToString()[1..];
}
