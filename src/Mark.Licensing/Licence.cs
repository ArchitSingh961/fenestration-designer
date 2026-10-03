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
