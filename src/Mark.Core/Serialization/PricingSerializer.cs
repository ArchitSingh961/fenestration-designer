using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Core.Models;

namespace Mark.Core.Serialization;

/// <summary>
/// Reads and writes a <see cref="PriceStructure"/> on its own (the company's default pricing), in the same JSON style
/// as project files (camelCase, enums as names).
/// </summary>
public static class PricingSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(PriceStructure pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        return JsonSerializer.Serialize(pricing, Options);
    }

    /// <exception cref="InvalidOperationException">The JSON is not a price structure.</exception>
    public static PriceStructure Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            return JsonSerializer.Deserialize<PriceStructure>(json, Options)
                   ?? throw new InvalidOperationException("The price structure is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The price structure is not valid: {ex.Message}", ex);
        }
    }
}
