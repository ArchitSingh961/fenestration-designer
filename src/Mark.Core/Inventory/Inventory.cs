using System.Globalization;
using System.Text.RegularExpressions;

namespace Mark.Core.Inventory;

/// <summary>What kind of library item is stocked: profiles in bars, glass in m², other items (hardware, gaskets, accessories) in their own unit.</summary>
public enum StockKind { Profile, Glass, Material }

/// <summary>A stocked item: the library item it is.</summary>
public readonly record struct StockKey(StockKind Kind, string ItemId)
{
    public override string ToString() => $"{Kind}:{ItemId}";
}

/// <summary>The stock of one item: how much is on hand, when to buy more and where it is kept.</summary>
/// <param name="ReorderLevel">Below this (after what open orders need), the item is low on stock. 0 = no alert.</param>
public sealed record StockLevel(StockKind Kind, string ItemId, double OnHand, double ReorderLevel = 0, string Location = "")
{
    public StockKey Key => new(Kind, ItemId);
}

/// <summary>Why stock went in or out.</summary>
public enum StockMoveReason
{
    /// <summary>Goods received from a supplier (a GRN).</summary>
    Received,

    /// <summary>Issued to a production order.</summary>
    Issued,

    /// <summary>Counted or corrected by hand.</summary>
    Adjusted,

    /// <summary>Returned from production.</summary>
    Returned
}

/// <summary>One movement of stock: + in, − out.</summary>
/// <param name="Reference">GRN-00001, the production order number, or a note's subject.</param>
public sealed record StockMove(long Id, DateTime Utc, StockKind Kind, string ItemId, double Quantity, StockMoveReason Reason,
    string Reference, string By, string Note = "");

/// <summary>A company the workshop buys from.</summary>
public sealed class Supplier
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string Gstin { get; set; } = "";

    /// <summary>What it supplies (to suggest it for purchase orders).</summary>
    public List<StockKind> Supplies { get; set; } = new();

    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public Supplier Copy() => new()
    {
        Id = Id, Name = Name, ContactPerson = ContactPerson, Phone = Phone, Email = Email, Address = Address, Gstin = Gstin,
        Supplies = Supplies.ToList(), Note = Note, IsActive = IsActive
    };

    public override string ToString() => Name;
}

/// <summary>The state of a purchase order (received follows from its goods receipts).</summary>
public enum PurchaseStatus { Draft, Ordered, PartReceived, Received, Cancelled }

/// <summary>A line of a purchase order: the item, how much and at what rate per unit.</summary>
public sealed record PurchaseLine(StockKind Kind, string ItemId, string Name, string Unit, double Quantity, decimal Rate)
{
    public StockKey Key => new(Kind, ItemId);
    public decimal Amount => Math.Round(Rate * (decimal)Quantity, 2, MidpointRounding.AwayFromZero);
}

/// <summary>A line of a goods receipt: how much of an item came in.</summary>
public sealed record ReceiptLine(StockKind Kind, string ItemId, double Quantity)
{
    public StockKey Key => new(Kind, ItemId);
}

/// <summary>Goods received against a purchase order (GRN-00001…).</summary>
public sealed record GoodsReceipt(string Number, DateTime Utc, string By, string SupplierInvoice, IReadOnlyList<ReceiptLine> Lines);

/// <summary>
/// A purchase order to a supplier (PO-00001…): what is bought at what rate, for which orders, and what has come in. Ordered
/// goods not received yet are "on order" and count against what orders need.
/// </summary>
public sealed class PurchaseOrder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? OrderedUtc { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public bool IsCancelled { get; set; }

    /// <summary>The customer orders it is for, e.g. "OR-00003, OR-00004" (empty: for stock).</summary>
    public string ForOrders { get; set; } = "";

    public string Note { get; set; } = "";
    public List<PurchaseLine> Lines { get; set; } = new();
    public List<GoodsReceipt> Receipts { get; set; } = new();

    public decimal Total => Lines.Sum(l => l.Amount);

    /// <summary>How much of an item has come in.</summary>
    public double ReceivedOf(StockKey key) => Receipts.SelectMany(r => r.Lines).Where(l => l.Key == key).Sum(l => l.Quantity);

    /// <summary>How much of an item is still to come (0 when cancelled or not ordered yet).</summary>
    public double DueOf(StockKey key)
        => Status is PurchaseStatus.Ordered or PurchaseStatus.PartReceived
            ? Math.Max(0, Lines.Where(l => l.Key == key).Sum(l => l.Quantity) - ReceivedOf(key))
            : 0;

    public PurchaseStatus Status
    {
        get
        {
            if (IsCancelled) return PurchaseStatus.Cancelled;
            if (OrderedUtc is null) return PurchaseStatus.Draft;
            if (Receipts.Count == 0) return PurchaseStatus.Ordered;
            bool all = Lines.GroupBy(l => l.Key).All(g => ReceivedOf(g.Key) + 1e-9 >= g.Sum(l => l.Quantity));
            return all ? PurchaseStatus.Received : PurchaseStatus.PartReceived;
        }
    }

    public static string StatusName(PurchaseStatus status) => status switch
    {
        PurchaseStatus.PartReceived => "Part received",
        _ => status.ToString()
    };

    /// <summary>The next number after the highest of <paramref name="used"/>: PO-00001, GRN-00001 ….</summary>
    public static string NextNumber(string prefix, IEnumerable<string> used)
    {
        int highest = 0;
        var pattern = new Regex($"^{Regex.Escape(prefix)}-(\\d+)$", RegexOptions.IgnoreCase);
        foreach (string number in used)
            if (pattern.Match(number ?? "") is { Success: true } m && int.TryParse(m.Groups[1].Value, out int n))
                highest = Math.Max(highest, n);
        return $"{prefix}-{(highest + 1).ToString("D5", CultureInfo.InvariantCulture)}";
    }
}

/// <summary>What one item needs from stock: for open orders, on hand, on order, and what is short.</summary>
/// <param name="Reserved">Needed by production orders whose stock has not been issued yet.</param>
/// <param name="OnOrder">Ordered from suppliers and not received yet.</param>
public sealed record StockPosition(StockKey Key, string Name, string Unit, double OnHand, double Reserved, double OnOrder,
    double ReorderLevel, string Location, IReadOnlyList<string> ForOrders)
{
    /// <summary>On hand less what open orders need (can be below 0).</summary>
    public double Available => OnHand - Reserved;

    /// <summary>What has to be bought: needed and below the reorder level, after what is on order. 0 when nothing.</summary>
    public double Shortfall => Math.Max(0, Math.Max(Reserved - OnHand, ReorderLevel > 0 ? ReorderLevel - Available : 0) - OnOrder);

    /// <summary>Available stock at or under the reorder level (or less than open orders need).</summary>
    public bool IsLow => Available < 0 || ReorderLevel > 0 && Available <= ReorderLevel;
}
