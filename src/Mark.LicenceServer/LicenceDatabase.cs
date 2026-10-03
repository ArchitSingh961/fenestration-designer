using System.Globalization;
using System.Text.Json;
using Mark.Licensing;
using Microsoft.Data.Sqlite;

namespace Mark.LicenceServer;

/// <summary>
/// The licence server's SQLite database: admins, packages, company types, companies, their users and computers, and
/// licence keys. Lists inside a row (products, add-ons, features) are stored as JSON. Created with the starter
/// packages and company types on first run.
/// </summary>
public sealed class LicenceDatabase
{
    public const int SchemaVersion = 4;

    private readonly string _connectionString;

    private LicenceDatabase(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public string Path { get; }

    /// <summary>Opens (and on first use creates) the database at <paramref name="path"/>.</summary>
    public static LicenceDatabase Open(string path)
    {
        if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        var database = new LicenceDatabase(path);
        database.CreateSchema();
        return database;
    }

    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void CreateSchema()
    {
        using var connection = Connect();
        long version = Scalar<long>(connection, "PRAGMA user_version");
        if (version == SchemaVersion) return;
        if (version > SchemaVersion)
            throw new InvalidOperationException($"The licence database {Path} is from a newer version of the server (schema {version}).");
        if (version == 0) CreateVersion1(connection);
        if (version < 2) UpgradeToVersion2(connection);
        if (version < 3) UpgradeToVersion3(connection);
        if (version < 4) UpgradeToVersion4(connection);
    }

    /// <summary>Version 4: each company's own items (products the owner made for that company only).</summary>
    private static void UpgradeToVersion4(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, """
            CREATE TABLE company_items (
                company_id TEXT PRIMARY KEY REFERENCES companies(id) ON DELETE CASCADE,
                items_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL);
            """, transaction);
        Execute(connection, "PRAGMA user_version = 4", transaction);
        transaction.Commit();
    }

    /// <summary>
    /// Version 3 (Milestone 14): staff logins. How many logins a company may have (existing accounts: as many as
    /// computers), each staff login's features, turned-off logins and the last sign-in. A computer is now registered
    /// once per login (two people sharing a PC each keep their sign-in); the limit counts distinct computers.
    /// </summary>
    private static void UpgradeToVersion3(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, """
            ALTER TABLE companies ADD COLUMN max_users INTEGER NOT NULL DEFAULT 1;
            UPDATE companies SET max_users = MAX(max_computers, 1);
            ALTER TABLE users ADD COLUMN permissions TEXT NULL;
            ALTER TABLE users ADD COLUMN disabled INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN last_sign_in_utc TEXT NULL;
            CREATE INDEX ix_users_company ON users (company_id);
            CREATE TABLE computers_v3 (
                id TEXT PRIMARY KEY,
                company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                user_ref TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                machine_id TEXT NOT NULL,
                machine_name TEXT NOT NULL,
                app_version TEXT,
                device_token_hash TEXT NOT NULL UNIQUE,
                first_seen_utc TEXT NOT NULL,
                last_check_in_utc TEXT,
                UNIQUE (company_id, machine_id, user_ref));
            INSERT INTO computers_v3 (id, company_id, user_ref, machine_id, machine_name, app_version, device_token_hash, first_seen_utc, last_check_in_utc)
                SELECT id, company_id, user_ref, machine_id, machine_name, app_version, device_token_hash, first_seen_utc, last_check_in_utc FROM computers;
            DROP TABLE computers;
            ALTER TABLE computers_v3 RENAME TO computers;
            CREATE INDEX ix_computers_company_machine ON computers (company_id, machine_id);
            """, transaction);
        Execute(connection, "PRAGMA user_version = 3", transaction);
        transaction.Commit();
    }

    /// <summary>Version 2 (Milestone 13): the owner's catalogue, and what each company and company type gets from it.</summary>
    private static void UpgradeToVersion2(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, """
            CREATE TABLE catalogue (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL,
                library_json TEXT NOT NULL,
                published_utc TEXT NOT NULL);
            ALTER TABLE companies ADD COLUMN catalogue TEXT NULL;
            ALTER TABLE company_types ADD COLUMN catalogue TEXT NULL;
            """, transaction);
        Execute(connection, "PRAGMA user_version = 2", transaction);
        transaction.Commit();
    }

    private static void CreateVersion1(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, """
            CREATE TABLE admins (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL UNIQUE COLLATE NOCASE,
                name TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                failed_attempts INTEGER NOT NULL DEFAULT 0,
                locked_until_utc TEXT,
                created_utc TEXT NOT NULL);
            CREATE TABLE packages (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                description TEXT,
                features TEXT NOT NULL);
            CREATE TABLE company_types (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                products TEXT NOT NULL,
                package_id TEXT REFERENCES packages(id) ON DELETE SET NULL,
                validity_days INTEGER NOT NULL);
            CREATE TABLE companies (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                logo TEXT,
                company_type_id TEXT REFERENCES company_types(id) ON DELETE SET NULL,
                package_id TEXT REFERENCES packages(id),
                valid_until_utc TEXT NOT NULL,
                max_computers INTEGER NOT NULL,
                suspended INTEGER NOT NULL DEFAULT 0,
                products TEXT NOT NULL,
                add_ons TEXT NOT NULL,
                removed_features TEXT NOT NULL,
                notes TEXT,
                created_utc TEXT NOT NULL);
            CREATE TABLE users (
                id TEXT PRIMARY KEY,
                company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                user_id TEXT NOT NULL UNIQUE COLLATE NOCASE,
                name TEXT NOT NULL,
                role TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                failed_attempts INTEGER NOT NULL DEFAULT 0,
                locked_until_utc TEXT,
                created_utc TEXT NOT NULL);
            CREATE TABLE computers (
                id TEXT PRIMARY KEY,
                company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                user_ref TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                machine_id TEXT NOT NULL,
                machine_name TEXT NOT NULL,
                app_version TEXT,
                device_token_hash TEXT NOT NULL UNIQUE,
                first_seen_utc TEXT NOT NULL,
                last_check_in_utc TEXT,
                UNIQUE (company_id, machine_id));
            CREATE TABLE licence_keys (
                id TEXT PRIMARY KEY,
                key TEXT NOT NULL UNIQUE,
                company_id TEXT,
                company_name TEXT,
                target TEXT NOT NULL,
                product TEXT,
                feature_id TEXT,
                valid_until_utc TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                state TEXT NOT NULL,
                used_by_company_name TEXT,
                used_utc TEXT,
                note TEXT);
            """, transaction);
        Seed(connection, transaction);
        Execute(connection, "PRAGMA user_version = 1", transaction);
        transaction.Commit();
    }

    /// <summary>The starter packages and company types (the owner can change or delete them).</summary>
    private static void Seed(SqliteConnection connection, SqliteTransaction transaction)
    {
        var packageIds = new Dictionary<string, Guid>();
        foreach (var (name, description, features) in StarterPackages.All)
        {
            var id = Guid.NewGuid();
            packageIds[name] = id;
            Execute(connection, "INSERT INTO packages (id, name, description, features) VALUES ($id, $name, $description, $features)",
                transaction, ("$id", id.ToString()), ("$name", name), ("$description", description), ("$features", ToJson(features)));
        }

        var types = new (string Name, Product[] Products, string Package, int Days)[]
        {
            ("uPVC fabricator", new[] { Product.Upvc }, "Professional", 365),
            ("Aluminium fabricator", new[] { Product.Aluminium }, "Professional", 365),
            ("uPVC + Aluminium fabricator", new[] { Product.Upvc, Product.Aluminium }, "Complete", 365),
            ("Trial", new[] { Product.Upvc, Product.Aluminium }, "Complete", 14)
        };
        foreach (var type in types)
            Execute(connection, "INSERT INTO company_types (id, name, products, package_id, validity_days) VALUES ($id, $name, $products, $package, $days)",
                transaction, ("$id", Guid.NewGuid().ToString()), ("$name", type.Name), ("$products", ToJson(type.Products)),
                ("$package", packageIds[type.Package].ToString()), ("$days", type.Days));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, transaction, parameters);
        command.ExecuteNonQuery();
    }

    public static T Scalar<T>(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, transaction, parameters);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    public static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? transaction,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, LicenceJson.Options);

    public static T FromJson<T>(string? json) where T : new()
        => string.IsNullOrEmpty(json) ? new T() : JsonSerializer.Deserialize<T>(json, LicenceJson.Options) ?? new T();

    public static string Time(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    public static DateTime ParseTime(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTime? ParseTimeOrNull(object? value) => value is string text && text.Length > 0 ? ParseTime(text) : null;
}
