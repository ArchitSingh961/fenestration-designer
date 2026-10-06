using Mark.Core.Models;
using Mark.Core.Utilities;

namespace Mark.Core.Library;

/// <summary>The library data is inconsistent (duplicate ids, negative prices, dangling references…).</summary>
public sealed class LibraryValidationException : InvalidOperationException
{
    public LibraryValidationException(IReadOnlyList<string> errors)
        : base("The product library is invalid: " + string.Join(" ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// In-memory, validated, immutable <see cref="IProductLibrary"/>. Products come from data
/// (<see cref="LibrarySerializer"/> or a caller), never from code. The constructor rejects inconsistent data,
/// so the calculation engine can trust every definition it finds.
/// </summary>
public sealed class ProductLibrary : IProductLibrary
{
    private readonly Dictionary<string, ProfileDefinition> _profiles;
    private readonly Dictionary<string, GlassDefinition> _glass;
    private readonly Dictionary<string, MaterialDefinition> _materials;
    private readonly Dictionary<string, ProductSystem> _systems;
    private readonly Dictionary<string, Bundle> _bundles;

    /// <summary>A library with no products (designing still works; calculations report unresolved items).</summary>
    public static ProductLibrary Empty { get; } = new();

    /// <exception cref="LibraryValidationException">The data is inconsistent.</exception>
    public ProductLibrary(
        IEnumerable<ProfileDefinition>? profiles = null,
        IEnumerable<GlassDefinition>? glass = null,
        IEnumerable<MaterialDefinition>? materials = null,
        LibraryDefaults? defaults = null,
        string currency = "",
        IEnumerable<ProductSystem>? systems = null,
        IEnumerable<Bundle>? bundles = null)
    {
        // JSON may supply null collections; normalise so consumers never see null. Null entries are kept
        // so that validation reports them.
        Profiles = (profiles ?? Array.Empty<ProfileDefinition>())
            .Select(p => p is null ? p! : p with
            {
                Roles = p.Roles ?? Array.Empty<ProfileType>(),
                StockLengthsMm = p.StockLengthsMm ?? Array.Empty<double>(),
                Materials = p.Materials ?? Array.Empty<MaterialUsage>(),
                Properties = p.Properties ?? new Dictionary<string, string>()
            }).ToList().AsReadOnly();
        Glass = (glass ?? Array.Empty<GlassDefinition>())
            .Select(g => g is null ? g! : g with
            {
                Materials = g.Materials ?? Array.Empty<MaterialUsage>(),
                Properties = g.Properties ?? new Dictionary<string, string>()
            }).ToList().AsReadOnly();
        Materials = (materials ?? Array.Empty<MaterialDefinition>())
            .Select(m => m is null ? m! : m with { Properties = m.Properties ?? new Dictionary<string, string>() })
            .ToList().AsReadOnly();
        Systems = (systems ?? Array.Empty<ProductSystem>()).ToList().AsReadOnly();
        Bundles = (bundles ?? Array.Empty<Bundle>())
            .Select(b => b is null ? b! : b with
            {
                OpeningTypes = b.OpeningTypes ?? Array.Empty<OpeningType>(),
                Parts = (b.Parts ?? Array.Empty<BundlePart>())
                    .Select(part => part is null ? part! : part with { Steps = part.Steps ?? Array.Empty<SizeStep>() }).ToList()
            }).ToList().AsReadOnly();
        Defaults = defaults ?? new LibraryDefaults();
        Currency = currency ?? "";

        var errors = Validate();
        if (errors.Count > 0)
            throw new LibraryValidationException(errors);

        _profiles = Profiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
        _glass = Glass.ToDictionary(g => g.Id, StringComparer.Ordinal);
        _materials = Materials.ToDictionary(m => m.Id, StringComparer.Ordinal);
        _systems = Systems.ToDictionary(x => x.Id, StringComparer.Ordinal);
        _bundles = Bundles.ToDictionary(b => b.Id, StringComparer.Ordinal);
    }

    public string Currency { get; }

    public LibraryDefaults Defaults { get; }

    public IReadOnlyList<ProfileDefinition> Profiles { get; }

    public IReadOnlyList<GlassDefinition> Glass { get; }

    public IReadOnlyList<MaterialDefinition> Materials { get; }

    public IReadOnlyList<ProductSystem> Systems { get; }

    public IReadOnlyList<Bundle> Bundles { get; }

    public ProductSystem? FindSystem(string? id) => id is not null && _systems.TryGetValue(id, out var x) ? x : null;

    public Bundle? FindBundle(string? id) => id is not null && _bundles.TryGetValue(id, out var b) ? b : null;

    public ProductSystem? DefaultSystem => FindSystem(Defaults.SystemId);

    /// <summary>The name of any item, system or bundle by id, or null.</summary>
    public string? NameOf(string? id)
        => FindProfile(id)?.Name ?? FindGlass(id)?.Name ?? FindMaterial(id)?.Name ?? FindSystem(id)?.Name ?? FindBundle(id)?.Name;

    public ProfileDefinition? FindProfile(string? id) => id is not null && _profiles.TryGetValue(id, out var p) ? p : null;

    public GlassDefinition? FindGlass(string? id) => id is not null && _glass.TryGetValue(id, out var g) ? g : null;

    public MaterialDefinition? FindMaterial(string? id) => id is not null && _materials.TryGetValue(id, out var m) ? m : null;

    public ProfileDefinition? DefaultProfileFor(ProfileType role) => FindProfile(Defaults.ProfileIdFor(role));

    public GlassDefinition? DefaultGlass => FindGlass(Defaults.GlassId);

    public IReadOnlyList<ProfileDefinition> SearchProfiles(LibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var words = Words(query.Text);
        return Take(Profiles.Where(p => (query.Role is not { } role || p.Supports(role))
                                        && Common(query, p.IsActive, p.Manufacturer)
                                        && Same(query.Group, p.Series)
                                        && Matches(words, p.Id, p.Name, p.Code, p.Manufacturer, p.Series)), query.Limit);
    }

    public IReadOnlyList<GlassDefinition> SearchGlass(LibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var words = Words(query.Text);
        return Take(Glass.Where(g => Common(query, g.IsActive, g.Manufacturer)
                                     && Same(query.Group, g.Category)
                                     && Matches(words, g.Id, g.Name, g.Code, g.Manufacturer, g.Category)), query.Limit);
    }

    public IReadOnlyList<MaterialDefinition> SearchMaterials(LibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var words = Words(query.Text);
        return Take(Materials.Where(m => Common(query, m.IsActive, m.Manufacturer)
                                         && (query.MaterialCategory is not { } category || m.Category == category)
                                         && Matches(words, m.Id, m.Name, m.Code, m.Manufacturer, m.Category.ToString())), query.Limit);
    }

    // ── Search ──────────────────────────────────────────────────────

    /// <summary>The filters every kind of item shares: active state and manufacturer.</summary>
    private static bool Common(LibraryQuery query, bool isActive, string? manufacturer)
        => (isActive || query.IncludeInactive) && Same(query.Manufacturer, manufacturer);

    /// <summary>True when there is no filter, or the value equals it (ignoring case and surrounding spaces).</summary>
    private static bool Same(string? filter, string? value)
        => string.IsNullOrWhiteSpace(filter) || string.Equals(filter.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string[] Words(string? text)
        => string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool Matches(string[] words, params string?[] fields)
        => words.All(w => fields.Any(f => f is not null && f.Contains(w, StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<T> Take<T>(IEnumerable<T> items, int? limit)
        => (limit is { } n ? items.Take(Math.Max(n, 0)) : items).ToList().AsReadOnly();

    // ── Validation ──────────────────────────────────────────────────

    private List<string> Validate()
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var materialIds = Materials.Where(m => m is not null).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        void CheckItem(object? item, string? id, string? name, string kind)
        {
            if (item is null) { errors.Add($"A {kind} entry is empty."); return; }
            if (string.IsNullOrWhiteSpace(id)) errors.Add($"A {kind} has no id.");
            else if (!ids.Add(id)) errors.Add($"The id '{id}' is used more than once.");
            if (string.IsNullOrWhiteSpace(name)) errors.Add($"The {kind} '{id}' has no name.");
        }

        void NonNegative(double value, string what, string id)
        {
            if (!double.IsFinite(value) || value < 0) errors.Add($"'{id}': {what} must be zero or more.");
        }

        void NonNegativeMoney(decimal value, string what, string id)
        {
            if (value < 0) errors.Add($"'{id}': {what} must be zero or more.");
        }

        void CheckUsages(IReadOnlyList<MaterialUsage>? usages, string ownerId, bool allowArea)
        {
            foreach (var usage in usages ?? Array.Empty<MaterialUsage>())
            {
                if (usage is null) { errors.Add($"'{ownerId}' has an empty material usage."); continue; }
                if (!materialIds.Contains(usage.MaterialId))
                    errors.Add($"'{ownerId}' uses material '{usage.MaterialId}', which is not in the library.");
                if (!double.IsFinite(usage.Quantity) || usage.Quantity < 0)
                    errors.Add($"'{ownerId}': the quantity of '{usage.MaterialId}' must be zero or more.");
                if (!Enum.IsDefined(usage.Basis) || (usage.Basis == UsageBasis.PerSquareMetre && !allowArea))
                    errors.Add($"'{ownerId}': '{usage.Basis}' is not a valid basis for '{usage.MaterialId}' here.");
            }
        }

        foreach (var m in Materials)
        {
            CheckItem(m, m?.Id, m?.Name, "material");
            if (m is null) continue;
            NonNegativeMoney(m.CostPerUnit, "the cost per unit", m.Id);
            if (!Enum.IsDefined(m.Category) || !Enum.IsDefined(m.Unit))
                errors.Add($"'{m.Id}': unknown category or unit.");
        }

        foreach (var p in Profiles)
        {
            CheckItem(p, p?.Id, p?.Name, "profile");
            if (p is null) continue;
            if (p.Roles is null || p.Roles.Count == 0)
                errors.Add($"'{p.Id}': a profile needs at least one role (frame, mullion, transom…).");
            if (!ValidationHelper.IsValidThickness(p.FaceWidthMm))
                errors.Add($"'{p.Id}': the face width must be greater than 0 and at most {Units.MaxThicknessMm} mm.");
            NonNegative(p.DepthMm, "the depth", p.Id);
            NonNegative(p.WeightKgPerMetre, "the weight per metre", p.Id);
            NonNegative(p.StockLengthMm, "the stock length", p.Id);
            if (p.StockLengthMm > Units.MaxDimensionMm)
                errors.Add($"'{p.Id}': the stock length must be at most {Units.MaxDimensionMm} mm.");
            foreach (var length in p.StockLengthsMm)
            {
                if (!double.IsFinite(length) || length <= 0 || length > Units.MaxDimensionMm)
                    errors.Add($"'{p.Id}': every stock length must be greater than 0 and at most {Units.MaxDimensionMm} mm.");
            }
            NonNegative(p.GlazingBiteMm, "the glazing bite", p.Id);
            NonNegativeMoney(p.CostPerMetre, "the cost per metre", p.Id);
            if (!double.IsFinite(p.CutAllowancePerEndMm) || Math.Abs(p.CutAllowancePerEndMm) > Units.MaxThicknessMm)
                errors.Add($"'{p.Id}': the cut allowance must be a number between -{Units.MaxThicknessMm} and {Units.MaxThicknessMm} mm.");
            CheckUsages(p.Materials, p.Id, allowArea: false);
        }

        foreach (var g in Glass)
        {
            CheckItem(g, g?.Id, g?.Name, "glass");
            if (g is null) continue;
            if (!ValidationHelper.IsValidThickness(g.ThicknessMm))
                errors.Add($"'{g.Id}': the thickness must be greater than 0 and at most {Units.MaxThicknessMm} mm.");
            NonNegativeMoney(g.CostPerSquareMetre, "the cost per m²", g.Id);
            if (g.WeightKgPerSquareMetre is { } w) NonNegative(w, "the weight per m²", g.Id);
            NonNegative(g.MinChargeableAreaM2, "the minimum chargeable area", g.Id);
            CheckUsages(g.Materials, g.Id, allowArea: true);
            if (g.Look?.Color is { } colour && !System.Text.RegularExpressions.Regex.IsMatch(colour, "^#[0-9A-Fa-f]{6}$"))
                errors.Add($"'{g.Id}': the glass colour must be written as #RRGGBB.");
        }

        ValidateSystems(errors, ids);

        foreach (var role in new[] { ProfileType.Frame, ProfileType.Mullion, ProfileType.Transom })
        {
            if (Defaults.ProfileIdFor(role) is not { } id) continue;
            var profile = Profiles.FirstOrDefault(p => p?.Id == id);
            if (profile is null) errors.Add($"The default {role.ToString().ToLowerInvariant()} profile '{id}' is not in the library.");
            else if (!profile.Supports(role)) errors.Add($"The default {role.ToString().ToLowerInvariant()} profile '{id}' cannot be used as a {role.ToString().ToLowerInvariant()}.");
        }
        if (Defaults.GlassId is { } glassId && Glass.All(g => g?.Id != glassId))
            errors.Add($"The default glass '{glassId}' is not in the library.");
        if (Defaults.SystemId is { } defaultSystem && Systems.All(x => x?.Id != defaultSystem))
            errors.Add($"The default system '{defaultSystem}' is not in the library.");

        return errors;
    }

    /// <summary>Systems, bundles, reinforcement and "used with" references.</summary>
    private void ValidateSystems(List<string> errors, HashSet<string> ids)
    {
        var profiles = Profiles.Where(p => p is not null).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var materialIds = Materials.Where(m => m is not null).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var glassIds = Glass.Where(g => g is not null).Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var systemIds = Systems.Where(x => x is not null).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        void CheckUsedWith(UsedWith? usedWith, string ownerId)
        {
            if (usedWith is null) return;
            foreach (string systemId in usedWith.SystemIds ?? Array.Empty<string>())
                if (!systemIds.Contains(systemId))
                    errors.Add($"'{ownerId}' is marked as used with system '{systemId}', which is not in the library.");
        }

        foreach (var p in Profiles.Where(p => p is not null))
        {
            CheckUsedWith(p.UsedWith, p.Id);
            if (p.Reinforcement is not { } rule) continue;
            if (!profiles.TryGetValue(rule.ProfileId ?? "", out var steel))
                errors.Add($"'{p.Id}' is reinforced with '{rule.ProfileId}', which is not in the library.");
            else if (!steel.Supports(ProfileType.Reinforcement))
                errors.Add($"'{p.Id}' is reinforced with '{steel.Name}', which does not have the reinforcement role.");
            if (!double.IsFinite(rule.MinLengthMm) || rule.MinLengthMm < 0 || !double.IsFinite(rule.CutDeductionMm) || rule.CutDeductionMm < 0)
                errors.Add($"'{p.Id}': the reinforcement's minimum length and cut deduction must be zero or more.");
        }
        foreach (var g in Glass.Where(g => g is not null)) CheckUsedWith(g.UsedWith, g.Id);
        foreach (var m in Materials.Where(m => m is not null)) CheckUsedWith(m.UsedWith, m.Id);

        foreach (var x in Systems)
        {
            if (x is null) { errors.Add("A system entry is empty."); continue; }
            if (string.IsNullOrWhiteSpace(x.Id)) errors.Add("A system has no id.");
            else if (!ids.Add(x.Id)) errors.Add($"The id '{x.Id}' is used more than once.");
            if (string.IsNullOrWhiteSpace(x.Name)) errors.Add($"The system '{x.Id}' has no name.");
            if (!Enum.IsDefined(x.Material) || !Enum.IsDefined(x.Use)) errors.Add($"'{x.Id}': unknown material or use.");
            foreach (var role in new[] { ProfileType.Frame, ProfileType.Mullion, ProfileType.Transom, ProfileType.Sash, ProfileType.MeshSash })
            {
                if (x.ProfileIdFor(role) is not { } id) continue;
                string what = role == ProfileType.MeshSash ? "mesh sash" : role.ToString().ToLowerInvariant();
                if (!profiles.TryGetValue(id, out var profile))
                    errors.Add($"The {what} profile '{id}' of system '{x.Name}' is not in the library.");
                else if (!profile.Supports(role))
                    errors.Add($"'{profile.Name}' cannot be the {what} profile of system '{x.Name}'.");
            }
            if (x.GlassId is { } glassId && !glassIds.Contains(glassId))
                errors.Add($"The glass '{glassId}' of system '{x.Name}' is not in the library.");
            if (!double.IsFinite(x.GlassMinThicknessMm) || !double.IsFinite(x.GlassMaxThicknessMm) || x.GlassMinThicknessMm < 0
                || x.GlassMaxThicknessMm < 0 || (x.GlassMaxThicknessMm > 0 && x.GlassMinThicknessMm > x.GlassMaxThicknessMm))
                errors.Add($"'{x.Name}': the glass thickness range is not valid.");
            foreach (var item in x.Items ?? Array.Empty<KitItem>())
            {
                if (item is null || !(profiles.ContainsKey(item.ItemId) || materialIds.Contains(item.ItemId)))
                    errors.Add($"System '{x.Name}' has an item '{item?.ItemId}' that is not a profile or material in the library.");
                else if (!Enum.IsDefined(item.Use) || !double.IsFinite(item.Quantity) || item.Quantity < 0
                         || !double.IsFinite(item.LengthMm) || item.LengthMm < 0 || item.LengthMm > Units.MaxDimensionMm)
                    errors.Add($"System '{x.Name}': the quantity or length of '{item.ItemId}' is not valid.");
            }
        }

        foreach (var b in Bundles)
        {
            if (b is null) { errors.Add("A bundle entry is empty."); continue; }
            if (string.IsNullOrWhiteSpace(b.Id)) errors.Add("A bundle has no id.");
            else if (!ids.Add(b.Id)) errors.Add($"The id '{b.Id}' is used more than once.");
            if (string.IsNullOrWhiteSpace(b.Name)) errors.Add($"The bundle '{b.Id}' has no name.");
            if (b.SystemId is { } systemId && !systemIds.Contains(systemId))
                errors.Add($"The bundle '{b.Name}' is for system '{systemId}', which is not in the library.");
            if (b.ProfileId is { } profileId && !profiles.ContainsKey(profileId))
                errors.Add($"The bundle '{b.Name}' is for profile '{profileId}', which is not in the library.");
            if (b.Parts.Count == 0) errors.Add($"The bundle '{b.Name}' has no parts.");
            foreach (var part in b.Parts)
            {
                if (part is null) { errors.Add($"The bundle '{b.Name}' has an empty part."); continue; }
                if (!profiles.ContainsKey(part.ItemId ?? "") && !materialIds.Contains(part.ItemId ?? ""))
                    errors.Add($"The bundle '{b.Name}' uses '{part.ItemId}', which is not a profile or material in the library.");
                if (!double.IsFinite(part.Quantity) || part.Quantity < 0 || !double.IsFinite(part.CutDeductionMm))
                    errors.Add($"The bundle '{b.Name}': the quantity of '{part.ItemId}' must be zero or more.");
                if (!Enum.IsDefined(part.Basis) || !Enum.IsDefined(part.Measure) || !Enum.IsDefined(part.Side))
                    errors.Add($"The bundle '{b.Name}': '{part.ItemId}' has an unknown basis, measure or side.");
                if (part.Basis == PartBasis.BySize)
                {
                    if (part.Steps.Count == 0)
                        errors.Add($"The bundle '{b.Name}': '{part.ItemId}' is counted by size but has no sizes.");
                    for (int i = 0; i < part.Steps.Count; i++)
                    {
                        var step = part.Steps[i];
                        if (step is null || !double.IsFinite(step.UpToMm) || step.UpToMm <= 0 || !double.IsFinite(step.Quantity) || step.Quantity < 0
                            || (i > 0 && part.Steps[i - 1] is { } previous && step.UpToMm <= previous.UpToMm))
                        {
                            errors.Add($"The bundle '{b.Name}': the sizes of '{part.ItemId}' must grow and quantities must be zero or more.");
                            break;
                        }
                    }
                }
            }
        }
    }
}
