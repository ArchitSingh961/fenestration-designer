using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Mark.Data;

/// <summary>
/// Each company signed in on a computer has its own local database (quotes, enquiries, history, settings, library), so
/// nothing of one company shows in another's: <c>%LOCALAPPDATA%\MARK\Companies\{company id}\mark.db</c>.
///
/// Up to Milestone 15 every company signed in on a computer shared <c>%LOCALAPPDATA%\MARK\mark.db</c>.
/// <see cref="AdoptSharedWork"/> copies a login's own work from there into its company's database, once per login:
/// the quotes it created (with their history and revisions) and its enquiries. Work saved before quotes recorded who
/// made them, and the price structure and quotation texts, go to the first company that signs in afterwards. The
/// shared file itself is left as it is (a backup); no company opens it any more.
/// </summary>
public static class CompanyDatabases
{
    /// <summary>Which logins' work was copied, and to which company (kept in the shared database).</summary>
    private const string AdoptedKey = "companies.adopted";

    /// <summary>Settings that are the company's own work (not the catalogue or the details the admin sets).</summary>
    private static readonly string[] EarlierSettings = { "pricing.default", "quotation.settings" };

    /// <summary><c>%LOCALAPPDATA%\MARK\Companies\{company id}\mark.db</c> (or under <paramref name="root"/>).</summary>
    public static string PathFor(Guid companyId, string? root = null)
        => Path.Combine(root ?? Path.GetDirectoryName(LocalStore.DefaultPath)!, "Companies", companyId.ToString("N"), "mark.db");

    /// <summary>
    /// Copies <paramref name="userId"/>'s work from the shared database at <paramref name="sharedPath"/> into
    /// <paramref name="company"/> (the database of <paramref name="companyId"/>), unless it was copied before. Returns a
    /// message for the user when something was copied or failed, else null.
    /// </summary>
    /// <param name="userName">The login's name: quotes recorded who made them by name.</param>
    public static string? AdoptSharedWork(LocalStore company, string sharedPath, Guid companyId, string userId, string userName)
    {
        if (!File.Exists(sharedPath) || string.IsNullOrEmpty(userId)
            || string.Equals(Path.GetFullPath(sharedPath), Path.GetFullPath(company.Database.FilePath), StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            // Bring the shared database to the current schema first, so both have the same columns.
            var shared = SqliteDatabase.Open(sharedPath);
            using var sharedConnection = shared.Connect();
            var adopted = ReadAdopted(sharedConnection);
            if (adopted.Users.ContainsKey(userId)) return null;
            adopted.EarlierWorkTo ??= companyId;
            bool earlier = adopted.EarlierWorkTo == companyId;

            int quotes, enquiries;
            using (var connection = company.Database.Connect())
            {
                Run(connection, null, "ATTACH DATABASE $path AS shared", ("$path", shared.FilePath));
                try
                {
                    using var transaction = connection.BeginTransaction();
                    (quotes, enquiries) = Copy(connection, transaction, userId, userName, earlier);
                    transaction.Commit();
                }
                finally
                {
                    Run(connection, null, "DETACH DATABASE shared");
                }
            }

            adopted.Users[userId] = companyId;
            using (var transaction = sharedConnection.BeginTransaction())
            {
                Run(sharedConnection, transaction, """
                    INSERT INTO app_settings (key, value_json) VALUES ($key, $value)
                    ON CONFLICT (key) DO UPDATE SET value_json = excluded.value_json
                    """, ("$key", AdoptedKey), ("$value", JsonSerializer.Serialize(adopted)));
                transaction.Commit();
            }
            SqliteConnection.ClearPool(sharedConnection);

            if (quotes == 0 && enquiries == 0) return null;
            return $"This company now keeps its own data on this computer. Your earlier work was brought over: {quotes} " +
                   $"quote{(quotes == 1 ? "" : "s")} and {enquiries} enquir{(enquiries == 1 ? "y" : "ies")}. Other companies signed in " +
                   "on this computer no longer see it.";
        }
        catch (Exception ex) when (ex is DataStoreException or SqliteException or IOException or UnauthorizedAccessException or JsonException)
        {
            return $"Your earlier quotes on this computer could not be brought over ({ex.Message}). They are still in {sharedPath}.";
        }
    }

    /// <summary>Copies the login's quotes, their references, history and revisions, and its enquiries; returns the counts.</summary>
    private static (int Quotes, int Enquiries) Copy(SqliteConnection connection, SqliteTransaction transaction, string userId,
        string userName, bool earlier)
    {
        (string, object)[] who = { ("$id", userId), ("$name", userName), ("$earlier", earlier ? 1 : 0) };

        // The login's quotes: recorded as made by it (by name or User ID), or created by it in the history. With the
        // earlier work, also quotes nobody is recorded for.
        Run(connection, transaction, """
            CREATE TEMP TABLE adopt_projects AS
            SELECT p.id FROM shared.projects p
            WHERE p.id NOT IN (SELECT id FROM main.projects)
              AND (p.created_by IN ($id, $name)
                   OR EXISTS (SELECT 1 FROM shared.project_history h WHERE h.project_id = p.id AND h.action = 'Created' AND h.user_id = $id)
                   OR ($earlier = 1 AND p.created_by = ''
                       AND NOT EXISTS (SELECT 1 FROM shared.project_history h
                                       WHERE h.project_id = p.id AND h.action = 'Created' AND h.user_id <> '')))
            """, who);
        try
        {
            string projectColumns = Columns(connection, transaction, "projects");
            int quotes = Run(connection, transaction,
                $"INSERT INTO main.projects ({projectColumns}) SELECT {projectColumns} FROM shared.projects WHERE id IN (SELECT id FROM adopt_projects)");
            Run(connection, transaction, """
                INSERT OR IGNORE INTO main.project_references (project_id, kind, definition_id)
                SELECT project_id, kind, definition_id FROM shared.project_references WHERE project_id IN (SELECT id FROM adopt_projects)
                """);
            Run(connection, transaction, """
                INSERT OR IGNORE INTO main.project_revisions (project_id, revision, document_json, value, currency, saved_utc, saved_by)
                SELECT project_id, revision, document_json, value, currency, saved_utc, saved_by FROM shared.project_revisions
                WHERE project_id IN (SELECT id FROM adopt_projects)
                """);
            // History of those quotes, and what the login did that is not tied to a quote still there (deletions).
            Run(connection, transaction, """
                INSERT INTO main.project_history (project_id, quote_number, project_name, time_utc, user_id, user_name, action, detail)
                SELECT project_id, quote_number, project_name, time_utc, user_id, user_name, action, detail FROM shared.project_history h
                WHERE h.project_id IN (SELECT id FROM adopt_projects)
                   OR (h.user_id = $id AND h.project_id NOT IN (SELECT id FROM shared.projects))
                ORDER BY h.time_utc, h.id
                """, who);

            string enquiryColumns = Columns(connection, transaction, "enquiries");
            int enquiries = Run(connection, transaction, $"""
                INSERT INTO main.enquiries ({enquiryColumns}) SELECT {enquiryColumns} FROM shared.enquiries e
                WHERE e.id NOT IN (SELECT id FROM main.enquiries)
                  AND (e.created_by IN ($id, $name) OR ($earlier = 1 AND e.created_by = ''))
                """, who);

            if (earlier)
                foreach (string key in EarlierSettings)
                    Run(connection, transaction, """
                        INSERT OR IGNORE INTO main.app_settings (key, value_json) SELECT key, value_json FROM shared.app_settings WHERE key = $key
                        """, ("$key", key));
            return (quotes, enquiries);
        }
        finally
        {
            Run(connection, transaction, "DROP TABLE temp.adopt_projects");
        }
    }

    /// <summary>The columns of a table in the company's database, for copying by name.</summary>
    private static string Columns(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}', 'main')";
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return string.Join(", ", names);
    }

    private static Adopted ReadAdopted(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM app_settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", AdoptedKey);
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<Adopted>(json) ?? new Adopted() : new Adopted();
    }

    private static int Run(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command.ExecuteNonQuery();
    }

    /// <summary>Whose work left the shared database, and the company that got the earlier work.</summary>
    private sealed class Adopted
    {
        public Dictionary<string, Guid> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Guid? EarlierWorkTo { get; set; }
    }
}
