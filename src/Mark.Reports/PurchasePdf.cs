using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Mark.Reports;

/// <summary>A line of a purchase order paper: item, quantity and unit, rate and amount, already formatted.</summary>
public sealed record PurchasePaperLine(string Item, string Quantity, string Unit, string Rate, string Amount);

/// <summary>What a purchase order paper shows: the company, the supplier, the order and its lines.</summary>
public sealed record PurchasePaper
{
    public string CompanyName { get; init; } = "";
    public IReadOnlyList<string> CompanyLines { get; init; } = Array.Empty<string>();
    public string SupplierName { get; init; } = "";
    public IReadOnlyList<string> SupplierLines { get; init; } = Array.Empty<string>();
    public string Number { get; init; } = "";
    public string Date { get; init; } = "";
    public string Expected { get; init; } = "";
    public string ForOrders { get; init; } = "";
    public string Currency { get; init; } = "";
    public IReadOnlyList<PurchasePaperLine> Lines { get; init; } = Array.Empty<PurchasePaperLine>();
    public string Total { get; init; } = "";
    public string Note { get; init; } = "";
}

/// <summary>Writes a purchase order to a supplier as an A4 PDF.</summary>
public static class PurchasePdf
{
    private static readonly Color RuleColor = new(0x9E, 0x9B, 0x7B);
    private static readonly Color Band = new(0xDE, 0xE6, 0xF1);
    private static readonly Color Line = new(0x9A, 0xA5, 0xB1);
    private static readonly Color Muted = new(0x55, 0x5F, 0x6B);
    private const double WidthCm = 18;

    public static void Write(PurchasePaper paper, Stream output)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(output);
        var renderer = new PdfDocumentRenderer { Document = Build(paper) };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    public static Document Build(PurchasePaper p)
    {
        var document = new Document();
        document.Info.Title = $"Purchase order {p.Number}".Trim();
        document.Info.Author = p.CompanyName;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

        var head = section.AddTable();
        head.AddColumn(Unit.FromCentimeter(10));
        head.AddColumn(Unit.FromCentimeter(WidthCm - 10));
        var row = head.AddRow();
        var company = row.Cells[0].AddParagraph(p.CompanyName.ToUpperInvariant());
        company.Format.Font.Bold = true;
        company.Format.Font.Size = 13;
        foreach (string line in p.CompanyLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            row.Cells[0].AddParagraph(line).Format.Font.Color = Muted;
        var title = row.Cells[1].AddParagraph("PURCHASE ORDER");
        title.Format.Alignment = ParagraphAlignment.Right;
        title.Format.Font.Bold = true;
        title.Format.Font.Size = 15;
        foreach (string line in new[] { p.Number, $"Date: {p.Date}", p.Expected.Length > 0 ? $"Wanted by: {p.Expected}" : "" }.Where(l => l.Length > 0))
        {
            var t = row.Cells[1].AddParagraph(line);
            t.Format.Alignment = ParagraphAlignment.Right;
        }

        var rule = section.AddParagraph();
        rule.Format.Borders.Bottom.Width = Unit.FromPoint(1.4);
        rule.Format.Borders.Bottom.Color = RuleColor;
        rule.Format.SpaceAfter = Unit.FromPoint(10);

        var to = section.AddParagraph("To");
        to.Format.Font.Color = Muted;
        var supplier = section.AddParagraph(p.SupplierName);
        supplier.Format.Font.Bold = true;
        supplier.Format.Font.Size = 11;
        foreach (string line in p.SupplierLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            section.AddParagraph(line);
        if (p.ForOrders.Length > 0)
        {
            var orders = section.AddParagraph($"For orders: {p.ForOrders}");
            orders.Format.SpaceBefore = Unit.FromPoint(6);
            orders.Format.Font.Color = Muted;
        }
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Line;
        foreach (double cm in new[] { 0.9, 8.1, 2.2, 1.4, 2.6, WidthCm - 15.2 })
            table.AddColumn(Unit.FromCentimeter(cm));
        var h = table.AddRow();
        h.Shading.Color = Band;
        h.HeadingFormat = true;
        string[] heads = { "#", "Item", "Quantity", "Unit", $"Rate {p.Currency}".Trim(), $"Amount {p.Currency}".Trim() };
        for (int i = 0; i < heads.Length; i++)
        {
            var cell = h.Cells[i].AddParagraph(heads[i]);
            cell.Format.Font.Bold = true;
            if (i is 2 or 4 or 5) cell.Format.Alignment = ParagraphAlignment.Right;
        }
        for (int n = 0; n < p.Lines.Count; n++)
        {
            var l = p.Lines[n];
            var r = table.AddRow();
            r.Cells[0].AddParagraph((n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            r.Cells[1].AddParagraph(l.Item);
            r.Cells[2].AddParagraph(l.Quantity).Format.Alignment = ParagraphAlignment.Right;
            r.Cells[3].AddParagraph(l.Unit);
            r.Cells[4].AddParagraph(l.Rate).Format.Alignment = ParagraphAlignment.Right;
            r.Cells[5].AddParagraph(l.Amount).Format.Alignment = ParagraphAlignment.Right;
        }
        var total = table.AddRow();
        total.Cells[0].MergeRight = 4;
        var label = total.Cells[0].AddParagraph("Total (before tax)");
        label.Format.Font.Bold = true;
        label.Format.Alignment = ParagraphAlignment.Right;
        var amount = total.Cells[5].AddParagraph(p.Total);
        amount.Format.Font.Bold = true;
        amount.Format.Alignment = ParagraphAlignment.Right;

        if (!string.IsNullOrWhiteSpace(p.Note))
        {
            var note = section.AddParagraph($"Note: {p.Note}");
            note.Format.SpaceBefore = Unit.FromPoint(10);
        }
        var sign = section.AddTable();
        sign.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        sign.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        var s = sign.AddRow();
        s.TopPadding = Unit.FromCentimeter(2.0);
        s.Cells[0].AddParagraph("Received by supplier");
        var by = s.Cells[1].AddParagraph($"For {p.CompanyName}\nAuthorized Signatory");
        by.Format.Alignment = ParagraphAlignment.Right;
        return document;
    }
}
