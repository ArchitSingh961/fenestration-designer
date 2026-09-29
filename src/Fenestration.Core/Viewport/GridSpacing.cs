using Fenestration.Core.Geometry;

namespace Fenestration.Core.Viewport;

/// <summary>
/// Adaptive CAD grid spacing in world millimetres. The spacing is chosen from a fixed ladder of
/// "round" fabrication-friendly values so that minor lines stay at least a minimum number of pixels
/// apart at the current zoom; major lines are an integer multiple of the minor spacing.
/// </summary>
/// <param name="MinorMm">Distance between minor grid lines, in mm.</param>
/// <param name="MajorMm">Distance between major grid lines, in mm (an integer multiple of <paramref name="MinorMm"/>).</param>
public readonly record struct GridSpacing(double MinorMm, double MajorMm)
{
    /// <summary>The spacing ladder, in mm.</summary>
    public static IReadOnlyList<double> StandardStepsMm { get; } =
        new double[] { 1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 };

    /// <summary>Major lines are at least this many minor steps apart.</summary>
    public const int MinMajorRatio = 4;

    /// <summary>Ratio used when the ladder has no suitable major step.</summary>
    public const int FallbackMajorRatio = 5;

    /// <summary>Beyond the ladder, spacing grows by this factor.</summary>
    private const double BeyondLadderGrowth = 10.0;

    /// <summary>Number of minor intervals per major interval.</summary>
    public int MajorEvery => (int)Math.Round(MajorMm / MinorMm);

    /// <summary>
    /// Picks the finest ladder step whose on-screen spacing (step × zoom) is at least
    /// <paramref name="minMinorPixelSpacing"/>, and a matching major step.
    /// </summary>
    public static GridSpacing ForZoom(double zoom, double minMinorPixelSpacing)
    {
        GeometryValidation.EnsurePositive(zoom);
        GeometryValidation.EnsurePositive(minMinorPixelSpacing);

        double minor = double.NaN;
        foreach (double step in StandardStepsMm)
        {
            if (step * zoom >= minMinorPixelSpacing)
            {
                minor = step;
                break;
            }
        }

        if (double.IsNaN(minor))
        {
            minor = StandardStepsMm[^1];
            while (minor * zoom < minMinorPixelSpacing)
                minor *= BeyondLadderGrowth;
        }

        return new GridSpacing(minor, ChooseMajor(minor));
    }

    private static double ChooseMajor(double minor)
    {
        foreach (double step in StandardStepsMm)
        {
            double ratio = step / minor;
            if (ratio >= MinMajorRatio && Math.Abs(ratio - Math.Round(ratio)) < GeometryTolerance.Epsilon)
                return step;
        }
        return minor * FallbackMajorRatio;
    }
}
