using System.Security.Cryptography;
using System.Text.Json;

namespace Mark.Licensing;

/// <summary>
/// Signs licences with the licence server's private key (ECDSA P-256). Only the server has the private key; MARK has
/// only the public key (<see cref="LicenceKeys.PublicKey"/>), so it can check licences but never make one.
/// </summary>
public sealed class LicenceSigner : IDisposable
{
    private readonly ECDsa _key;

    public LicenceSigner(ECDsa key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    /// <summary>A new random key (a new server, or tests).</summary>
    public static LicenceSigner CreateNew() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>The key from a PEM file written by <see cref="ExportPrivateKeyPem"/>.</summary>
    public static LicenceSigner FromPem(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return new LicenceSigner(key);
    }

    public string ExportPrivateKeyPem() => _key.ExportPkcs8PrivateKeyPem();

    /// <summary>The public key in the form MARK is built with (base64 SubjectPublicKeyInfo).</summary>
    public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public SignedLicence Sign(Licence licence)
    {
        ArgumentNullException.ThrowIfNull(licence);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(licence, LicenceJson.Options);
        byte[] signature = _key.SignData(payload, HashAlgorithmName.SHA256);
        return new SignedLicence(Convert.ToBase64String(payload), Convert.ToBase64String(signature));
    }

    public void Dispose() => _key.Dispose();
}

/// <summary>Checks a <see cref="SignedLicence"/> against the public key and reads it.</summary>
public sealed class LicenceVerifier
{
    private readonly byte[] _publicKey;

    /// <param name="publicKey">Base64 SubjectPublicKeyInfo (normally <see cref="LicenceKeys.PublicKey"/>).</param>
    public LicenceVerifier(string publicKey)
    {
        _publicKey = Convert.FromBase64String(publicKey ?? throw new ArgumentNullException(nameof(publicKey)));
    }

    /// <summary>The verifier for licences of the MARK licence server (the key MARK is built with).</summary>
    public static LicenceVerifier ForMark() => new(LicenceKeys.PublicKey);

    /// <summary>The licence, or null with the reason when the signature is wrong or the content cannot be read.</summary>
    public Licence? Verify(SignedLicence? signed, out string? error)
    {
        error = null;
        if (signed is null || string.IsNullOrEmpty(signed.Payload) || string.IsNullOrEmpty(signed.Signature))
        {
            error = "There is no licence.";
            return null;
        }

        try
        {
            byte[] payload = Convert.FromBase64String(signed.Payload);
            byte[] signature = Convert.FromBase64String(signed.Signature);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(_publicKey, out _);
            if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                error = "The licence has been changed or was not issued by the MARK licence server.";
                return null;
            }

            var licence = JsonSerializer.Deserialize<Licence>(payload, LicenceJson.Options);
            if (licence is null || licence.FormatVersion != 1)
            {
                error = "The licence is of a format this version of MARK does not know. Update MARK.";
                return null;
            }
            return licence;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            error = "The licence is damaged.";
            return null;
        }
    }
}
