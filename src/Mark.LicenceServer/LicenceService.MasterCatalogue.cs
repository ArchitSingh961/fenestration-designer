using System.Text;
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
/// changes. The owner can also make items for one company only (<see cref="Mark.Core.Library.CompanyItems"/>): that company
/// always gets them, with whatever of the catalogue they use. A company with nothing selected and no items of its own keeps
/// its own library.
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
        // Every company's own items must still fit: no id used twice, and what they use still there.
        var problems = new StringBuilder();
        foreach (var (name, own) in AllOwnItems(connection, transaction))
        {
            try
            {
                Mark.Core.Library.CompanyItems.Combine(library, own);
            }
            catch (InvalidOperationException ex)
            {
                problems.Append($"{name}'s own items: {ex.Message} ");
            }
        }
        if (problems.Length > 0)
            throw ApiException.Invalid($"The catalogue was not published. {problems.ToString().Trim()}");
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
        var own = OwnItemsOf(connection, company.Id, transaction);
        var master = Master(connection, transaction);
        if (own.IsEmpty && (company.Catalogue.IsEmpty || master is null)) return null;
        var combined = own.IsEmpty ? master! : Mark.Core.Library.CompanyItems.Combine(master ?? ProductLibrary.Empty, own);
        var now = Now;
        var licensed = company.Products.Where(p => !p.Suspended && p.ValidUntilUtc >= now).Select(p => p.Product).ToHashSet();
        var selection = new CatalogueSelection
        {
            SystemIds = company.Catalogue.SystemIds.Concat(own.Systems.Select(x => x.Id)).ToList(),
            ItemIds = company.Catalogue.ItemIds.Concat(own.ItemIds).ToList(),
            BundleIds = own.Bundles.Select(b => b.Id).ToList()
        };
        var library = CatalogueSelector.Select(combined, selection, material => licensed.Contains(ProductOf(material)));
        var ownIds = own.AllIds.ToHashSet(StringComparer.Ordinal);
        var sent = library.Profiles.Select(p => p.Id).Concat(library.Glass.Select(g => g.Id)).Concat(library.Materials.Select(m => m.Id))
            .Concat(library.Systems.Select(x => x.Id)).Concat(library.Bundles.Select(b => b.Id)).Where(ownIds.Contains).ToList();
        return LibrarySerializer.Serialize(library, sent.Count == 0 ? null
            : new OwnItemsLabel(company.Name, sent, OwnItemTab.Clean(own.Tabs, sent).Where(t => t.Ids.Count > 0).ToList()));
    }

    // ── A company's own items ───────────────────────────────────────

    public CompanyItemsInfo CompanyItems(Guid companyId)
    {
        using var connection = Connect();
        GetCompany(connection, companyId);
        using var command = Command(connection, "SELECT items_json, updated_utc FROM company_items WHERE company_id = $id", null,
            ("$id", companyId.ToString()));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new CompanyItemsInfo(reader.GetString(0), ParseTime(reader.GetString(1))) : new CompanyItemsInfo(null, null);
    }

    /// <summary>
    /// Replaces a company's own items. They must fit the catalogue (no id used twice, everything they use there); empty
    /// items remove them. The company gets them at its next check-in.
    /// </summary>
    public CompanyItemsInfo SaveCompanyItems(Guid companyId, string? itemsJson)
    {
        Mark.Core.Library.CompanyItems items;
        try
        {
            items = Mark.Core.Library.CompanyItems.Deserialize(itemsJson);
            items = items.WithTabs(items.Tabs);
        }
        catch (InvalidOperationException ex)
        {
            throw ApiException.Invalid(ex.Message);
        }

        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        GetCompany(connection, companyId, transaction);
        if (items.IsEmpty)
        {
            Execute(connection, "DELETE FROM company_items WHERE company_id = $id", transaction, ("$id", companyId.ToString()));
            transaction.Commit();
            return new CompanyItemsInfo(null, null);
        }
        try
        {
            Mark.Core.Library.CompanyItems.Combine(Master(connection, transaction) ?? ProductLibrary.Empty, items);
        }
        catch (InvalidOperationException ex)
        {
            throw ApiException.Invalid($"The company's own items do not fit the catalogue: {ex.Message}");
        }
        string json = Mark.Core.Library.CompanyItems.Serialize(items);
        Execute(connection, """
            INSERT INTO company_items (company_id, items_json, updated_utc) VALUES ($id, $json, $now)
            ON CONFLICT (company_id) DO UPDATE SET items_json = excluded.items_json, updated_utc = excluded.updated_utc
            """, transaction, ("$id", companyId.ToString()), ("$json", json), ("$now", Time(Now)));
        transaction.Commit();
        return new CompanyItemsInfo(json, Now);
    }

    private static Mark.Core.Library.CompanyItems OwnItemsOf(SqliteConnection connection, Guid companyId, SqliteTransaction? transaction)
    {
        string? json = Scalar<string?>(connection, "SELECT items_json FROM company_items WHERE company_id = $id", transaction,
            ("$id", companyId.ToString()));
        return Mark.Core.Library.CompanyItems.Deserialize(json);
    }

    private static List<(string Company, Mark.Core.Library.CompanyItems Items)> AllOwnItems(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = Command(connection, "SELECT c.name, i.items_json FROM company_items i JOIN companies c ON c.id = i.company_id", transaction);
        using var reader = command.ExecuteReader();
        var list = new List<(string, Mark.Core.Library.CompanyItems)>();
        while (reader.Read())
            list.Add((reader.GetString(0), Mark.Core.Library.CompanyItems.Deserialize(reader.GetString(1))));
        return list;
    }

    /// <summary>The product licence a system material needs.</summary>
    public static Product ProductOf(SystemMaterial material) => material == SystemMaterial.Upvc ? Product.Upvc : Product.Aluminium;
}
