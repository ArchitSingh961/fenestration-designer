using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mark.Calculation;

/// <summary>
/// Reads and writes <see cref="CalculationRules"/> as JSON (camelCase, enum names as strings, comments allowed), so a
/// workshop configures its saw and fabrication rules without a code change:
/// <code>
/// { "frameJoint": "mitre", "glassEdgeClearanceMm": 0,
///   "cutting": { "kerfMm": 3, "trimAllowanceMm": 5, "minUsableOffcutMm": 300 } }
/// </code>
/// Missing values keep their defaults.
/// </summary>
public static class CalculationRulesSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(CalculationRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return JsonSerializer.Serialize(rules, Options);
    }

    /// <exception cref="InvalidOperationException">The JSON is malformed or a rule is out of range.</exception>
    public static CalculationRules Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        CalculationRules rules;
        try
        {
            rules = JsonSerializer.Deserialize<CalculationRules>(json, Options)
                ?? throw new InvalidOperationException("The rules file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The rules file is not valid: {ex.Message}", ex);
        }

        try
        {
            rules.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidOperationException($"The rules file is not valid: {ex.Message}", ex);
        }
        return rules;
    }

    public static CalculationRules Load(string filePath) => Deserialize(File.ReadAllText(filePath));
}
