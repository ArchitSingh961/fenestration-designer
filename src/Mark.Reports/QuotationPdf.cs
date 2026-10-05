using System.Text.RegularExpressions;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Mark.Reports;

/// <summary>
/// Writes a <see cref="QuotationDocument"/> as an A4 PDF in the usual quotation layout: on every page the brand at the
/// top left, the company (logo, partner line, name, address, contact, GSTIN) at the top right, a rule and the quote line
/// (number / project / date), and in the footer the brand, "page of pages" and "powered by MARK". The pages: a covering
/// letter; the designs (code, size, name, system, location, glass; the drawing "View From Inside"; computed values;
/// profiles and accessories; remarks), about two to a page; the quote total; the terms and conditions with bank details,
/// the acceptance and both signatures; optionally one picture page.
/// </summary>
public static class QuotationPdf
{
    private static readonly Color Rule = new(0x9E, 0x9B, 0x7B);
    private static readonly Color Accent = new(0x2E, 0x75, 0xB6);
    private static readonly Color Band = new(0xDE, 0xE6, 0xF1);
    private static readonly Color Heading = new(0xC6, 0xD5, 0xEA);
    private static readonly Color Line = new(0x9A, 0xA5, 0xB1);
    private static readonly Color Muted = new(0x55, 0x5F, 0x6B);

    private const double PageWidthCm = 21.0;
    private const double SideMarginCm = 1.6;
    private const double ContentWidthCm = PageWidthCm - 2 * SideMarginCm;     // 17.8

    /// <summary>Writes the PDF to <paramref name="output"/> (left open).</summary>
    public static void Write(QuotationDocument quotation, Stream output)
    {
        ArgumentNullException.ThrowIfNull(quotation);
        ArgumentNullException.ThrowIfNull(output);
        var renderer = new PdfDocumentRenderer { Document = Build(quotation) };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    /// <summary>The number of pages the quotation takes.</summary>
    public static int CountPages(QuotationDocument quotation)
    {
        var renderer = new PdfDocumentRenderer { Document = Build(quotation) };
        renderer.RenderDocument();
        return renderer.PdfDocument.PageCount;
    }

    public static Document Build(QuotationDocument q)
    {
        var document = new Document();
        document.Info.Title = $"Quotation {q.QuoteNumber}".Trim();
        document.Info.Author = q.Company.Name;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 8.5;

        var section = document.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        page.LeftMargin = page.RightMargin = Unit.FromCentimeter(SideMarginCm);
        page.TopMargin = Unit.FromCentimeter(5.6);
        page.HeaderDistance = Unit.FromCentimeter(0.7);
        page.BottomMargin = Unit.FromCentimeter(1.9);
        page.FooterDistance = Unit.FromCentimeter(0.7);

        AddHeader(section, q);
        AddFooter(section, q);
        AddLetter(section, q);

        if (q.Designs.Count > 0)
        {
            section.AddPageBreak();
            for (int i = 0; i < q.Designs.Count; i++)
            {
                AddDesign(section, q.Designs[i]);
                section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
            }
        }

        if (q.Totals.Count > 0) AddTotals(section, q);
        AddTerms(section, q);

        if (q.ExtraPage is { } extra)
        {
            section.AddPageBreak();
            var paragraph = section.AddParagraph();
            paragraph.Format.Alignment = ParagraphAlignment.Center;
            FitImage(paragraph.AddImage(Base64(extra)), extra, ContentWidthCm, 21.0);
        }
        return document;
    }

    // ── Every page ──────────────────────────────────────────────────

    private static void AddHeader(Section section, QuotationDocument q)
    {
        var header = section.Headers.Primary;
        var table = header.AddTable();
        table.AddColumn(Unit.FromCentimeter(7.8));
        table.AddColumn(Unit.FromCentimeter(ContentWidthCm - 7.8));
        var row = table.AddRow();

        var left = row.Cells[0];
        left.VerticalAlignment = VerticalAlignment.Top;
        if (q.Brand?.Logo is { } brandLogo)
            FitImage(left.AddParagraph().AddImage(Base64(brandLogo)), brandLogo, 7.6, 2.8);
        else if (!string.IsNullOrWhiteSpace(q.Brand?.Name))
        {
            var brand = left.AddParagraph(q.Brand!.Name.ToUpperInvariant());
            brand.Format.Font.Size = 16;
            brand.Format.Font.Bold = true;
            brand.Format.Font.Color = Accent;
        }

        var right = row.Cells[1];
        right.Format.Alignment = ParagraphAlignment.Right;
        if (q.Company.Logo is { } logo)
        {
            var logoParagraph = right.AddParagraph();
            logoParagraph.Format.Alignment = ParagraphAlignment.Right;
            FitImage(logoParagraph.AddImage(Base64(logo)), logo, 4.5, 1.5);
        }
        if (!string.IsNullOrWhiteSpace(q.Company.PartnerLabel))
        {
            var partner = right.AddParagraph(q.Company.PartnerLabel.ToUpperInvariant());
            partner.Format.Font.Bold = true;
            partner.Format.Font.Size = 10.5;
            partner.Format.Font.Color = Accent;
            partner.Format.SpaceBefore = Unit.FromPoint(4);
        }
        var name = right.AddParagraph(q.Company.Name.ToUpperInvariant());
        name.Format.Font.Bold = true;
        name.Format.Font.Size = 9;
        foreach (string line in q.Company.Lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var p = right.AddParagraph(line);
            p.Format.Font.Bold = true;
            p.Format.Font.Size = 7.5;
        }

        var rule = header.AddParagraph();
        rule.Format.SpaceBefore = Unit.FromPoint(4);
        rule.Format.Borders.Bottom.Width = Unit.FromPoint(1.6);
        rule.Format.Borders.Bottom.Color = Rule;
        rule.Format.Font.Size = 2;

        var quote = header.AddParagraph(string.Join(" / ", new[]
        {
            Labelled("Quote No.", q.QuoteNumber), Labelled("Project", q.Project), Labelled("Date", q.Date)
        }.Where(t => t.Length > 0)));
        quote.Format.Alignment = ParagraphAlignment.Right;
        quote.Format.SpaceBefore = Unit.FromPoint(5);
        quote.Format.Font.Size = 8;

        static string Labelled(string label, string value) => string.IsNullOrWhiteSpace(value) ? "" : $"{label} : {value}";
    }

    private static void AddFooter(Section section, QuotationDocument q)
    {
        var table = section.Footers.Primary.AddTable();
        table.AddColumn(Unit.FromCentimeter(6.4));
        table.AddColumn(Unit.FromCentimeter(5.0));
        table.AddColumn(Unit.FromCentimeter(ContentWidthCm - 11.4));
        var row = table.AddRow();

        if (q.Brand?.Logo is { } logo)
            FitImage(row.Cells[0].AddParagraph().AddImage(Base64(logo)), logo, 3.2, 0.7);
        else if (!string.IsNullOrWhiteSpace(q.Brand?.Name))
        {
            var brand = row.Cells[0].AddParagraph(q.Brand!.Name.ToUpperInvariant());
            brand.Format.Font.Bold = true;
            brand.Format.Font.Size = 8;
            brand.Format.Font.Color = Accent;
        }

        var pages = row.Cells[1].AddParagraph();
        pages.Format.Alignment = ParagraphAlignment.Center;
        pages.Format.Font.Bold = true;
        pages.Format.Font.Size = 7.5;
        pages.AddPageField();
        pages.AddText(" of ");
        pages.AddNumPagesField();

        var powered = row.Cells[2].AddParagraph(q.PoweredBy);
        powered.Format.Alignment = ParagraphAlignment.Right;
        powered.Format.Font.Bold = true;
        powered.Format.Font.Size = 7.5;
    }

    // ── Pages ───────────────────────────────────────────────────────

    private static void AddLetter(Section section, QuotationDocument q)
    {
        var to = section.AddParagraph("To");
        to.Format.Font.Bold = true;
        to.Format.SpaceBefore = Unit.FromPoint(10);
        to.Format.SpaceAfter = Unit.FromPoint(6);
        for (int i = 0; i < q.To.Count; i++)
        {
            var line = section.AddParagraph(q.To[i]);
            if (i == 0) line.Format.Font.Bold = true;
        }

        // The company's own letter may start with its own greeting and list its own enclosures: then they are not repeated.
        bool ownGreeting = q.Letter.Count > 0 && q.Letter[0].StartsWith("Dear", StringComparison.OrdinalIgnoreCase);
        bool ownEnclosures = q.Letter.Any(IsNumbered);
        var dear = section.AddParagraph(ownGreeting ? "" : "Dear Customer,");
        dear.Format.SpaceBefore = Unit.FromCentimeter(2.2);
        dear.Format.SpaceAfter = Unit.FromPoint(ownGreeting ? 0 : 8);
        foreach (string text in q.Letter)
            section.AddParagraph(text).Format.SpaceAfter = Unit.FromPoint(9);

        var enclosures = new List<string> { "Window design, specification and value", "Terms and Conditions" };
        enclosures.AddRange(q.Sections.Select(x => x.Title));
        foreach (var (mark, text) in ownEnclosures ? Array.Empty<(string, string)>()
                     : enclosures.Select((text, i) => ($"{(char)('a' + i)}.", text)).ToArray())
        {
            var item = section.AddParagraph($"{mark}\t{text}");
            item.Format.LeftIndent = Unit.FromCentimeter(2.6);
            item.Format.FirstLineIndent = Unit.FromCentimeter(-0.5);
            item.Format.TabStops.AddTabStop(Unit.FromCentimeter(2.6));
            item.Format.SpaceAfter = Unit.FromPoint(7);
        }

        var forCompany = section.AddParagraph();
        forCompany.Format.SpaceBefore = Unit.FromPoint(12);
        forCompany.AddText("For ");
        forCompany.AddFormattedText($"{q.Company.Name} ,", TextFormat.Bold);
        var signatory = section.AddParagraph("Authorized Signatory");
        signatory.Format.SpaceBefore = Unit.FromCentimeter(1.6);
    }

    private static void AddDesign(Section section, QuotationDesign d)
    {
        // Columns: label | value (the drawing below) ‖ label | value | unit (profiles | accessories below).
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Line;
        table.Format.Font.Size = 8;
        foreach (double cm in new[] { 2.3, 4.5, 5.5, 4.3, 1.2 })
            table.AddColumn(Unit.FromCentimeter(cm));

        void HeadRow(string leftLabel, string leftValue, string rightLabel, string rightValue)
        {
            var row = table.AddRow();
            row.Shading.Color = Band;
            Text(row.Cells[0], leftLabel, bold: true);
            Text(row.Cells[1], leftValue);
            Text(row.Cells[2], rightLabel, bold: true);
            row.Cells[3].MergeRight = 1;
            Text(row.Cells[3], rightValue);
        }
        HeadRow("Code :", d.Code, "Size :", d.Size);
        HeadRow("Name :", d.Name, "Profile System :", d.System);
        HeadRow("Location :", d.Location, "Glass :", d.Glass);

        var values = table.AddRow();
        values.Cells[2].MergeRight = 2;
        values.Cells[2].Shading.Color = Heading;
        Text(values.Cells[2], "Computed Values", bold: true);
        var drawing = values.Cells[0];
        drawing.MergeRight = 1;
        drawing.MergeDown = d.Values.Count + 2;
        drawing.VerticalAlignment = VerticalAlignment.Top;
        if (d.Drawing is { } png)
        {
            var picture = drawing.AddParagraph();
            picture.Format.Alignment = ParagraphAlignment.Center;
            picture.Format.SpaceBefore = Unit.FromPoint(4);
            // The drawing sits beside the values; rows grow with long profile lists, never the other way round.
            FitImage(picture.AddImage(Base64(png)), png, 6.4, 4.6);
        }

        foreach (var value in d.Values)
        {
            var row = table.AddRow();
            Text(row.Cells[2], value.Label, bold: value.Bold);
            var amount = Text(row.Cells[3], value.Value, bold: value.Bold);
            amount.Format.Alignment = ParagraphAlignment.Right;
            Text(row.Cells[4], value.Unit);
        }

        var heads = table.AddRow();
        heads.Cells[2].Shading.Color = Heading;
        Text(heads.Cells[2], "Profile", bold: true);
        heads.Cells[3].MergeRight = 1;
        heads.Cells[3].Shading.Color = Heading;
        Text(heads.Cells[3], "Accessories", bold: true);

        var lists = table.AddRow();
        lists.HeightRule = RowHeightRule.AtLeast;
        lists.Height = Unit.FromCentimeter(2.3);                 // with the rows above, room for the drawing
        foreach (string line in d.Profiles) Text(lists.Cells[2], line);
        lists.Cells[3].MergeRight = 1;
        foreach (string line in d.Accessories) Text(lists.Cells[3], line);

        var remarks = table.AddRow();
        remarks.Cells[0].MergeRight = 1;
        var caption = Text(remarks.Cells[0], d.DrawingCaption);
        caption.Format.LeftIndent = Unit.FromCentimeter(0.8);
        remarks.Cells[2].MergeRight = 2;
        Text(remarks.Cells[2], string.IsNullOrWhiteSpace(d.Remarks) ? "Remarks :" : $"Remarks : {d.Remarks}");

        table.Rows[0].KeepWith = table.Rows.Count - 1;
    }

    private static void AddTotals(Section section, QuotationDocument q)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Line;
        table.Format.Font.Size = 8.5;
        table.AddColumn(Unit.FromCentimeter(11.0));
        table.AddColumn(Unit.FromCentimeter(5.0));
        table.AddColumn(Unit.FromCentimeter(ContentWidthCm - 16.0));

        var head = table.AddRow();
        head.Shading.Color = Heading;
        head.Cells[0].MergeRight = 2;
        Text(head.Cells[0], "Quote Total", bold: true);
        foreach (var total in q.Totals)
        {
            var row = table.AddRow();
            row.TopPadding = row.BottomPadding = Unit.FromPoint(2);
            Text(row.Cells[0], total.Label, bold: total.Bold);
            var amount = Text(row.Cells[1], total.Value, bold: total.Bold);
            amount.Format.Alignment = ParagraphAlignment.Right;
            Text(row.Cells[2], total.Unit);
        }
        table.Rows[0].KeepWith = table.Rows.Count - 1;

        var notes = section.AddParagraph(string.IsNullOrWhiteSpace(q.Notes) ? "Notes :" : $"Notes : {q.Notes}");
        notes.Format.SpaceBefore = Unit.FromPoint(14);
    }

    private static void AddTerms(Section section, QuotationDocument q)
    {
        if (q.Terms.Count == 0 && q.Sections.Count == 0 && q.Bank is null && string.IsNullOrWhiteSpace(q.Acceptance)) return;
        section.AddPageBreak();
        AddPoints(section, "Terms and Conditions", q.Terms, first: true);
        foreach (var more in q.Sections)
            AddPoints(section, more.Title, more.Points, first: false);

        if (q.Bank is { Rows.Count: > 0 } bank)
        {
            var heading = section.AddParagraph("Bank Details :");
            heading.Format.Font.Bold = true;
            heading.Format.LeftIndent = Unit.FromCentimeter(1.0);
            heading.Format.SpaceBefore = Unit.FromPoint(6);
            heading.Format.SpaceAfter = Unit.FromPoint(4);
            var table = section.AddTable();
            table.Rows.LeftIndent = Unit.FromCentimeter(1.0);
            table.AddColumn(Unit.FromCentimeter(3.0));
            table.AddColumn(Unit.FromCentimeter(0.5));
            table.AddColumn(Unit.FromCentimeter(8.0));
            foreach (var r in bank.Rows)
            {
                var row = table.AddRow();
                row.Cells[0].AddParagraph(r.Label);
                row.Cells[1].AddParagraph(":");
                row.Cells[2].AddParagraph(r.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(q.Acceptance))
        {
            var accept = section.AddParagraph(q.Acceptance);
            accept.Format.SpaceBefore = Unit.FromPoint(18);
        }
        var signatures = section.AddTable();
        signatures.AddColumn(Unit.FromCentimeter(ContentWidthCm / 2));
        signatures.AddColumn(Unit.FromCentimeter(ContentWidthCm / 2));
        var signRow = signatures.AddRow();
        signRow.TopPadding = Unit.FromCentimeter(1.6);
        signRow.Cells[0].AddParagraph("Authorized Signatory");
        var customer = signRow.Cells[1].AddParagraph("Signature of Customer");
        customer.Format.Alignment = ParagraphAlignment.Right;
    }

    /// <summary>
    /// A titled list: "Warranty:-" and its points, numbered 1., 2. … unless the company numbered or lettered them itself
    /// ("1. Payment terms:", "a. 100% advance …"): then they are printed as written, and its own headings in bold.
    /// </summary>
    private static void AddPoints(Section section, string heading, IReadOnlyList<string> points, bool first)
    {
        if (points.Count == 0) return;
        var title = section.AddParagraph();
        title.AddFormattedText($"{heading}:-", new Font { Bold = true, Underline = Underline.Single });
        title.Format.SpaceBefore = Unit.FromPoint(first ? 0 : 14);
        title.Format.SpaceAfter = Unit.FromPoint(8);
        title.Format.KeepWithNext = true;

        bool asWritten = points.Any(IsNumbered);
        for (int i = 0; i < points.Count; i++)
        {
            string text = points[i];
            var point = asWritten ? section.AddParagraph(text) : section.AddParagraph($"{i + 1}.\t{text}");
            point.Format.SpaceAfter = Unit.FromPoint(asWritten ? 4 : 6);
            if (asWritten)
            {
                bool sub = Regex.IsMatch(text, @"^([a-z]|[ivx]+)[.)]\s");                        // a. b. i. ii. under a point
                point.Format.LeftIndent = Unit.FromCentimeter(sub ? 1.0 : 0.45);
                if (!IsNumbered(text) && text.EndsWith(':')) point.Format.Font.Bold = true;     // "Bank Details :"
            }
            else
            {
                point.Format.LeftIndent = Unit.FromCentimeter(1.0);
                point.Format.FirstLineIndent = Unit.FromCentimeter(-0.55);
                point.Format.TabStops.AddTabStop(Unit.FromCentimeter(1.0));
            }
        }
    }

    /// <summary>A line that starts with its own number or letter: "1.", "2)", "a.", "b)", "iv.".</summary>
    private static bool IsNumbered(string line) => Regex.IsMatch(line.TrimStart(), @"^(\d{1,3}|[a-zA-Z]|[ivxIVX]{1,4})[.)]\s");

    // ── Helpers ─────────────────────────────────────────────────────

    private static Paragraph Text(Cell cell, string text, bool bold = false)
    {
        var paragraph = cell.AddParagraph(text ?? "");
        paragraph.Format.Font.Bold = bold;
        return paragraph;
    }

    private static string Base64(byte[] image) => "base64:" + Convert.ToBase64String(image);

    /// <summary>Scales an image to fit <paramref name="maxWidthCm"/> × <paramref name="maxHeightCm"/>, keeping its shape.</summary>
    private static void FitImage(MigraDoc.DocumentObjectModel.Shapes.Image image, byte[] bytes, double maxWidthCm, double maxHeightCm)
    {
        image.LockAspectRatio = true;
        var (width, height) = PixelSize(bytes);
        if (width <= 0 || height <= 0 || (double)width / height >= maxWidthCm / maxHeightCm)
            image.Width = Unit.FromCentimeter(maxWidthCm);
        else
            image.Height = Unit.FromCentimeter(maxHeightCm);
    }

    /// <summary>The pixel size of a PNG or JPEG (0 × 0 when it cannot be read).</summary>
    internal static (int Width, int Height) PixelSize(byte[] bytes)
    {
        if (bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
            return ((bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19],
                    (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
        if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < bytes.Length)
            {
                if (bytes[i] != 0xFF) { i++; continue; }
                byte marker = bytes[i + 1];
                int length = (bytes[i + 2] << 8) | bytes[i + 3];
                if (marker is >= 0xC0 and <= 0xC3)
                    return ((bytes[i + 7] << 8) | bytes[i + 8], (bytes[i + 5] << 8) | bytes[i + 6]);
                i += 2 + length;
            }
        }
        return (0, 0);
    }
}
