namespace Mark.Core.Library;

/// <summary>
/// Works out the part of the owner's master catalogue one company gets. From a <see cref="CatalogueSelection"/>:
/// <list type="bullet">
///   <item>the chosen <b>systems</b> whose material the company is licensed for, with their bundles, their default
///         profiles and glass, and every item marked as used with them;</item>
///   <item>the chosen single <b>items</b>;</item>
///   <item>everything those need to be complete: bundle parts, reinforcement sections and the materials profiles and glass
///         use. Bundles without a system come along when their profile (or, for opening sets, all their parts) is there.</item>
/// </list>
/// The result is a valid library on its own, in the master's order, with the master's currency and the defaults that
/// are still in it. "Used with" marks of systems the company does not get are dropped.
/// </summary>
public static class CatalogueSelector
{
    public static ProductLibrary Select(IProductLibrary master, CatalogueSelection selection, Func<SystemMaterial, bool>? materialAllowed = null)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(selection);
        var allowed = materialAllowed ?? (_ => true);

        var chosenSystems = selection.SystemIds.ToHashSet(StringComparer.Ordinal);
        var systems = master.Systems.Where(x => chosenSystems.Contains(x.Id) && allowed(x.Material)).ToList();
        var systemIds = systems.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        var items = selection.ItemIds.ToHashSet(StringComparer.Ordinal);
        bool UsedWithKept(UsedWith? usedWith) => usedWith?.SystemIds.Any(systemIds.Contains) ?? false;
        items.UnionWith(master.Profiles.Where(p => UsedWithKept(p.UsedWith)).Select(p => p.Id));
        items.UnionWith(master.Glass.Where(g => UsedWithKept(g.UsedWith)).Select(g => g.Id));
        items.UnionWith(master.Materials.Where(m => UsedWithKept(m.UsedWith)).Select(m => m.Id));
        foreach (var system in systems)
            items.UnionWith(new[] { system.FrameProfileId, system.MullionProfileId, system.TransomProfileId, system.SashProfileId,
                system.MeshSashProfileId, system.GlassId }.OfType<string>());

        var bundles = master.Bundles.Where(b => b.SystemId is { } id && systemIds.Contains(id)).ToList();
        foreach (var bundle in bundles)
            AddBundleItems(bundle, items);

        // Bundles without a system: with their profile, or (opening sets) when every part is already there.
        foreach (var bundle in master.Bundles.Where(b => b.SystemId is null))
        {
            bool include = bundle.ProfileId is { } profileId ? items.Contains(profileId) : bundle.Parts.All(p => items.Contains(p.ItemId));
            if (!include) continue;
            bundles.Add(bundle);
            AddBundleItems(bundle, items);
        }

        // Complete the closure: reinforcement sections and the materials profiles and glass use.
        bool grew;
        do
        {
            int before = items.Count;
            foreach (var profile in master.Profiles.Where(p => items.Contains(p.Id)))
            {
                if (profile.Reinforcement is { } rule) items.Add(rule.ProfileId);
                items.UnionWith(profile.Materials.Select(u => u.MaterialId));
            }
            foreach (var glass in master.Glass.Where(g => items.Contains(g.Id)))
                items.UnionWith(glass.Materials.Select(u => u.MaterialId));
            grew = items.Count > before;
        } while (grew);

        UsedWith? Keep(UsedWith? usedWith) => usedWith is null ? null
            : usedWith with { SystemIds = usedWith.SystemIds.Where(systemIds.Contains).ToList() };

        var profiles = master.Profiles.Where(p => items.Contains(p.Id)).Select(p => p with { UsedWith = Keep(p.UsedWith) }).ToList();
        var glassList = master.Glass.Where(g => items.Contains(g.Id)).Select(g => g with { UsedWith = Keep(g.UsedWith) }).ToList();
        var materials = master.Materials.Where(m => items.Contains(m.Id)).Select(m => m with { UsedWith = Keep(m.UsedWith) }).ToList();
        var bundleIds = bundles.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);

        string? KeepId(string? id) => id is not null && items.Contains(id) ? id : null;
        var d = master.Defaults;
        var defaults = new LibraryDefaults
        {
            FrameProfileId = KeepId(d.FrameProfileId),
            MullionProfileId = KeepId(d.MullionProfileId),
            TransomProfileId = KeepId(d.TransomProfileId),
            GlassId = KeepId(d.GlassId),
            SystemId = d.SystemId is { } systemId && systemIds.Contains(systemId) ? systemId : systems.FirstOrDefault()?.Id
        };

        return new ProductLibrary(profiles, glassList, materials, defaults, master.Currency,
            systems, master.Bundles.Where(b => bundleIds.Contains(b.Id)));
    }

    private static void AddBundleItems(Bundle bundle, HashSet<string> items)
    {
        if (bundle.ProfileId is { } profileId) items.Add(profileId);
        items.UnionWith(bundle.Parts.Select(p => p.ItemId));
    }
}
