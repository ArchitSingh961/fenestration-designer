using Microsoft.Data.Sqlite;

namespace Mark.Data;

/// <summary>
/// The local SQLite database file. <see cref="Open"/> creates it (and its folder) on first use, then checks it is a
/// MARK database, not damaged and not newer than this build, and upgrades an older schema step by step.
/// Every connection enforces foreign keys. Connections are not pooled, so the file is released as soon as an
/// operation finishes.
/// </summary>
public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    private SqliteDatabase(string path)
    {
        FilePath = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5           // seconds to wait for a lock held by another process before failing
        }.ToString();
    }

    public string FilePath { get; }

    /// <summary>True when <see cref="Open"/> created a new, empty database (first run).</summary>
    public bool WasCreated { get; private set; }

    /// <summary>The schema version this build writes.</summary>
    public static int CurrentSchemaVersion => DatabaseSchema.CurrentVersion;

    /// <summary>Opens (or creates) the database at <paramref name="path"/> and brings its schema up to date.</summary>
    /// <exception cref="DataStoreException">The file cannot be used; it was not modified.</exception>
    public static SqliteDatabase Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        try
        {
            if (Path.GetDirectoryName(fullPath) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DataStoreException($"The database folder for {fullPath} cannot be created: {ex.Message}", ex);
        }

        var database = new SqliteDatabase(fullPath);
        database.Initialize();
        return database;
    }

    /// <summary>A new open connection (foreign keys on). The caller disposes it.</summary>
    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Runs a database operation and turns a SQLite failure (file locked by another copy of the application, moved,
    /// deleted or damaged while the application is running) into a <see cref="DataStoreException"/> the user can read,
    /// so the caller can report it instead of crashing.
    /// </summary>
    /// <param name="action">What was being done, e.g. "read the library" (completes "could not …").</param>
    internal T Guard<T>(string action, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (SqliteException ex)
        {
            throw new DataStoreException(
                $"The local database could not {action}. It may be in use by another copy of the application, or it was " +
                $"moved or damaged ({ex.Message}). Nothing was changed.", ex);
        }
    }

    internal void Guard(string action, Action work) => Guard(action, () => { work(); return 0; });

    /// <summary>The schema version stored in the file.</summary>
    public int ReadSchemaVersion()
    {
        using var connection = Connect();
        return Pragma(connection, "user_version");
    }

    private void Initialize()
    {
        try
        {
            using var connection = Connect();
            if (Scalar(connection, "PRAGMA quick_check") is not "ok")
                throw new DataStoreException($"The database {FilePath} is damaged (integrity check failed).");

            int applicationId = Pragma(connection, "application_id");
            int version = Pragma(connection, "user_version");
            long tables = Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'"));

            if (applicationId == 0 && version == 0)
            {
                if (tables > 0)
                    throw new DataStoreException($"{FilePath} is a SQLite database of another application.");
                Create(connection);
                return;
            }

            if (applicationId != DatabaseSchema.ApplicationId)
                throw new DataStoreException($"{FilePath} is a SQLite database of another application.");
            if (version > DatabaseSchema.CurrentVersion)
                throw new DataStoreException(
                    $"The database was written by a newer version of the application (schema {version}; this version " +
                    $"supports up to {DatabaseSchema.CurrentVersion}). Please update the application.");
            if (version < 1)
                throw new DataStoreException($"The database {FilePath} has an invalid schema version ({version}).");

            Upgrade(connection, version);
        }
        catch (SqliteException ex)
        {
            throw new DataStoreException($"The database {FilePath} cannot be opened: it is damaged or not a database ({ex.Message}).", ex);
        }
    }

    private void Create(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, DatabaseSchema.Version1);
        Execute(connection, transaction, $"PRAGMA application_id = {DatabaseSchema.ApplicationId}");
        Execute(connection, transaction, "PRAGMA user_version = 1");
        transaction.Commit();
        WasCreated = true;
        Upgrade(connection, 1);
    }

    private static void Upgrade(SqliteConnection connection, int from)
    {
        for (int version = from; version < DatabaseSchema.CurrentVersion; version++)
        {
            if (!DatabaseSchema.Upgrades.TryGetValue(version, out var script))
                throw new DataStoreException($"No database upgrade is defined from schema version {version}.");
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, script);
            Execute(connection, transaction, $"PRAGMA user_version = {version + 1}");
            transaction.Commit();
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static int Pragma(SqliteConnection connection, string name) => Convert.ToInt32(Scalar(connection, $"PRAGMA {name}"));
}
