using System.Globalization;
using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Calculation;

/// <summary>Turns a design into quantities and costs: design + library + rules → <see cref="CalculationResult"/>.</summary>
public interface ICalculationEngine
{
    /// <summary>
    /// Calculates the whole project. Never modifies <paramref name="project"/>. Problems with individual items
    /// (e.g. a reference to a product that is not in the library) are reported in
    /// <see cref="CalculationResult.Issues"/> rather than thrown, so the rest of the design is still calculated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rules"/> are invalid.</exception>
    CalculationResult Calculate(Project project, IProductLibrary library, CalculationRules rules);
}

/// <summary>
/// The calculation engine. Pure and deterministic: no UI, no clock, no randomness; equal inputs give equal results.
///
/// <list type="bullet">
///   <item><b>References.</b> Each profile/glass uses its own library reference, or the library default for its role
///         when it has none. A reference that is not in the library is an <see cref="IssueSeverity.Error"/>: the item
///         keeps its geometry line but is not priced and is left out of the BOM.</item>
///   <item><b>Profiles.</b> Outer members are cut to the outer frame size (mitred, or butt-jointed with the verticals
///         running through). Mullions and transoms are cut face to face plus the definition's allowance per end.
///         Weight and cost are per metre of cut length.</item>
///   <item><b>Glass.</b> Size = face-to-face opening + the glazing bite of each surrounding profile − the edge
///         clearance per side. Cost = max(area, minimum chargeable area) × price per m².</item>
///   <item><b>Materials.</b> Usage rules in the definitions (per piece, per metre, per m²) add hardware, gaskets
///         and accessories.</item>
/// </list>
/// </summary>
public sealed class CalculationEngine : ICalculationEngine
{
    public CalculationResult Calculate(Project project, IProductLibrary library, CalculationRules rules)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        var run = new Run(library, rules);
        foreach (var frame in project.Frames)
            run.AddFrame(frame);
        return run.Build();
    }

    /// <summary>State of one calculation.</summary>
    private sealed class Run
    {
        private const double Tol = GeometryTolerance.Default;

        private readonly IProductLibrary _library;
        private readonly CalculationRules _rules;
        private readonly List<FrameCalculation> _frames = new();
        private readonly List<ProfileLine> _profiles = new();
        private readonly List<GlassLine> _glass = new();
        private readonly List<MaterialLine> _materials = new();
        private readonly List<CalculationIssue> _issues = new();

        public Run(IProductLibrary library, CalculationRules rules)
        {
            _library = library;
            _rules = rules;
        }

        public void AddFrame(Frame frame)
        {
            var outer = FrameMembers.Find(frame);
            var definitions = new Dictionary<Guid, ProfileDefinition?>();
            decimal profileCost = 0, glassCost = 0, materialCost = 0;
            double weight = 0;

            foreach (var profile in frame.Profiles)
            {
                var line = CalculateProfile(frame, outer, profile, out var definition);
                definitions[profile.Id] = definition;
                _profiles.Add(line);
                profileCost += line.Cost;
                weight += line.WeightKg;
                if (definition is not null)
                    materialCost += AddMaterials(frame.Id, profile.Id, definition.Materials, line.CutLengthMm / 1000.0, 0);
            }

            foreach (var panel in frame.GlassPanels)
            {
                var line = CalculateGlass(frame, panel, definitions, out var definition);
                _glass.Add(line);
                glassCost += line.Cost;
                weight += line.WeightKg ?? 0;
                if (definition is not null)
                    materialCost += AddMaterials(frame.Id, panel.Id, definition.Materials, line.PerimeterM, line.AreaM2);
            }

            _frames.Add(new FrameCalculation(frame.Id, frame.Width, frame.Height,
                new CostSummary(profileCost, glassCost, materialCost), Round(weight, _rules.QuantityDecimals)));
        }

        // ── Profiles ────────────────────────────────────────────────

        private ProfileLine CalculateProfile(Frame frame, FrameMembers? outer, Profile profile, out ProfileDefinition? definition)
        {
            bool isDefault = profile.ProfileDefinitionId is null;
            string? id = profile.ProfileDefinitionId ?? _library.Defaults.ProfileIdFor(profile.ProfileType);
            definition = _library.FindProfile(id);
            string role = profile.ProfileType.ToString().ToLowerInvariant();

            if (definition is null)
            {
                Error(id is null
                    ? $"{Members.Describe(profile)} has no profile assigned and the library has no default {role} profile."
                    : $"{Members.Describe(profile)} uses profile '{id}', which is not in the library.", profile.Id);
            }
            else
            {
                if (!definition.Supports(profile.ProfileType))
                    Error($"'{definition.Name}' cannot be used as a {role}.", profile.Id);
                if (Math.Abs(definition.FaceWidthMm - profile.Thickness) > Tol)
                    Warning($"{Members.Describe(profile)} is drawn {Format(profile.Thickness)} mm wide but '{definition.Name}' " +
                            $"is {Format(definition.FaceWidthMm)} mm.", profile.Id);
                if (!definition.IsActive)
                    Warning($"{Members.Describe(profile)} uses '{definition.Name}', which is retired in the library.", profile.Id);
            }

            double length = Round(CutLength(frame, outer, profile, definition, out double startAngle, out double endAngle),
                _rules.LengthDecimals);
            if (length <= 0)
                Error($"{Members.Describe(profile)} has no length left to cut.", profile.Id);

            double metres = Math.Max(length, 0) / 1000.0;
            return new ProfileLine
            {
                FrameId = frame.Id,
                ProfileId = profile.Id,
                Role = profile.ProfileType,
                DefinitionId = id,
                Name = definition?.Name ?? (id is null ? "(no profile)" : $"(missing: {id})"),
                IsResolved = definition is not null,
                IsDefault = isDefault,
                CutLengthMm = length,
                StartCutAngle = startAngle,
                EndCutAngle = endAngle,
                WeightKg = definition is null ? 0 : Round(metres * definition.WeightKgPerMetre, _rules.QuantityDecimals),
                CostPerMetre = definition?.CostPerMetre ?? 0,
                Cost = definition is null ? 0 : Money(ToDecimal(metres) * definition.CostPerMetre)
            };
        }

        private double CutLength(Frame frame, FrameMembers? outer, Profile profile, ProfileDefinition? definition,
            out double startAngle, out double endAngle)
        {
            startAngle = endAngle = 90;
            if (outer is not null && profile.ProfileType == ProfileType.Frame)
            {
                bool vertical = ReferenceEquals(profile, outer.Left) || ReferenceEquals(profile, outer.Right);
                if (_rules.FrameJoint == FrameJointType.Mitre)
                {
                    startAngle = endAngle = 45;
                    return vertical ? frame.Height : frame.Width;
                }
                return vertical ? frame.Height : frame.Width - outer.Left.Thickness - outer.Right.Thickness;
            }

            double allowance = 2 * (definition?.CutAllowancePerEndMm ?? 0);
            if (Members.IsDivision(profile) && Members.AxisOf(profile) is { } axis)
            {
                var body = FrameLayout.GetMemberBody(frame, profile);
                return (axis == MemberAxis.Vertical ? body.Height : body.Width) + allowance;
            }
            return profile.Length + allowance;
        }

        // ── Glass ───────────────────────────────────────────────────

        private GlassLine CalculateGlass(Frame frame, GlassPanel panel, IReadOnlyDictionary<Guid, ProfileDefinition?> profiles,
            out GlassDefinition? definition)
        {
            bool isDefault = panel.GlassDefinitionId is null;
            string? id = panel.GlassDefinitionId ?? _library.Defaults.GlassId;
            definition = _library.FindGlass(id);
            string where = $"The glass panel {Format(panel.Boundary.Width)} × {Format(panel.Boundary.Height)} mm";

            if (definition is null)
                Error(id is null
                    ? $"{where} has no glass type assigned and the library has no default glass."
                    : $"{where} uses glass type '{id}', which is not in the library.", panel.Id);
            else
            {
                if (Math.Abs(definition.ThicknessMm - panel.Thickness) > Tol)
                    Warning($"{where} is drawn {Format(panel.Thickness)} mm thick but '{definition.Name}' is " +
                            $"{Format(definition.ThicknessMm)} mm.", panel.Id);
                if (!definition.IsActive)
                    Warning($"{where} uses '{definition.Name}', which is retired in the library.", panel.Id);
            }

            var b = panel.Boundary;
            double clearance = _rules.GlassEdgeClearanceMm;
            double width = b.Width + Bite(frame, profiles, MemberAxis.Vertical, b.Left, +1, b.Top, b.Bottom)
                                   + Bite(frame, profiles, MemberAxis.Vertical, b.Right, -1, b.Top, b.Bottom) - 2 * clearance;
            double height = b.Height + Bite(frame, profiles, MemberAxis.Horizontal, b.Top, +1, b.Left, b.Right)
                                     + Bite(frame, profiles, MemberAxis.Horizontal, b.Bottom, -1, b.Left, b.Right) - 2 * clearance;
            width = Round(width, _rules.LengthDecimals);
            height = Round(height, _rules.LengthDecimals);
            if (width <= 0 || height <= 0)
            {
                Error($"{where} has no glass left after the edge clearance.", panel.Id);
                width = Math.Max(width, 0);
                height = Math.Max(height, 0);
            }

            double area = Round(width * height / 1_000_000.0, _rules.AreaDecimals);
            double chargeable = definition is null ? area : Math.Max(area, definition.MinChargeableAreaM2);
            return new GlassLine
            {
                FrameId = frame.Id,
                GlassPanelId = panel.Id,
                DefinitionId = id,
                Name = definition?.Name ?? (id is null ? "(no glass)" : $"(missing: {id})"),
                Category = definition?.Category,
                IsResolved = definition is not null,
                IsDefault = isDefault,
                ThicknessMm = definition?.ThicknessMm ?? panel.Thickness,
                WidthMm = width,
                HeightMm = height,
                AreaM2 = area,
                ChargeableAreaM2 = chargeable,
                PerimeterM = Round(2 * (width + height) / 1000.0, _rules.QuantityDecimals),
                WeightKg = definition?.WeightKgPerSquareMetre is { } perM2 ? Round(area * perM2, _rules.QuantityDecimals) : null,
                CostPerSquareMetre = definition?.CostPerSquareMetre ?? 0,
                Cost = definition is null ? 0 : Money(ToDecimal(chargeable) * definition.CostPerSquareMetre)
            };
        }

        /// <summary>
        /// Glazing bite of the structural member whose face forms one glass edge. <paramref name="side"/> is +1 when the
        /// glass lies on the member's far side (its left/top edge meets the member's right/bottom face), −1 otherwise.
        /// </summary>
        private static double Bite(Frame frame, IReadOnlyDictionary<Guid, ProfileDefinition?> profiles, MemberAxis axis,
            double edge, int side, double from, double to)
        {
            double bite = 0;
            foreach (var profile in frame.Profiles)
            {
                if (!Members.IsStructural(profile) || Members.AxisOf(profile) != axis) continue;
                double face = Members.PositionOf(profile, axis) + side * profile.Thickness / 2.0;
                var (start, end) = Members.SpanOf(profile, axis);
                if (Math.Abs(face - edge) > Tol || start >= to - Tol || end <= from + Tol) continue;
                if (profiles.TryGetValue(profile.Id, out var definition) && definition is not null)
                    bite = Math.Max(bite, definition.GlazingBiteMm);
            }
            return bite;
        }

        // ── Materials ───────────────────────────────────────────────

        private decimal AddMaterials(Guid frameId, Guid sourceId, IReadOnlyList<MaterialUsage> usages, double metres, double areaM2)
        {
            decimal total = 0;
            foreach (var usage in usages)
            {
                if (_library.FindMaterial(usage.MaterialId) is not { } material)
                {
                    Error($"Material '{usage.MaterialId}' is not in the library.", sourceId);
                    continue;
                }

                double quantity = Round(usage.Basis switch
                {
                    UsageBasis.PerMetre => usage.Quantity * metres,
                    UsageBasis.PerSquareMetre => usage.Quantity * areaM2,
                    _ => usage.Quantity
                }, _rules.QuantityDecimals);
                var cost = Money(ToDecimal(quantity) * material.CostPerUnit);
                total += cost;
                _materials.Add(new MaterialLine
                {
                    FrameId = frameId,
                    SourceId = sourceId,
                    MaterialId = material.Id,
                    Name = material.Name,
                    Category = material.Category,
                    Unit = material.Unit,
                    Quantity = quantity,
                    Cost = cost
                });
            }
            return total;
        }

        // ── Aggregation ─────────────────────────────────────────────

        public CalculationResult Build()
        {
            var bom = new List<BomLine>();

            foreach (var group in _profiles.Where(p => p.IsResolved).GroupBy(p => p.DefinitionId!).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                double length = Round(group.Sum(p => p.CutLengthMm), _rules.LengthDecimals);
                bom.Add(new BomLine(BomCategory.Profile, group.Key, group.First().Name,
                    $"{group.Count()} pcs, {Format(length / 1000.0, "0.###")} m", group.Count(), "pcs", length, null,
                    Round(group.Sum(p => p.WeightKg), _rules.QuantityDecimals), group.Sum(p => p.Cost)));
            }

            foreach (var group in _glass.Where(g => g.IsResolved)
                         .GroupBy(g => (Id: g.DefinitionId!, g.WidthMm, g.HeightMm))
                         .OrderBy(g => g.Key.Id, StringComparer.Ordinal)
                         .ThenByDescending(g => g.Key.WidthMm).ThenByDescending(g => g.Key.HeightMm))
            {
                var weights = group.Select(g => g.WeightKg).ToList();
                bom.Add(new BomLine(BomCategory.Glass, group.Key.Id, group.First().Name,
                    $"{Format(group.Key.WidthMm)} × {Format(group.Key.HeightMm)} mm", group.Count(), "pcs", null,
                    Round(group.Sum(g => g.AreaM2), _rules.AreaDecimals),
                    weights.All(w => w is null) ? null : Round(weights.Sum(w => w ?? 0), _rules.QuantityDecimals),
                    group.Sum(g => g.Cost)));
            }

            foreach (var group in _materials.GroupBy(m => m.MaterialId)
                         .OrderBy(g => CategoryOf(g.First().Category)).ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                var first = group.First();
                bom.Add(new BomLine(CategoryOf(first.Category), group.Key, first.Name, "",
                    Round(group.Sum(m => m.Quantity), _rules.QuantityDecimals), UnitText(first.Unit), null, null, null,
                    group.Sum(m => m.Cost)));
            }

            var cutList = _profiles.Where(p => p.IsResolved && p.CutLengthMm > 0)
                .GroupBy(p => (Id: p.DefinitionId!, p.CutLengthMm, p.StartCutAngle, p.EndCutAngle))
                .OrderBy(g => g.Key.Id, StringComparer.Ordinal)
                .ThenByDescending(g => g.Key.CutLengthMm).ThenBy(g => g.Key.StartCutAngle).ThenBy(g => g.Key.EndCutAngle)
                .Select(g => new CutListLine(g.Key.Id, g.First().Name, g.Key.CutLengthMm, g.Key.StartCutAngle, g.Key.EndCutAngle,
                    g.Count(), _library.FindProfile(g.Key.Id)?.StockLengthMm ?? 0))
                .ToList();

            var cost = _frames.Aggregate(CostSummary.Zero, (sum, f) => sum.Add(f.Cost));
            double weight = Round(_frames.Sum(f => f.WeightKg), _rules.QuantityDecimals);
            return new CalculationResult(_library.Currency, _frames.AsReadOnly(), _profiles.AsReadOnly(), _glass.AsReadOnly(),
                _materials.AsReadOnly(), cutList.AsReadOnly(), bom.AsReadOnly(), cost, weight, _issues.AsReadOnly());
        }

        // ── Helpers ─────────────────────────────────────────────────

        private void Error(string message, Guid? id) => _issues.Add(new CalculationIssue(IssueSeverity.Error, message, id));

        private void Warning(string message, Guid? id) => _issues.Add(new CalculationIssue(IssueSeverity.Warning, message, id));

        private decimal Money(decimal value) => Math.Round(value, _rules.MoneyDecimals, MidpointRounding.AwayFromZero);

        private static double Round(double value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

        /// <summary>Exact decimal of an already-rounded quantity (avoids binary noise such as 1.2000000000000002).</summary>
        private static decimal ToDecimal(double value) => (decimal)Math.Round(value, 9, MidpointRounding.AwayFromZero);

        private static string Format(double value, string format = "0.#") => value.ToString(format, CultureInfo.InvariantCulture);

        private static BomCategory CategoryOf(MaterialCategory category) => category switch
        {
            MaterialCategory.Hardware => BomCategory.Hardware,
            MaterialCategory.Gasket => BomCategory.Gasket,
            MaterialCategory.Consumable => BomCategory.Consumable,
            _ => BomCategory.Accessory
        };

        private static string UnitText(MaterialUnit unit) => unit switch
        {
            MaterialUnit.Metre => "m",
            MaterialUnit.SquareMetre => "m²",
            _ => "pcs"
        };
    }
}
