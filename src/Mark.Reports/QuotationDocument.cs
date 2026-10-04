namespace Mark.Reports;

/// <summary>The company the quotation is from: shown at the top right of every page.</summary>
/// <param name="PartnerLabel">A line above the name, e.g. "AUTHORISED PARTNER" (empty: none).</param>
/// <param name="Lines">Address, contact, e-mail, website and GSTIN lines, as printed.</param>
public sealed record QuotationCompany(string Name, string PartnerLabel, IReadOnlyList<string> Lines, byte[]? Logo);

/// <summary>The brand shown at the top left of every page (e.g. the profile system's maker), and small in the footer.</summary>
public sealed record QuotationBrand(string Name, byte[]? Logo);

/// <summary>A label, value and unit row, e.g. "Unit Price · 4,586.43 · Rs.".</summary>
public sealed record QuotationRow(string Label, string Value, string Unit = "", bool Bold = false);

/// <summary>One design (window type) of the quotation: its details, drawing, values, profiles and accessories.</summary>
public sealed record QuotationDesign
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
    public string Size { get; init; } = "";
    public string System { get; init; } = "";
    public string Glass { get; init; } = "";

    /// <summary>The drawing with its dimensions, as PNG (null: none).</summary>
    public byte[]? Drawing { get; init; }

    public string DrawingCaption { get; init; } = "View From Inside";

    /// <summary>Area per window, value per area, unit price, quantity and value.</summary>
    public IReadOnlyList<QuotationRow> Values { get; init; } = Array.Empty<QuotationRow>();

    public IReadOnlyList<string> Profiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Accessories { get; init; } = Array.Empty<string>();

    public string Remarks { get; init; } = "";
}

/// <summary>Bank details printed with the terms.</summary>
public sealed record QuotationBank(IReadOnlyList<QuotationRow> Rows);

/// <summary>
/// Everything a quotation PDF shows, already worded and formatted: no calculation happens when it is written
/// (<see cref="QuotationPdf"/>). Built from the quote by MARK.
/// </summary>
public sealed record QuotationDocument
{
    public required QuotationCompany Company { get; init; }

    public QuotationBrand? Brand { get; init; }

    public string QuoteNumber { get; init; } = "";
    public string Project { get; init; } = "";
    public string Date { get; init; } = "";

    /// <summary>The client's name and address lines (after "To").</summary>
    public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();

    /// <summary>The covering letter, paragraph by paragraph (after "Dear Customer,").</summary>
    public IReadOnlyList<string> Letter { get; init; } = Array.Empty<string>();

    public IReadOnlyList<QuotationDesign> Designs { get; init; } = Array.Empty<QuotationDesign>();

    /// <summary>The quote total: components, area, basic value … grand total, averages.</summary>
    public IReadOnlyList<QuotationRow> Totals { get; init; } = Array.Empty<QuotationRow>();

    public string Notes { get; init; } = "";

    /// <summary>Terms and conditions, one per item (numbered when printed).</summary>
    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();

    public QuotationBank? Bank { get; init; }

    /// <summary>The client's acceptance sentence above the signatures.</summary>
    public string Acceptance { get; init; } = "";

    /// <summary>A last page with one picture (e.g. care instructions), or null.</summary>
    public byte[]? ExtraPage { get; init; }

    /// <summary>Bottom right of every page.</summary>
    public string PoweredBy { get; init; } = "powered by MARK";
}
