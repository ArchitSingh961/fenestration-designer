using Mark.Core.Models;

namespace Mark.Data;

/// <summary>A saved project (quote) as listed in the quote list and dashboard (no design data).</summary>
public sealed record ProjectSummary(Guid Id, string Name, DateTime CreatedUtc, DateTime ModifiedUtc)
{
    public string QuoteNumber { get; init; } = "";
    public string ClientName { get; init; } = "";
    public QuoteStatus Status { get; init; } = QuoteStatus.Active;
    public int DesignCount { get; init; }

    /// <summary>Pieces: the designs' quantities added up.</summary>
    public int Quantity { get; init; }

    public double AreaM2 { get; init; }

    /// <summary>Quote value when it was last saved by the designer (null = not priced, e.g. saved before quotes existed).</summary>
    public decimal? Value { get; init; }

    public string Currency { get; init; } = "";

    /// <summary>Who saved the quote first ("" before Milestone 14).</summary>
    public string CreatedBy { get; init; } = "";

    /// <summary>Who saved it last ("" before Milestone 14).</summary>
    public string ModifiedBy { get; init; } = "";

    /// <summary>The client's city (for sales by city).</summary>
    public string ClientCity { get; init; } = "";

    /// <summary>When the quote was won or lost (null while active).</summary>
    public DateTime? DecidedUtc { get; init; }

    /// <summary>The order number once it became an order, or "".</summary>
    public string OrderNumber { get; init; } = "";

    /// <summary>0 for the first quote, 1 for R1…</summary>
    public int Revision { get; init; }

    /// <summary>"QT-00012 R1".</summary>
    public string NumberText => Revision > 0 && QuoteNumber.Length > 0 ? $"{QuoteNumber} R{Revision}" : QuoteNumber;
}

/// <summary>An earlier revision of a quote, as it was saved before the next revision was started.</summary>
public sealed record ProjectRevision(Guid ProjectId, int Revision, DateTime SavedUtc, string SavedBy, decimal? Value, string Currency)
{
    /// <summary>"R0" for the first quote, "R1"…</summary>
    public string Name => $"R{Revision}";
}

/// <summary>Who is working in MARK: recorded with every saved quote and in its history.</summary>
public sealed record ProjectUser(string UserId, string Name)
{
    public static ProjectUser Unknown { get; } = new("", "");

    /// <summary>The name, or the User ID when there is no name.</summary>
    public string DisplayName => Name.Length > 0 ? Name : UserId;
}

/// <summary>What happened to a quote.</summary>
public enum ProjectAction
{
    Created,
    Saved,
    Deleted
}

/// <summary>One line of a quote's history: when, who, what, and what changed (e.g. "Status Active → Won").</summary>
public sealed record ProjectHistoryEntry(
    Guid ProjectId,
    string QuoteNumber,
    string ProjectName,
    DateTime TimeUtc,
    string UserId,
    string UserName,
    ProjectAction Action,
    string Detail)
{
    public string Who => UserName.Length > 0 ? UserName : UserId.Length > 0 ? UserId : "Someone";
}

/// <summary>The priced value of a quote, stored with it for the quote list (computed by the caller's calculation).</summary>
public readonly record struct QuoteValue(decimal Amount, string Currency);

/// <summary>
/// Storage for projects. A project is saved whole, as one versioned document (the project-file JSON format), and every
/// Id inside it is preserved. Saving also records which library products the project references, so the library can
/// refuse to delete a product a saved project still uses.
/// </summary>
public interface IProjectRepository
{
    /// <summary>Who saves and deletes from now on (recorded with the quote and in its history).</summary>
    ProjectUser User { get; set; }

    /// <summary>A quote's history, newest first.</summary>
    IReadOnlyList<ProjectHistoryEntry> History(Guid projectId);

    /// <summary>The latest history of all quotes, newest first (at most <paramref name="count"/> lines).</summary>
    IReadOnlyList<ProjectHistoryEntry> RecentHistory(int count);

    /// <summary>
    /// Keeps the saved version of a quote as a revision (its current revision number) before the next one is started.
    /// </summary>
    /// <exception cref="DataStoreException">The quote is not saved.</exception>
    void KeepRevision(Guid projectId);

    /// <summary>The kept revisions of a quote, oldest first.</summary>
    IReadOnlyList<ProjectRevision> Revisions(Guid projectId);

    /// <summary>A kept revision as it was saved.</summary>
    /// <exception cref="DataStoreException">No such revision.</exception>
    Project LoadRevision(Guid projectId, int revision);

    /// <summary>Inserts the project, or replaces the saved version with the same <see cref="Project.Id"/> (one transaction).</summary>
    void Save(Project project) => Save(project, null);

    /// <summary>
    /// Saves the project with its priced value for the quote list. A quote without a number gets the next free one
    /// (<see cref="QuoteInfo.Number"/> is set on <paramref name="project"/>) in the same transaction.
    /// </summary>
    void Save(Project project, QuoteValue? value);

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
