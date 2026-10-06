using System.Globalization;
using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Production;
using Mark.Core.Serialization;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>What a production order's papers are made from.</summary>
/// <param name="Offcuts">Offcuts in stock the cutting list may use (empty: new bars only).</param>
public sealed record ProductionInputs(ProductionOrder Order, IProductLibrary Library, CalculationRules Rules, DesignRules DesignRules,
    IReadOnlyList<Offcut> Offcuts, string CompanyName, DateTime Now);

/// <summary>The papers of a production order and the cutting plan behind them.</summary>
public sealed record ProductionPapers(ProductionDocument Document, CuttingPlan Plan, Project Project, CalculationResult Result);

/// <summary>
/// Works out a production order's workshop papers (Milestone 16) from the designs as they were when production started:
/// the cutting list (profiles and steel, using offcuts in stock first when given), the glass order, the hardware pick
/// list, a shop drawing per window and a label for every piece and pane. Drawings are rendered like the canvas (WPF:
/// call on an STA thread).
/// </summary>
public static class ProductionBuilder
{
    public static ProductionPapers Build(ProductionInputs i)
    {
        ArgumentNullException.ThrowIfNull(i);
        var project = ProjectSerializer.Deserialize(i.Order.DocumentJson);
        var result = new CalculationEngine().Calculate(project, i.Library, i.Rules);
        var offcuts = i.Offcuts.Select(o => new StockOffcut(o.Id, o.DefinitionId, o.LengthMm)).ToList();
        var plan = new CuttingOptimizer().Optimize(result.Profiles, RatedLibrary.For(i.Library, project), i.Rules, offcuts);

        var windows = Windows(project);
        var names = new Names(project, windows);
        string Ref(Guid frameId) => windows.TryGetValue(frameId, out var w) ? w.Reference : "?";

        // Cutting list: a label number for every piece, in cutting order (profiles first, then steel).
        var labels = new List<PieceLabel>();
        var profiles = new List<CutProfile>();
        int pieceNumber = 0;
        foreach (var p in plan.Profiles.OrderBy(p => IsSteel(p, i.Library)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool steel = IsSteel(p, i.Library);
            var bars = new List<CutBar>();
            foreach (var bar in p.Bars)
            {
                var pieces = new List<CutPiece>();
                foreach (var cut in bar.Cuts)
                {
                    string label = $"P{++pieceNumber}";
                    string where = names.Where(cut);
                    string angles = Angles(cut);
                    pieces.Add(new CutPiece(label, cut.CutLengthMm, angles, where));
                    labels.Add(new PieceLabel(label, i.Order.OrderNumber, names.Window(cut.FrameId), p.Name,
                        $"{Mm(cut.CutLengthMm)} mm  ·  {angles}", names.Position(cut)));
                }
                string from = bar.IsOffcut ? $"Offcut {Mm(bar.StockLengthMm)} mm from stock" : $"{Mm(bar.StockLengthMm)} mm bar";
                string leftover = bar.HasRemnant ? $"Offcut {Mm(bar.RemnantMm)} mm → stock"
                    : bar.RemainingMm > 0 ? $"Waste {Mm(bar.RemainingMm)} mm" : "";
                bars.Add(new CutBar(bar.Number, from, bar.IsOffcut, pieces, leftover, bar.StockLengthMm,
                    Math.Max(0, bar.HasRemnant ? bar.RemnantMm : bar.RemainingMm), bar.HasRemnant));
            }
            var stockParts = p.Stock.Select(s => $"{s.Quantity} × {Mm(s.StockLengthMm)} mm").ToList();
            string stockText = (stockParts.Count > 0 ? string.Join(", ", stockParts) + " new" : "no new bars")
                               + (p.OffcutsUsed > 0 ? $" · {p.OffcutsUsed} offcut{(p.OffcutsUsed == 1 ? "" : "s")} from stock" : "");
            profiles.Add(new CutProfile(p.Name, i.Library.FindProfile(p.DefinitionId)?.Code ?? "", steel, stockText, bars));
        }
        int newBars = plan.Profiles.Sum(p => p.Stock.Sum(s => s.Quantity));
        int fromStock = plan.Profiles.Sum(p => p.OffcutsUsed);
        string cuttingSummary = $"{pieceNumber} piece{(pieceNumber == 1 ? "" : "s")} from {newBars} new bar{(newBars == 1 ? "" : "s")}"
                                + (fromStock > 0 ? $" and {fromStock} offcut{(fromStock == 1 ? "" : "s")} from stock" : "")
                                + $" · {plan.Utilization * 100:0} % used"
                                + (plan.Remnants.Any() ? $" · {plan.Remnants.Count()} new offcut{(plan.Remnants.Count() == 1 ? "" : "s")}" : "");

        // Glass: the same glass and size together, with the windows they are for.
        var glass = result.Glass.Where(g => g.IsResolved)
            .GroupBy(g => (g.Name, g.ThicknessMm, W: Math.Round(g.WidthMm, 1), H: Math.Round(g.HeightMm, 1)))
            .OrderBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(g => g.Key.W * g.Key.H)
            .Select(g => new GlassRow(g.Key.Name, g.Key.ThicknessMm, g.Key.W, g.Key.H, g.Sum(x => x.Quantity), g.First().AreaM2,
                string.Join(", ", g.GroupBy(x => x.FrameId).Select(f => $"{Ref(f.Key)} ({f.Sum(x => x.Quantity)})"))))
            .ToList();
        int paneNumber = 0;
        foreach (var g in result.Glass.Where(g => g.IsResolved))
        {
            var frame = project.Frames.FirstOrDefault(f => f.Id == g.FrameId);
            int pane = frame is null ? 0 : frame.GlassPanels.FindIndex(p => p.Id == g.GlassPanelId) + 1;
            for (int n = 0; n < g.Quantity; n++)
                labels.Add(new PieceLabel($"G{++paneNumber}", i.Order.OrderNumber, names.Window(g.FrameId), g.Name,
                    $"{Mm(g.WidthMm)} × {Mm(g.HeightMm)} mm", pane > 0 ? $"pane {pane} · {Mm(g.ThicknessMm)} mm" : $"{Mm(g.ThicknessMm)} mm"));
        }

        // Hardware and accessories for the whole order.
        var hardware = result.Materials
            .GroupBy(m => m.MaterialId)
            .Select(g =>
            {
                var first = g.First();
                double total = g.Sum(m => m.Quantity * m.Windows);
                var definition = i.Library.FindMaterial(g.Key);
                return new HardwareRow(first.Name, definition?.Code ?? "", CategoryName(first.Category), Quantity(total, first.Unit),
                    first.Unit.ToString().ToLowerInvariant(), string.Join(", ", g.Select(m => Ref(m.FrameId)).Distinct()));
            })
            .OrderBy(h => h.Category, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A shop drawing per window.
        var drawings = new List<ShopDrawing>();
        foreach (var frame in project.Frames)
        {
            var w = windows[frame.Id];
            var lines = new List<ShopLine>();
            foreach (var group in result.Profiles.Where(p => p.FrameId == frame.Id && p.IsResolved)
                         .GroupBy(p => (p.Name, Position: names.Position(p), p.CutLengthMm, Angles: Angles(p), Steel: p.Role == ProfileType.Reinforcement))
                         .OrderBy(g => g.Key.Steel).ThenBy(g => Rank(g.Key.Position)).ThenBy(g => g.Key.Position, StringComparer.Ordinal)
                         .ThenByDescending(g => g.Key.CutLengthMm))
                lines.Add(new ShopLine(group.Key.Steel ? "Steel" : "Profiles", group.Key.Name,
                    $"{Mm(group.Key.CutLengthMm)} mm · {group.Key.Angles} · {group.Key.Position}", group.Count().ToString(CultureInfo.InvariantCulture)));
            foreach (var g in result.Glass.Where(g => g.FrameId == frame.Id && g.IsResolved))
            {
                int pane = frame.GlassPanels.FindIndex(p => p.Id == g.GlassPanelId) + 1;
                lines.Add(new ShopLine("Glass", g.Name, $"{Mm(g.WidthMm)} × {Mm(g.HeightMm)} mm · pane {pane}", "1"));
            }
            foreach (var g in result.Materials.Where(m => m.FrameId == frame.Id).GroupBy(m => m.MaterialId))
                lines.Add(new ShopLine("Hardware", g.First().Name, CategoryName(g.First().Category),
                    Quantity(g.Sum(m => m.Quantity), g.First().Unit)));
            var system = frame.SystemId is { } id ? i.Library.FindSystem(id)?.Name : null;
            var openings = frame.GlassPanels.Select((p, n) => $"{n + 1}: {p.Opening.DisplayName()}");
            string details = string.Join("  ·  ", new[] { system ?? "", frame.Design.Location, string.Join(", ", openings) }
                .Where(t => !string.IsNullOrWhiteSpace(t)));
            drawings.Add(new ShopDrawing(w.Reference, frame.Design.Name, $"{Mm(frame.Width)} × {Mm(frame.Height)} mm", w.Quantity,
                details, QuotationBuilder.RenderDrawing(frame, i.DesignRules, i.Library, workshop: true), lines));
        }

        var document = new ProductionDocument
        {
            CompanyName = i.CompanyName,
            OrderNumber = i.Order.OrderNumber,
            QuoteNumber = i.Order.QuoteNumber,
            ProjectName = i.Order.ProjectName,
            ClientName = i.Order.ClientName,
            DueText = i.Order.DueDate is { } due ? $"Due {due.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}" : "",
            PrintedText = $"Printed {i.Now.ToLocalTime().ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)}",
            Profiles = profiles,
            CuttingSummary = cuttingSummary,
            CuttingIssues = plan.Issues.Where(x => x.Severity == IssueSeverity.Error).Select(x => x.Message).ToList(),
            Glass = glass,
            Hardware = hardware,
            Drawings = drawings,
            Labels = labels
        };
        return new ProductionPapers(document, plan, project, result);
    }

    /// <summary>Each design's reference ("W1", or "W3" by position when it has none) and quantity.</summary>
    public static IReadOnlyDictionary<Guid, (string Reference, int Quantity)> Windows(Project project)
        => project.Frames.Select((f, n) => (f.Id, Reference: string.IsNullOrWhiteSpace(f.Design.Reference) ? $"W{n + 1}" : f.Design.Reference.Trim(),
                Quantity: Math.Max(1, f.Design.Quantity)))
            .ToDictionary(x => x.Id, x => (x.Reference, x.Quantity));

    private static bool IsSteel(ProfileCuttingPlan plan, IProductLibrary library)
        => library.FindProfile(plan.DefinitionId) is { } p && p.Roles.Count > 0 && p.Roles.All(r => r == ProfileType.Reinforcement)
           || plan.Bars.SelectMany(b => b.Cuts).All(c => c.Role == ProfileType.Reinforcement) && plan.Bars.Count > 0;

    /// <summary>How pieces and windows are named on the papers: "W1", "frame top", "sash, opening 2".</summary>
    private sealed class Names
    {
        private readonly IReadOnlyDictionary<Guid, (string Reference, int Quantity)> _windows;
        private readonly Dictionary<Guid, string> _members = new();
        private readonly Dictionary<Guid, int> _openings = new();

        public Names(Project project, IReadOnlyDictionary<Guid, (string Reference, int Quantity)> windows)
        {
            _windows = windows;
            foreach (var frame in project.Frames)
            {
                for (int n = 0; n < frame.GlassPanels.Count; n++) _openings[frame.GlassPanels[n].Id] = n + 1;
                foreach (var member in frame.Profiles)
                    _members[member.Id] = member.ProfileType switch
                    {
                        ProfileType.Frame => $"frame {Side(member, frame)}",
                        ProfileType.Mullion => "mullion",
                        ProfileType.Transom => "transom",
                        _ => member.ProfileType.ToString().ToLowerInvariant()
                    };
            }
        }

        /// <summary>Which side of the frame an outer member is (frame coordinates: y grows downwards).</summary>
        private static string Side(Profile member, Frame frame)
        {
            bool horizontal = Math.Abs(member.EndPoint.X - member.StartPoint.X) >= Math.Abs(member.EndPoint.Y - member.StartPoint.Y);
            return horizontal
                ? (member.StartPoint.Y + member.EndPoint.Y) / 2 < frame.Height / 2 ? "top" : "bottom"
                : (member.StartPoint.X + member.EndPoint.X) / 2 < frame.Width / 2 ? "left" : "right";
        }

        /// <summary>"W1", or "W1 (2 windows)".</summary>
        public string Window(Guid frameId)
            => _windows.TryGetValue(frameId, out var w) ? w.Quantity > 1 ? $"{w.Reference} ({w.Quantity} windows)" : w.Reference : "";

        /// <summary>"W1 · frame top".</summary>
        public string Where(ProfileLine line)
            => $"{(_windows.TryGetValue(line.FrameId, out var w) ? w.Reference : "?")} · {Position(line)}";

        /// <summary>"frame top", "mullion", "sash, opening 2", "steel in frame left", "Sliding 2-track frame, opening 1".</summary>
        public string Position(ProfileLine line)
        {
            string member = _members.TryGetValue(line.ProfileId, out var m) ? m : "";
            string opening = line.OpeningId is { } o && _openings.TryGetValue(o, out int n) ? $"opening {n}" : "";
            string Join(params string[] parts) => string.Join(", ", parts.Where(t => t.Length > 0));
            if (line.PartOf == "Reinforcement")
                return member.Length > 0 ? $"steel in {member}" : Join("steel in sash", opening);
            if (line.PartOf is { Length: > 0 } part)
                return Join(part, opening);
            return line.Role switch
            {
                ProfileType.Frame => member.Length > 0 ? member : "frame",
                ProfileType.Mullion => "mullion",
                ProfileType.Transom => "transom",
                ProfileType.Sash => Join("sash", opening),
                ProfileType.MeshSash => Join("mesh sash", opening),
                _ => Join(line.Role.ToString().ToLowerInvariant(), opening)
            };
        }
    }

    /// <summary>The order pieces are listed in on a shop drawing: frame top, bottom, left, right; mullions and transoms; sashes; parts.</summary>
    private static int Rank(string position)
    {
        string p = position.StartsWith("steel in ", StringComparison.Ordinal) ? position[9..] : position;
        string[] order = { "frame top", "frame bottom", "frame left", "frame right", "mullion", "transom", "sash", "mesh sash" };
        int index = Array.FindIndex(order, o => p.StartsWith(o, StringComparison.Ordinal));
        return index < 0 ? order.Length : index;
    }

    private static string Angles(ProfileLine line)
        => $"{Mm(line.StartCutAngle)}° / {Mm(line.EndCutAngle)}°";

    private static string Mm(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Quantity(double value, MaterialUnit unit)
        => unit == MaterialUnit.Piece ? Math.Ceiling(value - 1e-9).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>"Hardware", "Gasket", "Glazing bead" … from the category's name.</summary>
    private static string CategoryName(MaterialCategory category)
    {
        string words = System.Text.RegularExpressions.Regex.Replace(category.ToString(), "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant();
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
