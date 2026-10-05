using Mark.Core.Models;

namespace Mark.Core.Library;

/// <summary>
/// Which product system a design goes in, decided by the design itself: a sliding design goes in a sliding system, a
/// casement design in a casement system, keeping the brand (the maker of the system's profiles) and material it was in.
/// </summary>
public static class SystemMatch
{
    /// <summary>Systems whose profiles name no maker are of this brand.</summary>
    public const string OtherBrand = "Other";

    /// <summary>The brand of a system: the maker of its frame profile (or of its first profile), else "Other".</summary>
    public static string BrandOf(ProductSystem system, IProductLibrary library)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(library);
        foreach (string? id in new[] { system.FrameProfileId, system.SashProfileId, system.MullionProfileId })
            if (id is not null && library.FindProfile(id)?.Manufacturer is { Length: > 0 } maker)
                return maker.Trim();
        return OtherBrand;
    }

    /// <summary>
    /// True when the system makes this opening: fixed glass always; an opening sash when the system has a sash and its
    /// opening sets (hardware bundles) cover it, or, without opening sets, when it is a sliding system (its frame is a
    /// track) for a sliding opening and a casement system for a hinged one.
    /// </summary>
    public static bool Takes(ProductSystem system, IProductLibrary library, OpeningType? opening)
    {
        if (opening is null or OpeningType.Fixed) return true;
        if (system.SashProfileId is null) return false;
        var covered = library.Bundles.Where(b => b.IsActive && b.IsOpeningSet && (b.SystemId is null || b.SystemId == system.Id))
            .SelectMany(b => b.OpeningTypes).ToHashSet();
        if (covered.Count > 0) return covered.Contains(opening.Value);
        bool sliding = library.FindProfile(system.FrameProfileId)?.Roles.Contains(ProfileType.Track) == true;
        return sliding ? opening.Value.IsSliding() : opening.Value.IsHinged();
    }

    /// <summary>True when the system makes every one of the openings.</summary>
    public static bool TakesAll(ProductSystem system, IProductLibrary library, IEnumerable<OpeningType?> openings)
        => openings.All(o => Takes(system, library, o));

    /// <summary>
    /// The system for a design with these openings: <paramref name="preferredId"/> when it makes them all; otherwise an
    /// active system that does, of the same brand and material first (then the same brand, then any). Only systems of
    /// <paramref name="brand"/> when given. Returns <paramref name="preferredId"/> when no system makes them all.
    /// </summary>
    public static string? For(IProductLibrary library, IEnumerable<OpeningType?> openings, string? preferredId, string? brand = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        var wanted = openings.ToList();
        var preferred = library.FindSystem(preferredId);
        var active = library.Systems.Where(s => s.IsActive && (brand is null || BrandOf(s, library) == brand)).ToList();
        if (preferred is not null && active.Any(s => s.Id == preferred.Id) && TakesAll(preferred, library, wanted))
            return preferred.Id;

        string? preferredBrand = brand ?? (preferred is null ? null : BrandOf(preferred, library));
        var fitting = active.Where(s => TakesAll(s, library, wanted))
            .OrderBy(s => preferredBrand is not null && BrandOf(s, library) == preferredBrand ? 0 : 1)
            .ThenBy(s => preferred is not null && s.Material == preferred.Material ? 0 : 1)
            .ThenBy(s => s.Id == library.Defaults.SystemId ? 0 : 1)
            .ToList();
        return fitting.FirstOrDefault()?.Id ?? (brand is not null ? active.FirstOrDefault()?.Id ?? preferredId : preferredId);
    }
}
