using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Utilities;

/// <summary>
/// Domain-level validation: fenestration-specific ranges for dimensions and thicknesses, and
/// whole-project checks. Pure geometric checks (finite values, degenerate segments) live in
/// <see cref="GeometryValidation"/>.
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Returns true if the given dimension is within the valid range
    /// [<see cref="Units.MinDimensionMm"/>, <see cref="Units.MaxDimensionMm"/>]. NaN is invalid.
    /// </summary>
    public static bool IsValidDimension(double valueMm)
        => valueMm >= Units.MinDimensionMm && valueMm <= Units.MaxDimensionMm;

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> if the dimension is invalid.
    /// </summary>
    public static void EnsureValidDimension(double valueMm, string paramName)
    {
        if (!IsValidDimension(valueMm))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                valueMm,
                $"Dimension must be between {Units.MinDimensionMm} mm and {Units.MaxDimensionMm} mm.");
        }
    }

    /// <summary>
    /// Returns true if the given thickness is positive and no more than <see cref="Units.MaxThicknessMm"/>.
    /// </summary>
    public static bool IsValidThickness(double thicknessMm)
        => thicknessMm > 0 && thicknessMm <= Units.MaxThicknessMm;

    public static void EnsureValidThickness(double thicknessMm, string paramName)
    {
        if (!IsValidThickness(thicknessMm))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                thicknessMm,
                $"Thickness must be greater than 0 mm and at most {Units.MaxThicknessMm} mm.");
        }
    }

    /// <summary>
    /// A library reference is either absent (null = the library default) or a non-blank id. Whether the id exists
    /// is checked by the calculation engine, since a project may be opened with a different library.
    /// </summary>
    public static void EnsureValidReference(string? id, string paramName)
    {
        if (id is not null && string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A library reference must not be blank.", paramName);
    }

    /// <summary>
    /// Clamps a value to the valid dimension range.
    /// </summary>
    public static double ClampDimension(double valueMm)
        => Math.Clamp(valueMm, Units.MinDimensionMm, Units.MaxDimensionMm);

    /// <summary>
    /// Checks an entire project (e.g. one just loaded from disk) so corrupt geometry is rejected
    /// at the boundary instead of surfacing later as NaN in the renderer or calculation engine.
    /// </summary>
    /// <exception cref="ArgumentException">Any object in the project has invalid geometry.</exception>
    public static void EnsureValidProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        foreach (var frame in project.Frames)
        {
            string where = $"Frame {frame.Id}";
            GeometryValidation.EnsureFinite(frame.X, $"{where} X");
            GeometryValidation.EnsureFinite(frame.Y, $"{where} Y");
            EnsureValidDimension(frame.Width, $"{where} Width");
            EnsureValidDimension(frame.Height, $"{where} Height");

            foreach (var profile in frame.Profiles)
            {
                GeometryValidation.EnsureNonDegenerate(profile.Segment, paramName: $"Profile {profile.Id}");
                EnsureValidThickness(profile.Thickness, $"Profile {profile.Id} Thickness");
                GeometryValidation.EnsureFinite(profile.Rotation, $"Profile {profile.Id} Rotation");
                EnsureValidReference(profile.ProfileDefinitionId, $"Profile {profile.Id} ProfileDefinitionId");
            }

            foreach (var glass in frame.GlassPanels)
            {
                EnsureValidThickness(glass.Thickness, $"Glass {glass.Id} Thickness");
                EnsureValidReference(glass.GlassDefinitionId, $"Glass {glass.Id} GlassDefinitionId");
            }

            foreach (var dimension in frame.Dimensions)
            {
                GeometryValidation.EnsureFinite(dimension.StartPoint, $"Dimension {dimension.Id} StartPoint");
                GeometryValidation.EnsureFinite(dimension.EndPoint, $"Dimension {dimension.Id} EndPoint");
            }
        }
    }
}
