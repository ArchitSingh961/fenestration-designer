using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Core.Serialization;

namespace Mark.Data;

/// <summary>
/// Company settings kept in the database (table <c>app_settings</c>, schema 3). Today: the default price structure that
/// every new quote starts from.
/// </summary>
public sealed class SettingsRepository
{
    private const string PricingKey = "pricing.default";
    private readonly SqliteDatabase _database;

    public SettingsRepository(SqliteDatabase database) => _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>The saved default price structure, or <see cref="PriceStructure.Default"/> if none was saved (or it is unreadable).</summary>
    public PriceStructure LoadDefaultPricing()
    {
        string? json = _database.Guard("read the settings", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value_json FROM app_settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", PricingKey);
            return command.ExecuteScalar() as string;
        });
        if (json is null) return PriceStructure.Default();
        try
        {
            var pricing = PricingSerializer.Deserialize(json);
            return PricingEditor.Validate(pricing) is null ? pricing : PriceStructure.Default();
        }
        catch (InvalidOperationException)
        {
            return PriceStructure.Default();
        }
    }

    /// <summary>Stores <paramref name="pricing"/> as the default for new quotes.</summary>
    /// <exception cref="ArgumentException">The structure is not valid (see <see cref="PricingEditor.Validate"/>).</exception>
    public void SaveDefaultPricing(PriceStructure pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        if (PricingEditor.Validate(pricing) is { } error)
            throw new ArgumentException(error, nameof(pricing));
        string json = PricingSerializer.Serialize(pricing);
        _database.Guard("save the default pricing", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO app_settings (key, value_json) VALUES ($key, $value)
                ON CONFLICT (key) DO UPDATE SET value_json = excluded.value_json
                """;
            command.Parameters.AddWithValue("$key", PricingKey);
            command.Parameters.AddWithValue("$value", json);
            command.ExecuteNonQuery();
        });
    }

    private const string CatalogueHashKey = "catalogue.hash";

    /// <summary>The fingerprint of the owner's catalogue last applied to the library, or null.</summary>
    public string? LoadCatalogueHash()
        => _database.Guard("read the settings", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value_json FROM app_settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", CatalogueHashKey);
            return command.ExecuteScalar() is string json ? System.Text.Json.JsonSerializer.Deserialize<string>(json) : null;
        });

    public void SaveCatalogueHash(string? hash)
        => _database.Guard("save the settings", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO app_settings (key, value_json) VALUES ($key, $value)
                ON CONFLICT (key) DO UPDATE SET value_json = excluded.value_json
                """;
            command.Parameters.AddWithValue("$key", CatalogueHashKey);
            command.Parameters.AddWithValue("$value", System.Text.Json.JsonSerializer.Serialize(hash));
            command.ExecuteNonQuery();
        });
}
