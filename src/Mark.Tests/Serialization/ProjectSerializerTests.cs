using System.IO;
using System.Text.Json.Nodes;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Xunit;

namespace Mark.Tests.Serialization;

public class ProjectSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEntireModel()
    {
        Project original = TestData.CreateDemoProject();

        Project loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(original));

        AssertProjectsEqual(original, loaded);
    }

    [Fact]
    public void Serialize_WritesVersionAndUnits_AndOmitsDerivedGeometry()
    {
        string json = ProjectSerializer.Serialize(TestData.CreateDemoProject());
        JsonObject root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(ProjectFormatVersion.Current, root["version"]!.GetValue<int>());
        Assert.Equal("mm", root["project"]!["units"]!.GetValue<string>());

        JsonObject frame = root["project"]!["frames"]![0]!.AsObject();
        Assert.False(frame.ContainsKey("bounds"));
        Assert.False(frame.ContainsKey("center"));

        JsonObject profile = frame["profiles"]![0]!.AsObject();
        Assert.False(profile.ContainsKey("length"));
        Assert.False(profile.ContainsKey("angle"));
        Assert.False(profile.ContainsKey("segment"));
        Assert.Equal("mullion", profile["profileType"]!.GetValue<string>());

        Assert.False(frame["dimensions"]![0]!.AsObject().ContainsKey("value"));
    }

    [Fact]
    public void Deserialize_RejectsNewerVersion()
    {
        string json = ProjectSerializer.Serialize(new Project())
            .Replace($"\"version\": {ProjectFormatVersion.Current}", "\"version\": 999");

        var ex = Assert.Throws<InvalidOperationException>(() => ProjectSerializer.Deserialize(json));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Deserialize_RejectsMissingVersion()
    {
        Assert.Throws<InvalidOperationException>(
            () => ProjectSerializer.Deserialize("{ \"project\": { \"name\": \"x\" } }"));
    }

    [Fact]
    public void Deserialize_RejectsMalformedJson()
    {
        Assert.Throws<InvalidOperationException>(() => ProjectSerializer.Deserialize("{ not json"));
    }

    [Fact]
    public void Deserialize_RejectsNegativeGlassSize()
    {
        string json = ProjectSerializer.Serialize(TestData.CreateDemoProject())
            .Replace("\"width\": 600", "\"width\": -600");

        Assert.Throws<InvalidOperationException>(() => ProjectSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_RejectsZeroLengthProfile()
    {
        Project project = TestData.CreateDemoProject();
        var mullion = project.Frames[0].Profiles[0];
        mullion.EndPoint = mullion.StartPoint;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)));
        Assert.Contains("Profile", ex.Message);
    }

    [Fact]
    public void Deserialize_RejectsInvalidFrameSize()
    {
        Project project = TestData.CreateDemoProject();
        project.Frames[0].Width = 0;

        Assert.Throws<InvalidOperationException>(
            () => ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)));
    }

    [Fact]
    public async Task SaveAndLoad_File_RoundTrips()
    {
        Project original = TestData.CreateDemoProject();
        string path = Path.Combine(Path.GetTempPath(), $"fenestration-test-{Guid.NewGuid():N}.json");
        try
        {
            await ProjectSerializer.SaveAsync(original, path);
            Project loaded = await ProjectSerializer.LoadAsync(path);
            AssertProjectsEqual(original, loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Snapshot_PreservesIds_ButIsIndependent()
    {
        Project original = TestData.CreateDemoProject();

        Project snapshot = ProjectSerializer.Snapshot(original);
        AssertProjectsEqual(original, snapshot);

        snapshot.Frames[0].Width = 9999;
        snapshot.Frames[0].Profiles.Clear();

        Assert.Equal(1200, original.Frames[0].Width);
        Assert.Equal(2, original.Frames[0].Profiles.Count);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    internal static void AssertProjectsEqual(Project expected, Project actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Units, actual.Units);
        Assert.Equal(expected.Metadata, actual.Metadata);
        Assert.Equal(expected.Frames.Count, actual.Frames.Count);

        for (int i = 0; i < expected.Frames.Count; i++)
        {
            Frame ef = expected.Frames[i], af = actual.Frames[i];
            Assert.Equal(ef.Id, af.Id);
            Assert.Equal(ef.Bounds, af.Bounds);
            Assert.Equal(ef.Metadata, af.Metadata);

            Assert.Equal(ef.Profiles.Count, af.Profiles.Count);
            for (int p = 0; p < ef.Profiles.Count; p++)
            {
                Profile ep = ef.Profiles[p], ap = af.Profiles[p];
                Assert.Equal(ep.Id, ap.Id);
                Assert.Equal(ep.ProfileType, ap.ProfileType);
                Assert.Equal(ep.StartPoint, ap.StartPoint);
                Assert.Equal(ep.EndPoint, ap.EndPoint);
                Assert.Equal(ep.Thickness, ap.Thickness);
                Assert.Equal(ep.Rotation, ap.Rotation);
                Assert.Equal(ep.Properties, ap.Properties);
            }

            Assert.Equal(ef.GlassPanels.Count, af.GlassPanels.Count);
            for (int g = 0; g < ef.GlassPanels.Count; g++)
            {
                Assert.Equal(ef.GlassPanels[g].Id, af.GlassPanels[g].Id);
                Assert.Equal(ef.GlassPanels[g].Boundary, af.GlassPanels[g].Boundary);
                Assert.Equal(ef.GlassPanels[g].Thickness, af.GlassPanels[g].Thickness);
                Assert.Equal(ef.GlassPanels[g].Properties, af.GlassPanels[g].Properties);
            }

            Assert.Equal(ef.Dimensions.Count, af.Dimensions.Count);
            for (int d = 0; d < ef.Dimensions.Count; d++)
            {
                Assert.Equal(ef.Dimensions[d].Id, af.Dimensions[d].Id);
                Assert.Equal(ef.Dimensions[d].StartPoint, af.Dimensions[d].StartPoint);
                Assert.Equal(ef.Dimensions[d].EndPoint, af.Dimensions[d].EndPoint);
                Assert.Equal(ef.Dimensions[d].Orientation, af.Dimensions[d].Orientation);
            }
        }
    }
}
