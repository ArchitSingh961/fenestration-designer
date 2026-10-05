using System.IO;
using Mark.Core.Library;
using Mark.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mark.Tests.Data;

/// <summary>Database creation, reopening, schema versioning and refusal of unusable files.</summary>
public class DatabaseTests
{
    [Fact]
    public void Open_CreatesTheFolderTheFileAndTheSchema()
    {
        using var temp = new TempDatabase();
        var database = SqliteDatabase.Open(temp.DatabasePath);

        Assert.True(File.Exists(temp.DatabasePath));
        Assert.True(database.WasCreated);
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, database.ReadSchemaVersion());
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        var tables = new List<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) tables.Add(reader.GetString(0));
        Assert.Equal(new[]
        {
            "app_settings", "bundles", "enquiries", "glass", "glass_material_usages", "library_settings", "materials", "offcuts",
            "production_orders", "profile_material_usages", "profile_roles", "profile_stock_lengths", "profiles", "project_history",
            "project_references", "project_revisions", "projects", "systems"
        }, tables);
    }

    [Fact]
    public void Reopening_KeepsTheData_AndDoesNotRecreate()
    {
        using var temp = new TempDatabase();
        var first = temp.Open(TempDatabase.ShippedLibraryPath);
        int products = first.Library.Current.Profiles.Count;
        Assert.True(first.Database.WasCreated);

        var second = temp.Open(TempDatabase.ShippedLibraryPath);
        Assert.False(second.Database.WasCreated);
        Assert.Equal(products, second.Library.Current.Profiles.Count);
        Assert.Empty(second.StartupMessages);                          // no second import
    }

    [Fact]
    public void ForeignKeys_AreEnforced()
    {
        using var temp = new TempDatabase();
        var database = SqliteDatabase.Open(temp.DatabasePath);
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO profile_roles (profile_id, position, role) VALUES ('NO-SUCH-PROFILE', 0, 'Frame')";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void ADatabaseOfAnotherApplication_IsRefused_AndLeftUntouched()
    {
        using var temp = new TempDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.DatabasePath)!);
        using (var connection = new SqliteConnection($"Data Source={temp.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE customers (id INTEGER PRIMARY KEY)";
            command.ExecuteNonQuery();
        }
        byte[] before = File.ReadAllBytes(temp.DatabasePath);

        var ex = Assert.Throws<DataStoreException>(() => SqliteDatabase.Open(temp.DatabasePath));
        Assert.Contains("another application", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(temp.DatabasePath));
    }

    [Fact]
    public void ANewerSchema_IsRefused()
    {
        using var temp = new TempDatabase();
        var database = SqliteDatabase.Open(temp.DatabasePath);
        using (var connection = database.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA user_version = {SqliteDatabase.CurrentSchemaVersion + 1}";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<DataStoreException>(() => SqliteDatabase.Open(temp.DatabasePath));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void ADamagedFile_IsRefused_AndLeftUntouched()
    {
        using var temp = new TempDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.DatabasePath)!);
        byte[] junk = Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray();
        File.WriteAllBytes(temp.DatabasePath, junk);

        Assert.Throws<DataStoreException>(() => LocalStore.Open(temp.DatabasePath, TempDatabase.ShippedLibraryPath));
        Assert.Equal(junk, File.ReadAllBytes(temp.DatabasePath));
    }

    [Fact]
    public void FirstRun_ImportsTheSeedLibrary_KeepingIdsOrderAndValues()
    {
        using var temp = new TempDatabase();
        var store = temp.Open(TempDatabase.ShippedLibraryPath);
        var file = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

        Assert.Equal(LibrarySerializer.Serialize(file), LibrarySerializer.Serialize(store.Library.Current));
        Assert.Contains("Created the local library", Assert.Single(store.StartupMessages));
    }

    [Fact]
    public void FirstRun_WithAnInvalidSeed_StartsWithAnEmptyLibrary_AndSaysWhy()
    {
        using var temp = new TempDatabase();
        Directory.CreateDirectory(temp.Folder);
        string seed = Path.Combine(temp.Folder, "bad.json");
        File.WriteAllText(seed, "{ \"profiles\": [ { \"id\": \"\" } ] }");

        var store = temp.Open(seed);

        Assert.Empty(store.Library.Current.Profiles);
        Assert.Contains("could not be imported", Assert.Single(store.StartupMessages));
    }

    [Fact]
    public void FirstRun_WithoutASeedFile_SaysSo()
    {
        using var temp = new TempDatabase();
        var store = temp.Open(Path.Combine(temp.Folder, "missing.json"));

        Assert.Empty(store.Library.Current.Glass);
        Assert.Contains("no library file", Assert.Single(store.StartupMessages));
    }
}
