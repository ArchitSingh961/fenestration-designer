using Fenestration.Core.Models;

namespace Fenestration.Core.Serialization;

/// <summary>
/// Versioned wrapper around a <see cref="Project"/> for JSON persistence.
/// Always check <see cref="Version"/> when loading to handle future migrations.
/// </summary>
public class ProjectFile
{
    /// <summary>
    /// File format version. See <see cref="ProjectFormatVersion"/>.
    /// </summary>
    public int Version { get; set; } = ProjectFormatVersion.Current;

    /// <summary>The project data.</summary>
    public Project Project { get; set; } = new();
}
