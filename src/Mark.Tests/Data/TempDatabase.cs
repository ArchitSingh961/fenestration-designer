using System.IO;
using Mark.Data;

namespace Mark.Tests.Data;

/// <summary>A database file in a fresh temporary folder, deleted afterwards. Each test gets its own.</summary>
internal sealed class TempDatabase : IDisposable
{
    /// <summary>A fixed clock, so saved-project timestamps (and list order) are deterministic in tests.</summary>
    public static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private DateTime _now = Start;

    public TempDatabase()
    {
        Folder = Path.Combine(Path.GetTempPath(), "mark-tests", Guid.NewGuid().ToString("N"));
        DatabasePath = System.IO.Path.Combine(Folder, "sub", "test.db");
    }

    public string Folder { get; }

    /// <summary>The database file (its folder does not exist until the store is opened).</summary>
    public string DatabasePath { get; }

    /// <summary>The test clock; advance it with <see cref="Tick"/>.</summary>
    public DateTime Now() => _now;

    public void Tick(int minutes = 1) => _now = _now.AddMinutes(minutes);

    public static string ShippedLibraryPath
        => System.IO.Path.Combine(TestPaths.RepositoryRoot, "src", "Mark.App", "Library", "library.json");

    public LocalStore Open(string? seed = null) => LocalStore.Open(DatabasePath, seed, Now);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail a test.
        }
    }
}
