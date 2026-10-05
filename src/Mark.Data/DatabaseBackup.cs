using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Mark.Data;

/// <summary>What a backup file holds: when, of which database, at which schema version.</summary>
public sealed record BackupInfo(string App, DateTime CreatedUtc, int SchemaVersion, string Source, string Note)
{
    public string Text => $"{CreatedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)} · schema {SchemaVersion}"
                          + (Note.Length > 0 ? $" · {Note}" : "");
}

/// <summary>
/// Backups of the local database (Milestone 20): a <c>.markbackup</c> file is a zip with a consistent copy of the
/// database (SQLite's online backup, so MARK can keep working) and a manifest. Automatic backups are made once a day into
/// the database's Backups folder, keeping the newest few. A restore checks the file first, keeps the current database as
/// a backup, and then puts the backup in its place; MARK starts again with it.
/// </summary>
public static class DatabaseBackup
{
    public const string Extension = ".markbackup";
    private const string DatabaseEntry = "mark.db";
    private const string ManifestEntry = "manifest.json";

    /// <summary>The Backups folder next to the database.</summary>
    public static string FolderFor(string databasePath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".", "Backups");

    /// <summary>Writes a backup of <paramref name="databasePath"/> to <paramref name="backupFile"/>.</summary>
    /// <exception cref="DataStoreException">The database cannot be read or the file cannot be written.</exception>
    public static BackupInfo Create(string databasePath, string backupFile, string note = "")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"mark-backup-{Guid.NewGuid():N}.db");
        try
        {
            using (var source = Open(databasePath, SqliteOpenMode.ReadOnly))
            using (var copy = Open(temp, SqliteOpenMode.ReadWriteCreate))
                source.BackupDatabase(copy);
            var info = new BackupInfo("MARK", DateTime.UtcNow, SchemaOf(temp), Path.GetFileName(databasePath), note ?? "");
            if (Path.GetDirectoryName(Path.GetFullPath(backupFile)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            string partial = backupFile + ".partial";
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(temp, DatabaseEntry, CompressionLevel.Optimal);
                using var writer = new StreamWriter(zip.CreateEntry(ManifestEntry).Open());
                writer.Write(JsonSerializer.Serialize(info));
            }
            File.Move(partial, backupFile, overwrite: true);
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            throw new DataStoreException($"The backup could not be made: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>What a backup file holds, after checking it is a MARK backup this version can open.</summary>
    /// <exception cref="DataStoreException">Not a MARK backup, damaged, or from a newer MARK.</exception>
    public static BackupInfo Inspect(string backupFile)
    {
        string temp = Extract(backupFile, out var info);
        TryDelete(temp);
        return info;
    }

    /// <summary>
    /// Puts the backup in place of <paramref name="databasePath"/>. The current database is first kept in the Backups
    /// folder ("before restore"). Nothing may be using the database (MARK restarts afterwards). Returns the kept copy.
    /// </summary>
    /// <exception cref="DataStoreException">The backup is not usable, or the database cannot be replaced (nothing changed).</exception>
    public static string Restore(string backupFile, string databasePath)
    {
        string temp = Extract(backupFile, out _);
        try
        {
            string kept = Path.Combine(FolderFor(databasePath), $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}{Extension}");
            if (File.Exists(databasePath)) Create(databasePath, kept, "before restore");
            SqliteConnection.ClearAllPools();
            File.Copy(temp, databasePath, overwrite: true);
            foreach (string side in new[] { "-journal", "-wal", "-shm" }) TryDelete(databasePath + side);
            return kept;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DataStoreException($"The backup could not be restored (the database was not changed): {ex.Message}", ex);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Makes the day's automatic backup when the newest one is older than <paramref name="every"/> (default 20 hours) and
    /// keeps the newest <paramref name="keep"/> automatic backups. Returns the new file, or null when none was due.
    /// </summary>
    public static string? AutoBackup(string databasePath, int keep = 10, TimeSpan? every = null, DateTime? now = null)
    {
        if (!File.Exists(databasePath)) return null;
        string folder = FolderFor(databasePath);
        var at = now ?? DateTime.Now;
        var autos = Directory.Exists(folder)
            ? new DirectoryInfo(folder).GetFiles($"auto-*{Extension}").OrderByDescending(f => f.Name).ToList()
            : new List<FileInfo>();
        if (autos.FirstOrDefault() is { } newest && at - newest.LastWriteTime < (every ?? TimeSpan.FromHours(20))) return null;
        string file = Path.Combine(folder, $"auto-{at:yyyyMMdd-HHmmss}{Extension}");
        Create(databasePath, file, "automatic");
        foreach (var old in new DirectoryInfo(folder).GetFiles($"auto-*{Extension}").OrderByDescending(f => f.Name).Skip(Math.Max(1, keep)))
            TryDelete(old.FullName);
        return file;
    }

    /// <summary>The backups in the database's Backups folder, newest first.</summary>
    public static IReadOnlyList<(string File, BackupInfo Info)> List(string databasePath)
    {
        string folder = FolderFor(databasePath);
        if (!Directory.Exists(folder)) return Array.Empty<(string, BackupInfo)>();
        var list = new List<(string, BackupInfo)>();
        foreach (var file in new DirectoryInfo(folder).GetFiles($"*{Extension}").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            try
            {
                list.Add((file.FullName, ReadManifest(file.FullName)));
            }
            catch (DataStoreException)
            {
                // Not readable: left out of the list.
            }
        }
        return list;
    }

    private static string Extract(string backupFile, out BackupInfo info)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"mark-restore-{Guid.NewGuid():N}.db");
        try
        {
            info = ReadManifest(backupFile);
            using (var zip = ZipFile.OpenRead(backupFile))
                (zip.GetEntry(DatabaseEntry) ?? throw new DataStoreException("This backup has no database in it.")).ExtractToFile(temp, true);
            using var connection = Open(temp, SqliteOpenMode.ReadOnly);
            if (Pragma(connection, "application_id") != DatabaseSchema.ApplicationId)
                throw new DataStoreException("This is not a MARK backup.");
            int schema = Pragma(connection, "user_version");
            if (schema > DatabaseSchema.CurrentVersion)
                throw new DataStoreException("This backup was made by a newer MARK. Update MARK first, then restore it.");
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check";
                if (check.ExecuteScalar() as string != "ok") throw new DataStoreException("The database in this backup is damaged.");
            }
            return temp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            TryDelete(temp);
            throw new DataStoreException($"This backup cannot be used: {ex.Message}", ex);
        }
        catch (DataStoreException)
        {
            TryDelete(temp);
            throw;
        }
    }

    private static BackupInfo ReadManifest(string backupFile)
    {
        try
        {
            using var zip = ZipFile.OpenRead(backupFile);
            var entry = zip.GetEntry(ManifestEntry) ?? throw new DataStoreException("This is not a MARK backup.");
            using var reader = new StreamReader(entry.Open());
            var info = JsonSerializer.Deserialize<BackupInfo>(reader.ReadToEnd());
            return info is { App: "MARK" } ? info : throw new DataStoreException("This is not a MARK backup.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            throw new DataStoreException($"This is not a MARK backup ({ex.Message}).", ex);
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static int SchemaOf(string path)
    {
        using var connection = Open(path, SqliteOpenMode.ReadOnly);
        return Pragma(connection, "user_version");
    }

    private static int Pragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name}";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary file left behind is harmless.
        }
    }
}
