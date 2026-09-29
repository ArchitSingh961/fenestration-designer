using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Tests;

/// <summary>Shared sample designs for tests.</summary>
internal static class TestData
{
    /// <summary>1200 × 1500 frame with one mullion, one transom, two glass panels and two dimensions.</summary>
    public static Project CreateDemoProject()
    {
        var frame = Frame.Create(0, 0, 1200, 1500);
        frame.Metadata["series"] = "demo";

        frame.Profiles.Add(new Profile
        {
            ProfileType = ProfileType.Mullion,
            StartPoint = new Point2D(600, 0),
            EndPoint = new Point2D(600, 1500),
            Thickness = 60
        });
        frame.Profiles.Add(new Profile
        {
            ProfileType = ProfileType.Transom,
            StartPoint = new Point2D(0, 900),
            EndPoint = new Point2D(600, 900),
            Properties = { ["finish"] = "anodised" }
        });

        frame.GlassPanels.Add(new GlassPanel { Boundary = new Rectangle2D(0, 0, 600, 900) });
        frame.GlassPanels.Add(new GlassPanel { Boundary = new Rectangle2D(600, 0, 600, 1500), Thickness = 24 });

        frame.Dimensions.Add(new Dimension
        {
            StartPoint = new Point2D(0, 0),
            EndPoint = new Point2D(1200, 0),
            Orientation = DimensionOrientation.Horizontal
        });
        frame.Dimensions.Add(new Dimension
        {
            StartPoint = new Point2D(0, 0),
            EndPoint = new Point2D(0, 1500),
            Orientation = DimensionOrientation.Vertical
        });

        var project = new Project { Name = "Demo Window" };
        project.Frames.Add(frame);
        return project;
    }
}
