using Mark.Licensing;
using Mark.Licensing.Api;
using Microsoft.Data.Sqlite;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>
/// MARK's calls: sign-in (registers the computer for that login within the account's limit of computers, which counts a
/// computer shared by two logins once, and issues a device token), check-in
/// (a fresh licence), licence keys and sign-out. Every successful call returns a newly signed licence.
/// </summary>
public sealed partial class LicenceService
{
    private sealed record UserRow(Guid Id, Guid CompanyId, string UserId, string Name, string PasswordHash, int FailedAttempts, string? LockedUntil,
        string Role, IReadOnlyList<string>? Permissions, bool Disabled)
    {
        public LicenceUser AsLicenceUser => new(UserId, Name, Role, Permissions);
    }

    private sealed record ComputerRow(Guid Id, Guid CompanyId, Guid UserRef, string MachineId, LicenceUser User);

    public SignInResponse ClientSignIn(SignInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string machineId = Required(request.MachineId, "computer id", 128);
        string machineName = string.IsNullOrWhiteSpace(request.MachineName) ? "Computer" : request.MachineName.Trim();
        if (machineName.Length > 100) machineName = machineName[..100];

        using var connection = Connect();
        var user = FindUser(connection, (request.UserId ?? "").Trim()) ?? throw BadCredentials();
        if (IsLocked(user.LockedUntil, Now, out int minutes)) throw LockedOut(minutes);
        if (!PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            RecordFailure(connection, "users", user.Id, user.FailedAttempts);
            throw BadCredentials();
        }
        if (user.Disabled)
            throw new ApiException(403, ErrorCodes.LoginDisabled,
                "Your login has been turned off by the owner of your MARK account. Ask them to turn it on again.");
        Execute(connection, "UPDATE users SET failed_attempts = 0, locked_until_utc = NULL, last_sign_in_utc = $now WHERE id = $id", null,
            ("$id", user.Id.ToString()), ("$now", Time(Now)));

        using var transaction = connection.BeginTransaction();
        var company = GetCompany(connection, user.CompanyId, transaction);
        string token = Secrets.NewToken();
        string? existing = Scalar<string?>(connection,
            "SELECT id FROM computers WHERE company_id = $company AND machine_id = $machine AND user_ref = $user", transaction,
            ("$company", company.Id.ToString()), ("$machine", machineId), ("$user", user.Id.ToString()));
        if (existing is not null)
        {
            Execute(connection, """
                UPDATE computers SET user_ref = $user, machine_name = $name, app_version = $version, device_token_hash = $token,
                    last_check_in_utc = $now WHERE id = $id
                """, transaction,
                ("$user", user.Id.ToString()), ("$name", machineName), ("$version", request.AppVersion), ("$token", Secrets.HashToken(token)),
                ("$now", Time(Now)), ("$id", existing));
        }
        else
        {
            bool known = Scalar<long>(connection, "SELECT COUNT(*) FROM computers WHERE company_id = $company AND machine_id = $machine",
                transaction, ("$company", company.Id.ToString()), ("$machine", machineId)) > 0;
            long used = ComputersInUse(connection, company.Id, transaction);
            if (!known && used >= company.MaxComputers)
                throw new ApiException(403, ErrorCodes.ComputerLimit,
                    $"Your account is already in use on {used} computer{(used == 1 ? "" : "s")}, the most it allows. " +
                    "Sign out on another computer, or ask your MARK supplier to free one or allow more.");
            Execute(connection, """
                INSERT INTO computers (id, company_id, user_ref, machine_id, machine_name, app_version, device_token_hash, first_seen_utc, last_check_in_utc)
                VALUES ($id, $company, $user, $machine, $name, $version, $token, $now, $now)
                """, transaction,
                ("$id", Guid.NewGuid().ToString()), ("$company", company.Id.ToString()), ("$user", user.Id.ToString()), ("$machine", machineId),
                ("$name", machineName), ("$version", request.AppVersion), ("$token", Secrets.HashToken(token)), ("$now", Time(Now)));
        }

        var licence = IssueLicence(connection, company, user.AsLicenceUser, machineId, transaction);
        transaction.Commit();
        return new SignInResponse(licence, token);
    }

    public LicenceResponse CheckIn(CheckInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        var computer = Computer(connection, request.DeviceToken, request.MachineId);
        Execute(connection, "UPDATE computers SET last_check_in_utc = $now WHERE id = $id", null, ("$now", Time(Now)), ("$id", computer.Id.ToString()));
        var company = GetCompany(connection, computer.CompanyId);
        return new LicenceResponse(IssueLicence(connection, company, computer.User, computer.MachineId));
    }

    public RedeemKeyResponse RedeemKey(RedeemKeyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        var computer = Computer(connection, request.DeviceToken, request.MachineId);
        using var transaction = connection.BeginTransaction();
        var (company, message) = ApplyKey(connection, GetCompany(connection, computer.CompanyId, transaction), request.Key, transaction);
        Execute(connection, "UPDATE computers SET last_check_in_utc = $now WHERE id = $id", transaction, ("$now", Time(Now)), ("$id", computer.Id.ToString()));
        var licence = IssueLicence(connection, company, computer.User, computer.MachineId, transaction);
        transaction.Commit();
        return new RedeemKeyResponse(licence, message);
    }

    /// <summary>The company's catalogue (library JSON) for a signed-in computer.</summary>
    public CatalogueResponse ClientCatalogue(CatalogueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        var computer = Computer(connection, request.DeviceToken, request.MachineId);
        var company = GetCompany(connection, computer.CompanyId);
        return new CatalogueResponse(CompanyCatalogueJson(connection, company)
                                     ?? throw ApiException.NotFound("A catalogue for your account"));
    }

    /// <summary>Frees the computer. Unknown tokens are ignored (already signed out).</summary>
    public void ClientSignOut(SignOutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        Execute(connection, "DELETE FROM computers WHERE device_token_hash = $token AND machine_id = $machine", null,
            ("$token", Secrets.HashToken(request.DeviceToken)), ("$machine", request.MachineId ?? ""));
    }

    /// <summary>Distinct computers signed in to the company (a computer shared by two logins counts once).</summary>
    private static long ComputersInUse(SqliteConnection connection, Guid companyId, SqliteTransaction? transaction)
        => Scalar<long>(connection, "SELECT COUNT(DISTINCT machine_id) FROM computers WHERE company_id = $company", transaction,
            ("$company", companyId.ToString()));

    private static UserRow? FindUser(SqliteConnection connection, string userId)
    {
        using var command = Command(connection,
            """
            SELECT id, company_id, user_id, name, password_hash, failed_attempts, locked_until_utc, role, permissions, disabled
            FROM users WHERE user_id = $user
            """, null, ("$user", userId));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new UserRow(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                ReadPermissions(reader, 8), reader.GetInt64(9) != 0)
            : null;
    }

    /// <summary>The computer of a device token, or <see cref="ErrorCodes.SignedOut"/> when it is no longer registered.</summary>
    private static ComputerRow Computer(SqliteConnection connection, string? deviceToken, string? machineId)
    {
        using var command = Command(connection, """
            SELECT m.id, m.company_id, m.user_ref, m.machine_id, u.user_id, u.name, u.role, u.permissions
            FROM computers m JOIN users u ON u.id = m.user_ref
            WHERE m.device_token_hash = $token AND m.machine_id = $machine
            """, null, ("$token", Secrets.HashToken(deviceToken)), ("$machine", machineId ?? ""));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new ApiException(401, ErrorCodes.SignedOut, "This computer has been signed out of MARK by your MARK supplier.");
        return new ComputerRow(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
            reader.GetString(3), new LicenceUser(reader.GetString(4), reader.GetString(5), reader.GetString(6), ReadPermissions(reader, 7)));
    }

    private static IReadOnlyList<string>? ReadPermissions(SqliteDataReader reader, int column)
        => reader.IsDBNull(column) ? null : FromJson<List<string>>(reader.GetString(column));
}
