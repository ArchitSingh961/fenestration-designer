using Mark.Core.Models;

namespace Mark.Core.Library;

/// <summary>
/// A search over library items. Every whitespace-separated word in <see cref="Text"/> must occur
/// (case-insensitively) in the item's id, name, code, manufacturer or series/category. The optional filters
/// below narrow the result further; retired (inactive) items are skipped unless <see cref="IncludeInactive"/>.
/// </summary>
/// <param name="Text">Free text; null or blank matches everything.</param>
/// <param name="Role">Profiles only: keep sections usable for this role.</param>
/// <param name="Limit">Maximum number of results (null = all).</param>
public sealed record LibraryQuery(string? Text = null, ProfileType? Role = null, int? Limit = null)
{
    /// <summary>Keep items of this manufacturer only (exact match, ignoring case).</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Keep profiles of this series/system, or glass of this category (exact match, ignoring case).</summary>
    public string? Group { get; init; }

    /// <summary>Materials only: keep this category (hardware, gasket, accessory, consumable).</summary>
    public MaterialCategory? MaterialCategory { get; init; }

    /// <summary>Also return retired items (e.g. in the library manager). Pickers leave this false.</summary>
    public bool IncludeInactive { get; init; }
}

/// <summary>
/// Read-only access to the product library (profiles, glass, other materials). The designer and the
/// calculation engine reach products only through this, by stable Id. Implementations are immutable and
/// already validated, so a successful lookup is always a usable definition.
/// </summary>
public interface IProductLibrary
{
    /// <summary>Currency of every price in the library, e.g. "INR".</summary>
    string Currency { get; }

    LibraryDefaults Defaults { get; }

    /// <summary>All profiles, in library order.</summary>
    IReadOnlyList<ProfileDefinition> Profiles { get; }

    /// <summary>All glass types, in library order.</summary>
    IReadOnlyList<GlassDefinition> Glass { get; }

    /// <summary>All other materials, in library order.</summary>
    IReadOnlyList<MaterialDefinition> Materials { get; }

    /// <summary>Product systems (e.g. "62mm Casement – uPVC"), in library order.</summary>
    IReadOnlyList<ProductSystem> Systems { get; }

    /// <summary>Bundles: parts that go together with a profile, or with an opening (hardware sets).</summary>
    IReadOnlyList<Bundle> Bundles { get; }

    ProductSystem? FindSystem(string? id);

    Bundle? FindBundle(string? id);

    /// <summary>The system new frames are drawn in, or null.</summary>
    ProductSystem? DefaultSystem { get; }

    ProfileDefinition? FindProfile(string? id);

    GlassDefinition? FindGlass(string? id);

    MaterialDefinition? FindMaterial(string? id);

    /// <summary>The library's default profile for a role, or null if it has none.</summary>
    ProfileDefinition? DefaultProfileFor(ProfileType role);

    /// <summary>The library's default glass, or null if it has none.</summary>
    GlassDefinition? DefaultGlass { get; }

    /// <summary>Profiles matching <paramref name="query"/>, in library order.</summary>
    IReadOnlyList<ProfileDefinition> SearchProfiles(LibraryQuery query);

    /// <summary>Glass types matching <paramref name="query"/>, in library order.</summary>
    IReadOnlyList<GlassDefinition> SearchGlass(LibraryQuery query);

    /// <summary>Materials (hardware, gaskets, accessories, consumables) matching <paramref name="query"/>, in library order.</summary>
    IReadOnlyList<MaterialDefinition> SearchMaterials(LibraryQuery query);
}
