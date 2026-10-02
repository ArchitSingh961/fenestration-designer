using Mark.Core.Library;

namespace Mark.Data;

/// <summary>
/// The application's local data: the SQLite database, the library service and the project repository, opened together.
///
/// First run: <see cref="Open"/> creates the database and schema, then, if the library is empty, imports the seed library
/// file (normally the shipped <c>library.json</c>) in one transaction, keeping its ids. Later runs open the existing
/// database and never re-import or overwrite it. A seed file that cannot be read leaves the library empty and is
/// reported in <see cref="StartupMessages"/>; the designer still starts.
/// </summary>
public sealed class LocalStore
{
    private LocalStore(SqliteDatabase database, LibraryService library, IProjectRepository projects, IReadOnlyList<string> messages)
    {
        Database = database;
        Library = library;
        Projects = projects;
        StartupMessages = messages;
    }

    /// <summary><c>%LOCALAPPDATA%\MARK\mark.db</c>.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MARK", "mark.db");

    /// <summary>Where versions before the rename to MARK kept the database: <c>%LOCALAPPDATA%\Fenestrationenestration.db</c>.</summary>
    public static string LegacyDefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fenestration", "fenestration.db");

    /// <summary>
    /// Brings the database of an earlier version along after the rename: if <paramref name="path"/> does not exist yet and
    /// <paramref name="legacyPath"/> does, the old file is COPIED there (the old file stays as a backup). Returns a message
    /// for the user when it copied, else null. A copy that fails is reported and the store starts empty.
    /// </summary>
    public static string? AdoptLegacyDatabase(string path, string legacyPath)
    {
        try
        {
            if (File.Exists(path) || !File.Exists(legacyPath))
                return null;
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);
            File.Copy(legacyPath, path);
            return $"Your saved quotes and library were copied from {legacyPath} (kept there as a backup).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The database of the earlier version ({legacyPath}) could not be copied: {ex.Message}";
        }
    }

    public SqliteDatabase Database { get; }

    public LibraryService Library { get; }

    public IProjectRepository Projects { get; }

    /// <summary>What happened at startup worth telling the user (seed import result or failure). Empty normally.</summary>
    public IReadOnlyList<string> StartupMessages { get; }

    /// <summary>Opens (or creates) the store at <paramref name="databasePath"/>.</summary>
    /// <param name="seedLibraryPath">Library file imported into an empty library (first run). Optional.</param>
    /// <param name="utcNow">Clock for project timestamps (tests); defaults to the system clock.</param>
    /// <exception cref="DataStoreException">The database cannot be used (damaged, foreign, newer schema); it was not modified.</exception>
    public static LocalStore Open(string databasePath, string? seedLibraryPath = null, Func<DateTime>? utcNow = null)
    {
        var database = SqliteDatabase.Open(databasePath);
        var libraryRepository = new SqliteLibraryRepository(database);
        var projects = new SqliteProjectRepository(database, utcNow);
        var library = new LibraryService(libraryRepository, projects);
        var messages = new List<string>();
        try
        {
            projects.BackfillQuoteSummaries();
        }
        catch (DataStoreException ex)
        {
            messages.Add($"The saved quotes could not be updated for the quote list: {ex.Message}");
        }

        if (libraryRepository.IsEmpty() && seedLibraryPath is not null)
        {
            if (!File.Exists(seedLibraryPath))
            {
                messages.Add($"The product library is empty and no library file was found at {seedLibraryPath}.");
            }
            else
            {
                try
                {
                    var result = library.Import(LibrarySerializer.Load(seedLibraryPath));
                    messages.Add($"Created the local library with {result.Added.Count} products from {Path.GetFileName(seedLibraryPath)}.");
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DataStoreException)
                {
                    messages.Add($"The library file {seedLibraryPath} could not be imported, so the library is empty: {ex.Message}");
                }
            }
        }

        return new LocalStore(database, library, projects, messages);
    }
}
