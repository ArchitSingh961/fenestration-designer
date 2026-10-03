using Mark.Licensing;
using Mark.Licensing.Api;
using Microsoft.Data.Sqlite;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>Packages (named sets of features) and company types (what a new account of a kind starts with).</summary>
public sealed partial class LicenceService
{
    // ── Packages ────────────────────────────────────────────────────

    public IReadOnlyList<PackageInfo> Packages()
    {
        using var connection = Connect();
        using var command = Command(connection, """
            SELECT p.id, p.name, p.description, p.features, (SELECT COUNT(*) FROM companies c WHERE c.package_id = p.id)
            FROM packages p ORDER BY p.name
            """, null);
        using var reader = command.ExecuteReader();
        var list = new List<PackageInfo>();
        while (reader.Read())
            list.Add(new PackageInfo(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                FromJson<List<string>>(reader.GetString(3)), reader.GetInt32(4)));
        return list;
    }

    private static PackageInfo? FindPackage(SqliteConnection connection, Guid id, SqliteTransaction? transaction = null)
    {
        using var command = Command(connection, "SELECT id, name, description, features FROM packages WHERE id = $id", transaction, ("$id", id.ToString()));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new PackageInfo(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                FromJson<List<string>>(reader.GetString(3)))
            : null;
    }

    /// <summary>Creates (empty id) or changes a package. Core features are always in it. Changes reach the companies of
    /// the package at their next check-in.</summary>
    public PackageInfo SavePackage(PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string name = Required(package.Name, "package name", 60);
        var unknown = package.Features.Where(f => !FeatureCatalog.Exists(f)).ToList();
        if (unknown.Count > 0) throw ApiException.Invalid($"Unknown feature: {string.Join(", ", unknown)}.");
        var features = FeatureCatalog.CoreIds.Concat(package.Features).Distinct()
            .OrderBy(f => FeatureCatalog.All.ToList().FindIndex(x => x.Id == f)).ToList();
        string? description = string.IsNullOrWhiteSpace(package.Description) ? null : package.Description.Trim();

        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var id = package.Id == Guid.Empty ? Guid.NewGuid() : package.Id;
        if (Scalar<long>(connection, "SELECT COUNT(*) FROM packages WHERE name = $name AND id <> $id", transaction,
                ("$name", name), ("$id", id.ToString())) > 0)
            throw ApiException.Conflict($"There is already a package called \"{name}\".");
        if (package.Id == Guid.Empty)
            Execute(connection, "INSERT INTO packages (id, name, description, features) VALUES ($id, $name, $description, $features)", transaction,
                ("$id", id.ToString()), ("$name", name), ("$description", description), ("$features", ToJson(features)));
        else if (FindPackage(connection, id, transaction) is null)
            throw ApiException.NotFound("The package");
        else
            Execute(connection, "UPDATE packages SET name = $name, description = $description, features = $features WHERE id = $id", transaction,
                ("$id", id.ToString()), ("$name", name), ("$description", description), ("$features", ToJson(features)));
        transaction.Commit();
        return Packages().First(p => p.Id == id);
    }

    public void DeletePackage(Guid id)
    {
        using var connection = Connect();
        long used = Scalar<long>(connection, "SELECT COUNT(*) FROM companies WHERE package_id = $id", null, ("$id", id.ToString()));
        if (used > 0)
            throw ApiException.Conflict($"The package is used by {used} compan{(used == 1 ? "y" : "ies")}. Give them another package first.");
        Execute(connection, "DELETE FROM packages WHERE id = $id", null, ("$id", id.ToString()));
    }

    // ── Company types ───────────────────────────────────────────────

    public IReadOnlyList<CompanyTypeInfo> CompanyTypes()
    {
        using var connection = Connect();
        using var command = Command(connection, """
            SELECT t.id, t.name, t.products, t.package_id, t.validity_days, (SELECT COUNT(*) FROM companies c WHERE c.company_type_id = t.id)
            FROM company_types t ORDER BY t.name
            """, null);
        using var reader = command.ExecuteReader();
        var list = new List<CompanyTypeInfo>();
        while (reader.Read())
            list.Add(new CompanyTypeInfo(Guid.Parse(reader.GetString(0)), reader.GetString(1), FromJson<List<Product>>(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetInt32(4), reader.GetInt32(5)));
        return list;
    }

    public CompanyTypeInfo SaveCompanyType(CompanyTypeInfo type)
    {
        ArgumentNullException.ThrowIfNull(type);
        string name = Required(type.Name, "company type name", 60);
        var products = type.Products.Distinct().ToList();
        if (products.Count == 0) throw ApiException.Invalid("Choose at least one product (uPVC or Aluminium).");
        if (type.ValidityDays is < 1 or > 3660) throw ApiException.Invalid("The validity must be between 1 day and 10 years.");

        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        if (type.PackageId is { } packageId && FindPackage(connection, packageId, transaction) is null)
            throw ApiException.NotFound("The package");
        var id = type.Id == Guid.Empty ? Guid.NewGuid() : type.Id;
        if (Scalar<long>(connection, "SELECT COUNT(*) FROM company_types WHERE name = $name AND id <> $id", transaction,
                ("$name", name), ("$id", id.ToString())) > 0)
            throw ApiException.Conflict($"There is already a company type called \"{name}\".");
        var parameters = new (string, object?)[]
        {
            ("$id", id.ToString()), ("$name", name), ("$products", ToJson(products)), ("$package", type.PackageId?.ToString()),
            ("$days", type.ValidityDays)
        };
        if (type.Id == Guid.Empty)
            Execute(connection, "INSERT INTO company_types (id, name, products, package_id, validity_days) VALUES ($id, $name, $products, $package, $days)",
                transaction, parameters);
        else if (Scalar<long>(connection, "SELECT COUNT(*) FROM company_types WHERE id = $id", transaction, ("$id", id.ToString())) == 0)
            throw ApiException.NotFound("The company type");
        else
            Execute(connection, "UPDATE company_types SET name = $name, products = $products, package_id = $package, validity_days = $days WHERE id = $id",
                transaction, parameters);
        transaction.Commit();
        return CompanyTypes().First(t => t.Id == id);
    }

    /// <summary>Deletes a company type; companies of that type keep everything and show no type.</summary>
    public void DeleteCompanyType(Guid id)
    {
        using var connection = Connect();
        Execute(connection, "DELETE FROM company_types WHERE id = $id", null, ("$id", id.ToString()));
    }
}
