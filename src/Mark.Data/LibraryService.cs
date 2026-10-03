using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Data;

/// <summary>What a library import did.</summary>
/// <param name="Added">Ids of the products, systems and bundles added, in import order.</param>
/// <param name="Skipped">Ids that already existed and were left unchanged (an import never overwrites).</param>
/// <param name="SettingsImported">True when currency and defaults were taken from the import (only into an empty library).</param>
public sealed record LibraryImportResult(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped, bool SettingsImported);

/// <summary>What applying the owner's catalogue did.</summary>
/// <param name="Added">New products, systems and bundles.</param>
/// <param name="Updated">Existing ones changed by the catalogue (prices kept).</param>
/// <param name="Retired">Ones no longer in the catalogue: retired, so saved quotes keep their copy.</param>
public sealed record CatalogueApplyResult(IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<string> Retired)
{
    public bool Changed => Added.Count + Updated.Count + Retired.Count > 0;
}

/// <summary>
/// The application service for the product library. It keeps the current library in memory as one immutable,
/// validated <see cref="ProductLibrary"/> snapshot (<see cref="Current"/>), so pickers, rendering and calculations never
/// touch the database. Every change is checked by building the whole candidate library (the same validation that
/// guards a library file: unique ids across all kinds, prices, roles, usages, systems, bundles, defaults), then written
/// in one transaction; the new snapshot is reloaded and <see cref="Changed"/> is raised. A rejected change writes nothing.
///
/// Ids are immutable: <c>Update</c> keeps the id, and a new id is an <c>Add</c>. Anything that is still referenced
/// (by a saved project, the open project, another product, a system, a bundle or the library defaults) is never
/// deleted; it can be retired instead (<see cref="SetActive"/>), which hides it from pickers but keeps every reference
/// resolving.
/// </summary>
public sealed class LibraryService
{
    private readonly ILibraryRepository _repository;
    private readonly IProjectRepository? _projects;

    public LibraryService(ILibraryRepository repository, IProjectRepository? projects = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _projects = projects;
        Current = repository.Load();
    }

    /// <summary>The current library snapshot.</summary>
    public ProductLibrary Current { get; private set; }

    /// <summary>Raised after a committed change, with the new snapshot.</summary>
    public event Action<ProductLibrary>? Changed;

    // ── Add / update ────────────────────────────────────────────────

    /// <exception cref="LibraryOperationException">The id is already used.</exception>
    /// <exception cref="LibraryValidationException">The product (or the library with it) is invalid.</exception>
    public void Add(ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        EnsureNewId(profile.Id);
        Commit(Candidate(profiles: Current.Profiles.Append(profile)), () => _repository.SaveProfile(profile));
    }

    public void Add(GlassDefinition glass)
    {
        ArgumentNullException.ThrowIfNull(glass);
        EnsureNewId(glass.Id);
        Commit(Candidate(glass: Current.Glass.Append(glass)), () => _repository.SaveGlass(glass));
    }

    public void Add(MaterialDefinition material)
    {
        ArgumentNullException.ThrowIfNull(material);
        EnsureNewId(material.Id);
        Commit(Candidate(materials: Current.Materials.Append(material)), () => _repository.SaveMaterial(material));
    }

    public void Add(ProductSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        EnsureNewId(system.Id);
        Commit(Candidate(systems: Current.Systems.Append(system)), () => _repository.SaveSystem(system));
    }

    public void Add(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        EnsureNewId(bundle.Id);
        Commit(Candidate(bundles: Current.Bundles.Append(bundle)), () => _repository.SaveBundle(bundle));
    }

    /// <summary>Replaces the product with the same id (its place in the library is kept).</summary>
    /// <exception cref="LibraryOperationException">No profile has this id.</exception>
    public void Update(ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        EnsureExists(LibraryItemKind.Profile, profile.Id);
        Commit(Candidate(profiles: Current.Profiles.Select(p => p.Id == profile.Id ? profile : p)), () => _repository.SaveProfile(profile));
    }

    public void Update(GlassDefinition glass)
    {
        ArgumentNullException.ThrowIfNull(glass);
        EnsureExists(LibraryItemKind.Glass, glass.Id);
        Commit(Candidate(glass: Current.Glass.Select(g => g.Id == glass.Id ? glass : g)), () => _repository.SaveGlass(glass));
    }

    public void Update(MaterialDefinition material)
    {
        ArgumentNullException.ThrowIfNull(material);
        EnsureExists(LibraryItemKind.Material, material.Id);
        Commit(Candidate(materials: Current.Materials.Select(m => m.Id == material.Id ? material : m)),
            () => _repository.SaveMaterial(material));
    }

    public void Update(ProductSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        EnsureExists(LibraryItemKind.System, system.Id);
        Commit(Candidate(systems: Current.Systems.Select(x => x.Id == system.Id ? system : x)), () => _repository.SaveSystem(system));
    }

    public void Update(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        EnsureExists(LibraryItemKind.Bundle, bundle.Id);
        Commit(Candidate(bundles: Current.Bundles.Select(b => b.Id == bundle.Id ? bundle : b)), () => _repository.SaveBundle(bundle));
    }

    /// <summary>Retires (false) or reinstates (true) an item. Retired items still resolve, so references stay valid; a
    /// retired bundle no longer adds its parts.</summary>
    public void SetActive(LibraryItemKind kind, string id, bool isActive)
    {
        switch (kind)
        {
            case LibraryItemKind.Profile:
                Update(Current.FindProfile(id) is { } p ? p with { IsActive = isActive } : throw NotFound(kind, id));
                break;
            case LibraryItemKind.Glass:
                Update(Current.FindGlass(id) is { } g ? g with { IsActive = isActive } : throw NotFound(kind, id));
                break;
            case LibraryItemKind.System:
                Update(Current.FindSystem(id) is { } x ? x with { IsActive = isActive } : throw NotFound(kind, id));
                break;
            case LibraryItemKind.Bundle:
                Update(Current.FindBundle(id) is { } b ? b with { IsActive = isActive } : throw NotFound(kind, id));
                break;
            default:
                Update(Current.FindMaterial(id) is { } m ? m with { IsActive = isActive } : throw NotFound(kind, id));
                break;
        }
    }

    /// <summary>Changes the currency and the default products (each default must exist and fit its role).</summary>
    public void UpdateSettings(string currency, LibraryDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        Commit(Candidate(defaults: defaults, currency: currency ?? ""), () => _repository.SaveSettings(currency ?? "", defaults));
    }

    // ── Delete ──────────────────────────────────────────────────────

    /// <summary>
    /// Why the item cannot be deleted: every saved project, open-project object, product usage, system, bundle or library
    /// default that still references it. Empty when deleting is safe.
    /// </summary>
    public IReadOnlyList<string> FindBlockers(LibraryItemKind kind, string id, Project? openProject = null)
    {
        var blockers = new List<string>();
        var defaults = Current.Defaults;
        switch (kind)
        {
            case LibraryItemKind.Profile:
                foreach (var (role, defaultId) in new[] { ("frame", defaults.FrameProfileId), ("mullion", defaults.MullionProfileId),
                             ("transom", defaults.TransomProfileId) })
                    if (defaultId == id) blockers.Add($"It is the library's default {role} profile.");
                if (openProject?.Frames.SelectMany(f => f.Profiles).Count(p => p.ProfileDefinitionId == id) is > 0 and var n)
                    blockers.Add($"The open project uses it for {n} member(s).");
                var reinforced = Current.Profiles.Where(p => p.Reinforcement?.ProfileId == id).Select(p => p.Name).ToList();
                if (reinforced.Count > 0) blockers.Add($"It reinforces {string.Join(", ", reinforced)}.");
                break;
            case LibraryItemKind.Glass:
                if (defaults.GlassId == id) blockers.Add("It is the library's default glass.");
                if (openProject?.Frames.SelectMany(f => f.GlassPanels).Count(g => g.GlassDefinitionId == id) is > 0 and var panes)
                    blockers.Add($"The open project uses it for {panes} glass panel(s).");
                break;
            case LibraryItemKind.Material:
                var users = Current.Profiles.Where(p => p.Materials.Any(u => u.MaterialId == id)).Select(p => p.Name)
                    .Concat(Current.Glass.Where(g => g.Materials.Any(u => u.MaterialId == id)).Select(g => g.Name)).ToList();
                if (users.Count > 0) blockers.Add($"It is used by {string.Join(", ", users)}.");
                break;
            case LibraryItemKind.System:
                if (defaults.SystemId == id) blockers.Add("It is the library's default system.");
                var systemBundles = Current.Bundles.Where(b => b.SystemId == id).Select(b => b.Name).ToList();
                if (systemBundles.Count > 0) blockers.Add($"Bundles belong to it: {string.Join(", ", systemBundles)}.");
                var marked = Current.Profiles.Where(p => p.UsedWith?.SystemIds.Contains(id) ?? false).Select(p => p.Name)
                    .Concat(Current.Glass.Where(g => g.UsedWith?.SystemIds.Contains(id) ?? false).Select(g => g.Name))
                    .Concat(Current.Materials.Where(m => m.UsedWith?.SystemIds.Contains(id) ?? false).Select(m => m.Name)).ToList();
                if (marked.Count > 0) blockers.Add($"Items are marked as used with it: {string.Join(", ", marked.Take(5))}{(marked.Count > 5 ? ", …" : "")}.");
                if (openProject?.Frames.Count(f => f.SystemId == id) is > 0 and var frames)
                    blockers.Add($"The open project uses it for {frames} design(s).");
                break;
        }

        if (kind is LibraryItemKind.Profile or LibraryItemKind.Glass or LibraryItemKind.Material)
        {
            var bundles = Current.Bundles.Where(b => b.ProfileId == id || b.Parts.Any(p => p.ItemId == id)).Select(b => b.Name).ToList();
            if (bundles.Count > 0) blockers.Add($"It is in the bundle(s) {string.Join(", ", bundles)}.");
            var systems = Current.Systems.Where(x => new[] { x.FrameProfileId, x.MullionProfileId, x.TransomProfileId, x.SashProfileId,
                x.MeshSashProfileId, x.GlassId }.Contains(id)).Select(x => x.Name).ToList();
            if (systems.Count > 0) blockers.Add($"It is a default of the system(s) {string.Join(", ", systems)}.");
        }

        if (kind is LibraryItemKind.Profile or LibraryItemKind.Glass or LibraryItemKind.System && _projects is not null)
        {
            // The saved version counts even for the open project: it may still use the item after unsaved edits.
            var saved = _projects!.FindUsing(kind, id).Select(p => p.Name).ToList();
            if (saved.Count > 0) blockers.Add($"Saved project(s) use it: {string.Join(", ", saved)}.");
        }
        return blockers;
    }

    /// <summary>Deletes an unreferenced item.</summary>
    /// <exception cref="LibraryOperationException">It is still referenced (see <see cref="LibraryOperationException.Reasons"/>);
    /// retire it instead.</exception>
    public void Delete(LibraryItemKind kind, string id, Project? openProject = null)
    {
        EnsureExists(kind, id);
        var blockers = FindBlockers(kind, id, openProject);
        if (blockers.Count > 0)
            throw new LibraryOperationException(
                $"'{id}' cannot be deleted because it is still used. Retire it instead to hide it from new designs.", blockers);

        var candidate = kind switch
        {
            LibraryItemKind.Profile => Candidate(profiles: Current.Profiles.Where(p => p.Id != id)),
            LibraryItemKind.Glass => Candidate(glass: Current.Glass.Where(g => g.Id != id)),
            LibraryItemKind.System => Candidate(systems: Current.Systems.Where(x => x.Id != id)),
            LibraryItemKind.Bundle => Candidate(bundles: Current.Bundles.Where(b => b.Id != id)),
            _ => Candidate(materials: Current.Materials.Where(m => m.Id != id))
        };
        Commit(candidate, () => _repository.Delete(kind, id));
    }

    // ── Import / export ─────────────────────────────────────────────

    /// <summary>
    /// Adds every product, system and bundle of <paramref name="source"/> whose id is not used yet, in one transaction;
    /// existing ids are skipped and reported, never overwritten. Currency and defaults are taken only when the library was
    /// empty. The merged library is validated first, so an import that would leave it inconsistent changes nothing.
    /// </summary>
    public LibraryImportResult Import(ProductLibrary source)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool wasEmpty = Current.Profiles.Count + Current.Glass.Count + Current.Materials.Count + Current.Systems.Count == 0;
        var used = AllIds();
        var materials = source.Materials.Where(m => !used.Contains(m.Id)).ToList();
        var profiles = source.Profiles.Where(p => !used.Contains(p.Id)).ToList();
        var glass = source.Glass.Where(g => !used.Contains(g.Id)).ToList();
        var systems = source.Systems.Where(x => !used.Contains(x.Id)).ToList();
        var bundles = source.Bundles.Where(b => !used.Contains(b.Id)).ToList();
        var skipped = IdsOf(source).Where(used.Contains).ToList();

        var settings = wasEmpty ? (source.Currency, source.Defaults) : ((string, LibraryDefaults)?)null;
        var candidate = Candidate(Current.Profiles.Concat(profiles), Current.Glass.Concat(glass), Current.Materials.Concat(materials),
            settings?.Item2, settings?.Item1, Current.Systems.Concat(systems), Current.Bundles.Concat(bundles));
        Commit(candidate, () => _repository.Insert(materials, profiles, glass, settings, systems, bundles));

        var added = materials.Select(m => m.Id).Concat(profiles.Select(p => p.Id)).Concat(glass.Select(g => g.Id))
            .Concat(systems.Select(x => x.Id)).Concat(bundles.Select(b => b.Id)).ToList();
        return new LibraryImportResult(added, skipped, wasEmpty);
    }

    /// <summary>
    /// Makes the library follow the owner's catalogue for this company: items, systems and bundles of the catalogue are
    /// added or updated (an existing product keeps the company's own price), and those no longer in it are retired, so
    /// saved quotes still find them. Currency and defaults come from the catalogue. One transaction; nothing changes when
    /// the result would be invalid.
    /// </summary>
    public CatalogueApplyResult ApplyCatalogue(ProductLibrary catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var before = Current;
        var added = new List<string>();
        var updated = new List<string>();
        var retired = new List<string>();

        List<T> Merge<T>(IEnumerable<T> current, IEnumerable<T> incoming, Func<T, string> id, Func<T, T, T> keepLocal,
            Func<T, bool> isActive, Func<T, T> retire) where T : class
        {
            var incomingById = incoming.ToDictionary(id, StringComparer.Ordinal);
            var result = new List<T>();
            foreach (var item in current)
            {
                if (incomingById.Remove(id(item), out var fromCatalogue))
                {
                    var merged = keepLocal(item, fromCatalogue);
                    if (!Equals(merged, item)) updated.Add(id(item));
                    result.Add(merged);
                }
                else
                {
                    if (isActive(item)) retired.Add(id(item));
                    result.Add(isActive(item) ? retire(item) : item);
                }
            }
            foreach (var item in incoming.Where(i => incomingById.ContainsKey(id(i))))
            {
                added.Add(id(item));
                result.Add(item);
            }
            return result;
        }

        var materials = Merge(before.Materials, catalogue.Materials, m => m.Id, (local, c) => c with { CostPerUnit = local.CostPerUnit },
            m => m.IsActive, m => m with { IsActive = false });
        var profiles = Merge(before.Profiles, catalogue.Profiles, p => p.Id, (local, c) => c with { CostPerMetre = local.CostPerMetre },
            p => p.IsActive, p => p with { IsActive = false });
        var glass = Merge(before.Glass, catalogue.Glass, g => g.Id, (local, c) => c with { CostPerSquareMetre = local.CostPerSquareMetre },
            g => g.IsActive, g => g with { IsActive = false });
        var systems = Merge(before.Systems, catalogue.Systems, x => x.Id, (_, c) => c, x => x.IsActive, x => x with { IsActive = false });
        var bundles = Merge(before.Bundles, catalogue.Bundles, b => b.Id, (_, c) => c, b => b.IsActive, b => b with { IsActive = false });

        // Records compare lists by reference: an unchanged item read again from the catalogue is not "updated".
        updated.RemoveAll(id => SameContent(before, catalogue, id));

        var result = new ProductLibrary(profiles, glass, materials, catalogue.Defaults, catalogue.Currency, systems, bundles);
        bool settingsChanged = result.Currency != before.Currency || result.Defaults != before.Defaults;
        if (added.Count + updated.Count + retired.Count == 0 && !settingsChanged)
            return new CatalogueApplyResult(added, updated, retired);
        Commit(result, () => _repository.SaveAll(result));
        return new CatalogueApplyResult(added, updated, retired);
    }

    /// <summary>True when the item <paramref name="id"/> is the same in both libraries apart from the price.</summary>
    private static bool SameContent(ProductLibrary local, ProductLibrary catalogue, string id)
    {
        static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
        if (local.FindProfile(id) is { } p && catalogue.FindProfile(id) is { } cp)
            return Json(p with { CostPerMetre = 0 }) == Json(cp with { CostPerMetre = 0 });
        if (local.FindGlass(id) is { } g && catalogue.FindGlass(id) is { } cg)
            return Json(g with { CostPerSquareMetre = 0 }) == Json(cg with { CostPerSquareMetre = 0 });
        if (local.FindMaterial(id) is { } m && catalogue.FindMaterial(id) is { } cm)
            return Json(m with { CostPerUnit = 0 }) == Json(cm with { CostPerUnit = 0 });
        if (local.FindSystem(id) is { } x && catalogue.FindSystem(id) is { } cx)
            return Json(x) == Json(cx);
        if (local.FindBundle(id) is { } b && catalogue.FindBundle(id) is { } cb)
            return Json(b) == Json(cb);
        return false;
    }

    /// <summary>The current library as library-file JSON (the same format <see cref="LibrarySerializer"/> reads).</summary>
    public string Export() => LibrarySerializer.Serialize(Current);

    // ── Internals ───────────────────────────────────────────────────

    private ProductLibrary Candidate(IEnumerable<ProfileDefinition>? profiles = null, IEnumerable<GlassDefinition>? glass = null,
        IEnumerable<MaterialDefinition>? materials = null, LibraryDefaults? defaults = null, string? currency = null,
        IEnumerable<ProductSystem>? systems = null, IEnumerable<Bundle>? bundles = null)
        => new(profiles ?? Current.Profiles, glass ?? Current.Glass, materials ?? Current.Materials,
            defaults ?? Current.Defaults, currency ?? Current.Currency, systems ?? Current.Systems, bundles ?? Current.Bundles);

    /// <summary>
    /// Writes a change whose result was already validated (constructing <paramref name="validated"/> throws
    /// <see cref="LibraryValidationException"/> otherwise, before anything is written), then reloads what was stored.
    /// </summary>
    private void Commit(ProductLibrary validated, Action write)
    {
        ArgumentNullException.ThrowIfNull(validated);
        write();
        Current = _repository.Load();
        Changed?.Invoke(Current);
    }

    private static IEnumerable<string> IdsOf(IProductLibrary library)
        => library.Materials.Select(m => m.Id).Concat(library.Profiles.Select(p => p.Id)).Concat(library.Glass.Select(g => g.Id))
            .Concat(library.Systems.Select(x => x.Id)).Concat(library.Bundles.Select(b => b.Id));

    private HashSet<string> AllIds() => IdsOf(Current).ToHashSet(StringComparer.Ordinal);

    private void EnsureNewId(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && AllIds().Contains(id))
            throw new LibraryOperationException($"The id '{id}' is already used in the library. Ids are permanent; choose another.",
                new[] { $"'{id}' already exists." });
    }

    private void EnsureExists(LibraryItemKind kind, string? id)
    {
        bool exists = kind switch
        {
            LibraryItemKind.Profile => Current.FindProfile(id) is not null,
            LibraryItemKind.Glass => Current.FindGlass(id) is not null,
            LibraryItemKind.System => Current.FindSystem(id) is not null,
            LibraryItemKind.Bundle => Current.FindBundle(id) is not null,
            _ => Current.FindMaterial(id) is not null
        };
        if (!exists) throw NotFound(kind, id);
    }

    private static LibraryOperationException NotFound(LibraryItemKind kind, string? id)
        => new($"The {kind.ToString().ToLowerInvariant()} '{id}' is not in the library.", new[] { "Not found." });
}
