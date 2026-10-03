using System.Globalization;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Core.Serialization;
using Microsoft.Data.Sqlite;

namespace Mark.Data;

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

    /// <summary>Quote numbers look like "QT-00012".</summary>
    public const string QuoteNumberPrefix = "QT-";

    /// <summary>Written to <c>summary_version</c> by this build; older rows are recomputed when the store opens.</summary>
    private const int SummaryVersion = 1;

    public void Save(Project project) => Save(project, null);

    public void Save(Project project, QuoteValue? value)
    {
        ArgumentNullException.ThrowIfNull(project);
        string now = Timestamp(_utcNow());
        string originalNumber = project.Quote.Number;

        try
        {
            _database.Guard($"save the project '{project.Name}'", () =>
            {
                using var connection = _database.Connect();
                using var transaction = connection.BeginTransaction();

                // A new quote takes the next number inside the same transaction, so two saves never share one.
                if (string.IsNullOrWhiteSpace(project.Quote.Number))
                    project.Quote.Number = ReadNumberOf(connection, transaction, project.Id) ?? NextQuoteNumber(connection, transaction);

                string document = ProjectSerializer.Serialize(project);
                var totals = QuoteTotals.Of(project);
                Run(connection, transaction, """
                    INSERT INTO projects (id, name, format_version, document_json, created_utc, modified_utc, quote_number,
                        client_name, status, design_count, quantity, area_m2, value, currency, summary_version)
                    VALUES ($id, $name, $version, $document, $now, $now, $number, $client, $status, $designs, $quantity,
                        $area, $value, $currency, $summary)
                    ON CONFLICT (id) DO UPDATE SET name = excluded.name, format_version = excluded.format_version,
                        document_json = excluded.document_json, modified_utc = excluded.modified_utc,
                        quote_number = excluded.quote_number, client_name = excluded.client_name, status = excluded.status,
                        design_count = excluded.design_count, quantity = excluded.quantity, area_m2 = excluded.area_m2,
                        value = excluded.value, currency = excluded.currency, summary_version = excluded.summary_version
                    """,
                    ("$id", Key(project.Id)), ("$name", project.Name), ("$version", ProjectFormatVersion.Current),
                    ("$document", document), ("$now", now), ("$number", project.Quote.Number),
                    ("$client", project.Quote.Client.DisplayName), ("$status", project.Quote.Status.ToString()),
                    ("$designs", totals.Designs), ("$quantity", totals.Quantity), ("$area", totals.AreaM2),
                    ("$value", value?.Amount.ToString(CultureInfo.InvariantCulture)), ("$currency", value?.Currency ?? ""),
                    ("$summary", SummaryVersion));

                Run(connection, transaction, "DELETE FROM project_references WHERE project_id = $id", ("$id", Key(project.Id)));
                foreach (var (kind, definitionId) in References(project))
                    Run(connection, transaction,
                        "INSERT INTO project_references (project_id, kind, definition_id) VALUES ($id, $kind, $definition)",
                        ("$id", Key(project.Id)), ("$kind", kind.ToString()), ("$definition", definitionId));

                transaction.Commit();
            });
        }
        catch
        {
            project.Quote.Number = originalNumber;   // nothing was saved, so the number was not taken
            throw;
        }
    }

    /// <summary>The number already stored for this project (a quote saved before it had a number in its document).</summary>
    private static string? ReadNumberOf(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT quote_number FROM projects WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        return command.ExecuteScalar() is string { Length: > 0 } number ? number : null;
    }

    /// <summary>QT- followed by one more than the highest number in use (at least 5 digits).</summary>
    private static string NextQuoteNumber(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT quote_number FROM projects WHERE quote_number LIKE 'QT-%'";
        long highest = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                if (long.TryParse(reader.GetString(0).AsSpan(QuoteNumberPrefix.Length), NumberStyles.None,
                        CultureInfo.InvariantCulture, out long n))
                    highest = Math.Max(highest, n);
        }
        return QuoteNumberPrefix + (highest + 1).ToString("D5", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Fills in the quote columns of rows saved before schema version 2 (or by an older summary format): number, client,
    /// status and totals are read from each document; the value stays unknown until the quote is saved again. Rows whose
    /// document cannot be read are left as they are (opening them reports the damage). Returns how many rows were updated.
    /// </summary>
    public int BackfillQuoteSummaries() => _database.Guard("update the saved quotes", () =>
    {
        using var connection = _database.Connect();
        var stale = new List<(string Id, string Document)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, document_json FROM projects WHERE summary_version < $v ORDER BY created_utc, id";
            command.Parameters.AddWithValue("$v", SummaryVersion);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                stale.Add((reader.GetString(0), reader.GetString(1)));
        }
        if (stale.Count == 0) return 0;

        using var transaction = connection.BeginTransaction();
        int updated = 0;
        foreach (var (id, json) in stale)
        {
            Project project;
            try
            {
                project = ProjectSerializer.Deserialize(json);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(project.Quote.Number))
                project.Quote.Number = NextQuoteNumber(connection, transaction);
            var totals = QuoteTotals.Of(project);
            updated += Run(connection, transaction, """
                UPDATE projects SET document_json = $document, quote_number = $number, client_name = $client,
                    status = $status, design_count = $designs, quantity = $quantity, area_m2 = $area,
                    summary_version = $summary
                WHERE id = $id
                """,
                ("$id", id), ("$document", ProjectSerializer.Serialize(project)), ("$number", project.Quote.Number),
                ("$client", project.Quote.Client.DisplayName), ("$status", project.Quote.Status.ToString()),
                ("$designs", totals.Designs), ("$quantity", totals.Quantity), ("$area", totals.AreaM2),
                ("$summary", SummaryVersion));
        }
        transaction.Commit();
        return updated;
    });

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

    private const string SummaryColumns = """
        p.id, p.name, p.created_utc, p.modified_utc, p.quote_number, p.client_name, p.status, p.design_count, p.quantity,
        p.area_m2, p.value, p.currency
        """;

    public IReadOnlyList<ProjectSummary> List()
        => Summaries($"SELECT {SummaryColumns} FROM projects p ORDER BY p.modified_utc DESC, p.name, p.id");

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
        return Summaries($"""
            SELECT {SummaryColumns} FROM projects p
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
        var systems = project.Frames.Select(f => f.SystemId);
        return profiles.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => (LibraryItemKind.Profile, id))
            .Concat(glass.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(id => (LibraryItemKind.Glass, id)))
            .Concat(systems.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(id => (LibraryItemKind.System, id)))
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
                    ParseTimestamp(r.GetString(3)))
                {
                    QuoteNumber = r.GetString(4),
                    ClientName = r.GetString(5),
                    Status = Enum.TryParse<QuoteStatus>(r.GetString(6), out var status) ? status : QuoteStatus.Active,
                    DesignCount = r.GetInt32(7),
                    Quantity = r.GetInt32(8),
                    AreaM2 = r.GetDouble(9),
                    Value = r.IsDBNull(10) ? null : decimal.Parse(r.GetString(10), NumberStyles.Number, CultureInfo.InvariantCulture),
                    Currency = r.GetString(11)
                });
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
