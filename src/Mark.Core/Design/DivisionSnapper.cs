using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>What a division position snapped to.</summary>
public enum DivisionSnapKind
{
    None,
    FrameCenter,
    BayCenter,
    AlignedDivision,
    Increment
}

/// <summary>A snapped division position.</summary>
public readonly record struct DivisionSnap(double Position, DivisionSnapKind Kind);

/// <summary>
/// One-dimensional snapping for dragging a mullion (X) or transom (Y). This is the basic snapping this
/// milestone needs; the general point snapping for later tools goes through <c>ISnapProvider</c>.
///
/// Targets, when within tolerance: the frame centre, the centre of the bay the division sits in (for an
/// equal split), and the position of a parallel division in another bay (alignment). Otherwise the
/// position is rounded to the increment (1 mm by default, or the snap-grid spacing).
/// </summary>
public static class DivisionSnapper
{
    public static DivisionSnap Snap(Frame frame, Guid divisionId, double proposed, double toleranceMm, double incrementMm)
    {
        GeometryValidation.EnsureFinite(proposed);
        GeometryValidation.EnsureValidTolerance(toleranceMm);
        GeometryValidation.EnsurePositive(incrementMm);

        var division = frame.Profiles.FirstOrDefault(p => p.Id == divisionId);
        if (division is null || Members.AxisOf(division) is not { } axis)
            return new DivisionSnap(RoundTo(proposed, incrementMm), DivisionSnapKind.Increment);

        var (spanStart, spanEnd) = Members.SpanOf(division, axis);
        var parallel = FrameLayout.BuildMembers(frame).Where(m => m.Axis == axis && m.Profile.Id != divisionId).ToList();

        var candidates = new List<(double Position, DivisionSnapKind Kind)>
        {
            ((axis == MemberAxis.Vertical ? frame.Width : frame.Height) / 2.0, DivisionSnapKind.FrameCenter)
        };

        // Bay centre: halfway between the nearest members on either side that share this division's span.
        var sharing = parallel.Where(m => m.SpanStart < spanEnd - GeometryTolerance.Default
                                          && m.SpanEnd > spanStart + GeometryTolerance.Default).ToList();
        double? below = sharing.Where(m => m.Position < proposed).Select(m => (double?)m.Position).Max();
        double? above = sharing.Where(m => m.Position > proposed).Select(m => (double?)m.Position).Min();
        if (below is { } lo && above is { } hi)
            candidates.Add(((lo + hi) / 2.0, DivisionSnapKind.BayCenter));

        // Alignment with divisions in other bays (those not sharing the span, which would collide instead).
        foreach (var m in parallel.Where(m => Members.IsDivision(m.Profile) && !sharing.Contains(m)))
            candidates.Add((m.Position, DivisionSnapKind.AlignedDivision));

        var best = candidates
            .Select(c => (c.Position, c.Kind, Distance: Math.Abs(c.Position - proposed)))
            .Where(c => c.Distance <= toleranceMm)
            .OrderBy(c => c.Distance)
            .FirstOrDefault();

        return best.Kind != DivisionSnapKind.None
            ? new DivisionSnap(best.Position, best.Kind)
            : new DivisionSnap(RoundTo(proposed, incrementMm), DivisionSnapKind.Increment);
    }

    private static double RoundTo(double value, double increment) => Math.Round(value / increment) * increment;
}
