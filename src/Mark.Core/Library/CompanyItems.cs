using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mark.Core.Library;

/// <summary>
/// Products the owner made for one company only: its own profiles, glass, hardware, systems and bundles, and the system
/// its new windows start in. They may use items of the master catalogue (an own system with a catalogue frame profile,
/// an own bundle with catalogue hinges), so they are complete only together with it (<see cref="Combine"/>). Their ids
/// must differ from the catalogue's.
/// </summary>
public sealed record CompanyItems
{
    public IReadOnlyList<ProfileDefinition> Profiles { get; init; } = Array.Empty<ProfileDefinition>();

    public IReadOnlyList<GlassDefinition> Glass { get; init; } = Array.Empty<GlassDefinition>();

    public IReadOnlyList<MaterialDefinition> Materials { get; init; } = Array.Empty<MaterialDefinition>();

    public IReadOnlyList<ProductSystem> Systems { get; init; } = Array.Empty<ProductSystem>();

    public IReadOnlyList<Bundle> Bundles { get; init; } = Array.Empty<Bundle>();

    /// <summary>The system the company's new windows start in, when it is not the catalogue's default.</summary>
    public string? DefaultSystemId { get; init; }

    public static CompanyItems Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty => Count == 0 && DefaultSystemId is null;

    [JsonIgnore]
    public int Count => Profiles.Count + Glass.Count + Materials.Count + Systems.Count + Bundles.Count;

    /// <summary>The ids of the profiles, glass and materials.</summary>
    [JsonIgnore]
    public IEnumerable<string> ItemIds => Profiles.Select(p => p.Id).Concat(Glass.Select(g => g.Id)).Concat(Materials.Select(m => m.Id));

    /// <summary>"2 profiles · 1 system · 3 hardware and accessories", or "None".</summary>
    [JsonIgnore]
    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            void Add(int n, string one, string many) { if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}"); }
            Add(Systems.Count, "system", "systems");
            Add(Bundles.Count, "bundle", "bundles");
            Add(Profiles.Count, "profile", "profiles");
            Add(Glass.Count, "glass", "glass");
            Add(Materials.Count, "hardware or accessory", "hardware and accessories");
            return parts.Count == 0 ? "None" : string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// The catalogue with the company's items added after its own (and the company's default system).
    /// </summary>
    /// <exception cref="InvalidOperationException">An id is used by both, or together they are not a valid library.</exception>
    public static ProductLibrary Combine(IProductLibrary master, CompanyItems own)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(own);
        var masterIds = IdsOf(master);
        var clashes = own.ItemIds.Concat(own.Systems.Select(x => x.Id)).Concat(own.Bundles.Select(b => b.Id))
            .Where(masterIds.Contains).Distinct().ToList();
        if (clashes.Count > 0)
            throw new InvalidOperationException(
                $"{string.Join(", ", clashes)} {(clashes.Count == 1 ? "is" : "are")} already used in the catalogue. Give the company's own items other ids.");
        var defaults = own.DefaultSystemId is { } systemId ? master.Defaults with { SystemId = systemId } : master.Defaults;
        return new ProductLibrary(master.Profiles.Concat(own.Profiles), master.Glass.Concat(own.Glass), master.Materials.Concat(own.Materials),
            defaults, master.Currency, master.Systems.Concat(own.Systems), master.Bundles.Concat(own.Bundles));
    }

    /// <summary>
    /// The company's part of an edited copy of the catalogue: every item whose id is not in the catalogue, and the default
    /// system when it differs from the catalogue's. Changes to catalogue items are not part of it (see <see cref="CatalogueChanges"/>).
    /// </summary>
    public static CompanyItems Split(IProductLibrary edited, IProductLibrary master)
    {
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(master);
        var ids = IdsOf(master);
        return new CompanyItems
        {
            Profiles = edited.Profiles.Where(p => !ids.Contains(p.Id)).ToList(),
            Glass = edited.Glass.Where(g => !ids.Contains(g.Id)).ToList(),
            Materials = edited.Materials.Where(m => !ids.Contains(m.Id)).ToList(),
            Systems = edited.Systems.Where(x => !ids.Contains(x.Id)).ToList(),
            Bundles = edited.Bundles.Where(b => !ids.Contains(b.Id)).ToList(),
            DefaultSystemId = edited.Defaults.SystemId is { } systemId && systemId != master.Defaults.SystemId ? systemId : null
        };
    }

    /// <summary>How many catalogue items an edited copy changed or removed (these changes are not kept for the company).</summary>
    public static int CatalogueChanges(IProductLibrary edited, IProductLibrary master)
    {
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(master);
        int Changed<T>(IEnumerable<T> before, IEnumerable<T> after, Func<T, string> id)
        {
            var now = after.ToDictionary(id, x => Json(x), StringComparer.Ordinal);
            return before.Count(x => !now.TryGetValue(id(x), out var json) || json != Json(x));
        }
        return Changed(master.Profiles, edited.Profiles, p => p.Id) + Changed(master.Glass, edited.Glass, g => g.Id)
             + Changed(master.Materials, edited.Materials, m => m.Id) + Changed(master.Systems, edited.Systems, x => x.Id)
             + Changed(master.Bundles, edited.Bundles, b => b.Id);
    }

    public static string Serialize(CompanyItems items) => JsonSerializer.Serialize(items ?? Empty, Options);

    /// <exception cref="InvalidOperationException">The JSON cannot be read.</exception>
    public static CompanyItems Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            var items = JsonSerializer.Deserialize<CompanyItems>(json, Options) ?? Empty;
            return items with
            {
                Profiles = items.Profiles ?? Array.Empty<ProfileDefinition>(),
                Glass = items.Glass ?? Array.Empty<GlassDefinition>(),
                Materials = items.Materials ?? Array.Empty<MaterialDefinition>(),
                Systems = items.Systems ?? Array.Empty<ProductSystem>(),
                Bundles = items.Bundles ?? Array.Empty<Bundle>()
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The company's own items could not be read: {ex.Message}", ex);
        }
    }

    private static HashSet<string> IdsOf(IProductLibrary library)
        => library.Profiles.Select(p => p.Id).Concat(library.Glass.Select(g => g.Id)).Concat(library.Materials.Select(m => m.Id))
            .Concat(library.Systems.Select(x => x.Id)).Concat(library.Bundles.Select(b => b.Id)).ToHashSet(StringComparer.Ordinal);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
