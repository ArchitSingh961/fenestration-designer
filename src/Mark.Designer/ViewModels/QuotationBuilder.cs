using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Designer.Rendering;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>What a quotation is made from: the quote, its calculation and price, the library and the company's setup.</summary>
public sealed record QuotationInputs(
    Project Project,
    CalculationResult Calculation,
    QuotePrice Price,
    IProductLibrary Library,
    DesignRules Rules,
    QuotationSettings Settings,
    string CompanyName,
    byte[]? CompanyLogo,
    DateTime Date);

/// <summary>
/// Turns a quote into a <see cref="QuotationDocument"/>: the company's details and letter, one block per design (code,
/// size, name, system, location, glass by pane, its drawing with dimensions, area and value per area, unit price,
/// quantity and value, profiles and hardware), the quote total and the terms. Drawings are rendered with the same
/// renderer as the canvas (glass looks included), so the quotation shows exactly what was designed.
/// </summary>
public static class QuotationBuilder
{
    private const double SquareFeetPerSquareMetre = 10.763910416709722;
    private static readonly CultureInfo Indian = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>The setup with the details, brand, bank and last page the MARK supplier set for the company.</summary>
    public static QuotationSettings WithProfile(QuotationSettings s, Mark.Licensing.Api.QuotationProfile p) => s with
    {
        PartnerLabel = p.PartnerLabel, Address = p.Address, Phone = p.Phone, Email = p.Email, Website = p.Website, Gstin = p.Gstin,
        BrandName = p.BrandName, BrandLogoBase64 = p.BrandLogoBase64, BankAccountName = p.BankAccountName,
        BankAccountNumber = p.BankAccountNumber, BankName = p.BankName, BankIfsc = p.BankIfsc, BankBranch = p.BankBranch,
        ExtraPageBase64 = p.ExtraPageBase64
    };

    public static QuotationDocument Build(QuotationInputs i)
    {
        ArgumentNullException.ThrowIfNull(i);
        var s = i.Settings;
        string areaUnit = s.AreaUnit == AreaUnit.SquareFeet ? "Sq.Ft." : "m²";
        string money = s.CurrencyLabel;
        var client = i.Project.Quote.Client;

        var designs = new List<QuotationDesign>();
        double totalArea = 0;
        int pieces = 0;
        for (int n = 0; n < i.Project.Frames.Count; n++)
        {
            var frame = i.Project.Frames[n];
            var price = i.Price.FindDesign(frame.Id);
            double area = Area(frame.Width * frame.Height / 1_000_000.0, s.AreaUnit);
            int quantity = Math.Max(1, frame.Design.Quantity);
            totalArea += area * quantity;
            pieces += quantity;
            designs.Add(new QuotationDesign
            {
                Code = string.IsNullOrWhiteSpace(frame.Design.Reference) ? $"W{n + 1}" : frame.Design.Reference,
                Name = frame.Design.Name,
                Location = string.Join(", ", new[] { frame.Design.Location, frame.Design.Floor }.Where(t => !string.IsNullOrWhiteSpace(t))),
                Size = $"W = {Number(frame.Width)}; H = {Number(frame.Height)}",
                System = i.Library.FindSystem(frame.SystemId)?.Name ?? "",
                Glass = GlassText(frame, i.Library),
                Drawing = Drawing(frame, i.Rules, i.Library),
                Values = new[]
                {
                    new QuotationRow($"{areaUnit} per window", Number(area), areaUnit),
                    new QuotationRow($"Value per {areaUnit}", price is null || area <= 0 ? "—" : Money(price.UnitPrice / (decimal)area), money),
                    new QuotationRow("Unit Price", price is null ? "—" : Money(price.UnitPrice), money),
                    new QuotationRow("Quantity", quantity.ToString(CultureInfo.InvariantCulture), "Pcs"),
                    new QuotationRow("Value", price is null ? "—" : Money(price.Total), money, Bold: true)
                },
                Profiles = Profiles(frame, i.Calculation),
                Accessories = Accessories(frame, i.Calculation),
                Remarks = frame.Design.Note
            });
        }

        var totals = new List<QuotationRow>
        {
            new("No. of Components", pieces.ToString(CultureInfo.InvariantCulture), "Pcs", Bold: true),
            new("Total Area", Number(totalArea), areaUnit, Bold: true),
            new("Basic Value", Money(i.Price.BasicValue), money, Bold: true)
        };
        foreach (var line in i.Price.Summary)
        {
            switch (line.Kind)
            {
                case PriceSummaryKind.Discount when line.Amount != 0:
                case PriceSummaryKind.Charge:
                case PriceSummaryKind.Tax:
                    totals.Add(new QuotationRow(line.Name, Money(line.Amount), money));
                    break;
                case PriceSummaryKind.SubTotal:
                    totals.Add(new QuotationRow("Sub Total", Money(line.Amount), money, Bold: true));
                    break;
                case PriceSummaryKind.Total:
                    totals.Add(new QuotationRow("Total Project Cost", Money(line.Amount), money, Bold: true));
                    break;
                case PriceSummaryKind.GrandTotal:
                    totals.Add(new QuotationRow("Grand Total", Money(line.Amount), money, Bold: true));
                    break;
            }
        }
        if (totalArea > 0)
        {
            bool gst = i.Price.Summary.Any(l => l.Kind == PriceSummaryKind.Tax && l.Name.Contains("GST", StringComparison.OrdinalIgnoreCase));
            totals.Add(new QuotationRow($"Average Price per {areaUnit} without {(gst ? "GST" : "tax")}", Money(i.Price.Total / (decimal)totalArea), money));
            totals.Add(new QuotationRow($"Average Price per {areaUnit}", Money(i.Price.GrandTotal / (decimal)totalArea), money));
        }

        var bank = new[]
        {
            new QuotationRow("Account Name", s.BankAccountName), new QuotationRow("Account Number", s.BankAccountNumber),
            new QuotationRow("Bank Name", s.BankName), new QuotationRow("IFSC", s.BankIfsc), new QuotationRow("Branch", s.BankBranch)
        }.Where(r => !string.IsNullOrWhiteSpace(r.Value)).ToList();

        return new QuotationDocument
        {
            Company = new QuotationCompany(i.CompanyName, s.PartnerLabel, CompanyLines(s), i.CompanyLogo),
            Brand = string.IsNullOrWhiteSpace(s.BrandName) && s.BrandLogoBase64 is null ? null
                : new QuotationBrand(s.BrandName, Image(s.BrandLogoBase64)),
            QuoteNumber = i.Project.Quote.Number,
            Project = i.Project.Name,
            Date = i.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            To = new[]
                {
                    client.DisplayName,
                    client.Company.Trim() != client.DisplayName ? client.Company : "",
                    client.AddressLine1, client.AddressLine2,
                    string.Join(" ", new[] { client.City, client.PostalCode }.Where(t => !string.IsNullOrWhiteSpace(t))),
                    string.Join(", ", new[] { client.State, client.Country }.Where(t => !string.IsNullOrWhiteSpace(t))),
                    string.IsNullOrWhiteSpace(client.Phone) ? "" : $"Phone : {client.Phone}"
                }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList(),
            Letter = QuotationSettings.LinesOf(s.Letter),
            Designs = designs,
            Totals = totals,
            Notes = s.Notes,
            Terms = QuotationSettings.LinesOf(s.Terms),
            Sections = new[]
                {
                    new QuotationSection("Cancellation Policy", QuotationSettings.LinesOf(s.CancellationPolicy)),
                    new QuotationSection("Warranty", QuotationSettings.LinesOf(s.Warranty)),
                    new QuotationSection("Pre-requisites for Installation", QuotationSettings.LinesOf(s.InstallationPrerequisites))
                }.Where(x => x.Points.Count > 0).ToList(),
            Bank = bank.Count == 0 ? null : new QuotationBank(bank),
            Acceptance = s.Acceptance,
            ExtraPage = Image(s.ExtraPageBase64)
        };
    }

    private static IReadOnlyList<string> CompanyLines(QuotationSettings s)
        => QuotationSettings.LinesOf(s.Address)
            .Concat(new[]
            {
                string.IsNullOrWhiteSpace(s.Phone) ? "" : $"Contact No. : {s.Phone}",
                string.IsNullOrWhiteSpace(s.Email) ? "" : $"Email : {s.Email}",
                string.IsNullOrWhiteSpace(s.Website) ? "" : $"Website : {s.Website}",
                string.IsNullOrWhiteSpace(s.Gstin) ? "" : $"GSTIN : {s.Gstin}"
            })
            .Where(l => l.Length > 0).ToList();

    /// <summary>"(1,2) 6mm Toughened Clear; (3) 5mm Frosted Toughened": the glass of each pane, by pane number.</summary>
    public static string GlassText(Frame frame, IProductLibrary library)
        => string.Join("; ", frame.GlassPanels
            .Select((g, index) => (Number: index + 1, Name: (g.GlassDefinitionId is null ? library.DefaultGlass : library.FindGlass(g.GlassDefinitionId))?.Name ?? "Glass"))
            .GroupBy(g => g.Name)
            .Select(g => $"({string.Join(",", g.Select(x => x.Number))}) {g.Key}"));

    /// <summary>"Outer : 62mm uPVC Casement Frame", "Reinforcement : …", "Mesh : Yes" for one design.</summary>
    private static IReadOnlyList<string> Profiles(Frame frame, CalculationResult calculation)
    {
        var lines = new List<string> { $"Mesh : {(frame.GlassPanels.Any(g => g.HasMesh) ? "Yes" : "No")}" };
        lines.AddRange(calculation.Profiles.Where(p => p.FrameId == frame.Id && p.IsResolved)
            .Select(p => $"{RoleName(p.Role)} : {p.Name}").Distinct());
        return lines;
    }

    private static IReadOnlyList<string> Accessories(Frame frame, CalculationResult calculation)
        => calculation.Materials.Where(m => m.FrameId == frame.Id && m.Category == MaterialCategory.Hardware)
            .GroupBy(m => m.Name)
            .Select(g => $"{g.Key} × {Number(g.Sum(m => m.Quantity), "0.##")}")
            .ToList();

    private static string RoleName(ProfileType role) => role switch
    {
        ProfileType.Frame => "Outer",
        ProfileType.MeshSash => "Mesh sash",
        ProfileType.GlazingBead => "Glazing bead",
        _ => role.ToString()
    };

    /// <summary>The design's drawing with its dimensions and opening numbers, as PNG (white background).</summary>
    private static byte[]? Drawing(Frame frame, DesignRules rules, IProductLibrary library)
    {
        const double width = 380, height = 300, margin = 46;
        try
        {
            var options = FrameRenderOptions.Drawing with { Reference = false, FloorLine = false, GlassSizes = false };
            var picture = DesignThumbnails.Render(frame, rules, width - 2 * margin, height - 2 * margin, options,
                id => DesignListViewModel.GlassLookIn(library, id));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawImage(picture, new Rect(margin, margin, width - 2 * margin, height - 2 * margin));
            }
            var bitmap = new RenderTargetBitmap((int)(width * 3), (int)(height * 3), 288, 288, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;                                          // the quotation still shows the design's values
        }
    }

    private static double Area(double squareMetres, AreaUnit unit)
        => unit == AreaUnit.SquareFeet ? squareMetres * SquareFeetPerSquareMetre : squareMetres;

    private static byte[]? Image(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Number(double value, string format = "N2") => value.ToString(format, Indian);

    private static string Money(decimal value) => value.ToString("N2", Indian);
}
