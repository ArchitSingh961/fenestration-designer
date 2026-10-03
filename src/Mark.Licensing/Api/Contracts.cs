namespace Mark.Licensing.Api;

// The messages between MARK / MARK Owner and the licence server (JSON, see LicenceJson). Times are UTC.

/// <summary>An error from the server: a stable <see cref="Code"/> for programs and a <see cref="Message"/> for people.</summary>
public sealed record ApiError(string Code, string Message);

/// <summary>Error codes the server sends.</summary>
public static class ErrorCodes
{
    public const string BadCredentials = "bad-credentials";
    public const string LockedOut = "locked-out";
    public const string ComputerLimit = "computer-limit";
    /// <summary>The computer is no longer signed in (freed by the owner, or the account was deleted).</summary>
    public const string SignedOut = "signed-out";
    public const string NotFound = "not-found";
    public const string Invalid = "invalid";
    public const string Conflict = "conflict";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string KeyInvalid = "key-invalid";
}

// ── MARK (client companies) ─────────────────────────────────────────

public sealed record SignInRequest(string UserId, string Password, string MachineId, string MachineName, string? AppVersion = null);

/// <summary>The licence for this computer and the token it uses for check-ins (kept secret on the computer).</summary>
public sealed record SignInResponse(SignedLicence Licence, string DeviceToken);

public sealed record CheckInRequest(string DeviceToken, string MachineId);

public sealed record LicenceResponse(SignedLicence Licence);

public sealed record RedeemKeyRequest(string DeviceToken, string MachineId, string Key);

/// <summary>The updated licence and what the key did, e.g. "uPVC is now valid until 3 Oct 2027.".</summary>
public sealed record RedeemKeyResponse(SignedLicence Licence, string Message);

public sealed record SignOutRequest(string DeviceToken, string MachineId);

public sealed record CatalogueRequest(string DeviceToken, string MachineId);

/// <summary>The company's catalogue: a library file (JSON) whose SHA-256 is the licence's catalogue hash.</summary>
public sealed record CatalogueResponse(string LibraryJson);

// ── MARK Owner (the admin) ──────────────────────────────────────────

/// <param name="CanSetUpHere">True when no admin exists yet and the request came from the server's own computer:
/// only then can the first admin account be created.</param>
public sealed record AdminStatus(bool HasAdmin, bool CanSetUpHere, string PublicKey, bool PublicKeyMatchesMark);

public sealed record AdminSetupRequest(string UserId, string Password, string Name);

public sealed record AdminSignInRequest(string UserId, string Password);

/// <summary>A signed-in admin: send <see cref="Token"/> as "Authorization: Bearer …".</summary>
public sealed record AdminSession(string Token, string Name, DateTime ExpiresUtc);

/// <summary>What the owner gives a company (or a company type) from the catalogue: whole systems and single items.</summary>
public sealed record CompanyCatalogue(IReadOnlyList<string> SystemIds, IReadOnlyList<string> ItemIds)
{
    public static CompanyCatalogue Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    public bool IsEmpty => SystemIds.Count == 0 && ItemIds.Count == 0;
}

/// <summary>The owner's master catalogue: a library file (JSON), its version and when it was published.</summary>
public sealed record CatalogueInfo(int Version, DateTime? PublishedUtc, string? LibraryJson);

public sealed record PublishCatalogueRequest(string LibraryJson);

/// <summary>A product line of an account, with its own validity; a suspended product is left out of licences.</summary>
public sealed record ProductLicence(Product Product, DateTime ValidUntilUtc, bool Suspended = false);

/// <summary>A feature given on top of the package, with its own validity.</summary>
public sealed record AddOn(string FeatureId, DateTime ValidUntilUtc);

public sealed record CompanySummary(
    Guid Id,
    string Name,
    bool HasLogo,
    string? CompanyType,
    string OwnerUserId,
    IReadOnlyList<ProductLicence> Products,
    string? PackageName,
    DateTime ValidUntilUtc,
    bool Suspended,
    int ComputersUsed,
    int MaxComputers,
    DateTime? LastCheckInUtc);

public sealed record ComputerInfo(Guid Id, string Name, string UserId, DateTime FirstSeenUtc, DateTime? LastCheckInUtc);

public sealed record CompanyDetail(
    Guid Id,
    string Name,
    string? LogoBase64,
    Guid? CompanyTypeId,
    string OwnerName,
    string OwnerUserId,
    IReadOnlyList<ProductLicence> Products,
    Guid? PackageId,
    DateTime ValidUntilUtc,
    int MaxComputers,
    IReadOnlyList<AddOn> AddOns,
    IReadOnlyList<string> RemovedFeatures,
    bool Suspended,
    string? Notes,
    DateTime CreatedUtc,
    IReadOnlyList<ComputerInfo> Computers,
    CompanyCatalogue? Catalogue = null);

/// <summary>A new account, or the changes to one.</summary>
/// <param name="OwnerPassword">Required for a new account; for an existing one a non-empty value sets a new password.</param>
/// <param name="RemovedFeatures">Package features this company does not get.</param>
public sealed record CompanyEdit(
    string Name,
    string? LogoBase64,
    Guid? CompanyTypeId,
    string OwnerName,
    string OwnerUserId,
    string? OwnerPassword,
    IReadOnlyList<ProductLicence> Products,
    Guid? PackageId,
    DateTime ValidUntilUtc,
    int MaxComputers,
    IReadOnlyList<AddOn> AddOns,
    IReadOnlyList<string> RemovedFeatures,
    string? Notes,
    CompanyCatalogue? Catalogue = null);

public sealed record SetSuspendedRequest(bool Suspended);

/// <summary>A package: a named set of features. <see cref="Id"/> is empty for a new one.</summary>
public sealed record PackageInfo(Guid Id, string Name, string? Description, IReadOnlyList<string> Features, int UsedBy = 0);

/// <summary>A kind of company and what a new account of that kind starts with. <see cref="Id"/> is empty for a new one.</summary>
public sealed record CompanyTypeInfo(Guid Id, string Name, IReadOnlyList<Product> Products, Guid? PackageId, int ValidityDays, int UsedBy = 0,
    CompanyCatalogue? Catalogue = null);

/// <summary>What a licence key gives: more account validity, a product, or a feature add-on.</summary>
public enum KeyTarget
{
    Account,
    Product,
    Feature
}

public enum KeyState
{
    Unused,
    Used,
    Revoked,
    Expired
}

/// <summary>A licence key. Redeeming it makes its target valid until <see cref="ValidUntilUtc"/> (it never shortens).</summary>
public sealed record LicenceKeyInfo(
    Guid Id,
    string Key,
    Guid? CompanyId,
    string? CompanyName,
    KeyTarget Target,
    Product? Product,
    string? FeatureId,
    DateTime ValidUntilUtc,
    DateTime CreatedUtc,
    KeyState State,
    string? UsedByCompany,
    DateTime? UsedUtc,
    string? Note);

/// <param name="CompanyId">Only this company can use the keys; null: any company.</param>
public sealed record GenerateKeysRequest(
    Guid? CompanyId,
    KeyTarget Target,
    Product? Product,
    string? FeatureId,
    DateTime ValidUntilUtc,
    int Count,
    string? Note);
