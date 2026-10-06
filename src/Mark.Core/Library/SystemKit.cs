using Mark.Core.Models;

namespace Mark.Core.Library;

/// <summary>Where an item ticked in a system goes, and so how many a window gets.</summary>
public enum KitUse
{
    /// <summary>On every bar of the frame (a profile is cut to the bar), e.g. a cover cap or a gasket clip.</summary>
    EachFrameBar,

    /// <summary>On the bottom bar of the frame, e.g. a guide rail or a low threshold.</summary>
    BottomFrameBar,

    /// <summary>On the top and the bottom bar of the frame, e.g. a guide rail on both tracks.</summary>
    TopAndBottomFrameBars,

    /// <summary>On every bar of every sash, e.g. a glazing bead.</summary>
    EachSashBar,

    /// <summary>On every bar of every mesh shutter.</summary>
    EachMeshBar,

    /// <summary>Per sliding sash (a profile is cut to the sash height), e.g. an interlock, rollers, a sliding lock.</summary>
    EachSlidingSash,

    /// <summary>Per opening sash of any kind (a profile is cut to the sash height), e.g. a handle or a lock.</summary>
    EachOpenableSash,

    /// <summary>Four per sash (its corners), e.g. corner cleats.</summary>
    SashCorners,

    /// <summary>Per hinged sash by its height: 2 up to 1200 mm, 3 above (× the quantity).</summary>
    HingesBySize,

    /// <summary>Per metre of frame bar, e.g. a frame gasket.</summary>
    PerMetreOfFrame,

    /// <summary>Per metre of sash bar, e.g. a sash gasket or brush seal.</summary>
    PerMetreOfSash,

    /// <summary>Once per window, e.g. a packing set or drainage caps.</summary>
    EachWindow
}

/// <summary>An item ticked in a system: it goes with every design in the system, as <see cref="Use"/> says.</summary>
public sealed record KitItem
{
    /// <summary>A profile or material (hardware, gasket, accessory) id.</summary>
    public string ItemId { get; init; } = "";

    public KitUse Use { get; init; } = KitUse.EachWindow;

    /// <summary>How many each time (or metres per metre).</summary>
    public double Quantity { get; init; } = 1;

    /// <summary>Profiles: cut every piece to this length (e.g. a 50 mm cleat); 0 = to the bar or sash it goes with.</summary>
    public double LengthMm { get; init; }
}

/// <summary>
/// The items ticked in a system (<see cref="ProductSystem.Items"/>), as the calculation uses them: each becomes a part of
/// a bundle for the system (on the system's frame, sash or mesh profile, or on its openings), so nobody has to build the
/// bundles by hand. The use of an item is guessed from its role or kind when it is ticked (<see cref="DefaultUse"/>).
/// </summary>
public static class SystemKit
{
    /// <summary>Bundles made from a system's items start with this id.</summary>
    public const string BundlePrefix = "kit:";

    public static bool IsKitBundle(Bundle bundle) => bundle.Id.StartsWith(BundlePrefix, StringComparison.Ordinal);

    private static readonly OpeningType[] Sliding = Enum.GetValues<OpeningType>().Where(t => t.IsSliding()).ToArray();
    private static readonly OpeningType[] Hinged = Enum.GetValues<OpeningType>().Where(t => t.IsHinged()).ToArray();

    /// <summary>The bundles a system's ticked items stand for (none without items).</summary>
    public static IReadOnlyList<Bundle> BundlesOf(ProductSystem? system)
    {
        if (system is null || system.Items.Count == 0) return Array.Empty<Bundle>();
        var bundles = new List<Bundle>();
        int n = 0;
        foreach (var item in system.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemId) || item.Quantity <= 0) continue;
            string id = $"{BundlePrefix}{system.Id}:{n++}";

            Bundle OnProfile(string? profileId, BarSide side, PartBasis basis = PartBasis.PerPiece)
                => new()
                {
                    Id = id, Name = system.Name, SystemId = system.Id, ProfileId = profileId,
                    Parts = new[] { Part(item, basis, SizeMeasure.Length, side) }
                };

            Bundle OnOpenings(IReadOnlyList<OpeningType> types, BundlePart part)
                => new() { Id = id, Name = system.Name, SystemId = system.Id, OpeningTypes = types, Parts = new[] { part } };

            Bundle? bundle = item.Use switch
            {
                KitUse.EachFrameBar => OnProfile(system.FrameProfileId, BarSide.Any),
                KitUse.BottomFrameBar => OnProfile(system.FrameProfileId, BarSide.Bottom),
                KitUse.TopAndBottomFrameBars => OnProfile(system.FrameProfileId, BarSide.Horizontal),
                KitUse.EachSashBar => OnProfile(system.SashProfileId, BarSide.Any),
                KitUse.EachMeshBar => OnProfile(system.MeshSashProfileId, BarSide.Any),
                KitUse.PerMetreOfFrame => OnProfile(system.FrameProfileId, BarSide.Any, PartBasis.PerMetre),
                KitUse.PerMetreOfSash => OnProfile(system.SashProfileId, BarSide.Any, PartBasis.PerMetre),
                KitUse.EachWindow => OnProfile(system.FrameProfileId, BarSide.Top),
                KitUse.EachSlidingSash => OnOpenings(Sliding, Part(item, PartBasis.PerPiece, SizeMeasure.Height)),
                KitUse.EachOpenableSash => OnOpenings(Array.Empty<OpeningType>(), Part(item, PartBasis.PerPiece, SizeMeasure.Height)),
                KitUse.SashCorners => OnOpenings(Array.Empty<OpeningType>(), Part(item, PartBasis.PerPiece, SizeMeasure.Height) with { Quantity = 4 * item.Quantity }),
                KitUse.HingesBySize => OnOpenings(Hinged, Part(item, PartBasis.BySize, SizeMeasure.Height) with
                {
                    Steps = new[]
                    {
                        new SizeStep { UpToMm = 1200, Quantity = 2 * item.Quantity },
                        new SizeStep { UpToMm = 100_000, Quantity = 3 * item.Quantity }
                    }
                }),
                _ => null
            };
            // A bundle on a profile the system does not set (e.g. no sash profile) cannot apply: leave it out.
            if (bundle is null || (bundle.IsOpeningSet == false && bundle.ProfileId is null)) continue;
            bundles.Add(bundle);
        }
        return bundles;
    }

    private static BundlePart Part(KitItem item, PartBasis basis, SizeMeasure measure, BarSide side = BarSide.Any) => new()
    {
        ItemId = item.ItemId, Basis = basis, Quantity = item.Quantity, Measure = measure, Side = side, FixedLengthMm = item.LengthMm
    };

    /// <summary>
    /// Where an item usually goes, from its role (profiles) or its kind and name (materials): an interlock per sliding
    /// sash, a guide rail on the bottom frame bar, rollers two per sliding sash, hinges by size, a gasket per metre…
    /// </summary>
    public static KitItem DefaultFor(string itemId, IProductLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var (use, quantity) = DefaultUse(itemId, library);
        return new KitItem { ItemId = itemId, Use = use, Quantity = quantity };
    }

    public static (KitUse Use, double Quantity) DefaultUse(string itemId, IProductLibrary library)
    {
        if (library.FindProfile(itemId) is { } profile)
        {
            string n = profile.Name.ToLowerInvariant();
            if (n.Contains("cleat") || n.Contains("corner")) return (KitUse.SashCorners, 1);
            if (profile.Supports(ProfileType.Interlock)) return (KitUse.EachSlidingSash, 1);
            if (profile.Supports(ProfileType.Track)) return (KitUse.BottomFrameBar, 1);
            if (profile.Supports(ProfileType.GlazingBead)) return (KitUse.EachSashBar, 1);
            if (profile.Supports(ProfileType.MeshSash)) return (KitUse.EachMeshBar, 1);
            if (n.Contains("threshold")) return (KitUse.BottomFrameBar, 1);
            return (KitUse.EachFrameBar, 1);
        }
        if (library.FindMaterial(itemId) is { } material)
        {
            string n = (material.Properties.TryGetValue("Type", out var type) ? type + " " : "") + material.Name;
            n = n.ToLowerInvariant();
            if (material.Unit == MaterialUnit.Metre)
                return (material.Category == MaterialCategory.Gasket && n.Contains("frame") ? KitUse.PerMetreOfFrame : KitUse.PerMetreOfSash, 1);
            if (n.Contains("roller") || n.Contains("wheel")) return (KitUse.EachSlidingSash, 2);
            if (n.Contains("hinge")) return (KitUse.HingesBySize, 1);
            if (n.Contains("friction") || n.Contains("stay")) return (KitUse.EachOpenableSash, 2);
            if (n.Contains("cleat") || n.Contains("corner")) return (KitUse.SashCorners, 1);
            if (n.Contains("handle") || n.Contains("lock") || n.Contains("espag") || n.Contains("latch") || n.Contains("cylinder"))
                return (KitUse.EachOpenableSash, 1);
            if (material.Category == MaterialCategory.Hardware) return (KitUse.EachOpenableSash, 1);
            return (KitUse.EachWindow, 1);
        }
        return (KitUse.EachWindow, 1);
    }

    /// <summary>
    /// The items that belong with a frame profile: the same series, or marked as used with the system, and fitting its
    /// style (a sliding frame: items for sliding). Used to fill a system in when its frame is chosen.
    /// </summary>
    public static IReadOnlyList<string> ItemsWithFrame(ProfileDefinition frame, string? systemId, IProductLibrary library)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(library);
        string series = (frame.Series ?? "").Trim();
        bool sliding = frame.Supports(ProfileType.Track) || frame.Name.Contains("track", StringComparison.OrdinalIgnoreCase)
                       || frame.Name.Contains("sliding", StringComparison.OrdinalIgnoreCase);
        bool Belongs(string? itemSeries, UsedWith? usedWith)
            => (series.Length > 0 && string.Equals((itemSeries ?? "").Trim(), series, StringComparison.OrdinalIgnoreCase))
               || (systemId is not null && usedWith?.SystemIds.Contains(systemId) == true);
        bool Fits(UsedWith? usedWith) => usedWith is null || (sliding ? usedWith.ForSliding : usedWith.ForCasement);

        var ids = new List<string>();
        foreach (var p in library.Profiles.Where(p => p.IsActive && p.Id != frame.Id && Belongs(p.Series, p.UsedWith) && Fits(p.UsedWith)))
            ids.Add(p.Id);
        foreach (var m in library.Materials.Where(m => m.IsActive && Belongs(m.Properties.TryGetValue("Series", out var s) ? s : null, m.UsedWith) && Fits(m.UsedWith)))
            ids.Add(m.Id);
        return ids;
    }
}
