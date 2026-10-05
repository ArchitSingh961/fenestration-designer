namespace Mark.Core.Quotes;

/// <summary>Where a quote's document is kept, as in the quote's Documents tab.</summary>
public enum DocumentCategory
{
    /// <summary>The site survey before production (measurements, photos).</summary>
    PreProductionSurvey,

    /// <summary>Quotation PDFs sent to the client (each one MARK writes is kept here).</summary>
    Quotations,

    /// <summary>Margin reports: cost and price of every design.</summary>
    Margins,

    /// <summary>Credit approval for orders on credit.</summary>
    CreditApproval,

    /// <summary>The sales order or contract signed with the client.</summary>
    SalesOrder,

    /// <summary>Anything else.</summary>
    Others,

    /// <summary>The designs (typologies) of each saved revision, and files about them.</summary>
    TypologyHistory
}

/// <summary>A file kept with a quote (the file itself is stored separately, see the document repository).</summary>
/// <param name="FileName">The file's name with its extension, as uploaded or written.</param>
/// <param name="Size">The file's size in bytes.</param>
/// <param name="AddedBy">Who added it (the signed-in user), or "MARK" for files MARK wrote itself.</param>
public sealed record ProjectDocument(Guid Id, Guid ProjectId, DocumentCategory Category, string Name, string FileName,
    DateTime AddedUtc, string AddedBy, long Size, string Note = "")
{
    /// <summary>The largest file kept with a quote: 25 MB.</summary>
    public const long MaxSize = 25L * 1024 * 1024;

    public static string CategoryName(DocumentCategory category) => category switch
    {
        DocumentCategory.PreProductionSurvey => "Pre Production Survey Report",
        DocumentCategory.Quotations => "Quotations",
        DocumentCategory.Margins => "Margins",
        DocumentCategory.CreditApproval => "Credit Approval",
        DocumentCategory.SalesOrder => "Sales Order/Contract",
        DocumentCategory.Others => "Others",
        DocumentCategory.TypologyHistory => "Typology History",
        _ => category.ToString()
    };

    /// <summary>"1.2 MB", "640 KB", "12 bytes".</summary>
    public static string SizeText(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes"
    };
}
