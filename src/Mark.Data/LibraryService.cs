using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Data;

/// <summary>What a library import did.</summary>
/// <param name="Added">Ids of the products added, in import order.</param>
/// <param name="Skipped">Ids that already existed and were left unchanged (an import never overwrites).</param>
/// <param name="SettingsImported">True when currency and defaults were taken from the import (only into an empty library).</param>
public sealed record LibraryImportResult(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped, bool SettingsImported);

/// <summary>
/// The application service for the product library. It keeps the current library in memory as one immutable,
/// validated <see cref="ProductLibrary"/> snapshot (<see cref="Current"/>), so pickers, rendering and calculations never
/// touch the database. Every change is checked by building the whole candidate library (the same validation that
/// guards a library file: unique ids across all kinds, prices, roles, usages, defaults), then written in one
/// transaction; the new snapshot is reloaded and <see cref="Changed"/> is raised. A rejected change writes nothing.
///
/// Ids are immutable: <c>Update</c> keeps the id, and a new id is an <c>Add</c>. A product that is still referenced
/// (by a saved project, the open project, another product's usages or the library defaults) is never deleted; it can
/// be retired instead (<see cref="SetActive"/>), which hides it from pickers but keeps every reference resolving.
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

    /// <summary>Retires (false) or reinstates (true) a product. Retired products still resolve, so references stay valid.</summary>
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
    /// Why the product cannot be deleted: every saved project, open-project object, product usage or library default that
    /// still references it. Empty when deleting is safe.
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
        }

        if (kind != LibraryItemKind.Material && _projects is not null)
        {
            // The saved version counts even for the open project: it may still use the product after unsaved edits.
            var saved = _projects.FindUsing(kind, id).Select(p => p.Name).ToList();
            if (saved.Count > 0) blockers.Add($"Saved project(s) use it: {string.Join(", ", saved)}.");
        }
        return blockers;
    }

    /// <summary>Deletes an unreferenced product.</summary>
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
            _ => Candidate(materials: Current.Materials.Where(m => m.Id != id))
        };
        Commit(candidate, () => _repository.Delete(kind, id));
    }

    // ── Import / export ─────────────────────────────────────────────

    /// <summary>
    /// Adds every product of <paramref name="source"/> whose id is not used yet, in one transaction; existing ids are
    /// skipped and reported, never overwritten. Currency and defaults are taken only when the library was empty.
    /// The merged library is validated first, so an import that would leave it inconsistent changes nothing.
    /// </summary>
    public LibraryImportResult Import(ProductLibrary source)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool wasEmpty = Current.Profiles.Count + Current.Glass.Count + Current.Materials.Count == 0;
        var used = AllIds();
        var materials = source.Materials.Where(m => !used.Contains(m.Id)).ToList();
        var profiles = source.Profiles.Where(p => !used.Contains(p.Id)).ToList();
        var glass = source.Glass.Where(g => !used.Contains(g.Id)).ToList();
        var skipped = source.Materials.Select(m => m.Id).Concat(source.Profiles.Select(p => p.Id)).Concat(source.Glass.Select(g => g.Id))
            .Where(used.Contains).ToList();

        var settings = wasEmpty ? (source.Currency, source.Defaults) : ((string, LibraryDefaults)?)null;
        var candidate = Candidate(Current.Profiles.Concat(profiles), Current.Glass.Concat(glass), Current.Materials.Concat(materials),
            settings?.Item2, settings?.Item1);
        Commit(candidate, () => _repository.Insert(materials, profiles, glass, settings));

        var added = materials.Select(m => m.Id).Concat(profiles.Select(p => p.Id)).Concat(glass.Select(g => g.Id)).ToList();
        return new LibraryImportResult(added, skipped, wasEmpty);
    }

    /// <summary>The current library as library-file JSON (the same format <see cref="LibrarySerializer"/> reads).</summary>
    public string Export() => LibrarySerializer.Serialize(Current);

    // ── Internals ───────────────────────────────────────────────────

    private ProductLibrary Candidate(IEnumerable<ProfileDefinition>? profiles = null, IEnumerable<GlassDefinition>? glass = null,
        IEnumerable<MaterialDefinition>? materials = null, LibraryDefaults? defaults = null, string? currency = null)
        => new(profiles ?? Current.Profiles, glass ?? Current.Glass, materials ?? Current.Materials,
            defaults ?? Current.Defaults, currency ?? Current.Currency);

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

    private HashSet<string> AllIds()
        => Current.Profiles.Select(p => p.Id).Concat(Current.Glass.Select(g => g.Id)).Concat(Current.Materials.Select(m => m.Id))
            .ToHashSet(StringComparer.Ordinal);

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
            _ => Current.FindMaterial(id) is not null
        };
        if (!exists) throw NotFound(kind, id);
    }

    private static LibraryOperationException NotFound(LibraryItemKind kind, string? id)
        => new($"The {kind.ToString().ToLowerInvariant()} '{id}' is not in the library.", new[] { "Not found." });
}
