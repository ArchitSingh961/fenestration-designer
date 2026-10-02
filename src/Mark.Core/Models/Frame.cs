using Mark.Core.Geometry;
using Mark.Core.Utilities;

namespace Mark.Core.Models;

/// <summary>
/// A window or door frame — the primary container in the design.
/// All child geometry (profiles, glass, dimensions) is stored here.
/// Position and size are in millimetres.
/// </summary>
public class Frame
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>X position of the frame's top-left corner in world coordinates (mm).</summary>
    public double X { get; set; }

    /// <summary>Y position of the frame's top-left corner in world coordinates (mm).</summary>
    public double Y { get; set; }

    /// <summary>Outer width of the frame in mm.</summary>
    public double Width { get; set; }

    /// <summary>Outer height of the frame in mm.</summary>
    public double Height { get; set; }

    /// <summary>Profiles (mullions, transoms, sashes, etc.) within this frame.</summary>
    public List<Profile> Profiles { get; set; } = new();

    /// <summary>Glass panels within this frame.</summary>
    public List<GlassPanel> GlassPanels { get; set; } = new();

    /// <summary>Dimension annotations attached to this frame.</summary>
    public List<Dimension> Dimensions { get; set; } = new();

    /// <summary>Extensible metadata (e.g. notes, tags, series name).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// What this frame is as a product: its design reference (e.g. "W1"), how many are needed, where they go.
    /// Never null; it is not geometry, so editing it never changes the layout.
    /// </summary>
    public DesignInfo Design
    {
        get => _design;
        set => _design = value ?? new DesignInfo();
    }

    private DesignInfo _design = new();

    /// <summary>
    /// Creates a frame with validated outer dimensions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Width or height is outside the valid range.</exception>
    public static Frame Create(double x, double y, double width, double height)
    {
        ValidationHelper.EnsureValidDimension(width, nameof(width));
        ValidationHelper.EnsureValidDimension(height, nameof(height));
        return new Frame { X = x, Y = y, Width = width, Height = height };
    }

    // ── Derived geometry ────────────────────────────────────────────

    /// <summary>Returns the axis-aligned bounding rectangle of the frame.</summary>
    public Rectangle2D Bounds => new(X, Y, Width, Height);

    /// <summary>The center point of the frame.</summary>
    public Point2D Center => new(X + Width / 2.0, Y + Height / 2.0);

    /// <summary>Creates a deep copy of this frame including all children. New Ids are assigned.</summary>
    public Frame Clone()
    {
        var clone = new Frame
        {
            Id = Guid.NewGuid(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Metadata = new Dictionary<string, string>(Metadata),
            Design = Design.Copy()
        };

        foreach (var p in Profiles)
            clone.Profiles.Add(p.Clone());

        foreach (var g in GlassPanels)
            clone.GlassPanels.Add(g.Clone());

        // Dimensions are re-created (they reference geometry positions).
        foreach (var d in Dimensions)
        {
            clone.Dimensions.Add(new Dimension
            {
                Id = Guid.NewGuid(),
                StartPoint = d.StartPoint,
                EndPoint = d.EndPoint,
                Orientation = d.Orientation
            });
        }

        return clone;
    }
}
