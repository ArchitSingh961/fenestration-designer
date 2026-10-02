using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mark.Core.Library;

/// <summary>
/// Reads and writes a product library as JSON (System.Text.Json, camelCase, enums as strings):
/// <code>
/// { "version": 1, "currency": "INR", "defaults": { "frameProfileId": "…", "glassId": "…" },
///   "profiles": [ … ], "glass": [ … ], "materials": [ … ] }
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
    }

    public static string Serialize(IProductLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return JsonSerializer.Serialize(new LibraryFile
        {
            Currency = library.Currency,
            Defaults = library.Defaults,
            Profiles = library.Profiles.ToList(),
            Glass = library.Glass.ToList(),
            Materials = library.Materials.ToList()
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

        return new ProductLibrary(file.Profiles, file.Glass, file.Materials, file.Defaults, file.Currency);
    }

    public static ProductLibrary Load(string filePath) => Deserialize(File.ReadAllText(filePath));

    public static async Task<ProductLibrary> LoadAsync(string filePath) => Deserialize(await File.ReadAllTextAsync(filePath));
}
