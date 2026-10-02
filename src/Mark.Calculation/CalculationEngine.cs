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
        private readonly List<OpeningLine> _openings = new();
        private readonly DesignRules _designRules = new();

        /// <summary>Quantity of the design being added (lines are per window; this is how many windows).</summary>
        private int _windows = 1;

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
            double weight = 0, metres = 0, glassArea = 0;
            _windows = Math.Max(1, frame.Design.Quantity);

            foreach (var profile in frame.Profiles)
            {
                var line = CalculateProfile(frame, outer, profile, out var definition);
                definitions[profile.Id] = definition;
                _profiles.Add(line);
                profileCost += line.Cost;
                weight += line.WeightKg;
                metres += line.CutLengthMm / 1000.0;
                if (definition is not null)
                    materialCost += AddMaterials(frame.Id, profile.Id, definition.Materials, line.CutLengthMm / 1000.0, 0);
            }

            foreach (var panel in frame.GlassPanels)
            {
                // The sash band is as wide as the sash profile's face, so the glass inside it is sized correctly.
                var sashDefinition = panel.Opening.IsOpenable() ? SashProfile(ProfileType.Sash, panel.Id) : null;
                var sashRules = sashDefinition is null ? _designRules : new DesignRules { SashFaceWidthMm = sashDefinition.FaceWidthMm };
                var sash = OpeningGeometry.SashOf(frame, panel, sashRules);
                if (sash is { } s)
                {
                    foreach (var bar in SashBars(frame, panel, s.Outer, ProfileType.Sash, sashDefinition))
                    {
                        profileCost += bar.Cost;
                        weight += bar.WeightKg;
                        metres += bar.CutLengthMm / 1000.0;
                        if (sashDefinition is not null)
                            materialCost += AddMaterials(frame.Id, panel.Id, sashDefinition.Materials, bar.CutLengthMm / 1000.0, 0);
                    }
                }

                double meshArea = 0;
                if (panel.HasMesh)
                {
                    var meshOuter = sash?.Outer ?? panel.Boundary;
                    var meshDefinition = SashProfile(ProfileType.MeshSash, panel.Id);
                    double face = meshDefinition?.FaceWidthMm ?? 0;
                    meshArea = Round(Math.Max(0, meshOuter.Width - 2 * face) * Math.Max(0, meshOuter.Height - 2 * face) / 1_000_000.0,
                        _rules.AreaDecimals);
                    foreach (var bar in SashBars(frame, panel, meshOuter, ProfileType.MeshSash, meshDefinition))
                    {
                        profileCost += bar.Cost;
                        weight += bar.WeightKg;
                        metres += bar.CutLengthMm / 1000.0;
                        if (meshDefinition is not null)
                            materialCost += AddMaterials(frame.Id, panel.Id, meshDefinition.Materials, bar.CutLengthMm / 1000.0, 0);
                    }
                }
                _openings.Add(new OpeningLine(frame.Id, panel.Id, panel.Opening, sash is not null, panel.HasMesh, meshArea, _windows));

                var line = CalculateGlass(frame, panel, definitions, sash, sashDefinition, out var definition);
                _glass.Add(line);
                glassCost += line.Cost;
                glassArea += line.AreaM2;
                weight += line.WeightKg ?? 0;
                if (definition is not null)
                    materialCost += AddMaterials(frame.Id, panel.Id, definition.Materials, line.PerimeterM, line.AreaM2);
            }

            _frames.Add(new FrameCalculation(frame.Id, frame.Width, frame.Height,
                new CostSummary(profileCost, glassCost, materialCost), Round(weight, _rules.QuantityDecimals))
            {
                Quantity = _windows,
                ProfileMetres = Round(metres, _rules.QuantityDecimals),
                GlassAreaM2 = Round(glassArea, _rules.AreaDecimals)
            });
        }

        // ── Sashes and mesh shutters ────────────────────────────────

        /// <summary>
        /// The library profile sash (or mesh-shutter) bars are made from: the first active profile with that role, else any
        /// with it. An opening that needs one when the library has none is an error (the bars are listed but not priced).
        /// </summary>
        private ProfileDefinition? SashProfile(ProfileType role, Guid panelId)
        {
            var definition = _library.Profiles.FirstOrDefault(p => p.IsActive && p.Supports(role))
                             ?? _library.Profiles.FirstOrDefault(p => p.Supports(role));
            if (definition is null)
                Error(role == ProfileType.Sash
                    ? "The library has no sash profile, so sash bars cannot be priced. Add a profile with the Sash role in the Library Manager."
                    : "The library has no mesh-shutter profile, so mesh bars cannot be priced. Add a profile with the Mesh sash role in the Library Manager.",
                    panelId);
            return definition;
        }

        /// <summary>Four mitred bars around <paramref name="outer"/> (frame-relative mm): two widths, two heights.</summary>
        private IEnumerable<ProfileLine> SashBars(Frame frame, GlassPanel panel, Rectangle2D outer, ProfileType role,
            ProfileDefinition? definition)
        {
            var bars = new List<ProfileLine>();
            foreach (double raw in new[] { outer.Width, outer.Width, outer.Height, outer.Height })
            {
                double length = Round(raw, _rules.LengthDecimals);
                double metres = length / 1000.0;
                var line = new ProfileLine
                {
                    FrameId = frame.Id,
                    ProfileId = Guid.Empty,
                    OpeningId = panel.Id,
                    Role = role,
                    DefinitionId = definition?.Id,
                    Name = definition?.Name ?? (role == ProfileType.Sash ? "(no sash profile)" : "(no mesh profile)"),
                    IsResolved = definition is not null,
                    IsDefault = true,
                    CutLengthMm = length,
                    StartCutAngle = 45,
                    EndCutAngle = 45,
                    Quantity = _windows,
                    WeightKg = definition is null ? 0 : Round(metres * definition.WeightKgPerMetre, _rules.QuantityDecimals),
                    CostPerMetre = definition?.CostPerMetre ?? 0,
                    Cost = definition is null ? 0 : Money(ToDecimal(metres) * definition.CostPerMetre)
                };
                _profiles.Add(line);
                bars.Add(line);
            }
            return bars;
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
                Quantity = _windows,
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

        /// <param name="sash">The opening's sash, if it has one: its glass sits in the sash, so the size comes from the
        /// sash's glass plus the sash profile's bite instead of from the frame members.</param>
        private GlassLine CalculateGlass(Frame frame, GlassPanel panel, IReadOnlyDictionary<Guid, ProfileDefinition?> profiles,
            SashLayout? sash, ProfileDefinition? sashDefinition, out GlassDefinition? definition)
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
            double width, height;
            if (sash is { } s)
            {
                double bite = sashDefinition?.GlazingBiteMm ?? 0;
                width = s.Glass.Width + 2 * bite - 2 * clearance;
                height = s.Glass.Height + 2 * bite - 2 * clearance;
            }
            else
            {
                width = b.Width + Bite(frame, profiles, MemberAxis.Vertical, b.Left, +1, b.Top, b.Bottom)
                                + Bite(frame, profiles, MemberAxis.Vertical, b.Right, -1, b.Top, b.Bottom) - 2 * clearance;
                height = b.Height + Bite(frame, profiles, MemberAxis.Horizontal, b.Top, +1, b.Left, b.Right)
                                  + Bite(frame, profiles, MemberAxis.Horizontal, b.Bottom, -1, b.Left, b.Right) - 2 * clearance;
            }
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
                Quantity = _windows,
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
                    Cost = cost,
                    Windows = _windows
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
                int pieces = group.Sum(p => p.Quantity);
                double length = Round(group.Sum(p => p.CutLengthMm * p.Quantity), _rules.LengthDecimals);
                bom.Add(new BomLine(BomCategory.Profile, group.Key, group.First().Name,
                    $"{pieces} pcs, {Format(length / 1000.0, "0.###")} m", pieces, "pcs", length, null,
                    Round(group.Sum(p => p.WeightKg * p.Quantity), _rules.QuantityDecimals), group.Sum(p => p.Cost * p.Quantity)));
            }

            foreach (var group in _glass.Where(g => g.IsResolved)
                         .GroupBy(g => (Id: g.DefinitionId!, g.WidthMm, g.HeightMm))
                         .OrderBy(g => g.Key.Id, StringComparer.Ordinal)
                         .ThenByDescending(g => g.Key.WidthMm).ThenByDescending(g => g.Key.HeightMm))
            {
                var weights = group.Select(g => g.WeightKg is { } w ? w * g.Quantity : (double?)null).ToList();
                bom.Add(new BomLine(BomCategory.Glass, group.Key.Id, group.First().Name,
                    $"{Format(group.Key.WidthMm)} × {Format(group.Key.HeightMm)} mm", group.Sum(g => g.Quantity), "pcs", null,
                    Round(group.Sum(g => g.AreaM2 * g.Quantity), _rules.AreaDecimals),
                    weights.All(w => w is null) ? null : Round(weights.Sum(w => w ?? 0), _rules.QuantityDecimals),
                    group.Sum(g => g.Cost * g.Quantity)));
            }

            foreach (var group in _materials.GroupBy(m => m.MaterialId)
                         .OrderBy(g => CategoryOf(g.First().Category)).ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                var first = group.First();
                bom.Add(new BomLine(CategoryOf(first.Category), group.Key, first.Name, "",
                    Round(group.Sum(m => m.Quantity * m.Windows), _rules.QuantityDecimals), UnitText(first.Unit), null, null, null,
                    group.Sum(m => m.Cost * m.Windows)));
            }

            var cutList = _profiles.Where(p => p.IsResolved && p.CutLengthMm > 0)
                .GroupBy(p => (Id: p.DefinitionId!, p.CutLengthMm, p.StartCutAngle, p.EndCutAngle))
                .OrderBy(g => g.Key.Id, StringComparer.Ordinal)
                .ThenByDescending(g => g.Key.CutLengthMm).ThenBy(g => g.Key.StartCutAngle).ThenBy(g => g.Key.EndCutAngle)
                .Select(g => new CutListLine(g.Key.Id, g.First().Name, g.Key.CutLengthMm, g.Key.StartCutAngle, g.Key.EndCutAngle,
                    g.Sum(p => p.Quantity), _library.FindProfile(g.Key.Id)?.StockLengthMm ?? 0))
                .ToList();

            var cost = _frames.Aggregate(CostSummary.Zero, (sum, f) => sum.Add(new CostSummary(
                f.Cost.Profiles * f.Quantity, f.Cost.Glass * f.Quantity, f.Cost.Materials * f.Quantity)));
            double weight = Round(_frames.Sum(f => f.WeightKg * f.Quantity), _rules.QuantityDecimals);
            return new CalculationResult(_library.Currency, _frames.AsReadOnly(), _profiles.AsReadOnly(), _glass.AsReadOnly(),
                _materials.AsReadOnly(), cutList.AsReadOnly(), bom.AsReadOnly(), cost, weight, _issues.AsReadOnly(),
                _openings.AsReadOnly());
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
