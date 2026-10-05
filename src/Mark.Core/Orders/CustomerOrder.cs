namespace Mark.Core.Orders;

/// <summary>Where an order is, from confirmation to installation, in order.</summary>
public enum OrderStage
{
    Confirmed,
    InProduction,
    Ready,
    Dispatched,
    Installed,
    Closed
}

/// <summary>What a payment is for.</summary>
public enum PaymentKind
{
    Advance,
    Stage,
    Final,
    Other
}

/// <summary>How a payment was made.</summary>
public enum PaymentMethod
{
    Cash,
    BankTransfer,
    Upi,
    Cheque,
    Card,
    Other
}

/// <summary>A visit to the site in the schedule.</summary>
public enum VisitKind
{
    Delivery,
    Installation,
    SiteVisit
}

/// <summary>A payment received for an order.</summary>
public sealed record OrderPayment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The day it was received.</summary>
    public DateTime Date { get; init; }

    public decimal Amount { get; init; }

    public PaymentKind Kind { get; init; } = PaymentKind.Advance;

    public PaymentMethod Method { get; init; } = PaymentMethod.BankTransfer;

    /// <summary>Cheque number, transaction id…</summary>
    public string Reference { get; init; } = "";

    public string Note { get; init; } = "";

    public string RecordedBy { get; init; } = "";

    /// <summary>"RCPT-00001" once a receipt has been made for it (Accounts › Receipts), else empty.</summary>
    public string ReceiptNumber { get; init; } = "";
}

/// <summary>A delivery, installation or site visit in the order's schedule.</summary>
public sealed record OrderVisit
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public VisitKind Kind { get; init; } = VisitKind.Delivery;

    /// <summary>The day.</summary>
    public DateTime Date { get; init; }

    /// <summary>"10:30" or "morning"; free text.</summary>
    public string Time { get; init; } = "";

    /// <summary>Who goes: the team or the person.</summary>
    public string Team { get; init; } = "";

    public string Note { get; init; } = "";

    public bool Done { get; init; }
}

/// <summary>A design on a dispatch note and how many of its windows go.</summary>
public sealed record DispatchLine
{
    public Guid FrameId { get; init; }

    /// <summary>"W1".</summary>
    public string Reference { get; init; } = "";

    /// <summary>"Sliding window · Living room".</summary>
    public string Description { get; init; } = "";

    /// <summary>"1200 × 1500 mm".</summary>
    public string SizeText { get; init; } = "";

    public int Quantity { get; init; }
}

/// <summary>A dispatch note: what left the workshop for the site, on which vehicle, on which day.</summary>
public sealed record DispatchNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>"DN-00001".</summary>
    public string Number { get; init; } = "";

    public DateTime Date { get; init; }

    public string Vehicle { get; init; } = "";

    public string Driver { get; init; } = "";

    public string Note { get; init; } = "";

    public IReadOnlyList<DispatchLine> Lines { get; init; } = Array.Empty<DispatchLine>();

    public string CreatedBy { get; init; } = "";

    public int WindowCount => Lines.Sum(l => l.Quantity);
}

/// <summary>The client's sign-off that the installation is complete.</summary>
public sealed record InstallationSignOff
{
    public DateTime Date { get; init; }

    /// <summary>Who signed for the client.</summary>
    public string SignedBy { get; init; } = "";

    /// <summary>Who installed (team or person).</summary>
    public string InstalledBy { get; init; } = "";

    /// <summary>Snags, pending work or comments.</summary>
    public string Remarks { get; init; } = "";

    public string RecordedBy { get; init; } = "";
}

/// <summary>A change of stage: when and by whom.</summary>
public sealed record StageChange(OrderStage Stage, DateTime AtUtc, string By);

/// <summary>
/// A customer order (Milestone 17): a quote converted to an order, followed from confirmation to installation — its
/// stage, the payments received (advance, stage, final), the delivery and installation schedule, the dispatch notes and
/// the installation sign-off. The designs and the price stay with the quote; the value is the quote's.
/// </summary>
public sealed class CustomerOrder
{
    public static IReadOnlyList<OrderStage> Stages { get; } = Enum.GetValues<OrderStage>();

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The quote the order is.</summary>
    public Guid ProjectId { get; init; }

    /// <summary>"OR-00012".</summary>
    public string OrderNumber { get; set; } = "";

    /// <summary>"QT-00031 R1".</summary>
    public string QuoteNumber { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public string ClientName { get; set; } = "";

    public string ClientPhone { get; set; } = "";

    /// <summary>Where the windows go.</summary>
    public string SiteAddress { get; set; } = "";

    /// <summary>The order's value (the quote's total), or null when the quote was not priced.</summary>
    public decimal? Value { get; set; }

    public string Currency { get; set; } = "";

    /// <summary>When the quote became an order.</summary>
    public DateTime ConfirmedUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public OrderStage Stage { get; set; } = OrderStage.Confirmed;

    public List<StageChange> History { get; set; } = new();

    public List<OrderPayment> Payments { get; set; } = new();

    public List<OrderVisit> Visits { get; set; } = new();

    public List<DispatchNote> Dispatches { get; set; } = new();

    public InstallationSignOff? SignOff { get; set; }

    public string Notes { get; set; } = "";

    /// <summary>Received so far.</summary>
    public decimal Paid => Payments.Sum(p => p.Amount);

    /// <summary>Still to receive (never below zero), or null when the value is not known.</summary>
    public decimal? Balance => Value is { } value ? Math.Max(0, value - Paid) : null;

    /// <summary>Received over the value, 0–1 (0 without a value).</summary>
    public double PaidFraction => Value is > 0 and { } value ? (double)Math.Min(1m, Paid / value) : 0;

    /// <summary>How many windows of a design have been dispatched.</summary>
    public int DispatchedOf(Guid frameId) => Dispatches.SelectMany(d => d.Lines).Where(l => l.FrameId == frameId).Sum(l => l.Quantity);

    /// <summary>Moves the order to a stage (recorded in the history). Nothing happens when it is there already.</summary>
    public void SetStage(OrderStage stage, DateTime atUtc, string by)
    {
        if (stage == Stage && History.Count > 0) return;
        Stage = stage;
        History.Add(new StageChange(stage, atUtc, by));
    }

    /// <summary>When the order reached a stage last, or null.</summary>
    public DateTime? ReachedUtc(OrderStage stage) => History.LastOrDefault(h => h.Stage == stage)?.AtUtc;

    public static string StageName(OrderStage stage) => stage switch
    {
        OrderStage.Confirmed => "Confirmed",
        OrderStage.InProduction => "In production",
        OrderStage.Ready => "Ready",
        OrderStage.Dispatched => "Dispatched",
        OrderStage.Installed => "Installed",
        _ => "Closed"
    };

    public static string KindName(PaymentKind kind) => kind switch
    {
        PaymentKind.Advance => "Advance",
        PaymentKind.Stage => "Stage payment",
        PaymentKind.Final => "Final payment",
        _ => "Other"
    };

    public static string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.BankTransfer => "Bank transfer",
        PaymentMethod.Upi => "UPI",
        PaymentMethod.Cheque => "Cheque",
        PaymentMethod.Card => "Card",
        _ => "Other"
    };

    public static string VisitName(VisitKind kind) => kind switch
    {
        VisitKind.Delivery => "Delivery",
        VisitKind.Installation => "Installation",
        _ => "Site visit"
    };

    /// <summary>The number after the last dispatch note number in use: "DN-00001", "DN-00002"…</summary>
    public static string NextDispatchNumber(IEnumerable<string> used)
    {
        int last = used.Select(n => n.StartsWith("DN-", StringComparison.Ordinal) && int.TryParse(n[3..], out int x) ? x : 0)
            .DefaultIfEmpty(0).Max();
        return $"DN-{last + 1:00000}";
    }
}
