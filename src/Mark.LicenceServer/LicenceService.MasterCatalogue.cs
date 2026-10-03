using Mark.Core.Library;
using Mark.Licensing;
using Mark.Licensing.Api;
using Microsoft.Data.Sqlite;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>
/// The owner's master catalogue (one library file: profiles, glass, hardware, systems, bundles) and the part of it each
/// company gets. A company's catalogue is its selection (systems and single items) cut from the master for the products it
/// is licensed for (<see cref="CatalogueSelector"/>); its hash goes into the company's licences, so MARK downloads it when it
/// changes. A company with nothing selected (or no catalogue published) keeps its own library.
/// </summary>
public sealed partial class LicenceService
{
    private readonly object _masterLock = new();
    private (int Version, ProductLibrary Library)? _master;

    public CatalogueInfo Catalogue()
    {
        using var connection = Connect();
        using var command = Command(connection, "SELECT version, library_json, published_utc FROM catalogue WHERE id = 1", null);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new CatalogueInfo(reader.GetInt32(0), ParseTime(reader.GetString(2)), reader.GetString(1))
            : new CatalogueInfo(0, null, null);
    }

    /// <summary>
    /// Publishes a new master catalogue. It must be a valid library file; it is stored in the normal form MARK writes. The
    /// companies get their part at their next check-in.
    /// </summary>
    public CatalogueInfo PublishCatalogue(string libraryJson)
    {
        ProductLibrary library;
        try
        {
            library = LibrarySerializer.Deserialize(libraryJson);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw ApiException.Invalid($"The catalogue is not a valid library: {ex.Message}");
        }

        string normal = LibrarySerializer.Serialize(library);
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        int version = Scalar<int>(connection, "SELECT COALESCE(MAX(version), 0) FROM catalogue", transaction) + 1;
        Execute(connection, """
            INSERT INTO catalogue (id, version, library_json, published_utc) VALUES (1, $version, $json, $now)
            ON CONFLICT (id) DO UPDATE SET version = excluded.version, library_json = excluded.library_json,
                published_utc = excluded.published_utc
            """, transaction, ("$version", version), ("$json", normal), ("$now", Time(Now)));
        transaction.Commit();
        lock (_masterLock) _master = (version, library);
        return new CatalogueInfo(version, Now, normal);
    }

    /// <summary>The master catalogue as a library (cached per version), or null when none is published.</summary>
    private ProductLibrary? Master(SqliteConnection connection, SqliteTransaction? transaction)
    {
        int version = Scalar<int>(connection, "SELECT COALESCE(MAX(version), 0) FROM catalogue", transaction);
        if (version == 0) return null;
        lock (_masterLock)
        {
            if (_master is { } cached && cached.Version == version) return cached.Library;
        }
        string json = Scalar<string>(connection, "SELECT library_json FROM catalogue WHERE id = 1", transaction);
        var library = LibrarySerializer.Deserialize(json);
        lock (_masterLock) _master = (version, library);
        return library;
    }

    /// <summary>
    /// The library JSON a company gets: its selection from the master, for the products it may use now (licensed, not
    /// suspended). Null when nothing is selected or no catalogue is published.
    /// </summary>
    private string? CompanyCatalogueJson(SqliteConnection connection, CompanyRow company, SqliteTransaction? transaction = null)
    {
        if (company.Catalogue.IsEmpty || Master(connection, transaction) is not { } master) return null;
        var now = Now;
        var licensed = company.Products.Where(p => !p.Suspended && p.ValidUntilUtc >= now).Select(p => p.Product).ToHashSet();
        var selection = new CatalogueSelection { SystemIds = company.Catalogue.SystemIds, ItemIds = company.Catalogue.ItemIds };
        var library = CatalogueSelector.Select(master, selection, material => licensed.Contains(ProductOf(material)));
        return LibrarySerializer.Serialize(library);
    }

    /// <summary>The product licence a system material needs.</summary>
    public static Product ProductOf(SystemMaterial material) => material == SystemMaterial.Upvc ? Product.Upvc : Product.Aluminium;
}
