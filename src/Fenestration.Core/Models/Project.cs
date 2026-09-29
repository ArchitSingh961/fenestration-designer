namespace Fenestration.Core.Models;

/// <summary>
/// Root domain object representing an entire fenestration project.
/// This is the top-level entity serialized to/from JSON.
/// </summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable project name.</summary>
    public string Name { get; set; } = "Untitled Project";

    /// <summary>
    /// The unit system used for all dimensions.
    /// Always "mm" in the current version.
    /// </summary>
    public string Units { get; set; } = "mm";

    /// <summary>All frames in this project.</summary>
    public List<Frame> Frames { get; set; } = new();

    /// <summary>Extensible project-level metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Creates a deep copy of this project. New Ids are assigned to all objects.</summary>
    public Project Clone()
    {
        var clone = new Project
        {
            Id = Guid.NewGuid(),
            Name = Name,
            Units = Units,
            Metadata = new Dictionary<string, string>(Metadata)
        };

        foreach (var f in Frames)
            clone.Frames.Add(f.Clone());

        return clone;
    }
}
