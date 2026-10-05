using System.Globalization;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Mark.Reports;

/// <summary>
/// Writes the workshop papers of a production order as A4 PDFs: the cutting list, the glass order, the hardware pick
/// list, shop drawings (a page per window) and piece labels (3 × 7 on a sheet). Every page but the labels has the
/// company, the paper's name and the order (number, quote, client, due date) at the top, and the printing time and
/// "page of pages" at the bottom.
/// </summary>
public static class ProductionPdf
{
    private static readonly Color RuleColor = new(0x9E, 0x9B, 0x7B);
    private static readonly Color Band = new(0xDE, 0xE6, 0xF1);
    private static readonly Color SteelBand = new(0xE9, 0xE4, 0xD8);
    private static readonly Color Offcut = new(0xE8, 0xF6, 0xEE);
    private static readonly Color Line = new(0x9A, 0xA5, 0xB1);
    private static readonly Color Muted = new(0x55, 0x5F, 0x6B);

    private const double SideMarginCm = 1.5;
    private const double ContentWidthCm = 21.0 - 2 * SideMarginCm;     // 18

    public static string Title(ProductionSheet sheet) => sheet switch
    {
        ProductionSheet.CuttingList => "Cutting list",
        ProductionSheet.GlassOrder => "Glass order",
        ProductionSheet.HardwarePickList => "Hardware pick list",
        ProductionSheet.ShopDrawings => "Shop drawings",
        _ => "Piece labels"
    };

    /// <summary>Writes one paper to <paramref name="output"/> (left open).</summary>
    public static void Write(ProductionDocument document, ProductionSheet sheet, Stream output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        var renderer = new PdfDocumentRenderer { Document = Build(document, sheet) };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    public static int CountPages(ProductionDocument document, ProductionSheet sheet)
    {
        var renderer = new PdfDocumentRenderer { Document = Build(document, sheet) };
        renderer.RenderDocument();
        return renderer.PdfDocument.PageCount;
    }

    public static Document Build(ProductionDocument d, ProductionSheet sheet)
    {
        var document = new Document();
        document.Info.Title = $"{Title(sheet)} {d.OrderNumber}".Trim();
        document.Info.Author = d.CompanyName;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 8.5;

        if (sheet == ProductionSheet.Labels)
        {
            AddLabels(document, d);
            return document;
        }

        var section = document.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        page.LeftMargin = page.RightMargin = Unit.FromCentimeter(SideMarginCm);
        page.TopMargin = Unit.FromCentimeter(3.4);
        page.HeaderDistance = Unit.FromCentimeter(0.9);
        page.BottomMargin = Unit.FromCentimeter(1.6);
        page.FooterDistance = Unit.FromCentimeter(0.7);
        AddHeader(section, d, Title(sheet));
        AddFooter(section, d);

        switch (sheet)
        {
            case ProductionSheet.CuttingList: AddCuttingList(section, d); break;
            case ProductionSheet.GlassOrder: AddGlassOrder(section, d); break;
            case ProductionSheet.HardwarePickList: AddHardware(section, d); break;
            default: AddShopDrawings(section, d); break;
        }
        return document;
    }

    // ── Every page ──────────────────────────────────────────────────

    private static void AddHeader(Section section, ProductionDocument d, string title)
    {
        var table = section.Headers.Primary.AddTable();
        table.AddColumn(Unit.FromCentimeter(6.0));
        table.AddColumn(Unit.FromCentimeter(5.5));
        table.AddColumn(Unit.FromCentimeter(ContentWidthCm - 11.5));
        var row = table.AddRow();
        var company = row.Cells[0].AddParagraph(d.CompanyName);
        company.Format.Font.Bold = true;
        company.Format.Font.Size = 11;
        var heading = row.Cells[1].AddParagraph(title.ToUpperInvariant());
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 13;
        heading.Format.Alignment = ParagraphAlignment.Center;
        var order = row.Cells[2].AddParagraph();
        order.Format.Alignment = ParagraphAlignment.Right;
        order.AddFormattedText(d.OrderNumber, TextFormat.Bold);
        if (d.QuoteNumber.Length > 0) order.AddText($"  ·  {d.QuoteNumber}");
        foreach (string text in new[] { Join(d.ClientName, d.ProjectName), d.DueText }.Where(t => t.Length > 0))
        {
            var p = row.Cells[2].AddParagraph(text);
            p.Format.Alignment = ParagraphAlignment.Right;
        }
        var rule = section.Headers.Primary.AddParagraph();
        rule.Format.SpaceBefore = Unit.FromPoint(4);
        rule.Format.Borders.Bottom.Width = Unit.FromPoint(1.2);
        rule.Format.Borders.Bottom.Color = RuleColor;
    }

    private static void AddFooter(Section section, ProductionDocument d)
    {
        var table = section.Footers.Primary.AddTable();
        table.AddColumn(Unit.FromCentimeter(ContentWidthCm / 3));
        table.AddColumn(Unit.FromCentimeter(ContentWidthCm / 3));
        table.AddColumn(Unit.FromCentimeter(ContentWidthCm / 3));
        var row = table.AddRow();
        row.Cells[0].AddParagraph(d.PrintedText).Format.Font.Color = Muted;
        var pages = row.Cells[1].AddParagraph();
        pages.Format.Alignment = ParagraphAlignment.Center;
        pages.AddPageField();
        pages.AddText(" of ");
        pages.AddNumPagesField();
        var mark = row.Cells[2].AddParagraph("powered by MARK");
        mark.Format.Alignment = ParagraphAlignment.Right;
        mark.Format.Font.Bold = true;
    }

    // ── Cutting list ────────────────────────────────────────────────

    private static void AddCuttingList(Section section, ProductionDocument d)
    {
        Intro(section, d.CuttingSummary,
            "Cut the bars in order, longest piece first. Stick each piece's label (P…) on it. Offcuts from stock are marked green.");
        foreach (string issue in d.CuttingIssues)
        {
            var p = section.AddParagraph($"Not planned: {issue}");
            p.Format.Font.Color = new Color(0xA3, 0x20, 0x20);
            p.Format.SpaceAfter = Unit.FromPoint(3);
        }
        if (d.Profiles.Count == 0)
        {
            section.AddParagraph("Nothing to cut.");
            return;
        }

        // Profiles first, then the steel.
        foreach (var profile in d.Profiles.OrderBy(p => p.IsSteel))
        {
            var heading = section.AddParagraph();
            heading.Format.SpaceBefore = Unit.FromPoint(10);
            heading.Format.SpaceAfter = Unit.FromPoint(3);
            heading.Format.KeepWithNext = true;
            heading.Format.Shading.Color = profile.IsSteel ? SteelBand : Band;
            heading.Format.LeftIndent = Unit.FromPoint(2);
            heading.AddFormattedText(profile.IsSteel ? "STEEL  " : "", TextFormat.Bold);
            heading.AddFormattedText(profile.Name, TextFormat.Bold);
            if (profile.Code.Length > 0) heading.AddText($"  ({profile.Code})");
            heading.AddText($"   —   {profile.StockText}");

            var table = Table(section, new[] { 1.2, 3.6, 1.4, 2.0, 2.2, 4.2, 3.4 },
                new[] { "Bar", "From", "Label", "Length mm", "Angles", "For", "Leftover" });
            foreach (var bar in profile.Bars)
            {
                for (int i = 0; i < bar.Pieces.Count; i++)
                {
                    var piece = bar.Pieces[i];
                    var row = table.AddRow();
                    if (i == 0)
                    {
                        Cell(row.Cells[0], bar.Number.ToString(CultureInfo.InvariantCulture), bold: true).Format.Alignment = ParagraphAlignment.Center;
                        Cell(row.Cells[1], bar.BarText);
                        Cell(row.Cells[6], bar.LeftoverText);
                        foreach (int c in new[] { 0, 1, 6 })
                        {
                            row.Cells[c].MergeDown = bar.Pieces.Count - 1;
                            if (bar.IsOffcut) row.Cells[c].Shading.Color = Offcut;
                        }
                        row.Borders.Top.Width = Unit.FromPoint(0.9);
                    }
                    Cell(row.Cells[2], piece.Label, bold: true);
                    Cell(row.Cells[3], Mm(piece.LengthMm), bold: true).Format.Alignment = ParagraphAlignment.Right;
                    Cell(row.Cells[4], piece.Angles).Format.Alignment = ParagraphAlignment.Center;
                    Cell(row.Cells[5], piece.Where);
                }
            }
        }
    }

    // ── Glass order ─────────────────────────────────────────────────

    private static void AddGlassOrder(Section section, ProductionDocument d)
    {
        int panes = d.Glass.Sum(g => g.Quantity);
        double area = d.Glass.Sum(g => g.AreaM2 * g.Quantity);
        Intro(section, d.Glass.Count == 0 ? "No glass." : $"{panes} pane{(panes == 1 ? "" : "s")} · {Area(area)} m² in all",
            "Sizes are the glass sizes to cut (width × height), already allowing for the frame and sash.");
        if (d.Glass.Count == 0) return;
        var table = Table(section, new[] { 0.8, 5.0, 1.6, 1.8, 1.8, 1.1, 1.6, 1.7, 2.6 },
            new[] { "#", "Glass", "mm", "Width mm", "Height mm", "Qty", "m² each", "m² total", "For" });
        int n = 0;
        foreach (var group in d.Glass.GroupBy(g => g.Name))
        {
            foreach (var g in group)
            {
                var row = table.AddRow();
                Cell(row.Cells[0], (++n).ToString(CultureInfo.InvariantCulture));
                Cell(row.Cells[1], g.Name, bold: true);
                Cell(row.Cells[2], Mm(g.ThicknessMm)).Format.Alignment = ParagraphAlignment.Right;
                Cell(row.Cells[3], Mm(g.WidthMm), bold: true).Format.Alignment = ParagraphAlignment.Right;
                Cell(row.Cells[4], Mm(g.HeightMm), bold: true).Format.Alignment = ParagraphAlignment.Right;
                Cell(row.Cells[5], g.Quantity.ToString(CultureInfo.InvariantCulture), bold: true).Format.Alignment = ParagraphAlignment.Right;
                Cell(row.Cells[6], Area(g.AreaM2)).Format.Alignment = ParagraphAlignment.Right;
                Cell(row.Cells[7], Area(g.AreaM2 * g.Quantity)).Format.Alignment = ParagraphAlignment.Right;
                Cell(row.Cells[8], g.Where);
            }
            var sub = table.AddRow();
            sub.Shading.Color = Band;
            int count = group.Sum(g => g.Quantity);
            Cell(sub.Cells[1], $"{group.Key}: {count} pane{(count == 1 ? "" : "s")}", bold: true);
            sub.Cells[1].MergeRight = 3;
            Cell(sub.Cells[7], Area(group.Sum(g => g.AreaM2 * g.Quantity)), bold: true).Format.Alignment = ParagraphAlignment.Right;
        }
    }

    // ── Hardware ────────────────────────────────────────────────────

    private static void AddHardware(Section section, ProductionDocument d)
    {
        Intro(section, d.Hardware.Count == 0 ? "No hardware." : $"{d.Hardware.Count} item{(d.Hardware.Count == 1 ? "" : "s")}",
            "Quantities are for the whole order (every window, times its quantity). Tick each line when it is taken from the store.");
        if (d.Hardware.Count == 0) return;
        var table = Table(section, new[] { 0.8, 5.4, 2.3, 2.3, 1.8, 1.4, 2.6, 1.4 },
            new[] { "#", "Item", "Code", "Kind", "Quantity", "Unit", "For", "Taken" });
        int n = 0;
        foreach (var h in d.Hardware)
        {
            var row = table.AddRow();
            Cell(row.Cells[0], (++n).ToString(CultureInfo.InvariantCulture));
            Cell(row.Cells[1], h.Name, bold: true);
            Cell(row.Cells[2], h.Code);
            Cell(row.Cells[3], h.Category);
            Cell(row.Cells[4], h.QuantityText, bold: true).Format.Alignment = ParagraphAlignment.Right;
            Cell(row.Cells[5], h.Unit);
            Cell(row.Cells[6], h.Where);                                   // "Taken" is left blank, to tick by hand
        }
    }

    // ── Shop drawings ───────────────────────────────────────────────

    private static void AddShopDrawings(Section section, ProductionDocument d)
    {
        if (d.Drawings.Count == 0)
        {
            section.AddParagraph("No windows.");
            return;
        }
        for (int i = 0; i < d.Drawings.Count; i++)
        {
            var w = d.Drawings[i];
            if (i > 0) section.AddPageBreak();
            var title = section.AddParagraph();
            title.Format.Font.Size = 13;
            title.AddFormattedText(w.Reference, TextFormat.Bold);
            if (w.Name.Length > 0) title.AddText($"   {w.Name}");
            var size = section.AddParagraph();
            size.Format.SpaceAfter = Unit.FromPoint(2);
            size.AddFormattedText(w.SizeText, TextFormat.Bold);
            size.AddText($"   ·   {w.Quantity} window{(w.Quantity == 1 ? "" : "s")}");
            if (w.Details.Length > 0)
            {
                var details = section.AddParagraph(w.Details);
                details.Format.Font.Color = Muted;
            }

            if (w.Drawing is { } png)
            {
                var picture = section.AddParagraph();
                picture.Format.Alignment = ParagraphAlignment.Center;
                picture.Format.SpaceBefore = Unit.FromPoint(8);
                picture.Format.SpaceAfter = Unit.FromPoint(4);
                var image = picture.AddImage("base64:" + Convert.ToBase64String(png));
                image.LockAspectRatio = true;
                image.Height = Unit.FromCentimeter(10.5);
                var caption = section.AddParagraph("View from inside");
                caption.Format.Alignment = ParagraphAlignment.Center;
                caption.Format.Font.Color = Muted;
                caption.Format.SpaceAfter = Unit.FromPoint(8);
            }

            var table = Table(section, new[] { 2.2, 7.6, 6.0, 2.2 }, new[] { "", "Item", "Size / detail", "Per window" });
            foreach (var group in w.Lines.GroupBy(l => l.Group))
            {
                bool first = true;
                foreach (var line in group)
                {
                    var row = table.AddRow();
                    if (first) Cell(row.Cells[0], group.Key, bold: true);
                    first = false;
                    Cell(row.Cells[1], line.What);
                    Cell(row.Cells[2], line.Detail);
                    Cell(row.Cells[3], line.Quantity).Format.Alignment = ParagraphAlignment.Right;
                }
            }
        }
    }

    // ── Labels: 3 × 7 on an A4 sheet (70 × 40 mm, 6 mm top and bottom margins) ─

    private static void AddLabels(Document document, ProductionDocument d)
    {
        var section = document.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        page.LeftMargin = page.RightMargin = Unit.FromCentimeter(0);
        page.TopMargin = page.BottomMargin = Unit.FromCentimeter(0.6);           // 7 rows of 4 cm fit with room to spare
        if (d.Labels.Count == 0)
        {
            section.AddParagraph("No labels.");
            return;
        }
        var table = section.AddTable();
        for (int c = 0; c < 3; c++) table.AddColumn(Unit.FromCentimeter(7.0));
        Row? row = null;
        for (int i = 0; i < d.Labels.Count; i++)
        {
            if (i % 3 == 0)
            {
                row = table.AddRow();
                row.Height = Unit.FromCentimeter(4.0);
                row.HeightRule = RowHeightRule.Exactly;
            }
            var label = d.Labels[i];
            var cell = row!.Cells[i % 3];
            cell.Borders.Width = Unit.FromPoint(0.25);
            cell.Borders.Color = new Color(0xD0, 0xD5, 0xDC);
            var top = cell.AddParagraph();
            top.Format.LeftIndent = top.Format.RightIndent = Unit.FromCentimeter(0.35);
            top.Format.SpaceBefore = Unit.FromCentimeter(0.3);
            top.Format.TabStops.AddTabStop(Unit.FromCentimeter(6.3), TabAlignment.Right);
            top.AddFormattedText(label.Number, new Font { Bold = true, Size = 14 });
            top.AddTab();
            top.AddFormattedText(label.OrderNumber, new Font { Bold = true, Size = 9 });
            foreach (var (text, bold, size) in new[] { (label.Window, false, 9.0), (label.What, true, 9.5), (label.Size, true, 12.0), (label.Detail, false, 8.0) })
            {
                if (text.Length == 0) continue;
                var p = cell.AddParagraph(text);
                p.Format.LeftIndent = p.Format.RightIndent = Unit.FromCentimeter(0.35);
                p.Format.Font.Bold = bold;
                p.Format.Font.Size = size;
                p.Format.SpaceBefore = Unit.FromPoint(1.5);
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void Intro(Section section, string summary, string help)
    {
        var p = section.AddParagraph(summary);
        p.Format.Font.Bold = true;
        p.Format.Font.Size = 10;
        var h = section.AddParagraph(help);
        h.Format.Font.Color = Muted;
        h.Format.SpaceAfter = Unit.FromPoint(6);
    }

    private static Table Table(Section section, double[] widthsCm, string[] headings)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.4);
        table.Borders.Color = Line;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(3);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(2);
        foreach (double w in widthsCm) table.AddColumn(Unit.FromCentimeter(w));
        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = new Color(0xC6, 0xD5, 0xEA);
        for (int i = 0; i < headings.Length; i++) Cell(head.Cells[i], headings[i], bold: true);
        return table;
    }

    private static Paragraph Cell(Cell cell, string text, bool bold = false)
    {
        var paragraph = cell.AddParagraph(text ?? "");
        paragraph.Format.Font.Bold = bold;
        return paragraph;
    }

    private static string Mm(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Area(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Join(params string[] parts) => string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
