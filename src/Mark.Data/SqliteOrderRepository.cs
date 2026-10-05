using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Core.Orders;

namespace Mark.Data;

/// <summary>
/// Customer orders (Milestone 17): one per quote that became an order, stored whole as JSON (stage, payments, schedule,
/// dispatch notes, sign-off).
/// </summary>
public sealed class SqliteOrderRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SqliteDatabase _database;
    private readonly Func<DateTime> _utcNow;

    public SqliteOrderRepository(SqliteDatabase database, Func<DateTime>? utcNow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Inserts or updates the order; a new one gets its creation time.</summary>
    public void Save(CustomerOrder order) => _database.Guard($"save the order {order.OrderNumber}".Trim(), () =>
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.CreatedUtc == default) order.CreatedUtc = _utcNow();
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO customer_orders (id, project_id, order_number, created_utc, order_json)
            VALUES ($id, $project, $number, $created, $json)
            ON CONFLICT (id) DO UPDATE SET order_number = excluded.order_number, order_json = excluded.order_json
            """;
        command.Parameters.AddWithValue("$id", Key(order.Id));
        command.Parameters.AddWithValue("$project", Key(order.ProjectId));
        command.Parameters.AddWithValue("$number", order.OrderNumber);
        command.Parameters.AddWithValue("$created", DateTime.SpecifyKind(order.CreatedUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(order, Json));
        command.ExecuteNonQuery();
    });

    /// <summary>Every order, newest order number first.</summary>
    public IReadOnlyList<CustomerOrder> List() => _database.Guard("read the orders", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_json FROM customer_orders ORDER BY order_number DESC, created_utc DESC";
        using var r = command.ExecuteReader();
        var list = new List<CustomerOrder>();
        while (r.Read())
            if (Read(r.GetString(0)) is { } order) list.Add(order);
        return (IReadOnlyList<CustomerOrder>)list.AsReadOnly();
    });

    /// <summary>The order of a quote, or null.</summary>
    public CustomerOrder? ForProject(Guid projectId) => _database.Guard("read the order", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_json FROM customer_orders WHERE project_id = $id";
        command.Parameters.AddWithValue("$id", Key(projectId));
        return command.ExecuteScalar() is string json ? Read(json) : null;
    });

    /// <exception cref="DataStoreException">No such order.</exception>
    public CustomerOrder Load(Guid id) => _database.Guard("open the order", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_json FROM customer_orders WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        return command.ExecuteScalar() is string json
            ? Read(json) ?? throw new DataStoreException("The saved order is damaged and cannot be opened.")
            : throw new DataStoreException("The order is not in the database (it may have been deleted).");
    });

    /// <summary>The next dispatch note number ("DN-00001"…), over every order.</summary>
    public string NextDispatchNumber() => CustomerOrder.NextDispatchNumber(List().SelectMany(o => o.Dispatches).Select(d => d.Number));

    private static CustomerOrder? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CustomerOrder>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Key(Guid id) => id.ToString("D");
}
