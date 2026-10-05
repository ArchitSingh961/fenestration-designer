namespace Mark.Reports;

/// <summary>Which workshop paper to print from a production order.</summary>
public enum ProductionSheet
{
    /// <summary>Profiles and steel reinforcement: bar by bar, the pieces to cut, from new bars or offcuts in stock.</summary>
    CuttingList,

    /// <summary>The glass to order from the glass supplier: type, size, quantity and area.</summary>
    GlassOrder,

    /// <summary>The hardware and accessories to take from the store.</summary>
    HardwarePickList,

    /// <summary>One page per window: drawing, size, and its pieces, glass and hardware.</summary>
    ShopDrawings,

    /// <summary>A label for every profile piece and glass pane, numbered as on the cutting list.</summary>
    Labels
}

/// <summary>
/// The workshop papers of a production order (Milestone 16), laid out by <see cref="ProductionPdf"/>. Everything is
/// worked out before: this holds the text and numbers to print.
/// </summary>
public sealed record ProductionDocument
{
    public string CompanyName { get; init; } = "";

    /// <summary>"OR-00012".</summary>
    public string OrderNumber { get; init; } = "";

    public string QuoteNumber { get; init; } = "";

    public string ProjectName { get; init; } = "";

    public string ClientName { get; init; } = "";

    /// <summary>"Due 20-10-2026", or empty.</summary>
    public string DueText { get; init; } = "";

    /// <summary>"Printed 05-10-2026 10:20".</summary>
    public string PrintedText { get; init; } = "";

    public IReadOnlyList<CutProfile> Profiles { get; init; } = Array.Empty<CutProfile>();

    /// <summary>Totals of the cutting list, e.g. "12 new bars · 3 offcuts from stock · 94 % used".</summary>
    public string CuttingSummary { get; init; } = "";

    /// <summary>Problems with the cutting list (pieces that could not be planned).</summary>
    public IReadOnlyList<string> CuttingIssues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<GlassRow> Glass { get; init; } = Array.Empty<GlassRow>();

    public IReadOnlyList<HardwareRow> Hardware { get; init; } = Array.Empty<HardwareRow>();

    public IReadOnlyList<ShopDrawing> Drawings { get; init; } = Array.Empty<ShopDrawing>();

    public IReadOnlyList<PieceLabel> Labels { get; init; } = Array.Empty<PieceLabel>();
}

/// <summary>The cutting of one profile (or steel section): the stock to take and every bar.</summary>
/// <param name="StockText">"3 × 6000 mm, 1 × 5800 mm new bars · 2 offcuts from stock".</param>
public sealed record CutProfile(string Name, string Code, bool IsSteel, string StockText, IReadOnlyList<CutBar> Bars);

/// <param name="BarText">"6000 mm" or "Offcut 1450 mm from stock".</param>
/// <param name="LeftoverText">"Offcut 820 mm → stock", "Waste 35 mm" or "".</param>
public sealed record CutBar(int Number, string BarText, bool IsOffcut, IReadOnlyList<CutPiece> Pieces, string LeftoverText);

/// <param name="Label">The piece's label number, "P12".</param>
/// <param name="Angles">"45° / 45°".</param>
/// <param name="Where">"W1 · frame top".</param>
public sealed record CutPiece(string Label, double LengthMm, string Angles, string Where);

/// <param name="Where">"W1 (2), W3 (1)": the windows the panes are for, with how many each.</param>
public sealed record GlassRow(string Name, double ThicknessMm, double WidthMm, double HeightMm, int Quantity, double AreaM2, string Where);

/// <param name="QuantityText">"12" or "4.5".</param>
/// <param name="Where">"W1, W2, W4".</param>
public sealed record HardwareRow(string Name, string Code, string Category, string QuantityText, string Unit, string Where);

/// <summary>One window of the order on its own page.</summary>
/// <param name="Lines">Its pieces, glass and hardware: (what, detail, quantity per window).</param>
public sealed record ShopDrawing(string Reference, string Name, string SizeText, int Quantity, string Details, byte[]? Drawing,
    IReadOnlyList<ShopLine> Lines);

/// <param name="Group">"Profiles", "Steel", "Glass" or "Hardware".</param>
public sealed record ShopLine(string Group, string What, string Detail, string Quantity);

/// <summary>A label to stick on a piece: order, window, what it is and its size.</summary>
/// <param name="Number">"P12" for a profile piece, "G3" for a glass pane.</param>
public sealed record PieceLabel(string Number, string OrderNumber, string Window, string What, string Size, string Detail);
