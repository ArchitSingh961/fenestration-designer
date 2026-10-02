using System.Windows;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Interaction;
using Fenestration.Core.Interfaces;
using Fenestration.Core.Models;
using Fenestration.Designer.Interaction;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// Draws interaction feedback on the overlay visual, never the design itself:
/// resize handles of the selected frame, ghost/invalid outlines of the current preview, the snap marker
/// (with an alignment line for one-axis snaps) and the box-selection rectangle.
/// </summary>
public sealed class InteractionOverlayLayer : IViewportLayer
{
    private readonly InteractionState _state;
    private readonly Func<Frame?> _handleFrame;

    /// <param name="handleFrame">The frame whose resize handles to show (the single selected frame), or null.</param>
    public InteractionOverlayLayer(InteractionState state, Func<Frame?> handleFrame)
    {
        _state = state;
        _handleFrame = handleFrame;
    }

    /// <summary>Overlays are not content; they never contribute to Fit to Screen.</summary>
    public BoundingBox2D Bounds => BoundingBox2D.Empty;

    public void Render(ViewportDrawingContext context)
    {
        var preview = _state.Preview;

        if (_state.DropTarget is { } drop)
            context.DrawRectangle(DesignTheme.DropTargetFill, DesignTheme.DropTargetPen, drop);

        foreach (var ghost in preview.Ghosts)
            context.DrawRectangle(preview.IsValid ? DesignTheme.GhostFill : DesignTheme.InvalidFill,
                preview.IsValid ? DesignTheme.GhostPen : DesignTheme.InvalidPen, ghost);

        if (_handleFrame() is { } frame)
            RenderHandles(context, preview.ReplacementFrames.TryGetValue(frame.Id, out var candidate) ? candidate : frame);

        if (preview.Snap is { } snap)
            RenderSnap(context, snap);

        if (_state.SelectionBox is { } box)
            context.DrawRectangle(
                _state.IsCrossingSelection ? DesignTheme.CrossingSelectionFill : DesignTheme.WindowSelectionFill,
                _state.IsCrossingSelection ? DesignTheme.CrossingSelectionPen : DesignTheme.WindowSelectionPen,
                box);
    }

    private static void RenderHandles(ViewportDrawingContext context, Frame frame)
    {
        const double h = DesignTheme.HandleHalfPixels;
        foreach (var (_, position) in FrameHandles.GetHandles(frame))
        {
            var p = context.ToScreen(position);
            context.DrawingContext.DrawRectangle(DesignTheme.HandleFill, DesignTheme.HandlePen,
                new Rect(ViewportDrawingContext.Crisp(p.X - h), ViewportDrawingContext.Crisp(p.Y - h), 2 * h, 2 * h));
        }
    }

    private static void RenderSnap(ViewportDrawingContext context, SnapIndicator snap)
    {
        var dc = context.DrawingContext;
        var pen = DesignTheme.SnapMarkerPen;
        var p = context.ToScreen(snap.Target);
        const double s = DesignTheme.SnapMarkerHalfPixels;

        if (snap.AlignFrom is { } from && from.DistanceTo(snap.Target) > GeometryTolerance.Default)
            dc.DrawLine(DesignTheme.SnapAlignPen, p, context.ToScreen(from));

        // One glyph per snap type, as in common CAD programs.
        switch (snap.Type)
        {
            case SnapType.Endpoint:
                dc.DrawRectangle(null, pen, new Rect(p.X - s, p.Y - s, 2 * s, 2 * s));
                break;
            case SnapType.Midpoint:
                dc.DrawLine(pen, new Point(p.X - s, p.Y + s), new Point(p.X, p.Y - s));
                dc.DrawLine(pen, new Point(p.X, p.Y - s), new Point(p.X + s, p.Y + s));
                dc.DrawLine(pen, new Point(p.X + s, p.Y + s), new Point(p.X - s, p.Y + s));
                break;
            case SnapType.Intersection:
                dc.DrawLine(pen, new Point(p.X - s, p.Y - s), new Point(p.X + s, p.Y + s));
                dc.DrawLine(pen, new Point(p.X - s, p.Y + s), new Point(p.X + s, p.Y - s));
                break;
            case SnapType.Center:
                dc.DrawEllipse(null, pen, p, s, s);
                break;
            case SnapType.Edge:
                dc.DrawLine(pen, new Point(p.X - s, p.Y - s), new Point(p.X + s, p.Y - s));
                dc.DrawLine(pen, new Point(p.X - s, p.Y + s), new Point(p.X + s, p.Y + s));
                dc.DrawLine(pen, new Point(p.X - s, p.Y - s), new Point(p.X + s, p.Y + s));
                break;
            case SnapType.Grid:
                dc.DrawLine(pen, new Point(p.X - s, p.Y), new Point(p.X + s, p.Y));
                dc.DrawLine(pen, new Point(p.X, p.Y - s), new Point(p.X, p.Y + s));
                break;
        }

        context.DrawText(snap.Type.ToString(), new Point(p.X + s + 3, p.Y + s), DesignTheme.SnapLabel);
    }
}
