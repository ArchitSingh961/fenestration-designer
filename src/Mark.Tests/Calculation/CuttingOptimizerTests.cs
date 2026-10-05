using System.Text.Json;
using Mark.Calculation;
using Mark.Core.Library;
using Mark.Core.Models;
using Xunit;

namespace Mark.Tests.Calculation;

/// <summary>
/// The M7 cutting optimiser on hand-checkable inputs. Every bar must balance: stock = cuts + remnant + waste.
/// </summary>
public class CuttingOptimizerTests
{
    private const string Bar6000 = "BAR-6000";      // 6000 only, 100/m, 1 kg/m
    private const string Bar7000 = "BAR-7000";      // 7000 only, 100/m
    private const string BarMulti = "BAR-MULTI";    // 6000 + 6500 + 7000, 200/m
    private const string NoStock = "BAR-NONE";      // no stock length

    private static readonly ProductLibrary Library = new(profiles: new[]
    {
        new ProfileDefinition { Id = Bar6000, Name = "Bar 6000", Roles = new[] { ProfileType.Mullion }, FaceWidthMm = 60,
            CostPerMetre = 100m, WeightKgPerMetre = 1.0, StockLengthMm = 6000 },
        new ProfileDefinition { Id = Bar7000, Name = "Bar 7000", Roles = new[] { ProfileType.Mullion }, FaceWidthMm = 60,
            CostPerMetre = 100m, StockLengthsMm = new double[] { 7000 } },
        new ProfileDefinition { Id = BarMulti, Name = "Bar multi", Roles = new[] { ProfileType.Mullion }, FaceWidthMm = 60,
            CostPerMetre = 200m, StockLengthMm = 6000, StockLengthsMm = new double[] { 7000, 6500 } },
        new ProfileDefinition { Id = NoStock, Name = "No stock", Roles = new[] { ProfileType.Mullion }, FaceWidthMm = 60,
            CostPerMetre = 100m }
    }, currency: "INR");

    private static int _nextId;

    /// <summary>A resolved M6 line for one piece (or <paramref name="quantity"/> identical pieces).</summary>
    private static ProfileLine Cut(double length, string definition = Bar6000, int quantity = 1)
        => new()
        {
            FrameId = new Guid(1, 0, 0, new byte[8]),
            ProfileId = new Guid(Interlocked.Increment(ref _nextId), 0, 0, new byte[8]),
            Role = ProfileType.Mullion,
            DefinitionId = definition,
            Name = definition,
            IsResolved = true,
            CutLengthMm = length,
            Quantity = quantity
        };

    private static CalculationRules Rules(double kerf = 0, double trim = 0, double minOffcut = 0)
        => new() { Cutting = new CuttingRules { KerfMm = kerf, TrimAllowanceMm = trim, MinUsableOffcutMm = minOffcut } };

    private static CuttingPlan Optimize(CalculationRules rules, params ProfileLine[] cuts)
        => new CuttingOptimizer().Optimize(cuts, Library, rules);

    private static IEnumerable<double> Lengths(StockBar bar) => bar.Cuts.Select(c => c.CutLengthMm);

    private static void AssertBalanced(CuttingPlan plan)
    {
        foreach (var bar in plan.Profiles.SelectMany(p => p.Bars))
        {
            Assert.Equal(bar.StockLengthMm, bar.CutLengthMm + bar.RemnantMm + bar.WasteMm, 6);
            Assert.Equal(bar.RemainingMm, bar.StockLengthMm - bar.TrimMm - bar.CutLengthMm - bar.KerfMm, 6);
            Assert.True(bar.RemainingMm >= 0);
        }
    }

    // ── Required cuts ───────────────────────────────────────────────

    [Fact]
    public void OneCut_OneBar()
    {
        var piece = Cut(1500);
        var plan = Optimize(Rules(), piece);

        var profile = Assert.Single(plan.Profiles);
        Assert.Equal((Bar6000, "Bar 6000"), (profile.DefinitionId, profile.Name));
        var bar = Assert.Single(profile.Bars);
        Assert.Equal((1, 6000.0, 1500.0, 4500.0), (bar.Number, bar.StockLengthMm, bar.CutLengthMm, bar.RemainingMm));
        Assert.Same(piece, Assert.Single(bar.Cuts));                    // the M6 line itself: source reference kept
        Assert.Equal(new[] { new StockRequirement(6000, 1) }, profile.Stock);
        Assert.True(plan.IsComplete);
        AssertBalanced(plan);
    }

    [Fact]
    public void MultipleCuts_ShareABar_LongestFirst()
    {
        var plan = Optimize(Rules(), Cut(1200), Cut(2000), Cut(1500));

        var bar = Assert.Single(Assert.Single(plan.Profiles).Bars);
        Assert.Equal(new[] { 2000.0, 1500, 1200 }, Lengths(bar));
        Assert.Equal((4700.0, 1300.0), (bar.CutLengthMm, bar.RemainingMm));
        AssertBalanced(plan);
    }

    [Fact]
    public void Quantity_IsExpandedIntoPieces()
    {
        var plan = Optimize(Rules(), Cut(1500, quantity: 5));

        var profile = Assert.Single(plan.Profiles);
        Assert.Equal(5, profile.PieceCount);
        Assert.Equal(2, profile.Bars.Count);
        Assert.Equal(new[] { 1500.0, 1500, 1500, 1500 }, Lengths(profile.Bars[0]));   // exactly 6000
        Assert.Equal(new[] { 1500.0 }, Lengths(profile.Bars[1]));
        Assert.All(profile.Bars.SelectMany(b => b.Cuts), c => Assert.Equal(1, c.Quantity));
        Assert.Equal(0, profile.Bars[0].RemainingMm);
        AssertBalanced(plan);
    }

    [Fact]
    public void DifferentProfiles_ArePlannedSeparately_InIdOrder()
    {
        var plan = Optimize(Rules(), Cut(1000, BarMulti), Cut(2000), Cut(1500, BarMulti), Cut(2500));

        Assert.Equal(new[] { Bar6000, BarMulti }, plan.Profiles.Select(p => p.DefinitionId));   // ordinal, not input order
        Assert.Equal(new[] { 2500.0, 2000 }, plan.Profiles[0].Bars.SelectMany(Lengths));
        Assert.Equal(new[] { 1500.0, 1000 }, plan.Profiles[1].Bars.SelectMany(Lengths));
    }

    [Fact]
    public void EveryResolvedPiece_IsPlacedExactlyOnce()
    {
        var cuts = new[] { 2900.0, 1100, 4100, 700, 2900, 1900, 3300, 450, 5999, 1 }.Select(l => Cut(l)).ToArray();
        var plan = Optimize(Rules(kerf: 4, trim: 10, minOffcut: 300), cuts);

        var placed = plan.Profiles.SelectMany(p => p.Bars).SelectMany(b => b.Cuts).Concat(plan.Profiles.SelectMany(p => p.Unplaced));
        Assert.Equal(cuts.Select(c => c.ProfileId).Order(), placed.Select(c => c.ProfileId).Order());
        Assert.Contains(plan.Issues, i => i.ObjectId == cuts[8].ProfileId);   // 5999 > 6000 − 10 trim: unplaced
        Assert.Equal(cuts[8], Assert.Single(plan.Profiles[0].Unplaced));
        AssertBalanced(plan);
    }

    // ── Stock lengths ───────────────────────────────────────────────

    [Fact]
    public void AvailableStockLengths_MergeTheSingleAndTheList_ShortestFirst()
    {
        Assert.Equal(new[] { 6000.0, 6500, 7000 }, Library.FindProfile(BarMulti)!.AvailableStockLengthsMm());
        Assert.Equal(new[] { 7000.0 }, Library.FindProfile(Bar7000)!.AvailableStockLengthsMm());
        Assert.Empty(Library.FindProfile(NoStock)!.AvailableStockLengthsMm());
        Assert.Equal(new[] { 6000.0, 6500, 7000 }, Optimize(Rules(), Cut(10, BarMulti)).Profiles[0].AvailableStockLengthsMm);
    }

    [Fact]
    public void DifferentStockLengths_GiveDifferentWaste()
    {
        var rules = Rules(minOffcut: 300);

        var short6000 = Optimize(rules, Cut(3400), Cut(3400)).Profiles[0];
        Assert.Equal(2, short6000.Bars.Count);                         // 6800 does not fit in one 6000 bar
        Assert.Equal((12000.0, 5200.0, 0.0), (short6000.TotalStockMm, short6000.TotalRemnantMm, short6000.TotalWasteMm));

        var long7000 = Optimize(rules, Cut(3400, Bar7000), Cut(3400, Bar7000)).Profiles[0];
        Assert.Single(long7000.Bars);
        Assert.Equal((7000.0, 0.0, 200.0), (long7000.TotalStockMm, long7000.TotalRemnantMm, long7000.TotalWasteMm));
    }

    [Fact]
    public void MultipleStockLengths_ChooseTheLengthThatFitsBest()
    {
        // 2 × 3200 = 6400: one 6500 bar, not two 6000 bars or a 7000 bar.
        var profile = Optimize(Rules(), Cut(3200, BarMulti), Cut(3200, BarMulti)).Profiles[0];

        Assert.Equal(new[] { new StockRequirement(6500, 1) }, profile.Stock);
        Assert.Equal(100.0, profile.Bars[0].RemainingMm);
    }

    [Fact]
    public void MultipleStockLengths_6000_6500_7000_UseLessMaterialThanASingleLength()
    {
        double[] pieces = { 4000, 2400, 2400, 2000, 1500 };             // 12,300 mm
        var rules = Rules(minOffcut: 300);

        var multi = new CuttingOptimizer().Optimize(pieces.Select(l => Cut(l, BarMulti)), Library, rules).Profiles[0];
        Assert.Equal(2, multi.Bars.Count);
        Assert.Equal(6500.0, multi.Bars[0].StockLengthMm);
        Assert.Equal(new[] { 4000.0, 2400 }, Lengths(multi.Bars[0]));
        Assert.Equal(6000.0, multi.Bars[1].StockLengthMm);
        Assert.Equal(new[] { 2400.0, 2000, 1500 }, Lengths(multi.Bars[1]));
        Assert.Equal(new[] { new StockRequirement(6500, 1), new StockRequirement(6000, 1) }, multi.Stock);
        Assert.Equal((12500.0, 12300.0, 200.0), (multi.TotalStockMm, multi.TotalCutMm, multi.TotalWasteMm));

        var single = new CuttingOptimizer().Optimize(pieces.Select(l => Cut(l)), Library, rules).Profiles[0];
        Assert.Equal(18000.0, single.TotalStockMm);                      // three 6000 bars (5700 mm left as remnants)
        Assert.Equal(0.984, multi.Utilization);                          // 12300 / 12500
    }

    [Fact]
    public void APieceLongerThanTheShortestStock_OpensALongerBar()
    {
        var profile = Optimize(Rules(), Cut(150, BarMulti), Cut(6800, BarMulti)).Profiles[0];

        var bar = Assert.Single(profile.Bars);
        Assert.Equal(7000.0, bar.StockLengthMm);
        Assert.Equal(new[] { 6800.0, 150 }, Lengths(bar));
    }

    // ── Kerf ────────────────────────────────────────────────────────

    [Fact]
    public void ZeroKerf_ThreePiecesFillTheBarExactly()
    {
        var bar = Assert.Single(Optimize(Rules(kerf: 0), Cut(2000), Cut(2000), Cut(2000)).Profiles[0].Bars);

        Assert.Equal((0.0, 0.0, 0.0), (bar.KerfMm, bar.RemainingMm, bar.WasteMm));
        Assert.Equal(2, bar.SawCuts);                                    // the last piece ends at the bar end
    }

    [Fact]
    public void Kerf_BetweenPieces_CanPushAPieceToTheNextBar()
    {
        // 2000 + 3 + 2000 + 3 + 2000 = 6006 > 6000.
        var plan = Optimize(Rules(kerf: 3), Cut(2000), Cut(2000), Cut(2000));
        var bars = plan.Profiles[0].Bars;

        Assert.Equal(2, bars.Count);
        Assert.Equal((2, 6.0, 1994.0), (bars[0].SawCuts, bars[0].KerfMm, bars[0].RemainingMm));
        Assert.Equal((1, 3.0, 3997.0), (bars[1].SawCuts, bars[1].KerfMm, bars[1].RemainingMm));
        AssertBalanced(plan);
    }

    [Fact]
    public void Kerf_MultipleCuts_OneKerfPerPiece()
    {
        var bar = Assert.Single(Optimize(Rules(kerf: 5), Cut(1000, quantity: 4)).Profiles[0].Bars);

        Assert.Equal((4, 20.0, 1980.0), (bar.SawCuts, bar.KerfMm, bar.RemainingMm));   // 6000 − 4000 − 20
    }

    [Fact]
    public void Kerf_APieceEndingExactlyAtTheBarEnd_NeedsNoFinalCut()
    {
        var bar = Assert.Single(Optimize(Rules(kerf: 3), Cut(3000), Cut(2997)).Profiles[0].Bars);   // 3000 + 3 + 2997 = 6000

        Assert.Equal((1, 3.0, 0.0, 3.0), (bar.SawCuts, bar.KerfMm, bar.RemainingMm, bar.WasteMm));
    }

    // ── Trim ────────────────────────────────────────────────────────

    [Fact]
    public void TrimAllowance_ReducesTheUsableLength()
    {
        var plan = Optimize(Rules(trim: 5), Cut(2000), Cut(2000), Cut(2000));   // 6000 > 5995 usable
        var bars = plan.Profiles[0].Bars;

        Assert.Equal(2, bars.Count);
        Assert.Equal((5.0, 1995.0), (bars[0].TrimMm, bars[0].RemainingMm));
        Assert.Equal(10.0, plan.Profiles[0].TotalTrimMm);
        AssertBalanced(plan);
    }

    [Theory]
    [InlineData(0, 1, 10)]          // 2 × 2995 = 5990 fits, 10 left
    [InlineData(10, 1, 0)]          // exactly the usable 5990
    [InlineData(20, 2, 2985)]       // 5980 usable: one piece per bar, 6000 − 20 − 2995 left
    public void DifferentTrimAllowances(double trim, int bars, double remaining)
    {
        var profile = Optimize(Rules(trim: trim), Cut(2995), Cut(2995)).Profiles[0];

        Assert.Equal(bars, profile.Bars.Count);
        Assert.Equal(remaining, profile.Bars[0].RemainingMm);
    }

    [Fact]
    public void TrimThatUsesUpEveryStockLength_IsReported()
    {
        var piece = Cut(100);
        var plan = Optimize(Rules(trim: 6000), piece);

        Assert.Empty(plan.Profiles[0].Bars);
        Assert.Equal(piece, Assert.Single(plan.Profiles[0].Unplaced));
        Assert.Contains("trim allowance", Assert.Single(plan.Issues).Message);
    }

    // ── Remnants and waste ──────────────────────────────────────────

    [Fact]
    public void ALeftoverOfAtLeastTheMinimum_IsAReusableRemnant()
    {
        var bar = Assert.Single(Optimize(Rules(minOffcut: 300), Cut(4700)).Profiles[0].Bars);

        Assert.True(bar.HasRemnant);
        Assert.Equal((1300.0, 1300.0, 0.0), (bar.RemainingMm, bar.RemnantMm, bar.WasteMm));
    }

    [Fact]
    public void ALeftoverShorterThanTheMinimum_IsWaste()
    {
        var bar = Assert.Single(Optimize(Rules(minOffcut: 300), Cut(5800)).Profiles[0].Bars);

        Assert.False(bar.HasRemnant);
        Assert.Equal((200.0, 0.0, 200.0), (bar.RemainingMm, bar.RemnantMm, bar.WasteMm));
    }

    [Fact]
    public void Remnants_KeepTheirProfile_LengthAndSourceBar()
    {
        // kerf 3: bar 1 = 5900 + final cut 3, 97 left (waste offcut); bar 2 = 3000 + 3 + 2500 + final cut 3, 494 left (remnant).
        var plan = Optimize(Rules(kerf: 3, minOffcut: 300), Cut(5900), Cut(3000), Cut(2500));
        var profile = plan.Profiles[0];

        Assert.Equal(new[] { 5900.0 }, Lengths(profile.Bars[0]));
        Assert.Equal(new[] { 3000.0, 2500 }, Lengths(profile.Bars[1]));
        var remnant = Assert.Single(profile.Remnants);
        Assert.Equal(new Remnant(Bar6000, "Bar 6000", 494, 2, 6000), remnant);
        Assert.Equal(profile.Remnants, plan.Remnants);
        Assert.Equal((1, 1), (profile.RemnantCount, profile.WasteOffcutCount));
        Assert.True(profile.Bars[0].HasWasteOffcut);
        Assert.False(profile.Bars[1].HasWasteOffcut);
        Assert.Equal(3 + 97 + 6.0, profile.TotalWasteMm);             // bar 1: kerf + waste offcut; bar 2: two kerfs
        AssertBalanced(plan);
    }

    [Fact]
    public void ABarCutToTheExactEnd_HasNeitherRemnantNorWasteOffcut()
    {
        var bar = Assert.Single(Optimize(Rules(minOffcut: 300), Cut(6000)).Profiles[0].Bars);

        Assert.Equal((0.0, false, false), (bar.RemainingMm, bar.HasRemnant, bar.HasWasteOffcut));
    }

    [Theory]
    [InlineData(5700, true)]        // 300 left: exactly the minimum is kept
    [InlineData(5700.1, false)]     // 299.9 left: waste
    public void MinimumUsableOffcut_Boundary(double piece, bool remnant)
    {
        var bar = Assert.Single(Optimize(Rules(minOffcut: 300), Cut(piece)).Profiles[0].Bars);

        Assert.Equal(remnant, bar.HasRemnant);
        Assert.Equal(remnant ? 0 : bar.RemainingMm, bar.WasteMm);
    }

    [Fact]
    public void Waste_IsTrimPlusKerfPlusAnUnusableLeftover()
    {
        // usable 5995: 2500 + 3 + 2500 + 3 + 800 = 5806, final kerf 3, 186 left (< 300).
        var plan = Optimize(Rules(kerf: 3, trim: 5, minOffcut: 300), Cut(2500), Cut(2500), Cut(800));
        var bar = Assert.Single(plan.Profiles[0].Bars);

        Assert.Equal((5800.0, 5.0, 9.0, 186.0), (bar.CutLengthMm, bar.TrimMm, bar.KerfMm, bar.RemainingMm));
        Assert.Equal(200.0, bar.WasteMm);                                // 5 + 9 + 186
        Assert.Equal(0.9667, plan.Profiles[0].Utilization);              // 5800 / 6000
        Assert.Equal(0.0333, plan.Profiles[0].WasteFraction);
    }

    [Fact]
    public void ZeroWaste_FullUtilization()
    {
        var profile = Optimize(Rules(), Cut(2000, quantity: 3)).Profiles[0];

        Assert.Equal((0.0, 0.0, 1.0, 0.0), (profile.TotalWasteMm, profile.TotalRemnantMm, profile.Utilization, profile.WasteFraction));
    }

    [Fact]
    public void LargeWaste_WhenTheLeftoverIsBelowTheMinimum()
    {
        var profile = Optimize(Rules(minOffcut: 6000), Cut(400)).Profiles[0];

        Assert.Equal((5600.0, 0.0), (profile.TotalWasteMm, profile.TotalRemnantMm));
        Assert.Equal((0.0667, 0.9333), (profile.Utilization, profile.WasteFraction));
    }

    // ── Utilisation ─────────────────────────────────────────────────

    [Fact]
    public void Utilization_IsCutOverConsumedMaterial_RemnantsDoNotCountAgainstIt()
    {
        var profile = Optimize(Rules(), Cut(1500)).Profiles[0];        // 4500 remnant returned to stock

        Assert.Equal(1.0, profile.Utilization);                          // 1500 / (6000 − 4500)
        Assert.Equal(0.0, profile.WasteFraction);
    }

    [Fact]
    public void Utilization_OfThePlan_CombinesEveryProfile()
    {
        // Bar 6000: 5800 cut, 200 waste. Bar 7000: 6500 cut, 500 kept (≥ 300).
        var plan = Optimize(Rules(minOffcut: 300), Cut(5800), Cut(6500, Bar7000));

        Assert.Equal((13000.0, 12300.0, 500.0, 200.0), (plan.TotalStockMm, plan.TotalCutMm, plan.TotalRemnantMm, plan.TotalWasteMm));
        Assert.Equal(0.984, plan.Utilization);                           // 12300 / (13000 − 500)
        Assert.Equal(0.016, plan.WasteFraction);
        Assert.Equal(2, plan.BarCount);
    }

    // ── Cost ────────────────────────────────────────────────────────

    [Fact]
    public void Cost_IsTheLibraryPricePerMetreOfEachStockBar()
    {
        var profile = Optimize(Rules(trim: 5), Cut(2000), Cut(2000), Cut(2000)).Profiles[0];

        Assert.All(profile.Bars, b => Assert.Equal(600.00m, b.Cost));    // 6 m × 100
        Assert.Equal(1200.00m, profile.StockCost);
        Assert.Equal(599.00m, profile.RemnantValue);                     // (1995 + 3995) mm × 100/m
        Assert.Equal(601.00m, profile.NetCost);
        Assert.Equal(100m, profile.CostPerMetre);
        Assert.Equal(12.0, profile.StockWeightKg);                        // 12 m × 1 kg/m
    }

    [Theory]
    [InlineData(100, "650.00")]
    [InlineData(249.99, "1624.94")]                                     // 6.5 m × 249.99 = 1624.935 → away from zero
    public void Cost_FollowsTheLibraryPrice(double costPerMetre, string expected)
    {
        var library = new ProductLibrary(profiles: new[]
        {
            new ProfileDefinition { Id = "P", Name = "P", Roles = new[] { ProfileType.Frame }, FaceWidthMm = 50,
                CostPerMetre = (decimal)costPerMetre, StockLengthsMm = new double[] { 6500 } }
        });
        var plan = new CuttingOptimizer().Optimize(new[] { Cut(1000, "P") }, library, Rules());

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), plan.StockCost);
    }

    // ── Determinism ─────────────────────────────────────────────────

    [Fact]
    public void SameInput_GivesTheSamePlan()
    {
        var cuts = new[] { 1500.0, 1500, 1200, 2400, 900, 3100, 1500, 700, 2600, 1200, 4400, 300 }
            .Select((l, i) => Cut(l, i % 2 == 0 ? BarMulti : Bar6000)).ToList();
        var rules = Rules(kerf: 3, trim: 5, minOffcut: 300);

        string a = JsonSerializer.Serialize(new CuttingOptimizer().Optimize(cuts, Library, rules));
        string b = JsonSerializer.Serialize(new CuttingOptimizer().Optimize(cuts, Library, rules));
        var optimizer = new CuttingOptimizer();
        optimizer.Optimize(cuts.AsEnumerable().Reverse(), Library, rules);   // an unrelated run in between
        string c = JsonSerializer.Serialize(optimizer.Optimize(cuts, Library, rules));

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void EqualLengths_KeepTheirInputOrder()
    {
        var cuts = Enumerable.Range(0, 6).Select(_ => Cut(1000)).ToList();
        var bar = Assert.Single(Optimize(Rules(), cuts.ToArray()).Profiles[0].Bars);

        Assert.Equal(cuts.Select(c => c.ProfileId), bar.Cuts.Select(c => c.ProfileId));
    }

    [Fact]
    public void LargeBatch_IsPlannedCompletelyAndBalanced()
    {
        var cuts = Enumerable.Range(0, 3000).Select(i => Cut(300 + (i * 397) % 2700, BarMulti)).ToList();
        var plan = new CuttingOptimizer().Optimize(cuts, Library, Rules(kerf: 3, trim: 5, minOffcut: 300));

        Assert.True(plan.IsComplete);
        Assert.Equal(3000, plan.Profiles[0].Bars.Sum(b => b.Cuts.Count));
        Assert.True(plan.Utilization > 0.95, $"utilisation {plan.Utilization}");
        AssertBalanced(plan);
    }

    // ── Pieces that cannot be planned ───────────────────────────────

    [Fact]
    public void NoStockLength_IsAnError_AndThePiecesAreUnplaced()
    {
        var plan = Optimize(Rules(), Cut(1000, NoStock), Cut(500, NoStock));

        Assert.False(plan.IsComplete);
        Assert.Empty(plan.Profiles[0].Bars);
        Assert.Equal(2, plan.Profiles[0].Unplaced.Count);
        Assert.Contains("no stock length", Assert.Single(plan.Issues).Message);
        Assert.Equal(0m, plan.StockCost);
    }

    [Fact]
    public void APieceLongerThanEveryBar_IsAnError_TheRestIsPlanned()
    {
        var tooLong = Cut(6001);
        var plan = Optimize(Rules(), tooLong, Cut(1000));

        Assert.Equal(tooLong, Assert.Single(plan.Profiles[0].Unplaced));
        Assert.Equal(tooLong.ProfileId, Assert.Single(plan.Issues).ObjectId);
        Assert.Equal(new[] { 1000.0 }, plan.Profiles[0].Bars.SelectMany(Lengths));
    }

    [Fact]
    public void UnresolvedOrEmptyLines_AreReported_NotPlanned()
    {
        var unresolved = Cut(1000) with { IsResolved = false, DefinitionId = "MISSING" };
        var empty = Cut(0);
        var plan = Optimize(Rules(), unresolved, empty, Cut(1000));

        Assert.Equal(new Guid?[] { unresolved.ProfileId, empty.ProfileId }, plan.Issues.Select(i => i.ObjectId));
        Assert.Equal(1, plan.Profiles.Single().PieceCount);
    }

    [Fact]
    public void AProfileNotInTheLibrary_IsAnError()
    {
        var plan = Optimize(Rules(), Cut(1000, "FROM-ANOTHER-LIBRARY"));

        Assert.Contains("not in the library", Assert.Single(plan.Issues).Message);
        Assert.Single(plan.Profiles[0].Unplaced);
    }

    [Fact]
    public void NoPieces_GiveAnEmptyPlan()
    {
        var plan = Optimize(Rules());

        Assert.Empty(plan.Profiles);
        Assert.Equal((0, 0.0, 0.0, 0m), (plan.BarCount, plan.TotalStockMm, plan.Utilization, plan.StockCost));
        Assert.True(plan.IsComplete);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, double.NaN, 0)]
    [InlineData(0, 0, -300)]
    [InlineData(0, 0, double.PositiveInfinity)]
    public void InvalidCuttingRules_AreRejected(double kerf, double trim, double minOffcut)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Optimize(Rules(kerf, trim, minOffcut), Cut(1000)));
    }

    [Fact]
    public void TheInputLinesAreNotModified()
    {
        var piece = Cut(1500, quantity: 2);
        Optimize(Rules(), piece);
        Assert.Equal(2, piece.Quantity);
    }

    // ── Offcuts in stock (Milestone 16) ─────────────────────────────

    [Fact]
    public void Offcuts_InStock_AreCutFirst_AndNeedNoNewBar()
    {
        var offcuts = new[] { new StockOffcut(1, Bar6000, 1300), new StockOffcut(2, Bar6000, 2100), new StockOffcut(3, BarMulti, 5000) };

        var plan = new CuttingOptimizer().Optimize(new[] { Cut(2000), Cut(1200), Cut(5000) }, Library, Rules(kerf: 0, trim: 10), offcuts)
            .Profiles.Single();

        Assert.Equal(3, plan.Bars.Count);
        Assert.Equal(2, plan.OffcutsUsed);                                           // 2000 → the 2100 offcut, 1200 → the 1300 one
        Assert.Equal(new long[] { 2, 1 }, plan.Bars.Where(b => b.IsOffcut).Select(b => b.OffcutId!.Value).OrderByDescending(x => x));
        Assert.All(plan.Bars.Where(b => b.IsOffcut), b => Assert.Equal(0m, b.Cost));
        Assert.All(plan.Bars.Where(b => b.IsOffcut), b => Assert.Equal(0, b.TrimMm));
        Assert.Equal(new[] { new StockRequirement(6000, 1) }, plan.Stock);           // only the 5000 needs a new bar
        Assert.Single(plan.Bars, b => !b.IsOffcut && b.Cuts.Single().CutLengthMm == 5000);
    }

    [Fact]
    public void WithoutOffcuts_ThePlanIsAsBefore()
    {
        var cuts = new[] { Cut(2000), Cut(1200), Cut(900) };
        string before = JsonSerializer.Serialize(new CuttingOptimizer().Optimize(cuts, Library, Rules(kerf: 3, trim: 5)));
        string none = JsonSerializer.Serialize(new CuttingOptimizer().Optimize(cuts, Library, Rules(kerf: 3, trim: 5), Array.Empty<StockOffcut>()));
        var other = new CuttingOptimizer().Optimize(cuts, Library, Rules(kerf: 3, trim: 5), new[] { new StockOffcut(9, "OTHER", 3000) });

        Assert.Equal(before, none);
        Assert.Equal(0, other.Profiles.Single().OffcutsUsed);                            // another profile's offcut is not used
    }
}
