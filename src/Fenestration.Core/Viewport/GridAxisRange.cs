using Fenestration.Core.Geometry;

namespace Fenestration.Core.Viewport;

/// <summary>
/// The grid lines of one axis that fall inside a visible world interval.
/// Line <c>i</c> sits at world position <c>i × SpacingMm</c>; computing each position from its index
/// (rather than by repeated addition) keeps lines exactly on the grid with no accumulated drift.
/// Nothing is allocated: callers loop from <see cref="FirstIndex"/> to <see cref="LastIndex"/>.
/// </summary>
public readonly record struct GridAxisRange(long FirstIndex, long LastIndex, double SpacingMm)
{
    /// <summary>Number of lines in the range (0 if none are visible).</summary>
    public long Count => LastIndex >= FirstIndex ? LastIndex - FirstIndex + 1 : 0;

    /// <summary>World position (mm) of grid line <paramref name="index"/>.</summary>
    public double PositionAt(long index) => index * SpacingMm;

    /// <summary>Grid lines with spacing <paramref name="spacingMm"/> lying within [<paramref name="minMm"/>, <paramref name="maxMm"/>].</summary>
    public static GridAxisRange Visible(double minMm, double maxMm, double spacingMm)
    {
        GeometryValidation.EnsureFinite(minMm);
        GeometryValidation.EnsureFinite(maxMm);
        GeometryValidation.EnsurePositive(spacingMm);
        return new GridAxisRange(
            (long)Math.Ceiling(minMm / spacingMm),
            (long)Math.Floor(maxMm / spacingMm),
            spacingMm);
    }
}
