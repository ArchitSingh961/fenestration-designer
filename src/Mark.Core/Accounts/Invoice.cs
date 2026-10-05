using System.Globalization;

namespace Mark.Core.Accounts;

/// <summary>A line of a tax invoice: what was sold, its HSN/SAC code, how many at what rate (before tax).</summary>
/// <param name="Key">What the line is for, so later invoices of the order know what was invoiced: a design's frame id
/// ("frame:…") or a charge ("charge:Transportation").</param>
public sealed record InvoiceLine(string Key, string Description, string Hsn, double Quantity, string Unit, decimal Rate)
{
    public decimal Taxable => Math.Round(Rate * (decimal)Quantity, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// A GST tax invoice for an order (INV/2026-27/0001…): who sells and buys (with GSTINs and states), the place of supply,
/// the lines, and the tax — CGST + SGST within the seller's state, IGST otherwise — rounded to the rupee.
/// </summary>
public sealed class Invoice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public Guid ProjectId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string QuoteNumber { get; set; } = "";
    public string ProjectName { get; set; } = "";

    public string ClientName { get; set; } = "";
    public string ClientAddress { get; set; } = "";
    public string ClientGstin { get; set; } = "";
    public string ClientPhone { get; set; } = "";

    /// <summary>The state the goods go to (GST code, e.g. "08").</summary>
    public string PlaceOfSupply { get; set; } = "";

    /// <summary>The seller's state (from its GSTIN).</summary>
    public string SellerState { get; set; } = "";

    public string SiteAddress { get; set; } = "";
    public decimal TaxPercent { get; set; }
    public List<InvoiceLine> Lines { get; set; } = new();
    public string Notes { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public bool IsCancelled { get; set; }
    public string CancelledReason { get; set; } = "";

    /// <summary>Within the seller's state (CGST + SGST); between states, or unknown states, IGST.</summary>
    public bool IsIntraState => SellerState.Length > 0 && SellerState == PlaceOfSupply;

    /// <summary>A business client with a GSTIN (B2B), else a consumer (B2C).</summary>
    public bool IsB2B => ClientGstin.Length > 0;

    public decimal Taxable => Lines.Sum(l => l.Taxable);

    /// <summary>Half the tax each, within the state; 0 between states.</summary>
    public decimal Cgst => IsIntraState ? Round(Taxable * TaxPercent / 200m) : 0;
    public decimal Sgst => Cgst;

    /// <summary>All the tax, between states; 0 within the state.</summary>
    public decimal Igst => IsIntraState ? 0 : Round(Taxable * TaxPercent / 100m);

    public decimal Tax => Cgst + Sgst + Igst;

    /// <summary>To the nearest rupee.</summary>
    public decimal RoundOff => Math.Round(Taxable + Tax, 0, MidpointRounding.AwayFromZero) - (Taxable + Tax);

    public decimal Total => Taxable + Tax + RoundOff;

    /// <summary>How much of a line key (a design or a charge) this invoice has (0 when cancelled).</summary>
    public double QuantityOf(string key) => IsCancelled ? 0 : Lines.Where(l => l.Key == key).Sum(l => l.Quantity);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The next invoice number after the highest in use with the same start: "INV/2026-27/0001" (by financial year) or
    /// "INV-00001".
    /// </summary>
    public static string NextNumber(string prefix, bool byFinancialYear, DateTime date, IEnumerable<string> used)
    {
        prefix = string.IsNullOrWhiteSpace(prefix) ? "INV" : prefix.Trim();
        string start = byFinancialYear ? $"{prefix}/{Gst.FinancialYear(date)}/" : $"{prefix}-";
        int digits = byFinancialYear ? 4 : 5;
        int highest = used.Where(n => n.StartsWith(start, StringComparison.OrdinalIgnoreCase))
            .Select(n => int.TryParse(n[start.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int x) ? x : 0)
            .DefaultIfEmpty(0).Max();
        return start + (highest + 1).ToString("D" + digits, CultureInfo.InvariantCulture);
    }
}

/// <summary>How the company invoices and exports to its accounts (Accounts › Export and setup).</summary>
public sealed record AccountsSettings
{
    /// <summary>"INV" → INV/2026-27/0001.</summary>
    public string InvoicePrefix { get; init; } = "INV";

    /// <summary>Numbers start again each financial year (INV/2026-27/0001), as is usual in India.</summary>
    public bool NumberByFinancialYear { get; init; } = true;

    /// <summary>HSN of aluminium windows and doors (7610: aluminium structures, doors, windows and their frames).</summary>
    public string HsnAluminium { get; init; } = "7610";

    /// <summary>HSN of uPVC windows and doors (3925: builders' ware of plastics, doors, windows and their frames).</summary>
    public string HsnUpvc { get; init; } = "3925";

    /// <summary>HSN/SAC of charges such as transport and loading (empty: none printed).</summary>
    public string SacCharges { get; init; } = "";

    /// <summary>The company's state when its GSTIN is not set (GST code, e.g. "08").</summary>
    public string CompanyState { get; init; } = "";

    // Tally ledgers (the names must match the ledgers in Tally).
    public string SalesLedger { get; init; } = "Sales";
    public string CgstLedger { get; init; } = "Output CGST";
    public string SgstLedger { get; init; } = "Output SGST";
    public string IgstLedger { get; init; } = "Output IGST";
    public string RoundOffLedger { get; init; } = "Round Off";
    public string CashLedger { get; init; } = "Cash";
    public string BankLedger { get; init; } = "Bank Account";

    /// <summary>The group new client ledgers go under in Tally.</summary>
    public string DebtorsGroup { get; init; } = "Sundry Debtors";

    /// <summary>The company's name in Tally (empty: the company open in Tally).</summary>
    public string TallyCompany { get; init; } = "";
}
