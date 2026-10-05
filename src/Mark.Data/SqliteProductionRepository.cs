using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Core.Production;
using Microsoft.Data.Sqlite;

namespace Mark.Data;

/// <summary>
/// Production orders and the offcuts kept in the workshop (Milestone 16). A production order is stored whole as JSON
/// (progress, due date, notes) next to the designs as they were when production started; the list reads only the first.
/// </summary>
public sealed class SqliteProductionRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SqliteDatabase _database;
    private readonly Func<DateTime> _utcNow;

    public SqliteProductionRepository(SqliteDatabase database, Func<DateTime>? utcNow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Who makes new production orders.</summary>
    public ProjectUser User { get; set; } = ProjectUser.Unknown;

    /// <summary>Inserts or updates the order; a new one gets its creation time and creator.</summary>
    public void Save(ProductionOrder order) => _database.Guard($"save the production order {order.OrderNumber}".Trim(), () =>
    {
        ArgumentNullException.ThrowIfNull(order);
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        Write(connection, transaction, order);
        transaction.Commit();
    });

    private void Write(SqliteConnection connection, SqliteTransaction transaction, ProductionOrder order)
    {
        bool exists = Count(connection, transaction, "SELECT COUNT(*) FROM production_orders WHERE id = $id", ("$id", Key(order.Id))) > 0;
        if (!exists)
        {
            order.CreatedUtc = _utcNow();
            if (string.IsNullOrWhiteSpace(order.CreatedBy)) order.CreatedBy = User.DisplayName;
        }
        Run(connection, transaction, """
            INSERT INTO production_orders (id, project_id, order_number, created_utc, order_json, document_json)
            VALUES ($id, $project, $number, $created, $order, $document)
            ON CONFLICT (id) DO UPDATE SET order_number = excluded.order_number, order_json = excluded.order_json
            """,
            ("$id", Key(order.Id)), ("$project", Key(order.ProjectId)), ("$number", order.OrderNumber), ("$created", Time(order.CreatedUtc)),
            ("$order", JsonSerializer.Serialize(order, Json)), ("$document", order.DocumentJson));
    }

    /// <summary>Every production order, newest first, without their designs (see <see cref="Load"/>).</summary>
    public IReadOnlyList<ProductionOrder> List() => _database.Guard("read the production orders", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_json FROM production_orders ORDER BY created_utc DESC, order_number DESC";
        using var r = command.ExecuteReader();
        var list = new List<ProductionOrder>();
        while (r.Read())
            if (Read(r.GetString(0)) is { } order) list.Add(order);
        return (IReadOnlyList<ProductionOrder>)list.AsReadOnly();
    });

    /// <summary>A production order with its designs.</summary>
    /// <exception cref="DataStoreException">No such order, or its data is damaged.</exception>
    public ProductionOrder Load(Guid id)
    {
        var (orderJson, document) = _database.Guard("open the production order", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT order_json, document_json FROM production_orders WHERE id = $id";
            command.Parameters.AddWithValue("$id", Key(id));
            using var r = command.ExecuteReader();
            if (!r.Read()) throw new DataStoreException("The production order is not in the database (it may have been deleted).");
            return (r.GetString(0), r.GetString(1));
        });
        var order = Read(orderJson) ?? throw new DataStoreException("The saved production order is damaged and cannot be opened.");
        order.DocumentJson = document;
        return order;
    }

    /// <summary>The production order of a quote, or null.</summary>
    public Guid? ForProject(Guid projectId) => _database.Guard("read the production orders", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM production_orders WHERE project_id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", Key(projectId));
        return command.ExecuteScalar() is string id ? Guid.Parse(id) : (Guid?)null;
    });

    /// <exception cref="DataStoreException">No such order.</exception>
    public void Delete(Guid id) => _database.Guard("delete the production order", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM production_orders WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        if (command.ExecuteNonQuery() == 0) throw new DataStoreException("The production order is not in the database (it may have been deleted).");
    });

    // ── Offcuts in stock ────────────────────────────────────────────

    /// <summary>The offcuts kept, by profile then longest first.</summary>
    public IReadOnlyList<Offcut> Offcuts() => _database.Guard("read the offcuts", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, definition_id, length_mm, added_utc, source FROM offcuts ORDER BY definition_id, length_mm DESC, id";
        using var r = command.ExecuteReader();
        var list = new List<Offcut>();
        while (r.Read())
            list.Add(new Offcut(r.GetInt64(0), r.GetString(1), r.GetDouble(2), ParseTime(r.GetString(3)), r.GetString(4)));
        return (IReadOnlyList<Offcut>)list.AsReadOnly();
    });

    /// <summary>Adds <paramref name="count"/> offcuts of a profile and length.</summary>
    public void AddOffcuts(string definitionId, double lengthMm, int count, string source) => _database.Guard("add the offcuts", () =>
    {
        if (string.IsNullOrWhiteSpace(definitionId)) throw new DataStoreException("Choose the profile of the offcut.");
        if (!(lengthMm > 0)) throw new DataStoreException("The length of an offcut must be more than 0 mm.");
        if (count < 1) throw new DataStoreException("Add at least one offcut.");
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        for (int i = 0; i < count; i++)
            Insert(connection, transaction, definitionId, lengthMm, source);
        transaction.Commit();
    });

    public void RemoveOffcut(long id) => _database.Guard("remove the offcut", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM offcuts WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    });

    /// <summary>
    /// After cutting an order: the offcuts it was cut from leave stock, its new reusable leftovers join it, and the order
    /// remembers that this was done (only once). Returns how many offcuts were taken and added.
    /// </summary>
    /// <exception cref="DataStoreException">Done before, or an offcut it used is no longer in stock.</exception>
    public (int Taken, int Added) UpdateOffcuts(ProductionOrder order, IReadOnlyCollection<long> used,
        IReadOnlyCollection<(string DefinitionId, double LengthMm)> leftovers) => _database.Guard("update the offcuts", () =>
    {
        if (order.OffcutsUpdatedUtc is not null)
            throw new DataStoreException($"The offcuts were already updated for {order.OrderNumber}.");
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        foreach (long id in used)
            if (Run(connection, transaction, "DELETE FROM offcuts WHERE id = $id", ("$id", id)) == 0)
                throw new DataStoreException("An offcut the cutting list uses is no longer in stock. Make the cutting list again.");
        foreach (var (definitionId, length) in leftovers)
            Insert(connection, transaction, definitionId, length, order.OrderNumber);
        order.OffcutsUpdatedUtc = _utcNow();
        try
        {
            Write(connection, transaction, order);
            transaction.Commit();
        }
        catch
        {
            order.OffcutsUpdatedUtc = null;
            throw;
        }
        return (used.Count, leftovers.Count);
    });

    private void Insert(SqliteConnection connection, SqliteTransaction transaction, string definitionId, double lengthMm, string source)
        => Run(connection, transaction, "INSERT INTO offcuts (definition_id, length_mm, added_utc, source) VALUES ($def, $len, $now, $source)",
            ("$def", definitionId.Trim()), ("$len", Math.Round(lengthMm, 1)), ("$now", Time(_utcNow())), ("$source", source ?? ""));

    // ── Helpers ─────────────────────────────────────────────────────

    private static ProductionOrder? Read(string json)
    {
        try
        {
            var order = JsonSerializer.Deserialize<ProductionOrder>(json, Json);
            if (order is not null) order.Progress ??= new();
            return order;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Run(SqliteConnection c, SqliteTransaction t, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteNonQuery();
    }

    private static long Count(SqliteConnection c, SqliteTransaction t, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Key(Guid id) => id.ToString("D");

    private static string Time(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTime(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
