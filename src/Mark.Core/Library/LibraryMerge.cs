namespace Mark.Core.Library;

/// <summary>What adding a library file to a library did.</summary>
/// <param name="Added">Ids of the profiles, glass, hardware, systems and bundles added.</param>
/// <param name="Skipped">Ids already used in the library (kept as they were, never overwritten).</param>
public sealed record LibraryMergeResult(ProductLibrary Library, IReadOnlyList<string> Added, IReadOnlyList<string> Skipped)
{
    /// <summary>"45 profiles · 2 systems" of what was added.</summary>
    public string AddedText(IProductLibrary source)
    {
        var added = Added.ToHashSet(StringComparer.Ordinal);
        var parts = new List<string>();
        void Add(int n, string one, string many) { if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}"); }
        Add(source.Systems.Count(x => added.Contains(x.Id)), "system", "systems");
        Add(source.Bundles.Count(b => added.Contains(b.Id)), "bundle", "bundles");
        Add(source.Profiles.Count(p => added.Contains(p.Id)), "profile", "profiles");
        Add(source.Glass.Count(g => added.Contains(g.Id)), "glass", "glass");
        Add(source.Materials.Count(m => added.Contains(m.Id)), "hardware or accessory", "hardware and accessories");
        return parts.Count == 0 ? "nothing" : string.Join(" · ", parts);
    }
}

/// <summary>Adds the items of one library to another, as importing a library file into a catalogue does.</summary>
public static class LibraryMerge
{
    /// <summary>
    /// <paramref name="target"/> with every profile, glass, hardware item, system and bundle of <paramref name="source"/>
    /// whose id it does not use yet. Existing ids are skipped, never overwritten. Currency and defaults stay the
    /// target's, unless the target is empty.
    /// </summary>
    /// <exception cref="InvalidOperationException">Together they are not a valid library.</exception>
    public static LibraryMergeResult Add(IProductLibrary target, IProductLibrary source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        var used = target.Profiles.Select(p => p.Id).Concat(target.Glass.Select(g => g.Id)).Concat(target.Materials.Select(m => m.Id))
            .Concat(target.Systems.Select(x => x.Id)).Concat(target.Bundles.Select(b => b.Id)).ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        var skipped = new List<string>();

        List<T> New<T>(IEnumerable<T> items, Func<T, string> id)
        {
            var result = new List<T>();
            foreach (var item in items)
            {
                if (used.Add(id(item)))
                {
                    result.Add(item);
                    added.Add(id(item));
                }
                else
                    skipped.Add(id(item));
            }
            return result;
        }

        bool empty = target.Profiles.Count + target.Glass.Count + target.Materials.Count + target.Systems.Count == 0;
        var library = new ProductLibrary(
            target.Profiles.Concat(New(source.Profiles, p => p.Id)),
            target.Glass.Concat(New(source.Glass, g => g.Id)),
            target.Materials.Concat(New(source.Materials, m => m.Id)),
            empty ? source.Defaults : target.Defaults,
            empty ? source.Currency : target.Currency,
            target.Systems.Concat(New(source.Systems, x => x.Id)),
            target.Bundles.Concat(New(source.Bundles, b => b.Id)));
        return new LibraryMergeResult(library, added, skipped);
    }
}
