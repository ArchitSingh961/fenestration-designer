using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Mark.Licensing;
using Mark.Licensing.Api;
using Microsoft.Data.Sqlite;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>A request the server refuses: HTTP <see cref="Status"/> with an <see cref="ApiError"/> body.</summary>
public sealed class ApiException : Exception
{
    public ApiException(int status, string code, string message) : base(message)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }

    public string Code { get; }

    public static ApiException Invalid(string message) => new(400, ErrorCodes.Invalid, message);

    public static ApiException NotFound(string what) => new(404, ErrorCodes.NotFound, $"{what} was not found.");

    public static ApiException Conflict(string message) => new(409, ErrorCodes.Conflict, message);
}

/// <summary>
/// Everything the licence server does, independent of HTTP (the endpoints only translate): the admin and their
/// sessions, packages and company types, company accounts with their users and computers, licence keys, and the
/// client side (sign-in, check-in, keys, sign-out) that issues signed licences.
/// </summary>
public sealed partial class LicenceService
{
    /// <summary>Wrong passwords in a row before a User ID is locked for <see cref="LockMinutes"/>.</summary>
    public const int MaxFailedAttempts = 5;

    public const int LockMinutes = 15;

    public const int MaxLogoBytes = 300 * 1024;

    private static readonly Regex UserIdPattern = new("^[A-Za-z0-9._@-]{3,40}$", RegexOptions.CultureInvariant);

    private readonly LicenceDatabase _database;
    private readonly LicenceSigner _signer;
    private readonly Func<DateTime> _utcNow;
    private readonly ConcurrentDictionary<string, AdminSessionEntry> _sessions = new();

    public LicenceService(LicenceDatabase database, LicenceSigner signer, Func<DateTime>? utcNow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The public key matching the server's signing key.</summary>
    public string PublicKey => _signer.PublicKey;

    /// <summary>True when copies of MARK built with <see cref="LicenceKeys.PublicKey"/> accept this server's licences.</summary>
    public bool PublicKeyMatchesMark => PublicKey == LicenceKeys.PublicKey;

    private DateTime Now => _utcNow();

    private SqliteConnection Connect() => _database.Connect();

    // ── Company rows ────────────────────────────────────────────────

    private sealed record CompanyRow(
        Guid Id,
        string Name,
        string? Logo,
        Guid? TypeId,
        Guid? PackageId,
        DateTime ValidUntilUtc,
        int MaxComputers,
        bool Suspended,
        List<ProductLicence> Products,
        List<AddOn> AddOns,
        List<string> RemovedFeatures,
        string? Notes,
        DateTime CreatedUtc,
        CompanyCatalogue Catalogue,
        int MaxUsers);

    private const string CompanyColumns =
        "id, name, logo, company_type_id, package_id, valid_until_utc, max_computers, suspended, products, add_ons, removed_features, notes, created_utc, catalogue, max_users";

    /// <summary>The number of columns in <see cref="CompanyColumns"/> (queries that add columns read them from here on).</summary>
    private const int CompanyColumnCount = 15;

    private static CompanyRow ReadCompany(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),
        r.GetString(1),
        r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)),
        r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
        ParseTime(r.GetString(5)),
        r.GetInt32(6),
        r.GetInt64(7) != 0,
        FromJson<List<ProductLicence>>(r.GetString(8)),
        FromJson<List<AddOn>>(r.GetString(9)),
        FromJson<List<string>>(r.GetString(10)),
        r.IsDBNull(11) ? null : r.GetString(11),
        ParseTime(r.GetString(12)),
        ReadCatalogue(r.IsDBNull(13) ? null : r.GetString(13)),
        r.GetInt32(14));

    private static CompanyCatalogue ReadCatalogue(string? json)
        => string.IsNullOrEmpty(json) ? CompanyCatalogue.Empty
            : System.Text.Json.JsonSerializer.Deserialize<CompanyCatalogue>(json, LicenceJson.Options) ?? CompanyCatalogue.Empty;

    private static CompanyRow? FindCompany(SqliteConnection connection, Guid id, SqliteTransaction? transaction = null)
    {
        using var command = Command(connection, $"SELECT {CompanyColumns} FROM companies WHERE id = $id", transaction, ("$id", id.ToString()));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCompany(reader) : null;
    }

    private static CompanyRow GetCompany(SqliteConnection connection, Guid id, SqliteTransaction? transaction = null)
        => FindCompany(connection, id, transaction) ?? throw ApiException.NotFound("The company");

    private static void WriteCompany(SqliteConnection connection, CompanyRow c, SqliteTransaction transaction, bool insert)
    {
        string sql = insert
            ? $"INSERT INTO companies ({CompanyColumns}) VALUES ($id, $name, $logo, $type, $package, $until, $max, $suspended, $products, $addOns, $removed, $notes, $created, $catalogue, $maxUsers)"
            : """
              UPDATE companies SET name = $name, logo = $logo, company_type_id = $type, package_id = $package, valid_until_utc = $until,
                  max_computers = $max, suspended = $suspended, products = $products, add_ons = $addOns, removed_features = $removed,
                  notes = $notes, catalogue = $catalogue, max_users = $maxUsers WHERE id = $id
              """;
        Execute(connection, sql, transaction,
            ("$id", c.Id.ToString()), ("$name", c.Name), ("$logo", c.Logo), ("$type", c.TypeId?.ToString()),
            ("$package", c.PackageId?.ToString()), ("$until", Time(c.ValidUntilUtc)), ("$max", c.MaxComputers),
            ("$suspended", c.Suspended ? 1 : 0), ("$products", ToJson(c.Products)), ("$addOns", ToJson(c.AddOns)),
            ("$removed", ToJson(c.RemovedFeatures)), ("$notes", c.Notes), ("$created", Time(c.CreatedUtc)),
            ("$catalogue", ToJson(c.Catalogue)), ("$maxUsers", c.MaxUsers));
    }

    // ── Licences ────────────────────────────────────────────────────

    /// <summary>Who a licence is for: the account owner (<see cref="Permissions"/> null) or a staff login.</summary>
    private sealed record LicenceUser(string UserId, string Name, string Role, IReadOnlyList<string>? Permissions);

    /// <summary>
    /// The licence of one computer: the package's features (and the core features) valid until the account date, add-ons
    /// with their own dates, minus the removed features; the products that are not suspended. A staff login's licence
    /// also carries the features its account owner gave it.
    /// </summary>
    private SignedLicence IssueLicence(SqliteConnection connection, CompanyRow company, LicenceUser user, string machineId,
        SqliteTransaction? transaction = null)
    {
        var package = company.PackageId is { } packageId ? FindPackage(connection, packageId, transaction) : null;
        var features = new Dictionary<string, DateTime>();
        foreach (string id in FeatureCatalog.CoreIds.Concat(package?.Features ?? Array.Empty<string>()))
            features[id] = company.ValidUntilUtc;
        foreach (var addOn in company.AddOns)
            features[addOn.FeatureId] = features.TryGetValue(addOn.FeatureId, out var until) && until > addOn.ValidUntilUtc ? until : addOn.ValidUntilUtc;
        foreach (string removed in company.RemovedFeatures.Where(f => !(FeatureCatalog.Find(f)?.IsCore ?? false)))
            features.Remove(removed);

        string? typeName = company.TypeId is { } typeId
            ? Scalar<string?>(connection, "SELECT name FROM company_types WHERE id = $id", transaction, ("$id", typeId.ToString()))
            : null;

        var licence = new Licence
        {
            LicenceId = Guid.NewGuid(),
            CompanyId = company.Id,
            CompanyName = company.Name,
            CompanyType = typeName,
            LogoBase64 = company.Logo,
            UserId = user.UserId,
            UserName = user.Name,
            Role = user.Role,
            Permissions = user.Role == UserRoles.Staff ? user.Permissions ?? Array.Empty<string>() : null,
            MaxUsers = company.MaxUsers,
            MachineId = machineId,
            IssuedUtc = Now,
            ValidUntilUtc = company.ValidUntilUtc,
            Suspended = company.Suspended,
            PackageName = package?.Name ?? "",
            MaxComputers = company.MaxComputers,
            Products = company.Products.Where(p => !p.Suspended).Select(p => new ProductGrant(p.Product, p.ValidUntilUtc)).ToList(),
            Features = features.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new FeatureGrant(f.Key, f.Value)).ToList(),
            CatalogueHash = CompanyCatalogueJson(connection, company, transaction) is { } catalogue ? Licensing.CatalogueHash.Of(catalogue) : null,
            ProfileHash = ProfileJsonOf(connection, company.Id, transaction) is { } profile ? Licensing.CatalogueHash.Of(profile) : null
        };
        return _signer.Sign(licence);
    }

    // ── Validation helpers ──────────────────────────────────────────

    private static string CheckUserId(string? userId)
    {
        string id = (userId ?? "").Trim();
        if (!UserIdPattern.IsMatch(id))
            throw ApiException.Invalid("The User ID must be 3 to 40 letters, digits or . _ @ - (no spaces).");
        return id;
    }

    private static void CheckPassword(string? password)
    {
        if (PasswordHasher.Check(password) is { } problem)
            throw ApiException.Invalid(problem);
    }

    private static string Required(string? text, string what, int maxLength = 100)
    {
        string value = (text ?? "").Trim();
        if (value.Length == 0) throw ApiException.Invalid($"Enter the {what}.");
        if (value.Length > maxLength) throw ApiException.Invalid($"The {what} can have at most {maxLength} characters.");
        return value;
    }

    private static string? CheckLogo(string? logoBase64) => CheckImage(logoBase64, "logo", MaxLogoBytes);

    /// <summary>A PNG or JPEG of at most <paramref name="maxBytes"/>, as base64 (null when empty).</summary>
    private static string? CheckImage(string? logoBase64, string what, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(logoBase64)) return null;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(logoBase64);
        }
        catch (FormatException)
        {
            throw ApiException.Invalid($"The {what} could not be read.");
        }
        if (bytes.Length > maxBytes)
            throw ApiException.Invalid($"The {what} is too large ({bytes.Length / 1024} KB); use an image of at most {maxBytes / 1024} KB.");
        bool png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        bool jpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
        if (!png && !jpeg) throw ApiException.Invalid($"The {what} must be a PNG or JPEG image.");
        return logoBase64;
    }

    private static bool IsLocked(object? lockedUntil, DateTime now, out int minutes)
    {
        minutes = 0;
        if (ParseTimeOrNull(lockedUntil) is not { } until || until <= now) return false;
        minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
        return true;
    }

    private static ApiException LockedOut(int minutes)
        => new(429, ErrorCodes.LockedOut, $"Too many wrong passwords. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");

    private static ApiException BadCredentials()
        => new(401, ErrorCodes.BadCredentials, "The User ID or password is not correct.");
}
