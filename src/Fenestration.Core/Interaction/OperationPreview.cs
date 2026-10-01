using Fenestration.Core.Geometry;
using Fenestration.Core.Interfaces;
using Fenestration.Core.Models;

namespace Fenestration.Core.Interaction;

/// <summary>Where a snap happened, for on-screen feedback (world mm).</summary>
/// <param name="Target">The snapped-to geometry (e.g. the endpoint).</param>
/// <param name="Type">What kind of snap it was.</param>
/// <param name="AlignFrom">For one-axis snaps: the point on the moving object, so the UI can draw an alignment line to <paramref name="Target"/>.</param>
public readonly record struct SnapIndicator(Point2D Target, SnapType Type, Point2D? AlignFrom = null);

/// <summary>
/// What an interaction WOULD do, shown while the user is still dragging. It is never written into the model.
/// <list type="bullet">
///   <item><see cref="ReplacementFrames"/>: fully validated candidate copies of frames (with re-derived glass),
///         drawn in place of the committed frames.</item>
///   <item><see cref="Ghosts"/>: outlines of a candidate that is NOT valid (drawn as a warning), or of something
///         being created that doesn't exist yet.</item>
///   <item><see cref="IsValid"/> / <see cref="Message"/>: whether the raw candidate passes validation, and why not.</item>
/// </list>
/// </summary>
public sealed class OperationPreview
{
    public static OperationPreview Empty { get; } = new(new Dictionary<Guid, Frame>(), Array.Empty<Rectangle2D>(), true, null, null);

    public OperationPreview(
        IReadOnlyDictionary<Guid, Frame> replacementFrames,
        IReadOnlyList<Rectangle2D> ghosts,
        bool isValid,
        string? message,
        SnapIndicator? snap)
    {
        ReplacementFrames = replacementFrames;
        Ghosts = ghosts;
        IsValid = isValid;
        Message = message;
        Snap = snap;
    }

    public IReadOnlyDictionary<Guid, Frame> ReplacementFrames { get; }

    public IReadOnlyList<Rectangle2D> Ghosts { get; }

    public bool IsValid { get; }

    public string? Message { get; }

    public SnapIndicator? Snap { get; }

    public bool IsEmpty => ReplacementFrames.Count == 0 && Ghosts.Count == 0 && Message is null && Snap is null;
}
