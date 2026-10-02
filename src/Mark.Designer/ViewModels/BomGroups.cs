using System.Globalization;
using Mark.Calculation;

namespace Mark.Designer.ViewModels;

/// <summary>A size line under a glass type, e.g. "2 × 245.8 × 1290 mm   792.76".</summary>
public sealed record BomSubLine(string Text, string Cost);

/// <summary>One item of a bill-of-materials group: name, what and how much, cost; glass also lists its sizes.</summary>
public sealed record BomItem(string Name, string Detail, string Cost, IReadOnlyList<BomSubLine> SubLines)
{
    public bool HasSubLines => SubLines.Count > 0;
}

/// <summary>
/// A category of the bill of materials (Profiles, Glass, Hardware…) with its colour, item count, subtotal and share of
/// the total. <see cref="IsExpanded"/> is remembered while the design changes.
/// </summary>
public sealed class BomGroup : ViewModelBase
{
    private readonly Action<string, bool> _rememberExpanded;

    public BomGroup(BomCategory category, string title, string glyph, string accent, IReadOnlyList<BomItem> items,
        decimal subtotal, double share, bool isExpanded, Action<string, bool> rememberExpanded)
    {
        Category = category;
        Title = title;
        Glyph = glyph;
        Accent = accent;
        Items = items;
        SubtotalValue = subtotal;
        Subtotal = Money(subtotal);
        Share = share;
        _isExpanded = isExpanded;
        _rememberExpanded = rememberExpanded;
    }

    public BomCategory Category { get; }
    public string Title { get; }

    /// <summary>A Segoe MDL2 Assets icon.</summary>
    public string Glyph { get; }

    /// <summary>The group's colour (hex), used for its icon, header bar and share of the cost bar.</summary>
    public string Accent { get; }

    public IReadOnlyList<BomItem> Items { get; }
    public decimal SubtotalValue { get; }
    public string Subtotal { get; }

    /// <summary>"3 items".</summary>
    public string CountText => Items.Count == 1 ? "1 item" : $"{Items.Count} items";

    /// <summary>Fraction (0–1) of the bill's total.</summary>
    public double Share { get; }

    /// <summary>"38 %".</summary>
    public string ShareText => $"{Math.Round(Share * 100):0} %";

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                _rememberExpanded(Title, value);
        }
    }

    internal static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}

/// <summary>
/// Turns the calculated bill of materials into colour-coded groups for the panel: one group per category in a fixed
/// order, items sorted by cost (largest first); glass is one item per glass type with its pane sizes listed under it.
/// </summary>
public static class BomGroupBuilder
{
    private static readonly (BomCategory Category, string Title, string Glyph, string Accent)[] Layout =
    {
        (BomCategory.Profile, "Profiles", "", "#1E63C7"),
        (BomCategory.Glass, "Glass", "", "#0E9FB0"),
        (BomCategory.Hardware, "Hardware", "", "#E07B00"),
        (BomCategory.Gasket, "Gaskets", "", "#7C4DFF"),
        (BomCategory.Accessory, "Accessories", "", "#2E9E5B"),
        (BomCategory.Consumable, "Consumables", "", "#7A818B")
    };

    public static IReadOnlyList<BomGroup> Build(IReadOnlyList<BomLine> bom, Func<string, bool> isExpanded,
        Action<string, bool> rememberExpanded)
    {
        decimal total = bom.Sum(l => l.Cost);
        var groups = new List<BomGroup>();
        foreach (var (category, title, glyph, accent) in Layout)
        {
            var lines = bom.Where(l => l.Category == category).ToList();
            if (lines.Count == 0) continue;

            var items = category == BomCategory.Glass ? GlassItems(lines) : lines
                .OrderByDescending(l => l.Cost)
                .Select(l => new BomItem(l.Name, Detail(l), BomGroup.Money(l.Cost), Array.Empty<BomSubLine>()))
                .ToList();
            decimal subtotal = lines.Sum(l => l.Cost);
            groups.Add(new BomGroup(category, title, glyph, accent, items, subtotal,
                total == 0 ? 0 : (double)(subtotal / total), isExpanded(title), rememberExpanded));
        }
        return groups;
    }

    /// <summary>One item per glass type: panes and area, with each size (panes × width × height) and its cost below.</summary>
    private static List<BomItem> GlassItems(IReadOnlyList<BomLine> lines)
        => lines.GroupBy(l => l.ItemId)
            .Select(g =>
            {
                double panes = g.Sum(l => l.Quantity);
                double area = g.Sum(l => l.AreaM2 ?? 0);
                var sizes = g.OrderByDescending(l => l.Cost)
                    .Select(l => new BomSubLine($"{Number(l.Quantity)} × {l.Description}", BomGroup.Money(l.Cost)))
                    .ToList();
                return (Cost: g.Sum(l => l.Cost), Item: new BomItem(g.First().Name,
                    $"{Number(panes)} {(panes == 1 ? "pane" : "panes")} · {area.ToString("0.##", CultureInfo.InvariantCulture)} m²",
                    BomGroup.Money(g.Sum(l => l.Cost)), sizes));
            })
            .OrderByDescending(x => x.Cost)
            .Select(x => x.Item)
            .ToList();

    private static string Detail(BomLine line) => line.Category switch
    {
        BomCategory.Profile => $"{Number(line.Quantity)} pcs · {((line.LengthMm ?? 0) / 1000).ToString("0.##", CultureInfo.InvariantCulture)} m"
                               + (line.WeightKg is { } kg and > 0 ? $" · {kg.ToString("0.#", CultureInfo.InvariantCulture)} kg" : ""),
        _ => $"{line.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} {line.Unit}"
    };

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
