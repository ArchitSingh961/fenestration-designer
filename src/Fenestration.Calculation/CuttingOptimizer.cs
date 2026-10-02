using System.Globalization;
using Fenestration.Core.Library;

namespace Fenestration.Calculation;

/// <summary>Turns required profile pieces into a <see cref="CuttingPlan"/> of stock bars.</summary>
public interface ICuttingOptimizer
{
    /// <summary>
    /// Plans every resolved piece in <paramref name="cuts"/> (normally <see cref="CalculationResult.Profiles"/>).
    /// Stock lengths and prices come from <paramref name="library"/>, kerf/trim/offcut from
    /// <see cref="CalculationRules.Cutting"/>, rounding from <paramref name="rules"/>. Pieces that cannot be planned
    /// are reported in <see cref="CuttingPlan.Issues"/>, not thrown. Never modifies its inputs.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rules"/> are invalid.</exception>
    CuttingPlan Optimize(IEnumerable<ProfileLine> cuts, IProductLibrary library, CalculationRules rules);
}

public static class CuttingOptimizerExtensions
{
    /// <summary>Plans the profile pieces of an M6 calculation.</summary>
    public static CuttingPlan Optimize(this ICuttingOptimizer optimizer, CalculationResult result, IProductLibrary library,
        CalculationRules rules)
    {
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(result);
        return optimizer.Optimize(result.Profiles, library, rules);
    }
}

/// <summary>
/// Deterministic one-dimensional cutting optimiser (a heuristic, not a proven optimum):
/// <list type="number">
///   <item>Skip unresolved lines (reported); expand <see cref="ProfileLine.Quantity"/> into single pieces.</item>
///   <item>Group pieces by library profile id; groups are processed in ordinal id order.</item>
///   <item>Within a profile, sort pieces by length descending, then by their position in the input (stable).</item>
///   <item>Usable length of a bar = stock length − trim allowance. Each placed piece is followed by one kerf, except a
///         piece that ends exactly at the end of the usable length.</item>
///   <item>Best-Fit Decreasing: each piece goes into the open bar it leaves the least room in (earliest bar on ties);
///         if none fits, a new bar is opened.</item>
///   <item>Multiple stock lengths: the packing is run once with each available stock length as the length new bars are
///         opened with (a piece longer than that opens the shortest bar it fits). After packing, every bar is shrunk to
///         the shortest stock length that still holds its pieces. The run that takes the least stock material wins;
///         ties go to fewer bars, then to the shorter opening length.</item>
///   <item>The leftover of each bar is a remnant if it is at least the minimum usable offcut, otherwise waste.</item>
/// </list>
/// Runtime is O(s · n · b) per profile (s stock lengths, n pieces, b bars), fine for workshop batches. No randomness,
/// no clock, no dictionary iteration order: equal inputs give equal plans.
/// </summary>
public sealed class CuttingOptimizer : ICuttingOptimizer
{
    /// <summary>Lengths come in rounded to 0.1 mm, so this only absorbs binary rounding noise.</summary>
    private const double Eps = 1e-6;

    public CuttingPlan Optimize(IEnumerable<ProfileLine> cuts, IProductLibrary library, CalculationRules rules)
    {
        ArgumentNullException.ThrowIfNull(cuts);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        var issues = new List<CalculationIssue>();
        var pieces = new List<Piece>();
        foreach (var line in cuts)
        {
            if (line is null)
                throw new ArgumentException("The cut list contains an empty line.", nameof(cuts));
            if (!line.IsResolved || line.DefinitionId is null)
            {
                issues.Add(Error($"A {Role(line)} has no library profile, so it cannot be planned.", line.ProfileId));
                continue;
            }
            if (line.CutLengthMm <= 0)
            {
                issues.Add(Error($"A {Role(line)} of '{line.Name}' has no length to cut.", line.ProfileId));
                continue;
            }
            var single = line.Quantity == 1 ? line : line with { Quantity = 1 };
            for (int i = 0; i < line.Quantity; i++)
                pieces.Add(new Piece(single, pieces.Count));
        }

        var profiles = pieces
            .GroupBy(p => p.Line.DefinitionId!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ProfileRun(g.Key, g.ToList(), library, rules, issues).Plan())
            .ToList();

        double stock = Round(profiles.Sum(p => p.TotalStockMm), rules.LengthDecimals);
        double remnant = Round(profiles.Sum(p => p.TotalRemnantMm), rules.LengthDecimals);
        double cut = Round(profiles.Sum(p => p.TotalCutMm), rules.LengthDecimals);
        double waste = Round(profiles.Sum(p => p.TotalWasteMm), rules.LengthDecimals);
        return new CuttingPlan
        {
            Currency = library.Currency,
            Rules = rules.Cutting,
            Profiles = profiles.AsReadOnly(),
            Issues = issues.AsReadOnly(),
            BarCount = profiles.Sum(p => p.Bars.Count),
            TotalStockMm = stock,
            TotalCutMm = cut,
            TotalRemnantMm = remnant,
            TotalWasteMm = waste,
            Utilization = Fraction(cut, stock - remnant),
            WasteFraction = Fraction(waste, stock - remnant),
            StockCost = profiles.Sum(p => p.StockCost),
            RemnantValue = profiles.Sum(p => p.RemnantValue)
        };
    }

    /// <param name="Order">Position in the input: the stable tie-breaker between equal lengths.</param>
    private sealed record Piece(ProfileLine Line, int Order)
    {
        public double Length => Line.CutLengthMm;
    }

    /// <summary>A bar being filled. <see cref="Occupied"/> = pieces + the kerfs between them.</summary>
    private sealed class Bin
    {
        public Bin(double stockLength) => StockLength = stockLength;

        public double StockLength { get; set; }
        public List<Piece> Pieces { get; } = new();
        public double Occupied { get; private set; }

        /// <summary>Room left in <paramref name="usable"/> after adding a piece (negative: it does not fit).</summary>
        public double RoomAfter(double length, double usable, double kerf)
            => usable - (Occupied + (Pieces.Count > 0 ? kerf : 0) + length);

        public void Add(Piece piece, double kerf)
        {
            Occupied += (Pieces.Count > 0 ? kerf : 0) + piece.Length;
            Pieces.Add(piece);
        }
    }

    /// <summary>Plans one library profile.</summary>
    private sealed class ProfileRun
    {
        private readonly string _id;
        private readonly List<Piece> _pieces;
        private readonly IProductLibrary _library;
        private readonly CalculationRules _rules;
        private readonly List<CalculationIssue> _issues;
        private readonly double _kerf;
        private readonly double _trim;

        public ProfileRun(string id, List<Piece> pieces, IProductLibrary library, CalculationRules rules, List<CalculationIssue> issues)
        {
            _id = id;
            _pieces = pieces;
            _library = library;
            _rules = rules;
            _issues = issues;
            _kerf = rules.Cutting.KerfMm;
            _trim = rules.Cutting.TrimAllowanceMm;
        }

        public ProfileCuttingPlan Plan()
        {
            var sorted = _pieces.OrderByDescending(p => p.Length).ThenBy(p => p.Order).ToList();
            var definition = _library.FindProfile(_id);   // looked up once per profile
            string name = definition?.Name ?? sorted[0].Line.Name;
            if (definition is null)
            {
                _issues.Add(Error($"Profile '{_id}' is not in the library, so its {Count(sorted.Count)} cannot be planned.",
                    sorted[0].Line.ProfileId));
                return Build(name, null, Array.Empty<double>(), new List<Bin>(), sorted);
            }

            var available = definition.AvailableStockLengthsMm();
            var stock = available.Where(s => s - _trim > Eps).ToList();   // shortest first
            if (stock.Count == 0)
            {
                _issues.Add(Error(available.Count == 0
                    ? $"'{name}' has no stock length in the library, so its {Count(sorted.Count)} cannot be planned."
                    : $"Every stock length of '{name}' is used up by the {Format(_trim)} mm trim allowance, so its " +
                      $"{Count(sorted.Count)} cannot be planned.", sorted[0].Line.ProfileId));
                return Build(name, definition, available, new List<Bin>(), sorted);
            }

            double longestUsable = stock[^1] - _trim;
            var placeable = new List<Piece>(sorted.Count);
            var unplaced = new List<Piece>();
            foreach (var piece in sorted)
            {
                if (piece.Length <= longestUsable + Eps)
                {
                    placeable.Add(piece);
                    continue;
                }
                unplaced.Add(piece);
                _issues.Add(Error($"A {Role(piece.Line)} of '{name}' is {Format(piece.Length)} mm, longer than the longest usable " +
                                  $"bar ({Format(stock[^1])} mm stock − {Format(_trim)} mm trim).", piece.Line.ProfileId));
            }

            List<Bin>? best = null;
            double bestStock = 0;
            foreach (var opening in stock)        // ascending, so ties keep the shorter opening length
            {
                var bins = Pack(placeable, opening, stock);
                double total = bins.Sum(b => b.StockLength);
                if (best is null || total < bestStock - Eps || (Math.Abs(total - bestStock) <= Eps && bins.Count < best.Count))
                    (best, bestStock) = (bins, total);
            }

            return Build(name, definition, available, best!, unplaced);
        }

        /// <summary>Best-Fit Decreasing with new bars opened at <paramref name="opening"/>, then each bar shrunk.</summary>
        private List<Bin> Pack(List<Piece> pieces, double opening, List<double> stock)
        {
            var bins = new List<Bin>();
            foreach (var piece in pieces)
            {
                Bin? target = null;
                double targetRoom = double.MaxValue;
                foreach (var bin in bins)
                {
                    double room = bin.RoomAfter(piece.Length, bin.StockLength - _trim, _kerf);
                    if (room >= -Eps && room < targetRoom - Eps)
                        (target, targetRoom) = (bin, room);
                }

                if (target is null)
                {
                    double length = piece.Length <= opening - _trim + Eps
                        ? opening
                        : stock.First(s => piece.Length <= s - _trim + Eps);
                    target = new Bin(length);
                    bins.Add(target);
                }
                target.Add(piece, _kerf);
            }

            foreach (var bin in bins)
                bin.StockLength = stock.First(s => bin.Occupied <= s - _trim + Eps);
            return bins;
        }

        private ProfileCuttingPlan Build(string name, ProfileDefinition? definition, IReadOnlyList<double> available,
            List<Bin> bins, List<Piece> unplaced)
        {
            int lengthDp = _rules.LengthDecimals;
            decimal costPerMetre = definition?.CostPerMetre ?? 0;
            double minOffcut = _rules.Cutting.MinUsableOffcutMm;

            var bars = new List<StockBar>(bins.Count);
            foreach (var bin in bins)
            {
                int n = bin.Pieces.Count;
                double cut = Round(bin.Pieces.Sum(p => p.Length), lengthDp);
                double rest = bin.StockLength - _trim - bin.Occupied;
                if (rest < Eps) rest = 0;
                double finalKerf = rest > 0 ? Math.Min(_kerf, rest) : 0;   // separates the last piece from the leftover
                double kerf = Round((n - 1) * _kerf + finalKerf, lengthDp);
                double remaining = Round(rest - finalKerf, lengthDp);
                double remnant = remaining > 0 && remaining >= minOffcut - Eps ? remaining : 0;
                bars.Add(new StockBar
                {
                    Number = bars.Count + 1,
                    StockLengthMm = bin.StockLength,
                    Cuts = bin.Pieces.Select(p => p.Line).ToList().AsReadOnly(),
                    CutLengthMm = cut,
                    TrimMm = _trim,
                    SawCuts = n - 1 + (rest > 0 ? 1 : 0),
                    KerfMm = kerf,
                    RemainingMm = remaining,
                    RemnantMm = remnant,
                    WasteMm = Round(bin.StockLength - cut - remnant, lengthDp),
                    Cost = Money(Metres(bin.StockLength) * costPerMetre)
                });
            }

            double stock = Round(bars.Sum(b => b.StockLengthMm), lengthDp);
            double remnantTotal = Round(bars.Sum(b => b.RemnantMm), lengthDp);
            double cutTotal = Round(bars.Sum(b => b.CutLengthMm), lengthDp);
            double wasteTotal = Round(bars.Sum(b => b.WasteMm), lengthDp);
            return new ProfileCuttingPlan
            {
                DefinitionId = _id,
                Name = name,
                AvailableStockLengthsMm = available,
                Bars = bars.AsReadOnly(),
                Stock = bars.GroupBy(b => b.StockLengthMm).OrderByDescending(g => g.Key)
                    .Select(g => new StockRequirement(g.Key, g.Count())).ToList().AsReadOnly(),
                Unplaced = unplaced.Select(p => p.Line).ToList().AsReadOnly(),
                PieceCount = bars.Sum(b => b.Cuts.Count) + unplaced.Count,
                TotalStockMm = stock,
                TotalCutMm = cutTotal,
                TotalTrimMm = Round(bars.Sum(b => b.TrimMm), lengthDp),
                TotalKerfMm = Round(bars.Sum(b => b.KerfMm), lengthDp),
                TotalRemnantMm = remnantTotal,
                RemnantCount = bars.Count(b => b.HasRemnant),
                Remnants = bars.Where(b => b.HasRemnant)
                    .Select(b => new Remnant(_id, name, b.RemnantMm, b.Number, b.StockLengthMm)).ToList().AsReadOnly(),
                TotalWasteMm = wasteTotal,
                WasteOffcutCount = bars.Count(b => b.HasWasteOffcut),
                Utilization = Fraction(cutTotal, stock - remnantTotal),
                WasteFraction = Fraction(wasteTotal, stock - remnantTotal),
                CostPerMetre = costPerMetre,
                StockCost = bars.Sum(b => b.Cost),
                RemnantValue = Money(Metres(remnantTotal) * costPerMetre),
                StockWeightKg = Math.Round(stock / 1000.0 * (definition?.WeightKgPerMetre ?? 0), _rules.QuantityDecimals,
                    MidpointRounding.AwayFromZero)
            };
        }

        private decimal Money(decimal value) => Math.Round(value, _rules.MoneyDecimals, MidpointRounding.AwayFromZero);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static CalculationIssue Error(string message, Guid? id) => new(IssueSeverity.Error, message, id);

    private static double Round(double value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    /// <summary>Exact decimal metres of an already-rounded length in mm (avoids binary noise).</summary>
    private static decimal Metres(double mm) => (decimal)Math.Round(mm, 6, MidpointRounding.AwayFromZero) / 1000m;

    /// <summary><paramref name="part"/> / <paramref name="whole"/> to 4 decimal places; 0 when there is nothing.</summary>
    private static double Fraction(double part, double whole) => whole > Eps ? Round(part / whole, 4) : 0;

    private static string Role(ProfileLine line) => line.Role.ToString().ToLowerInvariant();

    private static string Count(int pieces) => pieces == 1 ? "1 piece" : $"{pieces} pieces";

    private static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
