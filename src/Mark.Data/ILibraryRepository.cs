using Mark.Core.Library;

namespace Mark.Data;

/// <summary>The kinds of library product that can be stored and referenced.</summary>
public enum LibraryItemKind
{
    Profile,
    Glass,
    Material
}

/// <summary>
/// Storage for the product library. Reads return a validated, immutable <see cref="ProductLibrary"/> in library order;
/// each write is one transaction. Callers (normally <see cref="LibraryService"/>) validate a change against the whole
/// library before writing it, so this layer only persists.
/// </summary>
public interface ILibraryRepository
{
    /// <summary>The stored library, in library order (explicit sort order, never storage order).</summary>
    /// <exception cref="DataStoreException">The stored data is invalid.</exception>
    ProductLibrary Load();

    /// <summary>True when no product of any kind is stored.</summary>
    bool IsEmpty();

    /// <summary>Inserts a new product (appended to the library order) or updates an existing one (keeps its place).</summary>
    void SaveProfile(ProfileDefinition profile);

    void SaveGlass(GlassDefinition glass);

    void SaveMaterial(MaterialDefinition material);

    /// <summary>Removes a product and its own child rows. Fails if another row still references it.</summary>
    void Delete(LibraryItemKind kind, string id);

    void SaveSettings(string currency, LibraryDefaults defaults);

    /// <summary>
    /// Inserts many new products in one transaction (materials first, so usages can reference them) and, if given,
    /// the settings. Nothing is written unless everything succeeds.
    /// </summary>
    void Insert(IReadOnlyList<MaterialDefinition> materials, IReadOnlyList<ProfileDefinition> profiles,
        IReadOnlyList<GlassDefinition> glass, (string Currency, LibraryDefaults Defaults)? settings);
}
