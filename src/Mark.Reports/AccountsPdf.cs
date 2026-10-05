using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Mark.Reports;

/// <summary>A line of a tax invoice, formatted.</summary>
public sealed record InvoicePaperLine(string Description, string Hsn, string Quantity, string Unit, string Rate, string Taxable);

/// <summary>Everything a tax invoice shows, already worded and formatted.</summary>
public sealed record InvoicePaper
{
    public string Title { get; init; } = "TAX INVOICE";
    public string CompanyName { get; init; } = "";
    public IReadOnlyList<string> CompanyLines { get; init; } = Array.Empty<string>();
    public byte[]? Logo { get; init; }
    public string Number { get; init; } = "";
    public string Date { get; init; } = "";
    public string OrderNumber { get; init; } = "";
    public string PlaceOfSupply { get; init; } = "";
    public string ReverseCharge { get; init; } = "No";
    public string BuyerName { get; init; } = "";
    public IReadOnlyList<string> BuyerLines { get; init; } = Array.Empty<string>();
    public string ShipTo { get; init; } = "";
    public IReadOnlyList<InvoicePaperLine> Lines { get; init; } = Array.Empty<InvoicePaperLine>();

    /// <summary>Taxable value, CGST/SGST or IGST, round off, total: label, amount.</summary>
    public IReadOnlyList<(string Label, string Amount, bool Bold)> Totals { get; init; } = Array.Empty<(string, string, bool)>();

    public string AmountInWords { get; init; } = "";
    public IReadOnlyList<string> BankLines { get; init; } = Array.Empty<string>();
    public string Notes { get; init; } = "";
    public bool IsCancelled { get; init; }
}

/// <summary>Everything a payment receipt shows, already worded and formatted.</summary>
public sealed record ReceiptPaper
{
    public string CompanyName { get; init; } = "";
    public IReadOnlyList<string> CompanyLines { get; init; } = Array.Empty<string>();
    public byte[]? Logo { get; init; }
    public string Number { get; init; } = "";
    public string Date { get; init; } = "";
    public string ReceivedFrom { get; init; } = "";
    public string Amount { get; init; } = "";
    public string AmountInWords { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Against { get; init; } = "";
    public string Kind { get; init; } = "";
    public string OrderValue { get; init; } = "";
    public string ReceivedSoFar { get; init; } = "";
    public string Balance { get; init; } = "";
    public string Note { get; init; } = "";
}

/// <summary>Writes GST tax invoices and payment receipts as A4 PDFs.</summary>
public static class AccountsPdf
{
    private static readonly Color RuleColor = new(0x9E, 0x9B, 0x7B);
    private static readonly Color Band = new(0xDE, 0xE6, 0xF1);
    private static readonly Color Line = new(0x9A, 0xA5, 0xB1);
    private static readonly Color Muted = new(0x55, 0x5F, 0x6B);
    private const double WidthCm = 18;

    public static void WriteInvoice(InvoicePaper paper, Stream output) => Save(Invoice(paper), output);

    public static void WriteReceipt(ReceiptPaper paper, Stream output) => Save(Receipt(paper), output);

    private static void Save(Document document, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    private static (Document, Section) Start(string title, string author)
    {
        var document = new Document();
        document.Info.Title = title;
        document.Info.Author = author;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 8.5;
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.4);
        return (document, section);
    }

    private static void Head(Section section, string company, IReadOnlyList<string> lines, byte[]? logo, string title, IEnumerable<string> right)
    {
        var table = section.AddTable();
        table.AddColumn(Unit.FromCentimeter(11));
        table.AddColumn(Unit.FromCentimeter(WidthCm - 11));
        var row = table.AddRow();
        if (logo is { Length: > 0 })
        {
            var image = row.Cells[0].AddParagraph().AddImage("base64:" + Convert.ToBase64String(logo));
            image.LockAspectRatio = true;
            image.Height = Unit.FromCentimeter(1.3);
        }
        var name = row.Cells[0].AddParagraph(company.ToUpperInvariant());
        name.Format.Font.Bold = true;
        name.Format.Font.Size = 13;
        foreach (string line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            row.Cells[0].AddParagraph(line).Format.Font.Color = Muted;
        var t = row.Cells[1].AddParagraph(title);
        t.Format.Alignment = ParagraphAlignment.Right;
        t.Format.Font.Bold = true;
        t.Format.Font.Size = 15;
        foreach (string line in right.Where(l => !string.IsNullOrWhiteSpace(l)))
            row.Cells[1].AddParagraph(line).Format.Alignment = ParagraphAlignment.Right;
        var rule = section.AddParagraph();
        rule.Format.Borders.Bottom.Width = Unit.FromPoint(1.4);
        rule.Format.Borders.Bottom.Color = RuleColor;
        rule.Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static Document Invoice(InvoicePaper p)
    {
        var (document, section) = Start($"{p.Title} {p.Number}", p.CompanyName);
        Head(section, p.CompanyName, p.CompanyLines, p.Logo, p.IsCancelled ? $"{p.Title} (CANCELLED)" : p.Title,
            new[] { $"Invoice No.: {p.Number}", $"Date: {p.Date}", p.OrderNumber.Length > 0 ? $"Order: {p.OrderNumber}" : "" });

        var parties = section.AddTable();
        parties.Borders.Width = Unit.FromPoint(0.5);
        parties.Borders.Color = Line;
        parties.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        parties.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        var head = parties.AddRow();
        head.Shading.Color = Band;
        head.Cells[0].AddParagraph("Bill to").Format.Font.Bold = true;
        head.Cells[1].AddParagraph("Supply details").Format.Font.Bold = true;
        var row = parties.AddRow();
        row.Cells[0].AddParagraph(p.BuyerName).Format.Font.Bold = true;
        foreach (string line in p.BuyerLines.Where(l => !string.IsNullOrWhiteSpace(l))) row.Cells[0].AddParagraph(line);
        row.Cells[1].AddParagraph($"Place of supply: {p.PlaceOfSupply}");
        row.Cells[1].AddParagraph($"Reverse charge: {p.ReverseCharge}");
        if (p.ShipTo.Length > 0) row.Cells[1].AddParagraph($"Site: {p.ShipTo}");
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);

        var lines = section.AddTable();
        lines.Borders.Width = Unit.FromPoint(0.5);
        lines.Borders.Color = Line;
        foreach (double cm in new[] { 0.8, 7.6, 1.6, 1.5, 1.2, 2.4, WidthCm - 15.1 })
            lines.AddColumn(Unit.FromCentimeter(cm));
        var h = lines.AddRow();
        h.Shading.Color = Band;
        h.HeadingFormat = true;
        string[] heads = { "#", "Description", "HSN/SAC", "Qty", "Unit", "Rate", "Taxable value" };
        for (int i = 0; i < heads.Length; i++)
        {
            var c = h.Cells[i].AddParagraph(heads[i]);
            c.Format.Font.Bold = true;
            if (i is 3 or 5 or 6) c.Format.Alignment = ParagraphAlignment.Right;
        }
        for (int n = 0; n < p.Lines.Count; n++)
        {
            var l = p.Lines[n];
            var r = lines.AddRow();
            r.Cells[0].AddParagraph((n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            r.Cells[1].AddParagraph(l.Description);
            r.Cells[2].AddParagraph(l.Hsn);
            r.Cells[3].AddParagraph(l.Quantity).Format.Alignment = ParagraphAlignment.Right;
            r.Cells[4].AddParagraph(l.Unit);
            r.Cells[5].AddParagraph(l.Rate).Format.Alignment = ParagraphAlignment.Right;
            r.Cells[6].AddParagraph(l.Taxable).Format.Alignment = ParagraphAlignment.Right;
        }
        foreach (var (label, amount, bold) in p.Totals)
        {
            var r = lines.AddRow();
            r.Cells[0].MergeRight = 5;
            var lp = r.Cells[0].AddParagraph(label);
            lp.Format.Alignment = ParagraphAlignment.Right;
            lp.Format.Font.Bold = bold;
            var ap = r.Cells[6].AddParagraph(amount);
            ap.Format.Alignment = ParagraphAlignment.Right;
            ap.Format.Font.Bold = bold;
            if (bold) r.Shading.Color = Band;
        }

        var words = section.AddParagraph($"Amount in words: {p.AmountInWords}");
        words.Format.SpaceBefore = Unit.FromPoint(8);
        words.Format.Font.Bold = true;
        if (p.BankLines.Count > 0)
        {
            var bank = section.AddParagraph("Bank details");
            bank.Format.SpaceBefore = Unit.FromPoint(10);
            bank.Format.Font.Bold = true;
            foreach (string line in p.BankLines) section.AddParagraph(line);
        }
        if (!string.IsNullOrWhiteSpace(p.Notes))
        {
            var notes = section.AddParagraph($"Notes: {p.Notes}");
            notes.Format.SpaceBefore = Unit.FromPoint(8);
        }
        var declaration = section.AddParagraph("We declare that this invoice shows the actual price of the goods described and that all particulars are true and correct.");
        declaration.Format.SpaceBefore = Unit.FromPoint(10);
        declaration.Format.Font.Size = 7.5;
        declaration.Format.Font.Color = Muted;
        Signatures(section, "Customer's signature", $"For {p.CompanyName}\nAuthorised Signatory");
        return document;
    }

    private static Document Receipt(ReceiptPaper p)
    {
        var (document, section) = Start($"Receipt {p.Number}", p.CompanyName);
        Head(section, p.CompanyName, p.CompanyLines, p.Logo, "PAYMENT RECEIPT", new[] { $"Receipt No.: {p.Number}", $"Date: {p.Date}" });
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Line;
        table.AddColumn(Unit.FromCentimeter(5));
        table.AddColumn(Unit.FromCentimeter(WidthCm - 5));
        foreach (var (label, value, bold) in new[]
                 {
                     ("Received with thanks from", p.ReceivedFrom, true), ("The sum of", p.Amount, true), ("In words", p.AmountInWords, false),
                     ("Payment", p.Kind, false), ("Mode", p.Mode, false), ("Reference", p.Reference, false), ("Against", p.Against, false),
                     ("Order value", p.OrderValue, false), ("Received so far", p.ReceivedSoFar, false), ("Balance", p.Balance, true),
                     ("Note", p.Note, false)
                 }.Where(x => !string.IsNullOrWhiteSpace(x.Item2)))
        {
            var r = table.AddRow();
            r.TopPadding = r.BottomPadding = Unit.FromPoint(3);
            r.Cells[0].Shading.Color = Band;
            r.Cells[0].AddParagraph(label).Format.Font.Bold = true;
            r.Cells[1].AddParagraph(value).Format.Font.Bold = bold;
        }
        var note = section.AddParagraph("Subject to realisation of the cheque / transfer.");
        note.Format.SpaceBefore = Unit.FromPoint(8);
        note.Format.Font.Size = 7.5;
        note.Format.Font.Color = Muted;
        Signatures(section, "", $"For {p.CompanyName}\nAuthorised Signatory");
        return document;
    }

    private static void Signatures(Section section, string left, string right)
    {
        var sign = section.AddTable();
        sign.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        sign.AddColumn(Unit.FromCentimeter(WidthCm / 2));
        var s = sign.AddRow();
        s.TopPadding = Unit.FromCentimeter(1.8);
        s.Cells[0].AddParagraph(left);
        s.Cells[1].AddParagraph(right).Format.Alignment = ParagraphAlignment.Right;
    }
}
