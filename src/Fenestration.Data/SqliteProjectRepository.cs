using System.Globalization;
using Fenestration.Core.Models;
using Fenestration.Core.Serialization;
using Microsoft.Data.Sqlite;

namespace Fenestration.Data;

/// <summary>
/// <see cref="IProjectRepository"/> on <see cref="SqliteDatabase"/>. The document is written and read with the existing
/// <see cref="ProjectSerializer"/>, so a saved project and a project file are the same format, versioning and
/// validation included, and every Id survives. References are only explicit ones (a null reference means "library
/// default", which the library protects through its defaults).
/// </summary>
public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly SqliteDatabase _database;
    private readonly Func<DateTime> _utcNow;

    /// <param name="utcNow">Clock for the created/modified times (tests pass a fixed one). Defaults to the system clock.</param>
    public SqliteProjectRepository(SqliteDatabase database, Func<DateTime>? utcNow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void Save(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        string document = ProjectSerializer.Serialize(project);
        string now = Timestamp(_utcNow());

        _database.Guard($"save the project '{project.Name}'", () =>
        {
            using var connection = _database.Connect();
            using var transaction = connection.BeginTransaction();
            Run(connection, transaction, """
                INSERT INTO projects (id, name, format_version, document_json, created_utc, modified_utc)
                VALUES ($id, $name, $version, $document, $now, $now)
                ON CONFLICT (id) DO UPDATE SET name = excluded.name, format_version = excluded.format_version,
                    document_json = excluded.document_json, modified_utc = excluded.modified_utc
                """,
                ("$id", Key(project.Id)), ("$name", project.Name), ("$version", ProjectFormatVersion.Current),
                ("$document", document), ("$now", now));

            Run(connection, transaction, "DELETE FROM project_references WHERE project_id = $id", ("$id", Key(project.Id)));
            foreach (var (kind, definitionId) in References(project))
                Run(connection, transaction,
                    "INSERT INTO project_references (project_id, kind, definition_id) VALUES ($id, $kind, $definition)",
                    ("$id", Key(project.Id)), ("$kind", kind.ToString()), ("$definition", definitionId));

            transaction.Commit();
        });
    }

    public Project Load(Guid id)
    {
        var (name, document) = _database.Guard("open the project", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, document_json FROM projects WHERE id = $id";
            command.Parameters.AddWithValue("$id", Key(id));
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new DataStoreException("The project is not in the database (it may have been deleted).");
            return (reader.GetString(0), reader.GetString(1));
        });

        try
        {
            var project = ProjectSerializer.Deserialize(document);
            if (project.Id != id)
                throw new InvalidOperationException("the stored document belongs to a different project");
            return project;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new DataStoreException($"The saved project '{name}' is damaged and cannot be opened: {ex.Message}", ex);
        }
    }

    public bool Exists(Guid id) => _database.Guard("read the saved projects", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM projects WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    });

    public IReadOnlyList<ProjectSummary> List()
        => Summaries("SELECT id, name, created_utc, modified_utc FROM projects ORDER BY modified_utc DESC, name, id");

    public void Delete(Guid id) => _database.Guard("delete the project", () =>
    {
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        if (Run(connection, transaction, "DELETE FROM projects WHERE id = $id", ("$id", Key(id))) == 0)
            throw new DataStoreException("The project is not in the database (it may have been deleted).");
        transaction.Commit();
    });

    public IReadOnlyList<ProjectSummary> FindUsing(LibraryItemKind kind, string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        return Summaries("""
            SELECT p.id, p.name, p.created_utc, p.modified_utc FROM projects p
            JOIN project_references r ON r.project_id = p.id
            WHERE r.kind = $kind AND r.definition_id = $definition
            ORDER BY p.modified_utc DESC, p.name, p.id
            """, ("$kind", kind.ToString()), ("$definition", definitionId));
    }

    /// <summary>The distinct explicit library references of a project, in a fixed order.</summary>
    public static IReadOnlyList<(LibraryItemKind Kind, string DefinitionId)> References(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var profiles = project.Frames.SelectMany(f => f.Profiles).Select(p => p.ProfileDefinitionId);
        var glass = project.Frames.SelectMany(f => f.GlassPanels).Select(g => g.GlassDefinitionId);
        return profiles.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => (LibraryItemKind.Profile, id))
            .Concat(glass.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(id => (LibraryItemKind.Glass, id)))
            .ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private IReadOnlyList<ProjectSummary> Summaries(string sql, params (string Name, object? Value)[] parameters)
        => _database.Guard("read the saved projects", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var r = command.ExecuteReader();
            var list = new List<ProjectSummary>();
            while (r.Read())
                list.Add(new ProjectSummary(Guid.Parse(r.GetString(0)), r.GetString(1), ParseTimestamp(r.GetString(2)),
                    ParseTimestamp(r.GetString(3))));
            return (IReadOnlyList<ProjectSummary>)list.AsReadOnly();
        });

    private static int Run(SqliteConnection c, SqliteTransaction t, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteNonQuery();
    }

    /// <summary>Ids are stored in the canonical lower-case "D" format so lookups never depend on casing.</summary>
    private static string Key(Guid id) => id.ToString("D");

    /// <summary>Round-trip UTC timestamps; this text format also sorts chronologically.</summary>
    private static string Timestamp(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTimestamp(string text)
        => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
