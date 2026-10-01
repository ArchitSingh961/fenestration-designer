using Fenestration.Core.Geometry;

namespace Fenestration.Core.Interaction;

/// <summary>
/// Finds how far a drag can go before it becomes invalid. Given a valid offset and an invalid target offset,
/// it binary-searches along the straight line between them for the furthest valid offset, rounded to the
/// increment. The object then stops at the constraint instead of refusing to move.
/// </summary>
public static class DragClamp
{
    private const int MaxIterations = 40;

    public static Vector2D Clamp(Vector2D lastValid, Vector2D target, Func<Vector2D, bool> isValid, double incrementMm)
    {
        GeometryValidation.EnsurePositive(incrementMm);
        Vector2D span = target - lastValid;
        double length = span.Length;
        if (length <= GeometryTolerance.Epsilon) return lastValid;

        double lo = 0.0, hi = 1.0;
        Vector2D best = lastValid;
        for (int i = 0; i < MaxIterations && (hi - lo) * length > incrementMm / 4.0; i++)
        {
            double mid = (lo + hi) / 2.0;
            Vector2D candidate = Round(lastValid + span * mid, incrementMm);
            if (isValid(candidate))
            {
                lo = mid;
                best = candidate;
            }
            else
            {
                hi = mid;
            }
        }
        return best;
    }

    public static Vector2D Round(Vector2D v, double incrementMm)
        => new(Math.Round(v.X / incrementMm) * incrementMm, Math.Round(v.Y / incrementMm) * incrementMm);

    public static double Round(double value, double incrementMm) => Math.Round(value / incrementMm) * incrementMm;
}
