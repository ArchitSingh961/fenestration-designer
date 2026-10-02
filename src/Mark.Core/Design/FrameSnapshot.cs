using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>
/// An immutable deep copy of one frame's state, with every Id preserved. Used to edit frames atomically
/// (work on a copy, commit only if valid) and as the memento behind frame-editing undo/redo.
/// It holds domain data only, never rendering state.
/// </summary>
public sealed class FrameSnapshot
{
    private readonly Frame _state;

    private FrameSnapshot(Frame state) => _state = state;

    public Guid FrameId => _state.Id;

    public static FrameSnapshot Capture(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var copy = new Frame { Id = frame.Id };
        CopyInto(frame, copy);
        return new FrameSnapshot(copy);
    }

    /// <summary>A new, independent frame with the snapshot's state and Ids.</summary>
    public Frame ToFrame()
    {
        var frame = new Frame { Id = _state.Id };
        CopyInto(_state, frame);
        return frame;
    }

    /// <summary>Overwrites <paramref name="target"/>'s geometry and children with fresh copies of this snapshot.</summary>
    public void ApplyTo(Frame target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Id != _state.Id)
            throw new ArgumentException("Snapshot belongs to a different frame.", nameof(target));
        CopyInto(_state, target);
    }

    private static void CopyInto(Frame source, Frame target)
    {
        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.Height = source.Height;
        target.Metadata = new Dictionary<string, string>(source.Metadata);
        target.Design = source.Design.Copy();
        target.Profiles = source.Profiles.Select(CopyOf).ToList();
        target.GlassPanels = source.GlassPanels.Select(CopyOf).ToList();
        target.Dimensions = source.Dimensions.Select(CopyOf).ToList();
    }

    internal static Profile CopyOf(Profile p) => new()
    {
        Id = p.Id,
        ProfileType = p.ProfileType,
        StartPoint = p.StartPoint,
        EndPoint = p.EndPoint,
        Thickness = p.Thickness,
        Rotation = p.Rotation,
        ProfileDefinitionId = p.ProfileDefinitionId,
        Properties = new Dictionary<string, string>(p.Properties)
    };

    internal static GlassPanel CopyOf(GlassPanel g) => new()
    {
        Id = g.Id,
        Boundary = g.Boundary,
        Thickness = g.Thickness,
        GlassDefinitionId = g.GlassDefinitionId,
        Opening = g.Opening,
        HasMesh = g.HasMesh,
        Properties = new Dictionary<string, string>(g.Properties)
    };

    internal static Dimension CopyOf(Dimension d) => new()
    {
        Id = d.Id,
        StartPoint = d.StartPoint,
        EndPoint = d.EndPoint,
        Orientation = d.Orientation
    };
}
