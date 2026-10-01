using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Interfaces;
using Fenestration.Core.Models;

namespace Fenestration.Core.Snapping;

/// <summary>
/// Combines the snap providers. Call <see cref="BeginSession"/> when an interaction starts. The session
/// collects every target once and then answers snap queries cheaply on each mouse move.
///
/// Resolution rule (deterministic):
/// <list type="number">
///   <item>Walk <see cref="SnapSettings.Priority"/> from highest to lowest
///         (default Intersection → Endpoint → Midpoint → Center → Edge → Grid).</item>
///   <item>The first type with any enabled target within tolerance wins; within that type, the nearest target wins.</item>
///   <item>Grid applies only if no geometry target is within tolerance. It quantises the position to the world grid.</item>
///   <item>Otherwise nothing snaps (<see cref="SnapType.None"/>) and the raw position is returned.</item>
/// </list>
/// Snapping only proposes a position; constraint validation is a separate, later step.
/// </summary>
public sealed class SnapEngine
{
    public SnapEngine(SnapSettings settings, DesignRules rules, IEnumerable<ISnapTargetProvider>? providers = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Providers = (providers ?? DefaultProviders()).ToList();
    }

    public SnapSettings Settings { get; }

    public DesignRules Rules { get; }

    public IReadOnlyList<ISnapTargetProvider> Providers { get; }

    public static IEnumerable<ISnapTargetProvider> DefaultProviders() => new ISnapTargetProvider[]
    {
        new IntersectionSnapProvider(),
        new EndpointSnapProvider(),
        new MidpointSnapProvider(),
        new CenterSnapProvider(),
        new EdgeSnapProvider()
    };

    /// <param name="excludeIds">Objects being edited; they (and geometry that depends on them) are not targets.</param>
    public SnapSession BeginSession(Project project, IEnumerable<Guid>? excludeIds = null)
    {
        Settings.Validate();
        var targets = new Dictionary<SnapType, List<SnapTarget>>();
        var enabled = Providers.Where(p => Settings.IsEnabled(p.Type)).ToList();
        if (enabled.Count > 0)
        {
            var scene = new SnapScene(project, excludeIds, Rules);
            foreach (var provider in enabled)
            {
                if (!targets.TryGetValue(provider.Type, out var list))
                    targets[provider.Type] = list = new List<SnapTarget>();
                provider.CollectTargets(scene, list);
            }
        }
        return new SnapSession(Settings, targets);
    }
}

/// <summary>Snap queries for one interaction. Targets are fixed when the session starts.</summary>
public sealed class SnapSession
{
    private readonly SnapSettings _settings;
    private readonly IReadOnlyDictionary<SnapType, List<SnapTarget>> _targets;

    internal SnapSession(SnapSettings settings, IReadOnlyDictionary<SnapType, List<SnapTarget>> targets)
    {
        _settings = settings;
        _targets = targets;
    }

    /// <summary>A session that never snaps to geometry (grid rules still apply).</summary>
    public static SnapSession Empty(SnapSettings settings) => new(settings, new Dictionary<SnapType, List<SnapTarget>>());

    public int TargetCount => _targets.Values.Sum(t => t.Count);

    public IEnumerable<SnapTarget> Targets(SnapType type)
        => _targets.TryGetValue(type, out var list) ? list : Enumerable.Empty<SnapTarget>();

    /// <summary>Snaps a free 2-D point (e.g. a frame corner being placed).</summary>
    public SnapResult SnapPoint(Point2D point, double toleranceMm)
    {
        GeometryValidation.EnsureFinite(point);
        GeometryValidation.EnsureValidTolerance(toleranceMm);

        foreach (var type in _settings.Priority)
        {
            if (type == SnapType.Grid || !_targets.TryGetValue(type, out var list)) continue;

            SnapResult? best = null;
            foreach (var target in list)
            {
                Point2D candidate = target.Line is { } line ? line.ClosestPoint(point) : target.Point;
                double distance = candidate.DistanceTo(point);
                if (distance <= toleranceMm && (best is null || distance < best.Distance))
                    best = new SnapResult { Point = candidate, Type = type, Distance = distance };
            }
            if (best is not null) return best;
        }

        if (_settings.GridEnabled && _settings.Priority.Contains(SnapType.Grid))
        {
            var snapped = GridSnapProvider.Snap(point, _settings.GridSpacingMm);
            return new SnapResult { Point = snapped, Type = SnapType.Grid, Distance = snapped.DistanceTo(point) };
        }

        return new SnapResult { Point = point, Type = SnapType.None, Distance = 0 };
    }

    /// <summary>
    /// Snaps one coordinate (e.g. a mullion's X). Distance is measured along <paramref name="axis"/> only, so a
    /// mullion aligns with any target in the same column. Point targets contribute their coordinate; edge targets
    /// contribute only if they run perpendicular to the axis (a vertical edge has a single X).
    /// </summary>
    public AxisSnapResult SnapCoordinate(SnapAxis axis, double value, double toleranceMm)
    {
        GeometryValidation.EnsureFinite(value);
        GeometryValidation.EnsureValidTolerance(toleranceMm);

        foreach (var type in _settings.Priority)
        {
            if (type == SnapType.Grid || !_targets.TryGetValue(type, out var list)) continue;

            AxisSnapResult? best = null;
            double bestDistance = double.MaxValue;
            foreach (var target in list)
            {
                if (CoordinateOf(target, axis) is not { } coordinate) continue;
                double distance = Math.Abs(coordinate - value);
                if (distance <= toleranceMm && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = new AxisSnapResult(coordinate, type, target.Point);
                }
            }
            if (best is not null) return best.Value;
        }

        if (_settings.GridEnabled && _settings.Priority.Contains(SnapType.Grid))
            return new AxisSnapResult(GridSnapProvider.Snap(value, _settings.GridSpacingMm), SnapType.Grid, null);

        return new AxisSnapResult(value, SnapType.None, null);
    }

    private static double? CoordinateOf(SnapTarget target, SnapAxis axis)
    {
        if (target.Line is not { } line)
            return axis == SnapAxis.X ? target.Point.X : target.Point.Y;

        bool vertical = Math.Abs(line.End.X - line.Start.X) <= GeometryTolerance.Default;
        bool horizontal = Math.Abs(line.End.Y - line.Start.Y) <= GeometryTolerance.Default;
        if (axis == SnapAxis.X && vertical) return line.Start.X;
        if (axis == SnapAxis.Y && horizontal) return line.Start.Y;
        return null;
    }
}

/// <summary>
/// Adapter exposing the snap engine through the original Milestone 1 <see cref="ISnapProvider"/> contract
/// (one-off point queries against the current project).
/// </summary>
public sealed class ProjectSnapProvider : ISnapProvider
{
    private readonly SnapEngine _engine;
    private readonly Func<Project> _project;

    public ProjectSnapProvider(SnapEngine engine, Func<Project> project)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public bool IsEnabled
    {
        get => _engine.Settings.ObjectSnapEnabled;
        set => _engine.Settings.ObjectSnapEnabled = value;
    }

    public double ToleranceMm { get; set; } = 5.0;

    public SnapResult? FindSnap(Point2D worldPoint, double toleranceMm, IReadOnlySet<Guid>? excludeIds = null)
    {
        var result = _engine.BeginSession(_project(), excludeIds).SnapPoint(worldPoint, toleranceMm);
        return result.Type == SnapType.None ? null : result;
    }
}
