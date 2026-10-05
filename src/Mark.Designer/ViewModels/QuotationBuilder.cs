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
                Profiles = Profiles(frame, i.Calculation, i.Library),
                Accessories = Accessories(frame, i.Calculation, i.Library),
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
                    totals.Add(new QuotationRow(line.Name, Money(line.Amount), money));
                    break;
                case PriceSummaryKind.Tax:
                    // "Gst @18%", as on the usual quotation.
                    string taxName = string.IsNullOrWhiteSpace(i.Project.Pricing.TaxName) ? "Tax" : i.Project.Pricing.TaxName.Trim();
                    totals.Add(new QuotationRow($"{taxName} @{i.Project.Pricing.TaxPercent.ToString("0.##", CultureInfo.InvariantCulture)}%",
                        Money(line.Amount), money));
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

    /// <summary>
    /// The profile list of one design, as on the usual quotation: "Profile Color : White", "MeshType : (3,4) SS Flymesh",
    /// then each profile by what it is ("OUTER : 62mm Casement Frame", "SLIDING SASH : …", "INTERLOCK : …"), each followed
    /// by its reinforcement ("OUTER RI : …").
    /// </summary>
    public static IReadOnlyList<string> Profiles(Frame frame, CalculationResult calculation, IProductLibrary library)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(frame.Design.ProfileColour))
            lines.Add($"Profile Color : {frame.Design.ProfileColour}");
        var mesh = frame.GlassPanels.Select((g, index) => (g, Number: index + 1)).Where(x => x.g.HasMesh).Select(x => x.Number).ToList();
        lines.Add(mesh.Count == 0 ? "MeshType : No"
            : $"MeshType : ({string.Join(",", mesh)}) {(string.IsNullOrWhiteSpace(frame.Design.MeshType) ? "Mesh" : frame.Design.MeshType)}");

        var openings = frame.GlassPanels.ToDictionary(g => g.Id, g => g.Opening);
        var mine = calculation.Profiles.Where(p => p.FrameId == frame.Id && p.IsResolved).ToList();
        bool IsReinforcement(ProfileLine p) => p.Role == ProfileType.Reinforcement || p.PartOf == "Reinforcement";

        // (what it is, for ordering) → lines; reinforcement goes right after what it reinforces.
        var entries = new List<(string Group, bool Ri, string Text)>();
        foreach (var line in mine.Where(p => !IsReinforcement(p)))
        {
            string label = RoleLabel(line, openings);
            entries.Add((label, false, $"{label} : {line.Name}"));
        }
        foreach (var steel in mine.Where(IsReinforcement))
        {
            var host = mine.FirstOrDefault(p => !IsReinforcement(p) && p.ProfileId == steel.ProfileId && p.OpeningId == steel.OpeningId
                                                && library.FindProfile(p.DefinitionId)?.Reinforcement?.ProfileId == steel.DefinitionId);
            string label = host is null ? "PROFILE" : RoleLabel(host, openings);
            entries.Add((label, true, $"{label} RI : {steel.Name}"));
        }
        var order = entries.Select(e => e.Group).Distinct().ToList();
        lines.AddRange(entries.OrderBy(e => order.IndexOf(e.Group)).ThenBy(e => e.Ri).Select(e => e.Text).Distinct());
        return lines;
    }

    /// <summary>What a profile is, in capitals as on the quotation: OUTER, TRACK, MULLION, SLIDING SASH, FLYMESH SASH …</summary>
    private static string RoleLabel(ProfileLine line, IReadOnlyDictionary<Guid, OpeningType> openings)
    {
        bool sliding = line.OpeningId is { } id && openings.TryGetValue(id, out var opening) && opening.IsSliding();
        return line.Role switch
        {
            ProfileType.Frame => "OUTER",
            ProfileType.Track => "TRACK",
            ProfileType.Mullion => "MULLION",
            ProfileType.Transom => "TRANSOM",
            ProfileType.Sash => sliding ? "SLIDING SASH" : "CASEMENT SASH",
            ProfileType.MeshSash => "FLYMESH SASH",
            ProfileType.Interlock => "INTERLOCK",
            ProfileType.Coupler => "COUPLER",
            ProfileType.GlazingBead => "GLAZING BEAD",
            _ when !string.IsNullOrWhiteSpace(line.PartOf) => line.PartOf!.ToUpperInvariant(),
            _ => "PROFILE"
        };
    }

    /// <summary>
    /// The hardware of one design, as on the usual quotation: "Locking : Multi-point", "Handle color : White", then each
    /// item by kind with the sashes it is on ("Hinge : S1-3D Hinges", "Handle Type : S1,S2-Espag Handle").
    /// </summary>
    public static IReadOnlyList<string> Accessories(Frame frame, CalculationResult calculation, IProductLibrary library)
    {
        var lines = new List<string>();
        // Sashes are numbered S1, S2 … in pane order, as on the drawing.
        var sashes = calculation.Openings.Where(o => o.FrameId == frame.Id && o.HasSash).Select(o => o.GlassPanelId).ToList();
        var openingOf = calculation.Openings.Where(o => o.FrameId == frame.Id).ToDictionary(o => o.GlassPanelId, o => o.Opening);
        if (sashes.Count > 0)
            lines.Add("Locking : " + string.Join(", ", sashes.Select(id => Locking(openingOf[id]))));
        if (!string.IsNullOrWhiteSpace(frame.Design.HandleColour))
            lines.Add($"Handle color : {frame.Design.HandleColour}");

        var hardware = calculation.Materials.Where(m => m.FrameId == frame.Id && m.Category == MaterialCategory.Hardware).ToList();
        foreach (var item in hardware.GroupBy(m => m.MaterialId))
        {
            var first = item.First();
            var on = item.Select(m => sashes.IndexOf(m.SourceId)).Where(n => n >= 0).Distinct().OrderBy(n => n).Select(n => $"S{n + 1}").ToList();
            string kind = HardwareKind(library.FindMaterial(first.MaterialId), first.Name);
            lines.Add(on.Count == 0 ? $"{kind} : {first.Name}" : $"{kind} : {string.Join(",", on)}-{first.Name}");
        }
        return lines;
    }

    /// <summary>Sliding, hung and pivot sashes usually lock at one point; side-hung and tilt &amp; turn at several.</summary>
    private static string Locking(OpeningType opening) => opening switch
    {
        OpeningType.SideHungLeft or OpeningType.SideHungRight or OpeningType.TiltTurnLeft or OpeningType.TiltTurnRight => "Multi-point",
        _ => "Single-point"
    };

    /// <summary>The kind of a hardware item: its "Type" property, or what its name says (handle, hinge, roller …).</summary>
    public static string HardwareKind(MaterialDefinition? item, string name)
    {
        if (item?.Properties.TryGetValue("Type", out string? type) == true && !string.IsNullOrWhiteSpace(type)) return type.Trim();
        string n = name.ToLowerInvariant();
        return n switch
        {
            _ when n.Contains("espag") => "Lockable Espag",
            _ when n.Contains("handle") => "Handle Type",
            _ when n.Contains("hinge") => "Hinge",
            _ when n.Contains("friction") || n.Contains("stay") => "Friction",
            _ when n.Contains("roller") || n.Contains("wheel") => "Roller",
            _ when n.Contains("cylinder") => "Cylinder",
            _ when n.Contains("lock") || n.Contains("latch") => "Lock",
            _ => "Hardware"
        };
    }

    private static byte[]? Drawing(Frame frame, DesignRules rules, IProductLibrary library) => RenderDrawing(frame, rules, library);

    /// <summary>
    /// The design's drawing with its dimensions and opening numbers, as PNG (white background); for the workshop (shop
    /// drawings) also with the glass sizes. WPF: call on an STA thread.
    /// </summary>
    public static byte[]? RenderDrawing(Frame frame, DesignRules rules, IProductLibrary library, bool workshop = false)
    {
        const double width = 380, height = 300, margin = 46;
        try
        {
            var options = FrameRenderOptions.Drawing with { Reference = false, FloorLine = false, GlassSizes = workshop };
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
