using Mark.Licensing;
using Mark.Licensing.Api;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>The admin (the owner of MARK): first setup, sign-in, sessions.</summary>
public sealed partial class LicenceService
{
    /// <summary>How long an admin stays signed in to MARK Owner without signing in again.</summary>
    public static readonly TimeSpan AdminSessionLength = TimeSpan.FromHours(12);

    private sealed record AdminSessionEntry(Guid AdminId, string Name, DateTime ExpiresUtc);

    /// <summary>The admin a session token belongs to.</summary>
    public sealed record AdminIdentity(Guid Id, string Name);

    public bool HasAdmin()
    {
        using var connection = Connect();
        return Scalar<long>(connection, "SELECT COUNT(*) FROM admins") > 0;
    }

    public AdminStatus Status(bool fromServerComputer)
    {
        bool hasAdmin = HasAdmin();
        return new AdminStatus(hasAdmin, !hasAdmin && fromServerComputer, PublicKey, PublicKeyMatchesMark);
    }

    /// <summary>
    /// Creates the first admin. Only possible while there is none, and only from the server's own computer (the
    /// endpoint checks that), so nobody else can claim a new server.
    /// </summary>
    public AdminSession SetUp(AdminSetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        if (Scalar<long>(connection, "SELECT COUNT(*) FROM admins", transaction) > 0)
            throw new ApiException(403, ErrorCodes.Forbidden, "The admin account has already been set up. Sign in instead.");
        string userId = CheckUserId(request.UserId);
        CheckPassword(request.Password);
        string name = Required(request.Name, "name");
        var id = Guid.NewGuid();
        Execute(connection, "INSERT INTO admins (id, user_id, name, password_hash, created_utc) VALUES ($id, $user, $name, $hash, $now)",
            transaction, ("$id", id.ToString()), ("$user", userId), ("$name", name), ("$hash", PasswordHasher.Hash(request.Password)),
            ("$now", Time(Now)));
        transaction.Commit();
        return NewSession(id, name);
    }

    public AdminSession SignIn(AdminSignInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        using var command = Command(connection, "SELECT id, name, password_hash, failed_attempts, locked_until_utc FROM admins WHERE user_id = $user",
            null, ("$user", (request.UserId ?? "").Trim()));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw BadCredentials();

        var id = Guid.Parse(reader.GetString(0));
        string name = reader.GetString(1);
        string hash = reader.GetString(2);
        int failed = reader.GetInt32(3);
        object? lockedUntil = reader.IsDBNull(4) ? null : reader.GetString(4);
        reader.Close();

        if (IsLocked(lockedUntil, Now, out int minutes)) throw LockedOut(minutes);
        if (!PasswordHasher.Verify(request.Password, hash))
        {
            RecordFailure(connection, "admins", id, failed);
            throw BadCredentials();
        }
        Execute(connection, "UPDATE admins SET failed_attempts = 0, locked_until_utc = NULL WHERE id = $id", null, ("$id", id.ToString()));
        return NewSession(id, name);
    }

    public void SignOut(string? token)
    {
        if (token is not null)
            _sessions.TryRemove(Secrets.HashToken(token), out _);
    }

    /// <summary>The admin of a valid session token, or null.</summary>
    public AdminIdentity? Authenticate(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        string key = Secrets.HashToken(token);
        if (!_sessions.TryGetValue(key, out var session)) return null;
        if (session.ExpiresUtc <= Now)
        {
            _sessions.TryRemove(key, out _);
            return null;
        }
        return new AdminIdentity(session.AdminId, session.Name);
    }

    private AdminSession NewSession(Guid adminId, string name)
    {
        string token = Secrets.NewToken();
        var expires = Now + AdminSessionLength;
        _sessions[Secrets.HashToken(token)] = new AdminSessionEntry(adminId, name, expires);
        return new AdminSession(token, name, expires);
    }

    /// <summary>Counts a wrong password; the <see cref="MaxFailedAttempts"/>th locks the User ID for a while.</summary>
    private void RecordFailure(Microsoft.Data.Sqlite.SqliteConnection connection, string table, Guid id, int failedBefore)
    {
        int failed = failedBefore + 1;
        string? lockedUntil = failed >= MaxFailedAttempts ? Time(Now.AddMinutes(LockMinutes)) : null;
        Execute(connection, $"UPDATE {table} SET failed_attempts = $failed, locked_until_utc = $locked WHERE id = $id", null,
            ("$failed", lockedUntil is null ? failed : 0), ("$locked", lockedUntil), ("$id", id.ToString()));
    }
}
