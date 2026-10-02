using Fenestration.Core.Models;

namespace Fenestration.Data;

/// <summary>A saved project as listed in the Open dialog (no design data).</summary>
public sealed record ProjectSummary(Guid Id, string Name, DateTime CreatedUtc, DateTime ModifiedUtc);

/// <summary>
/// Storage for projects. A project is saved whole, as one versioned document (the project-file JSON format), and every
/// Id inside it is preserved. Saving also records which library products the project references, so the library can
/// refuse to delete a product a saved project still uses.
/// </summary>
public interface IProjectRepository
{
    /// <summary>Inserts the project, or replaces the saved version with the same <see cref="Project.Id"/> (one transaction).</summary>
    void Save(Project project);

    /// <summary>Loads a saved project (older formats are upgraded and the design is validated on the way in).</summary>
    /// <exception cref="DataStoreException">No such project, or its stored data is damaged.</exception>
    Project Load(Guid id);

    bool Exists(Guid id);

    /// <summary>All saved projects, most recently modified first (then by name and Id, so the order is always defined).</summary>
    IReadOnlyList<ProjectSummary> List();

    /// <summary>Deletes a saved project and its reference records.</summary>
    /// <exception cref="DataStoreException">No such project.</exception>
    void Delete(Guid id);

    /// <summary>The saved projects that reference this library product, in the same order as <see cref="List"/>.</summary>
    IReadOnlyList<ProjectSummary> FindUsing(LibraryItemKind kind, string definitionId);
}
