using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Mark.Reports;

/// <summary>A line of a design's cost sheet in the margin report: "Profit · 20 % of @Sub Total Including Labour · 3,154.20".</summary>
public sealed record MarginLine(string Name, string Formula, string Amount, bool IsTotal);

/// <summary>One design in the margin report: its cost sheet (one window) and cost, price and margin for all its windows.</summary>
public sealed record MarginDesign(string Reference, string Name, int Quantity, IReadOnlyList<MarginLine> Lines,
    string Cost, string Price, string Margin, string MarginPercent);

/// <summary>Everything the margin report shows, already worded and formatted.</summary>
public sealed record MarginReport
{
    public string Company { get; init; } = "";
    public string QuoteNumber { get; init; } = "";
    public string Project { get; init; } = "";
    public string Client { get; init; } = "";
    public string Date { get; init; } = "";
    public string Currency { get; init; } = "";

    /// <summary>What "cost" means in this report (which lines are the margin).</summary>
    public string Basis { get; init; } = "";

    public IReadOnlyList<MarginDesign> Designs { get; init; } = Array.Empty<MarginDesign>();

    /// <summary>The quote's cost, price (before charges and tax), margin and margin %.</summary>
    public IReadOnlyList<QuotationRow> Totals { get; init; } = Array.Empty<QuotationRow>();
}

/// <summary>
/// Writes a <see cref="MarginReport"/> as an A4 PDF for the company (not the client): the quote's cost, price and margin,
/// then every design's cost sheet line by line.
/// </summary>
public static class MarginPdf
{
    private static readonly Color Heading = new(0xC6, 0xD5, 0xEA);
    private static readonly Color Band = new(0xEA, 0xF2, 0xFB);
    private static readonly Color Line = new(0x9A, 0xA5, 0xB1);
    private static readonly Color Muted = new(0x55, 0x5F, 0x6B);
    private const double ContentWidthCm = 17.8;

    public static void Write(MarginReport report, Stream output)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);
        var renderer = new PdfDocumentRenderer { Document = Build(report) };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    public static Document Build(MarginReport r)
    {
        var document = new Document();
        document.Info.Title = $"Margins {r.QuoteNumber}".Trim();
        document.Info.Author = r.Company;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 8.5;
        var section = document.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        page.LeftMargin = page.RightMargin = Unit.FromCentimeter(1.6);
        page.TopMargin = Unit.FromCentimeter(1.6);
        page.BottomMargin = Unit.FromCentimeter(1.8);
        page.FooterDistance = Unit.FromCentimeter(0.7);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        footer.AddText($"{r.Company} · Margins · for internal use, not for the client · page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        var title = section.AddParagraph("Margins");
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        var sub = section.AddParagraph(string.Join("  ·  ", new[] { r.QuoteNumber, r.Project, r.Client, r.Date }.Where(t => !string.IsNullOrWhiteSpace(t))));
        sub.Format.Font.Color = Muted;
        sub.Format.SpaceAfter = Unit.FromPoint(4);
        var basis = section.AddParagraph(r.Basis);
        basis.Format.Font.Size = 7.5;
        basis.Format.Font.Color = Muted;
        basis.Format.SpaceAfter = Unit.FromPoint(10);

        // The quote at a glance.
        var totals = Table(section, 11.0, 4.8, ContentWidthCm - 15.8);
        HeadRow(totals, "Quote", "", "");
        foreach (var row in r.Totals)
        {
            var line = totals.AddRow();
            Cell(line.Cells[0], row.Label, row.Bold);
            Cell(line.Cells[1], row.Value, row.Bold).Format.Alignment = ParagraphAlignment.Right;
            Cell(line.Cells[2], row.Unit);
        }

        // Each design.
        var designs = Table(section, 1.6, 6.0, 1.4, 3.0, 3.0, ContentWidthCm - 15.0);
        designs.Rows.LeftIndent = 0;
        var head = designs.AddRow();
        head.Shading.Color = Heading;
        head.HeadingFormat = true;
        foreach (var (text, i) in new[] { "Ref.", "Design", "Qty", "Cost", "Price", "Margin" }.Select((t, i) => (t, i)))
            Cell(head.Cells[i], text, true).Format.Alignment = i >= 2 ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        foreach (var d in r.Designs)
        {
            var row = designs.AddRow();
            Cell(row.Cells[0], d.Reference, true);
            Cell(row.Cells[1], d.Name);
            Cell(row.Cells[2], d.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)).Format.Alignment = ParagraphAlignment.Right;
            Cell(row.Cells[3], d.Cost).Format.Alignment = ParagraphAlignment.Right;
            Cell(row.Cells[4], d.Price).Format.Alignment = ParagraphAlignment.Right;
            Cell(row.Cells[5], $"{d.Margin} ({d.MarginPercent})", true).Format.Alignment = ParagraphAlignment.Right;
        }

        foreach (var d in r.Designs)
        {
            var heading = section.AddParagraph($"{d.Reference}{(string.IsNullOrWhiteSpace(d.Name) ? "" : " · " + d.Name)} — cost sheet of one window ({r.Currency})");
            heading.Format.Font.Bold = true;
            heading.Format.Font.Size = 10;
            heading.Format.SpaceBefore = Unit.FromPoint(14);
            heading.Format.SpaceAfter = Unit.FromPoint(4);
            heading.Format.KeepWithNext = true;
            var sheet = Table(section, 6.2, 8.2, ContentWidthCm - 14.4);
            HeadRow(sheet, "Cost head", "Formula", "Amount");
            foreach (var line in d.Lines)
            {
                var row = sheet.AddRow();
                if (line.IsTotal) row.Shading.Color = Band;
                Cell(row.Cells[0], line.Name, line.IsTotal);
                var formula = Cell(row.Cells[1], line.Formula);
                formula.Format.Font.Name = "Courier New";
                formula.Format.Font.Color = Muted;
                Cell(row.Cells[2], line.Amount, line.IsTotal).Format.Alignment = ParagraphAlignment.Right;
            }
            sheet.Rows[0].KeepWith = Math.Min(sheet.Rows.Count - 1, 12);
        }
        return document;
    }

    private static Table Table(Section section, params double[] widthsCm)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Line;
        table.Format.Font.Size = 8;
        foreach (double cm in widthsCm) table.AddColumn(Unit.FromCentimeter(cm));
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
        return table;
    }

    private static void HeadRow(Table table, params string[] texts)
    {
        var row = table.AddRow();
        row.Shading.Color = Heading;
        row.HeadingFormat = true;
        for (int i = 0; i < texts.Length; i++)
        {
            var p = Cell(row.Cells[i], texts[i], true);
            if (i == texts.Length - 1 && texts.Length == 3 && texts[i] == "Amount") p.Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static Paragraph Cell(Cell cell, string text, bool bold = false)
    {
        var paragraph = cell.AddParagraph(text ?? "");
        paragraph.Format.Font.Bold = bold;
        return paragraph;
    }
}
