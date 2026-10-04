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

    /// <summary>Order numbers look like "OR-00003".</summary>
    public const string OrderNumberPrefix = "OR-";

    /// <summary>Written to <c>summary_version</c> by this build; older rows are recomputed when the store opens.</summary>
    private const int SummaryVersion = 2;

    public ProjectUser User { get; set; } = ProjectUser.Unknown;

    public void Save(Project project) => Save(project, null);

    public void Save(Project project, QuoteValue? value)
    {
        ArgumentNullException.ThrowIfNull(project);
        string now = Timestamp(_utcNow());
        string originalNumber = project.Quote.Number;
        string originalOrder = project.Quote.OrderNumber;

        try
        {
            _database.Guard($"save the project '{project.Name}'", () =>
            {
                using var connection = _database.Connect();
                using var transaction = connection.BeginTransaction();

                // A new quote takes the next number inside the same transaction, so two saves never share one.
                if (string.IsNullOrWhiteSpace(project.Quote.Number))
                    project.Quote.Number = ReadNumberOf(connection, transaction, project.Id) ?? NextQuoteNumber(connection, transaction);
                if (project.Quote.OrderedUtc is not null && string.IsNullOrWhiteSpace(project.Quote.OrderNumber))
                    project.Quote.OrderNumber = NextNumber(connection, transaction, "projects", "order_number", OrderNumberPrefix);

                string document = ProjectSerializer.Serialize(project);
                var totals = QuoteTotals.Of(project);
                var before = ReadSummaryOf(connection, transaction, project.Id);
                string user = User.DisplayName;
                // Won or lost: when it was decided (kept while it stays decided); active again: not decided.
                string? decided = project.Quote.Status == QuoteStatus.Active ? null
                    : before is { Status: not nameof(QuoteStatus.Active) } && before.DecidedUtc is { } kept ? kept : now;
                Run(connection, transaction, """
                    INSERT INTO projects (id, name, format_version, document_json, created_utc, modified_utc, quote_number,
                        client_name, status, design_count, quantity, area_m2, value, currency, summary_version, created_by, modified_by,
                        client_city, decided_utc, order_number, revision)
                    VALUES ($id, $name, $version, $document, $now, $now, $number, $client, $status, $designs, $quantity,
                        $area, $value, $currency, $summary, $user, $user, $city, $decided, $order, $revision)
                    ON CONFLICT (id) DO UPDATE SET name = excluded.name, format_version = excluded.format_version,
                        document_json = excluded.document_json, modified_utc = excluded.modified_utc,
                        quote_number = excluded.quote_number, client_name = excluded.client_name, status = excluded.status,
                        design_count = excluded.design_count, quantity = excluded.quantity, area_m2 = excluded.area_m2,
                        value = excluded.value, currency = excluded.currency, summary_version = excluded.summary_version,
                        modified_by = excluded.modified_by, client_city = excluded.client_city, decided_utc = excluded.decided_utc,
                        order_number = excluded.order_number, revision = excluded.revision
                    """,
                    ("$id", Key(project.Id)), ("$name", project.Name), ("$version", ProjectFormatVersion.Current),
                    ("$document", document), ("$now", now), ("$number", project.Quote.Number),
                    ("$client", project.Quote.Client.DisplayName), ("$status", project.Quote.Status.ToString()),
                    ("$designs", totals.Designs), ("$quantity", totals.Quantity), ("$area", totals.AreaM2),
                    ("$value", value?.Amount.ToString(CultureInfo.InvariantCulture)), ("$currency", value?.Currency ?? ""),
                    ("$summary", SummaryVersion), ("$user", user), ("$city", project.Quote.Client.City.Trim()), ("$decided", decided),
                    ("$order", project.Quote.OrderNumber), ("$revision", project.Quote.Revision));

                var after = new SavedSummary(project.Name, project.Quote.Client.DisplayName, project.Quote.Status.ToString(), totals.Designs,
                    value?.Amount, value?.Currency ?? "", decided, project.Quote.OrderNumber, project.Quote.Revision);
                AddHistory(connection, transaction, project.Id, project.Quote.Number, project.Name, now,
                    before is null ? ProjectAction.Created : ProjectAction.Saved, before is null ? "" : Changes(before, after));

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
            project.Quote.Number = originalNumber;   // nothing was saved, so the numbers were not taken
            project.Quote.OrderNumber = originalOrder;
            throw;
        }
    }

    /// <summary>What the quote list showed for a saved quote, to say what a save changed.</summary>
    private sealed record SavedSummary(string Name, string Client, string Status, int Designs, decimal? Value, string Currency,
        string? DecidedUtc = null, string Order = "", int Revision = 0);

    private static SavedSummary? ReadSummaryOf(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT name, client_name, status, design_count, value, currency, decided_utc, order_number, revision FROM projects WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        using var r = command.ExecuteReader();
        return r.Read()
            ? new SavedSummary(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
                r.IsDBNull(4) ? null : decimal.Parse(r.GetString(4), NumberStyles.Number, CultureInfo.InvariantCulture), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7), r.GetInt32(8))
            : null;
    }

    /// <summary>"Status Active → Won · 3 → 4 designs · Value 1,20,000.00 → 1,35,000.00 INR", or "" when none of these changed.</summary>
    private static string Changes(SavedSummary before, SavedSummary after)
    {
        var parts = new List<string>();
        if (before.Name != after.Name) parts.Add($"Renamed \"{before.Name}\" → \"{after.Name}\"");
        if (before.Client != after.Client)
            parts.Add(before.Client.Length == 0 ? $"Client {after.Client}" : $"Client {before.Client} → {(after.Client.Length == 0 ? "none" : after.Client)}");
        if (before.Status != after.Status) parts.Add($"Status {before.Status} → {after.Status}");
        if (before.Designs != after.Designs) parts.Add($"{before.Designs} → {after.Designs} design{(after.Designs == 1 ? "" : "s")}");
        if (before.Order.Length == 0 && after.Order.Length > 0) parts.Add($"Order {after.Order}");
        if (after.Revision > before.Revision) parts.Add($"Revision R{after.Revision}");
        if (after.Value is { } value && before.Value != value)
            parts.Add(before.Value is { } old
                ? $"Value {Money(old)} → {Money(value)} {after.Currency}".TrimEnd()
                : $"Value {Money(value)} {after.Currency}".TrimEnd());
        return string.Join(" · ", parts);
    }

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private void AddHistory(SqliteConnection connection, SqliteTransaction transaction, Guid id, string number, string name, string time,
        ProjectAction action, string detail)
        => Run(connection, transaction, """
            INSERT INTO project_history (project_id, quote_number, project_name, time_utc, user_id, user_name, action, detail)
            VALUES ($id, $number, $name, $time, $userId, $userName, $action, $detail)
            """,
            ("$id", Key(id)), ("$number", number), ("$name", name), ("$time", time), ("$userId", User.UserId), ("$userName", User.Name),
            ("$action", action.ToString()), ("$detail", detail));

    // ── Revisions ───────────────────────────────────────────────────

    public void KeepRevision(Guid projectId) => _database.Guard("keep the revision", () =>
    {
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        int kept = Run(connection, transaction, """
            INSERT OR REPLACE INTO project_revisions (project_id, revision, document_json, value, currency, saved_utc, saved_by)
            SELECT id, revision, document_json, value, currency, modified_utc, modified_by FROM projects WHERE id = $id
            """, ("$id", Key(projectId)));
        if (kept == 0) throw new DataStoreException("Save the quote first: only a saved quote can get a new revision.");
        transaction.Commit();
    });

    public IReadOnlyList<ProjectRevision> Revisions(Guid projectId) => _database.Guard("read the revisions", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision, saved_utc, saved_by, value, currency FROM project_revisions WHERE project_id = $id ORDER BY revision";
        command.Parameters.AddWithValue("$id", Key(projectId));
        using var r = command.ExecuteReader();
        var list = new List<ProjectRevision>();
        while (r.Read())
            list.Add(new ProjectRevision(projectId, r.GetInt32(0), ParseTimestamp(r.GetString(1)), r.GetString(2),
                r.IsDBNull(3) ? null : decimal.Parse(r.GetString(3), NumberStyles.Number, CultureInfo.InvariantCulture), r.GetString(4)));
        return (IReadOnlyList<ProjectRevision>)list.AsReadOnly();
    });

    public Project LoadRevision(Guid projectId, int revision)
    {
        string document = _database.Guard("open the revision", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT document_json FROM project_revisions WHERE project_id = $id AND revision = $revision";
            command.Parameters.AddWithValue("$id", Key(projectId));
            command.Parameters.AddWithValue("$revision", revision);
            return command.ExecuteScalar() as string ?? throw new DataStoreException($"Revision R{revision} is not kept.");
        });
        try
        {
            return ProjectSerializer.Deserialize(document);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new DataStoreException($"Revision R{revision} is damaged and cannot be opened: {ex.Message}", ex);
        }
    }

    public IReadOnlyList<ProjectHistoryEntry> History(Guid projectId)
        => HistoryRows("WHERE project_id = $id ORDER BY time_utc DESC, id DESC", ("$id", Key(projectId)));

    public IReadOnlyList<ProjectHistoryEntry> RecentHistory(int count)
        => HistoryRows("ORDER BY time_utc DESC, id DESC LIMIT $count", ("$count", Math.Max(0, count)));

    private IReadOnlyList<ProjectHistoryEntry> HistoryRows(string where, params (string Name, object? Value)[] parameters)
        => _database.Guard("read the quote history", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT project_id, quote_number, project_name, time_utc, user_id, user_name, action, detail FROM project_history {where}";
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var r = command.ExecuteReader();
            var list = new List<ProjectHistoryEntry>();
            while (r.Read())
                list.Add(new ProjectHistoryEntry(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), ParseTimestamp(r.GetString(3)),
                    r.GetString(4), r.GetString(5), Enum.TryParse<ProjectAction>(r.GetString(6), out var action) ? action : ProjectAction.Saved,
                    r.GetString(7)));
            return (IReadOnlyList<ProjectHistoryEntry>)list.AsReadOnly();
        });

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
        => NextNumber(connection, transaction, "projects", "quote_number", QuoteNumberPrefix);

    /// <summary>The prefix followed by one more than the highest number in use in <paramref name="column"/> (at least 5 digits).</summary>
    internal static string NextNumber(SqliteConnection connection, SqliteTransaction? transaction, string table, string column, string prefix)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {column} FROM {table} WHERE {column} LIKE $prefix";
        command.Parameters.AddWithValue("$prefix", prefix + "%");
        long highest = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                if (long.TryParse(reader.GetString(0).AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long n))
                    highest = Math.Max(highest, n);
        }
        return prefix + (highest + 1).ToString("D5", CultureInfo.InvariantCulture);
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
                    summary_version = $summary, client_city = $city, order_number = $order, revision = $revision,
                    decided_utc = CASE WHEN $status = 'Active' THEN NULL ELSE COALESCE(decided_utc, modified_utc) END
                WHERE id = $id
                """,
                ("$city", project.Quote.Client.City.Trim()), ("$order", project.Quote.OrderNumber), ("$revision", project.Quote.Revision),
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
        p.area_m2, p.value, p.currency, p.created_by, p.modified_by, p.client_city, p.decided_utc, p.order_number, p.revision
        """;

    public IReadOnlyList<ProjectSummary> List()
        => Summaries($"SELECT {SummaryColumns} FROM projects p ORDER BY p.modified_utc DESC, p.name, p.id");

    public void Delete(Guid id) => _database.Guard("delete the project", () =>
    {
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        string number = "", name = "";
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT quote_number, name FROM projects WHERE id = $id";
            command.Parameters.AddWithValue("$id", Key(id));
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                number = reader.GetString(0);
                name = reader.GetString(1);
            }
        }
        if (Run(connection, transaction, "DELETE FROM projects WHERE id = $id", ("$id", Key(id))) == 0)
            throw new DataStoreException("The project is not in the database (it may have been deleted).");
        AddHistory(connection, transaction, id, number, name, Timestamp(_utcNow()), ProjectAction.Deleted, "");
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
                    Currency = r.GetString(11),
                    CreatedBy = r.GetString(12),
                    ModifiedBy = r.GetString(13),
                    ClientCity = r.GetString(14),
                    DecidedUtc = r.IsDBNull(15) ? null : ParseTimestamp(r.GetString(15)),
                    OrderNumber = r.GetString(16),
                    Revision = r.GetInt32(17)
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
