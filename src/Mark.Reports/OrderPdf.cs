using System.Globalization;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Mark.Reports;

/// <summary>A line of a dispatch note: a design and how many of its windows go.</summary>
public sealed record OrderPaperLine(string Reference, string Description, string SizeText, int Quantity);

/// <summary>
/// What an order paper shows: the company, the order and client, the site, and the paper's own part — a dispatch note
/// (number, date, vehicle, driver, lines) or an installation certificate (date, who signed, who installed, remarks).
/// </summary>
public sealed record OrderPaper
{
    public string CompanyName { get; init; } = "";
    public string CompanyAddress { get; init; } = "";
    public string CompanyPhone { get; init; } = "";
    public string OrderNumber { get; init; } = "";
    public string QuoteNumber { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public string ClientName { get; init; } = "";
    public string ClientPhone { get; init; } = "";
    public string SiteAddress { get; init; } = "";

    /// <summary>"DN-00003" (dispatch note), or "" for the certificate.</summary>
    public string Number { get; init; } = "";
    public DateTime Date { get; init; }
    public string Vehicle { get; init; } = "";
    public string Driver { get; init; } = "";
    public string SignedBy { get; init; } = "";
    public string InstalledBy { get; init; } = "";
    public string Note { get; init; } = "";
    public IReadOnlyList<OrderPaperLine> Lines { get; init; } = Array.Empty<OrderPaperLine>();
    public string PrintedText { get; init; } = "";
}

/// <summary>
/// Writes a dispatch note (what leaves for the site, signed on receipt) or an installation certificate (the client's
/// sign-off that the windows are installed) for an order, as an A4 PDF.
/// </summary>
public static class OrderPdf
{
    private static readonly Color RuleColor = new(0x9E, 0x9B, 0x7B);
    private static readonly Color Band = new(0xDE, 0xE6, 0xF1);
    private static readonly Color Line = new(0x9A, 0xA5, 0xB1);
    private static readonly Color Muted = new(0x55, 0x5F, 0x6B);
    private const double WidthCm = 18;

    public static void WriteDispatchNote(OrderPaper paper, Stream output) => Save(Build(paper, dispatch: true), output);

    public static void WriteCertificate(OrderPaper paper, Stream output) => Save(Build(paper, dispatch: false), output);

    private static void Save(Document document, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    public static Document Build(OrderPaper p, bool dispatch)
    {
        ArgumentNullException.ThrowIfNull(p);
        string title = dispatch ? "Dispatch note" : "Installation certificate";
        var document = new Document();
        document.Info.Title = $"{title} {(dispatch ? p.Number : p.OrderNumber)}".Trim();
        document.Info.Author = p.CompanyName;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9.5;

        var section = document.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        page.LeftMargin = page.RightMargin = Unit.FromCentimeter(1.5);
        page.TopMargin = Unit.FromCentimeter(1.5);
        page.BottomMargin = Unit.FromCentimeter(1.6);
        page.FooterDistance = Unit.FromCentimeter(0.7);

        // Company and title
        var head = section.AddTable();
        head.AddColumn(Unit.FromCentimeter(10));
        head.AddColumn(Unit.FromCentimeter(WidthCm - 10));
        var row = head.AddRow();
        var company = row.Cells[0].AddParagraph(p.CompanyName);
        company.Format.Font.Bold = true;
        company.Format.Font.Size = 14;
        foreach (string text in new[] { p.CompanyAddress, p.CompanyPhone }.Where(t => t.Length > 0))
            row.Cells[0].AddParagraph(text).Format.Font.Color = Muted;
        var heading = row.Cells[1].AddParagraph(title.ToUpperInvariant());
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 15;
        heading.Format.Alignment = ParagraphAlignment.Right;
        var number = row.Cells[1].AddParagraph();
        number.Format.Alignment = ParagraphAlignment.Right;
        if (dispatch) number.AddFormattedText(p.Number, TextFormat.Bold);
        number.AddText($"{(dispatch ? "  ·  " : "")}{p.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");
        var rule = section.AddParagraph();
        rule.Format.SpaceBefore = Unit.FromPoint(6);
        rule.Format.SpaceAfter = Unit.FromPoint(10);
        rule.Format.Borders.Bottom.Width = Unit.FromPoint(1.2);
        rule.Format.Borders.Bottom.Color = RuleColor;

        // Order, client, site
        var info = section.AddTable();
        info.Borders.Width = 0.5;
        info.Borders.Color = Line;
        info.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        info.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        var a = info.AddRow();
        a.TopPadding = a.BottomPadding = Unit.FromPoint(4);
        Block(a.Cells[0], "Order", new[] { Join(p.OrderNumber, p.QuoteNumber), p.ProjectName });
        Block(a.Cells[1], "Client", new[] { p.ClientName, p.ClientPhone });
        var b = info.AddRow();
        b.TopPadding = b.BottomPadding = Unit.FromPoint(4);
        Block(b.Cells[0], "Site", new[] { p.SiteAddress.Length > 0 ? p.SiteAddress : "—" });
        Block(b.Cells[1], dispatch ? "Transport" : "Installed by",
            dispatch ? new[] { Labelled("Vehicle", p.Vehicle), Labelled("Driver", p.Driver) } : new[] { p.InstalledBy.Length > 0 ? p.InstalledBy : "—" });

        // Lines
        var space = section.AddParagraph();
        space.Format.SpaceBefore = Unit.FromPoint(10);
        var lines = section.AddTable();
        lines.Borders.Width = 0.5;
        lines.Borders.Color = Line;
        lines.AddColumn(Unit.FromCentimeter(2.2));
        lines.AddColumn(Unit.FromCentimeter(9.3));
        lines.AddColumn(Unit.FromCentimeter(4.0));
        lines.AddColumn(Unit.FromCentimeter(WidthCm - 15.5));
        var header = lines.AddRow();
        header.Shading.Color = Band;
        header.Format.Font.Bold = true;
        header.HeadingFormat = true;
        foreach (var (i, text) in new[] { "Ref.", "Description", "Size (W × H)", dispatch ? "Qty" : "Installed" }.Select((t, i) => (i, t)))
            header.Cells[i].AddParagraph(text);
        foreach (var l in p.Lines)
        {
            var r = lines.AddRow();
            r.TopPadding = r.BottomPadding = Unit.FromPoint(2);
            r.Cells[0].AddParagraph(l.Reference).Format.Font.Bold = true;
            r.Cells[1].AddParagraph(l.Description);
            r.Cells[2].AddParagraph(l.SizeText);
            var q = r.Cells[3].AddParagraph(l.Quantity.ToString(CultureInfo.InvariantCulture));
            q.Format.Alignment = ParagraphAlignment.Right;
        }
        var total = lines.AddRow();
        total.Format.Font.Bold = true;
        total.Cells[0].MergeRight = 2;
        total.Cells[0].AddParagraph(dispatch ? "Windows and doors dispatched" : "Windows and doors installed");
        var sum = total.Cells[3].AddParagraph(p.Lines.Sum(l => l.Quantity).ToString(CultureInfo.InvariantCulture));
        sum.Format.Alignment = ParagraphAlignment.Right;

        if (p.Note.Length > 0)
        {
            var note = section.AddParagraph();
            note.Format.SpaceBefore = Unit.FromPoint(10);
            note.AddFormattedText(dispatch ? "Note: " : "Remarks: ", TextFormat.Bold);
            note.AddText(p.Note);
        }

        var statement = section.AddParagraph(dispatch
            ? "Received the above in good condition. Any damage or shortage must be written on this note at delivery."
            : "The windows and doors listed above have been installed at the site and handed over. The client has checked them and accepts the installation, subject to the remarks above.");
        statement.Format.SpaceBefore = Unit.FromPoint(14);
        statement.Format.Font.Color = Muted;

        // Signatures
        var signs = section.AddTable();
        signs.AddColumn(Unit.FromCentimeter(8));
        signs.AddColumn(Unit.FromCentimeter(WidthCm - 16));
        signs.AddColumn(Unit.FromCentimeter(8));
        var gap = signs.AddRow();
        gap.Height = Unit.FromCentimeter(2.0);
        var names = signs.AddRow();
        foreach (var (cell, label, who) in new[]
                 {
                     (names.Cells[0], dispatch ? "Dispatched by" : "Installed by", dispatch ? p.CompanyName : p.InstalledBy),
                     (names.Cells[2], dispatch ? "Received by (name and signature)" : "Client (name and signature)", dispatch ? "" : p.SignedBy)
                 })
        {
            cell.Borders.Top.Width = 0.75;
            cell.Borders.Top.Color = Line;
            cell.AddParagraph(label).Format.Font.Color = Muted;
            if (who.Length > 0) cell.AddParagraph(who).Format.Font.Bold = true;
        }

        var footer = section.Footers.Primary.AddTable();
        footer.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        footer.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        var f = footer.AddRow();
        f.Cells[0].AddParagraph(p.PrintedText).Format.Font.Color = Muted;
        var mark = f.Cells[1].AddParagraph("powered by MARK");
        mark.Format.Alignment = ParagraphAlignment.Right;
        mark.Format.Font.Bold = true;
        return document;
    }

    private static void Block(Cell cell, string label, IEnumerable<string> lines)
    {
        var l = cell.AddParagraph(label.ToUpperInvariant());
        l.Format.Font.Size = 7.5;
        l.Format.Font.Color = Muted;
        foreach (string text in lines.Where(t => !string.IsNullOrWhiteSpace(t)))
            cell.AddParagraph(text);
    }

    private static string Labelled(string label, string value) => value.Length > 0 ? $"{label}: {value}" : "";

    private static string Join(params string[] parts) => string.Join("  ·  ", parts.Where(t => !string.IsNullOrWhiteSpace(t)));
}
