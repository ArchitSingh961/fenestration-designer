using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Xunit;

namespace Mark.Tests.Library;

/// <summary>The design's library references survive copying, snapshots and saving, and blank ones are rejected.</summary>
public class LibraryReferenceTests
{
    private static Frame AssignedFrame()
    {
        var (_, frame) = TestLibrary.SingleFrame();
        var library = TestLibrary.Create();
        FrameEditor.AssignGlass(frame, frame.GlassPanels.Select(g => g.Id).ToList(), TestLibrary.Toughened8, library, TestLibrary.Rules);
        FrameEditor.AssignProfile(frame, frame.Profiles.Select(p => p.Id).ToList(), TestLibrary.Frame50, library, TestLibrary.Rules);
        return frame;
    }

    [Fact]
    public void NewObjects_HaveNoReference()
    {
        Assert.Null(new Profile().ProfileDefinitionId);
        Assert.Null(new GlassPanel().GlassDefinitionId);
    }

    [Fact]
    public void Clone_CopiesReferences()
    {
        var clone = AssignedFrame().Clone();
        Assert.All(clone.Profiles, p => Assert.Equal(TestLibrary.Frame50, p.ProfileDefinitionId));
        Assert.All(clone.GlassPanels, g => Assert.Equal(TestLibrary.Toughened8, g.GlassDefinitionId));
    }

    [Fact]
    public void Snapshot_RestoresReferences()
    {
        var frame = AssignedFrame();
        var snapshot = FrameSnapshot.Capture(frame);
        frame.GlassPanels[0].GlassDefinitionId = null;
        frame.Profiles[0].ProfileDefinitionId = null;

        snapshot.ApplyTo(frame);
        Assert.Equal(TestLibrary.Toughened8, frame.GlassPanels[0].GlassDefinitionId);
        Assert.Equal(TestLibrary.Frame50, frame.Profiles[0].ProfileDefinitionId);
    }

    [Fact]
    public void ProjectFile_RoundTripsReferences()
    {
        var project = new Project();
        project.Frames.Add(AssignedFrame());

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        Assert.All(loaded.Frames[0].Profiles, p => Assert.Equal(TestLibrary.Frame50, p.ProfileDefinitionId));
        Assert.All(loaded.Frames[0].GlassPanels, g => Assert.Equal(TestLibrary.Toughened8, g.GlassDefinitionId));
    }

    [Fact]
    public void ProjectFile_WithoutReferences_StillLoads_AndWritesNoReferenceFields()
    {
        var json = ProjectSerializer.Serialize(TestData.CreateDemoProject());
        Assert.DoesNotContain("DefinitionId", json);
        Assert.All(ProjectSerializer.Deserialize(json).Frames[0].GlassPanels, g => Assert.Null(g.GlassDefinitionId));
    }

    [Fact]
    public void ProjectFile_WithABlankReference_IsRejected()
    {
        var project = TestData.CreateDemoProject();
        project.Frames[0].GlassPanels[0].GlassDefinitionId = "  ";
        var ex = Assert.Throws<InvalidOperationException>(() => ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)));
        Assert.Contains("must not be blank", ex.Message);
    }

    [Fact]
    public void MovingADivision_KeepsGlassReferences()
    {
        var (_, frame) = TestLibrary.SingleFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, TestLibrary.Rules);
        FrameEditor.AssignGlass(frame, new[] { frame.GlassPanels[0].Id }, TestLibrary.Laminated10, TestLibrary.Create(), TestLibrary.Rules);

        FrameEditor.MoveDivision(frame, mullion, 500, TestLibrary.Rules);

        var left = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();
        Assert.Equal(TestLibrary.Laminated10, left.GlassDefinitionId);
        Assert.Equal(new Rectangle2D(60, 60, 410, 1380), left.Boundary);
        Assert.Null(frame.GlassPanels.OrderBy(g => g.Boundary.Left).Last().GlassDefinitionId);
    }
}
