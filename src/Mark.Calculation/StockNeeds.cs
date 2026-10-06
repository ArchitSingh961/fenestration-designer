using Mark.Core.Inventory;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Calculation;

/// <summary>How much of one item a job needs from stock.</summary>
public sealed record StockNeed(StockKey Key, string Name, string Unit, double Quantity);

/// <summary>
/// What a job (the designs of a production order) needs from stock (Milestone 18): profiles and steel in whole bars, as
/// the cutting plan cuts them from new bars; glass in m²; hardware, gaskets and accessories in their own unit. Every
/// window of every design is included.
/// </summary>
public static class StockNeeds
{
    public static IReadOnlyList<StockNeed> For(Project project, IProductLibrary library, CalculationRules rules)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(library);
        var result = new CalculationEngine().Calculate(project, library, rules);
        var plan = new CuttingOptimizer().Optimize(result.Profiles, RatedLibrary.For(library, project), rules);   // the quote's bar lengths
        var needs = new List<StockNeed>();

        foreach (var profile in plan.Profiles.Where(p => p.DefinitionId.Length > 0))
        {
            int bars = profile.Stock.Sum(s => s.Quantity);
            if (bars > 0) needs.Add(new StockNeed(new StockKey(StockKind.Profile, profile.DefinitionId), profile.Name, "bars", bars));
        }
        foreach (var glass in result.Bom.Where(b => b.Category == BomCategory.Glass && !string.IsNullOrEmpty(b.ItemId)).GroupBy(b => b.ItemId))
        {
            double area = Math.Round(glass.Sum(b => b.AreaM2 ?? 0), 3);
            if (area > 0) needs.Add(new StockNeed(new StockKey(StockKind.Glass, glass.Key), glass.First().Name, "m²", area));
        }
        foreach (var item in result.Bom.Where(b => b.Category is BomCategory.Hardware or BomCategory.Gasket or BomCategory.Accessory
                                                    or BomCategory.Consumable && !string.IsNullOrEmpty(b.ItemId)).GroupBy(b => b.ItemId))
        {
            double quantity = Math.Round(item.Sum(b => b.Quantity), 3);
            if (quantity > 0)
                needs.Add(new StockNeed(new StockKey(StockKind.Material, item.Key), item.First().Name, UnitOf(library.FindMaterial(item.Key)), quantity));
        }
        return needs;
    }

    /// <summary>"pcs", "m" or "m²": how a library item is counted in stock.</summary>
    public static string UnitOf(MaterialDefinition? material) => material?.Unit switch
    {
        MaterialUnit.Metre => "m",
        MaterialUnit.SquareMetre => "m²",
        _ => "pcs"
    };

    /// <summary>The unit of any stocked item.</summary>
    public static string UnitOf(StockKey key, IProductLibrary library) => key.Kind switch
    {
        StockKind.Profile => "bars",
        StockKind.Glass => "m²",
        _ => UnitOf(library.FindMaterial(key.ItemId))
    };

    /// <summary>The library name of a stocked item (its id when the library no longer has it).</summary>
    public static string NameOf(StockKey key, IProductLibrary library) => key.Kind switch
    {
        StockKind.Profile => library.FindProfile(key.ItemId)?.Name,
        StockKind.Glass => library.FindGlass(key.ItemId)?.Name,
        _ => library.FindMaterial(key.ItemId)?.Name
    } ?? key.ItemId;

    /// <summary>The library price of one unit: a bar of the profile's stock length, a m² of glass, a unit of the item.</summary>
    public static decimal RateOf(StockKey key, IProductLibrary library) => key.Kind switch
    {
        StockKind.Profile when library.FindProfile(key.ItemId) is { } p
            => Math.Round(p.CostPerMetre * (decimal)(p.AvailableStockLengthsMm().DefaultIfEmpty(6000).Max() / 1000.0), 2),
        StockKind.Glass => library.FindGlass(key.ItemId)?.CostPerSquareMetre ?? 0,
        StockKind.Material => library.FindMaterial(key.ItemId)?.CostPerUnit ?? 0,
        _ => 0
    };

    /// <summary>
    /// The stock position of every item that has stock, is needed by <paramref name="needs"/> (per open order) or is on
    /// order: on hand, reserved for orders, on order, and so what to buy.
    /// </summary>
    public static IReadOnlyList<StockPosition> Positions(IReadOnlyList<StockLevel> levels,
        IReadOnlyList<(string Order, IReadOnlyList<StockNeed> Needs)> needs, IReadOnlyList<PurchaseOrder> purchases, IProductLibrary library)
    {
        var keys = levels.Select(l => l.Key)
            .Concat(needs.SelectMany(n => n.Needs).Select(n => n.Key))
            .Concat(purchases.Where(p => p.Status is PurchaseStatus.Ordered or PurchaseStatus.PartReceived).SelectMany(p => p.Lines).Select(l => l.Key))
            .Distinct().ToList();
        var byKey = levels.ToDictionary(l => l.Key);
        return keys.Select(key =>
        {
            byKey.TryGetValue(key, out var level);
            var wanted = needs.Select(n => (n.Order, Quantity: n.Needs.Where(x => x.Key == key).Sum(x => x.Quantity))).Where(x => x.Quantity > 0).ToList();
            string name = NameOf(key, library);
            if (name == key.ItemId && needs.SelectMany(n => n.Needs).FirstOrDefault(n => n.Key == key) is { } need) name = need.Name;
            return new StockPosition(key, name, UnitOf(key, library), level?.OnHand ?? 0, wanted.Sum(w => w.Quantity),
                purchases.Sum(p => p.DueOf(key)), level?.ReorderLevel ?? 0, level?.Location ?? "", wanted.Select(w => w.Order).ToList());
        }).OrderBy(p => p.Key.Kind).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
