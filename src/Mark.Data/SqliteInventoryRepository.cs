using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Core.Inventory;
using Microsoft.Data.Sqlite;

namespace Mark.Data;

/// <summary>
/// Purchasing and inventory (Milestone 18): suppliers and purchase orders (as JSON), the stock of every item and the
/// ledger of stock moves. Stock on hand changes only through a move, in the same transaction, so the ledger always adds
/// up to what is on hand.
/// </summary>
public sealed class SqliteInventoryRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SqliteDatabase _database;
    private readonly Func<DateTime> _utcNow;

    public SqliteInventoryRepository(SqliteDatabase database, Func<DateTime>? utcNow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // ── Suppliers ───────────────────────────────────────────────────

    public IReadOnlyList<Supplier> Suppliers() => _database.Guard("read the suppliers", () =>
    {
        var list = new List<Supplier>();
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT supplier_json FROM suppliers ORDER BY name COLLATE NOCASE";
        using var r = command.ExecuteReader();
        while (r.Read())
            if (Read<Supplier>(r.GetString(0)) is { } supplier) list.Add(supplier);
        return (IReadOnlyList<Supplier>)list.AsReadOnly();
    });

    public void SaveSupplier(Supplier supplier) => _database.Guard($"save the supplier {supplier.Name}".Trim(), () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO suppliers (id, name, supplier_json) VALUES ($id, $name, $json)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, supplier_json = excluded.supplier_json
            """;
        command.Parameters.AddWithValue("$id", Key(supplier.Id));
        command.Parameters.AddWithValue("$name", supplier.Name);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(supplier, Json));
        command.ExecuteNonQuery();
    });

    // ── Purchase orders ─────────────────────────────────────────────

    /// <summary>Every purchase order, newest number first.</summary>
    public IReadOnlyList<PurchaseOrder> PurchaseOrders() => _database.Guard("read the purchase orders", () =>
    {
        var list = new List<PurchaseOrder>();
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT order_json FROM purchase_orders ORDER BY number DESC, created_utc DESC";
        using var r = command.ExecuteReader();
        while (r.Read())
            if (Read<PurchaseOrder>(r.GetString(0)) is { } order) list.Add(order);
        return (IReadOnlyList<PurchaseOrder>)list.AsReadOnly();
    });

    /// <summary>Inserts or updates; a new one gets its creation time and the next number (PO-00001…).</summary>
    public void SavePurchaseOrder(PurchaseOrder order) => _database.Guard($"save the purchase order {order.Number}".Trim(), () =>
    {
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        Write(connection, transaction, order);
        transaction.Commit();
    });

    /// <summary>
    /// Books goods received against <paramref name="order"/>: the receipt (GRN-00001…) is added to the order, every line
    /// goes into stock, and both are saved together. Returns the receipt.
    /// </summary>
    /// <exception cref="DataStoreException">Nothing received, the order is not ordered, or the database cannot be written.</exception>
    public GoodsReceipt Receive(PurchaseOrder order, IReadOnlyList<ReceiptLine> lines, string by, string supplierInvoice)
        => _database.Guard($"receive the goods of {order.Number}", () =>
    {
        var received = lines.Where(l => l.Quantity > 0).ToList();
        if (received.Count == 0) throw new DataStoreException("Enter how much came in.");
        if (order.Status is not (PurchaseStatus.Ordered or PurchaseStatus.PartReceived))
            throw new DataStoreException($"{order.Number} is {PurchaseOrder.StatusName(order.Status).ToLowerInvariant()}: only an ordered purchase order can be received.");
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        string number = PurchaseOrder.NextNumber("GRN", ReceiptNumbers(connection, transaction));
        var receipt = new GoodsReceipt(number, _utcNow(), by ?? "", (supplierInvoice ?? "").Trim(), received);
        foreach (var line in received)
            AddMove(connection, transaction, new StockKey(line.Kind, line.ItemId), line.Quantity, StockMoveReason.Received,
                number, by ?? "", $"{order.Number} · {order.SupplierName}".Trim(' ', '·'));
        order.Receipts.Add(receipt);
        try
        {
            Write(connection, transaction, order);
            transaction.Commit();
        }
        catch
        {
            order.Receipts.Remove(receipt);
            throw;
        }
        return receipt;
    });

    // ── Stock ───────────────────────────────────────────────────────

    /// <summary>Every item that has a stock record.</summary>
    public IReadOnlyList<StockLevel> Levels() => _database.Guard("read the stock", () =>
    {
        var list = new List<StockLevel>();
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind, item_id, on_hand, reorder_level, location FROM stock_levels";
        using var r = command.ExecuteReader();
        while (r.Read())
            if (Enum.TryParse(r.GetString(0), out StockKind kind))
                list.Add(new StockLevel(kind, r.GetString(1), r.GetDouble(2), r.GetDouble(3), r.GetString(4)));
        return (IReadOnlyList<StockLevel>)list.AsReadOnly();
    });

    /// <summary>Sets when to reorder an item and where it is kept (on hand is not touched).</summary>
    public void SetLevel(StockKey key, double reorderLevel, string location) => _database.Guard("save the stock settings", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO stock_levels (kind, item_id, on_hand, reorder_level, location) VALUES ($kind, $item, 0, $reorder, $location)
            ON CONFLICT (kind, item_id) DO UPDATE SET reorder_level = excluded.reorder_level, location = excluded.location
            """;
        command.Parameters.AddWithValue("$kind", key.Kind.ToString());
        command.Parameters.AddWithValue("$item", key.ItemId);
        command.Parameters.AddWithValue("$reorder", Math.Max(0, reorderLevel));
        command.Parameters.AddWithValue("$location", (location ?? "").Trim());
        command.ExecuteNonQuery();
    });

    /// <summary>Moves stock in (+) or out (−) with why; several moves are booked together or not at all.</summary>
    public void Move(IReadOnlyList<(StockKey Key, double Quantity)> moves, StockMoveReason reason, string reference, string by, string note = "")
        => _database.Guard("book the stock", () =>
    {
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        // A move of no item (empty id) only records that something happened, e.g. a job issued with nothing from stock.
        foreach (var (key, quantity) in moves.Where(m => m.Quantity != 0 || m.Key.ItemId.Length == 0))
            AddMove(connection, transaction, key, quantity, reason, reference ?? "", by ?? "", note ?? "");
        transaction.Commit();
    });

    /// <summary>The ledger, newest first: of one item, or of everything (at most <paramref name="limit"/> moves).</summary>
    public IReadOnlyList<StockMove> Moves(StockKey? key = null, int limit = 200) => _database.Guard("read the stock moves", () =>
    {
        var list = new List<StockMove>();
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = key is null
            ? "SELECT id, utc, kind, item_id, quantity, reason, reference, by_user, note FROM stock_moves WHERE item_id <> '' ORDER BY id DESC LIMIT $limit"
            : "SELECT id, utc, kind, item_id, quantity, reason, reference, by_user, note FROM stock_moves WHERE kind = $kind AND item_id = $item ORDER BY id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        if (key is { } k)
        {
            command.Parameters.AddWithValue("$kind", k.Kind.ToString());
            command.Parameters.AddWithValue("$item", k.ItemId);
        }
        using var r = command.ExecuteReader();
        while (r.Read())
        {
            if (!Enum.TryParse(r.GetString(2), out StockKind kind) || !Enum.TryParse(r.GetString(5), out StockMoveReason reason)) continue;
            list.Add(new StockMove(r.GetInt64(0), DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                kind, r.GetString(3), r.GetDouble(4), reason, r.GetString(6), r.GetString(7), r.GetString(8)));
        }
        return (IReadOnlyList<StockMove>)list.AsReadOnly();
    });

    /// <summary>The production orders whose stock has been issued (by their order number).</summary>
    public IReadOnlySet<string> IssuedOrders() => _database.Guard("read the stock moves", () =>
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT reference FROM stock_moves WHERE reason = 'Issued'";
        using var r = command.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return (IReadOnlySet<string>)set;
    });

    // ── Helpers ─────────────────────────────────────────────────────

    private void AddMove(SqliteConnection c, SqliteTransaction t, StockKey key, double quantity, StockMoveReason reason, string reference,
        string by, string note)
    {
        Run(c, t, """
            INSERT INTO stock_moves (utc, kind, item_id, quantity, reason, reference, by_user, note)
            VALUES ($utc, $kind, $item, $quantity, $reason, $reference, $by, $note)
            """, ("$utc", Time(_utcNow())), ("$kind", key.Kind.ToString()), ("$item", key.ItemId), ("$quantity", quantity),
            ("$reason", reason.ToString()), ("$reference", reference), ("$by", by), ("$note", note));
        if (key.ItemId.Length == 0) return;
        Run(c, t, """
            INSERT INTO stock_levels (kind, item_id, on_hand, reorder_level, location) VALUES ($kind, $item, $quantity, 0, '')
            ON CONFLICT (kind, item_id) DO UPDATE SET on_hand = on_hand + excluded.on_hand
            """, ("$kind", key.Kind.ToString()), ("$item", key.ItemId), ("$quantity", quantity));
    }

    private void Write(SqliteConnection c, SqliteTransaction t, PurchaseOrder order)
    {
        if (order.CreatedUtc == default) order.CreatedUtc = _utcNow();
        if (string.IsNullOrWhiteSpace(order.Number))
            order.Number = PurchaseOrder.NextNumber("PO", Numbers(c, t));
        Run(c, t, """
            INSERT INTO purchase_orders (id, number, supplier_id, created_utc, order_json) VALUES ($id, $number, $supplier, $created, $json)
            ON CONFLICT (id) DO UPDATE SET number = excluded.number, supplier_id = excluded.supplier_id, order_json = excluded.order_json
            """, ("$id", Key(order.Id)), ("$number", order.Number), ("$supplier", Key(order.SupplierId)),
            ("$created", Time(order.CreatedUtc)), ("$json", JsonSerializer.Serialize(order, Json)));
    }

    private static List<string> Numbers(SqliteConnection c, SqliteTransaction t)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "SELECT number FROM purchase_orders";
        using var r = command.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static List<string> ReceiptNumbers(SqliteConnection c, SqliteTransaction t)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "SELECT DISTINCT reference FROM stock_moves WHERE reason = 'Received'";
        using var r = command.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
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

    private static T? Read<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Key(Guid id) => id.ToString("D");

    private static string Time(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
