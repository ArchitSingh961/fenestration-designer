using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mark.Licensing;

/// <summary>A product line in a licence, valid until <see cref="ValidUntilUtc"/>.</summary>
public sealed record ProductGrant(Product Product, DateTime ValidUntilUtc);

/// <summary>A feature in a licence, valid until <see cref="ValidUntilUtc"/> (package features share the account's date;
/// add-ons have their own).</summary>
public sealed record FeatureGrant(string FeatureId, DateTime ValidUntilUtc);

/// <summary>
/// What one computer of a client company may do: issued and signed by the licence server at sign-in and at every
/// check-in, verified by MARK offline. Its content cannot be changed without breaking the signature. All times UTC.
/// </summary>
public sealed record Licence
{
    public int FormatVersion { get; init; } = 1;

    public Guid LicenceId { get; init; }

    public Guid CompanyId { get; init; }

    /// <summary>The company name shown in MARK (set by the owner only).</summary>
    public string CompanyName { get; init; } = "";

    public string? CompanyType { get; init; }

    /// <summary>The company logo (PNG or JPEG), base64, or null.</summary>
    public string? LogoBase64 { get; init; }

    /// <summary>The User ID that signed in.</summary>
    public string UserId { get; init; } = "";

    public string UserName { get; init; } = "";

    /// <summary>
    /// <see cref="UserRoles.Owner"/> for the account owner (the login the MARK supplier set up), or
    /// <see cref="UserRoles.Staff"/> for a staff login the account owner added.
    /// </summary>
    public string Role { get; init; } = UserRoles.Owner;

    /// <summary>
    /// For a staff login: the features the account owner gave it (only those of the account's features work). Null for
    /// the account owner, who has every feature of the account.
    /// </summary>
    public IReadOnlyList<string>? Permissions { get; init; }

    /// <summary>How many people may have a login (the account owner and staff).</summary>
    public int MaxUsers { get; init; }

    [JsonIgnore]
    public bool IsStaff => Role == UserRoles.Staff;

    /// <summary>The computer this licence is for (see <c>MachineIdentity</c> in MARK).</summary>
    public string MachineId { get; init; } = "";

    /// <summary>When the server issued it: the last successful sign-in or check-in.</summary>
    public DateTime IssuedUtc { get; init; }

    /// <summary>End of the account (package) validity.</summary>
    public DateTime ValidUntilUtc { get; init; }

    /// <summary>Suspended by the owner: MARK is read-only until reactivated.</summary>
    public bool Suspended { get; init; }

    public string PackageName { get; init; } = "";

    public int MaxComputers { get; init; }

    /// <summary>Days MARK keeps working without reaching the licence server.</summary>
    public int OfflineGraceDays { get; init; } = 7;

    public IReadOnlyList<ProductGrant> Products { get; init; } = Array.Empty<ProductGrant>();

    public IReadOnlyList<FeatureGrant> Features { get; init; } = Array.Empty<FeatureGrant>();

    /// <summary>
    /// SHA-256 (hex) of the company's catalogue (the library JSON it gets from the owner), or null when the owner gives
    /// it none and the company keeps its own library. MARK downloads the catalogue when this changes and checks it.
    /// </summary>
    public string? CatalogueHash { get; init; }
}

/// <summary>Who signed in: the account owner or a staff login.</summary>
public static class UserRoles
{
    public const string Owner = "owner";
    public const string Staff = "staff";
}

/// <summary>The fingerprint of a catalogue as the licence carries it.</summary>
public static class CatalogueHash
{
    public static string Of(string libraryJson)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(libraryJson ?? "")));
}

/// <summary>A licence as stored and sent: the exact JSON bytes (base64) and their ECDSA P-256 / SHA-256 signature.</summary>
public sealed record SignedLicence(string Payload, string Signature);

/// <summary>The JSON settings used everywhere in licensing (camelCase, enums as strings).</summary>
public static class LicenceJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
