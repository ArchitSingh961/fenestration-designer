using Mark.Core.Geometry;
using Mark.Designer.Rendering;

namespace Mark.Tests.Viewport;

/// <summary>Test double for a content layer with fixed world bounds (replaces the removed Milestone 3 demo layer).</summary>
internal sealed class TestRectangleLayer : IViewportLayer
{
    public TestRectangleLayer(Rectangle2D rectangle) => Rectangle = rectangle;

    public Rectangle2D Rectangle { get; }

    public BoundingBox2D Bounds => Rectangle.Bounds;

    public void Render(ViewportDrawingContext context) => context.DrawRectangle(null, null, Rectangle);
}
