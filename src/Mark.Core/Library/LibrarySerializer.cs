using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mark.Core.Library;

/// <summary>
/// Reads and writes a product library as JSON (System.Text.Json, camelCase, enums as strings):
/// <code>
/// { "version": 1, "currency": "INR", "defaults": { "frameProfileId": "…", "glassId": "…" },
///   "profiles": [ … ], "glass": [ … ], "materials": [ … ], "systems": [ … ], "bundles": [ … ] }
/// </code>
/// A library is data: adding or changing products never needs a code change.
/// </summary>
public static class LibrarySerializer
{
    /// <summary>The library file version written by this build.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed class LibraryFile
    {
        public int Version { get; set; } = CurrentVersion;
        public string Currency { get; set; } = "";
        public LibraryDefaults? Defaults { get; set; }
        public List<ProfileDefinition> Profiles { get; set; } = new();
        public List<GlassDefinition> Glass { get; set; } = new();
        public List<MaterialDefinition> Materials { get; set; } = new();
        public List<ProductSystem> Systems { get; set; } = new();
        public List<Bundle> Bundles { get; set; } = new();

        /// <summary>A company's catalogue: which items are its own (only read by <see cref="ReadOwnItems"/>).</summary>
        public OwnItemsLabel? OwnItems { get; set; }
    }

    public static string Serialize(IProductLibrary library) => Serialize(library, null);

    /// <summary>The library file, with the company's own items marked (a company's catalogue from the licence server).</summary>
    public static string Serialize(IProductLibrary library, OwnItemsLabel? ownItems)
    {
        ArgumentNullException.ThrowIfNull(library);
        return JsonSerializer.Serialize(new LibraryFile
        {
            OwnItems = ownItems,
            Currency = library.Currency,
            Defaults = library.Defaults,
            Profiles = library.Profiles.ToList(),
            Glass = library.Glass.ToList(),
            Materials = library.Materials.ToList(),
            Systems = library.Systems.ToList(),
            Bundles = library.Bundles.ToList()
        }, Options);
    }

    /// <exception cref="InvalidOperationException">The JSON is malformed, too new, or the library is inconsistent
    /// (<see cref="LibraryValidationException"/>).</exception>
    public static ProductLibrary Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        LibraryFile file;
        try
        {
            file = JsonSerializer.Deserialize<LibraryFile>(json, Options)
                ?? throw new InvalidOperationException("The library file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The library file is not valid: {ex.Message}", ex);
        }

        if (file.Version > CurrentVersion)
            throw new InvalidOperationException(
                $"Library file version {file.Version} is newer than this application supports (version {CurrentVersion}).");

        return new ProductLibrary(file.Profiles, file.Glass, file.Materials, file.Defaults, file.Currency, file.Systems, file.Bundles);
    }

    /// <summary>Which items of a company's catalogue are its own, or null (any other library file).</summary>
    public static OwnItemsLabel? ReadOwnItems(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LibraryFile>(json, Options)?.OwnItems is { CompanyName: not null, Ids: not null } own ? own : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ProductLibrary Load(string filePath) => Deserialize(File.ReadAllText(filePath));

    public static async Task<ProductLibrary> LoadAsync(string filePath) => Deserialize(await File.ReadAllTextAsync(filePath));
}
