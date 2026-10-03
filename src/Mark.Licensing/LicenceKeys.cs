namespace Mark.Licensing;

/// <summary>
/// The public half of the MARK licence server's signing key. MARK accepts only licences signed with the matching
/// private key, which exists only on the licence server (signing-key.pem in its data folder, never in this
/// repository). A server with another key works, but MARK rejects its licences; the server says so when it starts.
/// </summary>
public static class LicenceKeys
{
    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEIq2wKgM/oB8ZkEi+zYsTSP97FZFYWAhzb2+DzeoDi0RzgL9IubERnc8sx1qDTcdHrC+ZFOfHy9X/QHGSTjZKTg==";
}
