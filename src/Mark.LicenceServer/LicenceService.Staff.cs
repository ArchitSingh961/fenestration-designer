using Mark.Licensing;
using Mark.Licensing.Api;
using Microsoft.Data.Sqlite;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>
/// Staff logins (Milestone 14): the account owner adds people with their own User ID, password and a part of the
/// company's features, up to the number of users the admin allows (the account owner counts as one, turned-off logins
/// do not count). Only the account owner, signed in to MARK, can change them; the admin can remove them.
/// </summary>
public sealed partial class LicenceService
{
    /// <summary>The company's staff logins (from MARK, the account owner only).</summary>
    public StaffList ClientStaff(StaffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        var company = OwnerCompany(connection, request.DeviceToken, request.MachineId);
        return StaffListOf(connection, company, null);
    }

    /// <summary>Adds a staff login (<see cref="StaffEdit.Id"/> empty) or changes one. Turning a login off signs it out.</summary>
    public StaffList ClientSaveStaff(SaveStaffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var edit = request.Staff ?? throw ApiException.Invalid("Say which staff login to save.");
        using var connection = Connect();
        var company = OwnerCompany(connection, request.DeviceToken, request.MachineId);
        if (company.Suspended || company.ValidUntilUtc < Now)
            throw new ApiException(403, ErrorCodes.Forbidden, "Staff logins cannot be changed while your MARK account is suspended or has ended.");

        string name = Required(edit.Name, "name");
        string userId = CheckUserId(edit.UserId);
        var permissions = (edit.Permissions ?? Array.Empty<string>()).Distinct().ToList();
        if (permissions.Count == 0) throw ApiException.Invalid("Choose at least one thing this person may use.");
        var unknown = permissions.Where(f => !FeatureCatalog.Exists(f)).ToList();
        if (unknown.Count > 0) throw ApiException.Invalid($"Unknown feature: {string.Join(", ", unknown)}.");
        bool isNew = edit.Id == Guid.Empty;
        if (isNew || !string.IsNullOrEmpty(edit.Password)) CheckPassword(edit.Password);

        using var transaction = connection.BeginTransaction();
        var id = isNew ? Guid.NewGuid() : edit.Id;
        bool wasDisabled = true;
        if (!isNew)
        {
            using var command = Command(connection, "SELECT disabled FROM users WHERE id = $id AND company_id = $company AND role = $role",
                transaction, ("$id", id.ToString()), ("$company", company.Id.ToString()), ("$role", UserRoles.Staff));
            wasDisabled = command.ExecuteScalar() is long disabled
                ? disabled != 0
                : throw ApiException.NotFound("The staff login");
        }

        long taken = Scalar<long>(connection, "SELECT COUNT(*) FROM users WHERE user_id = $user AND id <> $id", transaction,
            ("$user", userId), ("$id", id.ToString()));
        if (taken > 0) throw ApiException.Conflict($"The User ID \"{userId}\" is already in use. Choose another.");

        // A login that becomes active needs a free place; turning one off always works.
        if (!edit.Disabled && wasDisabled && UsersInUse(connection, company.Id, transaction) >= company.MaxUsers)
            throw new ApiException(403, ErrorCodes.UserLimit,
                $"Your account allows {Users(company.MaxUsers)} (you and {company.MaxUsers - 1} staff), and all are in use. " +
                "Turn off or remove a staff login you no longer need, or ask your MARK supplier to allow more.");

        if (isNew)
        {
            Execute(connection, """
                INSERT INTO users (id, company_id, user_id, name, role, password_hash, created_utc, permissions, disabled)
                VALUES ($id, $company, $user, $name, $role, $hash, $now, $permissions, $disabled)
                """, transaction,
                ("$id", id.ToString()), ("$company", company.Id.ToString()), ("$user", userId), ("$name", name), ("$role", UserRoles.Staff),
                ("$hash", PasswordHasher.Hash(edit.Password!)), ("$now", Time(Now)), ("$permissions", ToJson(permissions)),
                ("$disabled", edit.Disabled ? 1 : 0));
        }
        else
        {
            Execute(connection, "UPDATE users SET user_id = $user, name = $name, permissions = $permissions, disabled = $disabled WHERE id = $id",
                transaction, ("$user", userId), ("$name", name), ("$permissions", ToJson(permissions)), ("$disabled", edit.Disabled ? 1 : 0),
                ("$id", id.ToString()));
            if (!string.IsNullOrEmpty(edit.Password))
                Execute(connection, "UPDATE users SET password_hash = $hash, failed_attempts = 0, locked_until_utc = NULL WHERE id = $id",
                    transaction, ("$hash", PasswordHasher.Hash(edit.Password)), ("$id", id.ToString()));
        }
        if (edit.Disabled)
            Execute(connection, "DELETE FROM computers WHERE user_ref = $id", transaction, ("$id", id.ToString()));

        var list = StaffListOf(connection, company, transaction);
        transaction.Commit();
        return list;
    }

    /// <summary>Removes a staff login; MARK on its computers is signed out at their next check-in.</summary>
    public StaffList ClientDeleteStaff(DeleteStaffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        var company = OwnerCompany(connection, request.DeviceToken, request.MachineId);
        DeleteStaff(connection, company.Id, request.StaffId);
        return StaffListOf(connection, company, null);
    }

    /// <summary>The admin removes a staff login of a company.</summary>
    public CompanyDetail RemoveStaff(Guid companyId, Guid staffId)
    {
        using var connection = Connect();
        var company = GetCompany(connection, companyId);
        DeleteStaff(connection, companyId, staffId);
        return Detail(connection, company);
    }

    private static void DeleteStaff(SqliteConnection connection, Guid companyId, Guid staffId)
    {
        using var command = Command(connection, "DELETE FROM users WHERE id = $id AND company_id = $company AND role = $role", null,
            ("$id", staffId.ToString()), ("$company", companyId.ToString()), ("$role", UserRoles.Staff));
        if (command.ExecuteNonQuery() == 0) throw ApiException.NotFound("The staff login");
    }

    /// <summary>The company of a signed-in computer whose login is the account owner's.</summary>
    private static CompanyRow OwnerCompany(SqliteConnection connection, string? deviceToken, string? machineId)
    {
        var computer = Computer(connection, deviceToken, machineId);
        if (computer.User.Role != UserRoles.Owner)
            throw new ApiException(403, ErrorCodes.Forbidden, "Only the owner of the MARK account can manage staff logins.");
        return GetCompany(connection, computer.CompanyId);
    }

    /// <summary>Active logins of a company: the account owner and the staff logins that are not turned off.</summary>
    private static int UsersInUse(SqliteConnection connection, Guid companyId, SqliteTransaction? transaction)
        => (int)Scalar<long>(connection, "SELECT COUNT(*) FROM users WHERE company_id = $company AND disabled = 0", transaction,
            ("$company", companyId.ToString()));

    private static StaffList StaffListOf(SqliteConnection connection, CompanyRow company, SqliteTransaction? transaction)
        => new(company.MaxUsers, UsersInUse(connection, company.Id, transaction), StaffOf(connection, company.Id, transaction));

    private static IReadOnlyList<StaffInfo> StaffOf(SqliteConnection connection, Guid companyId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, """
            SELECT u.id, u.name, u.user_id, u.permissions, u.disabled, u.created_utc, u.last_sign_in_utc,
                (SELECT COUNT(*) FROM computers m WHERE m.user_ref = u.id)
            FROM users u WHERE u.company_id = $company AND u.role = $role ORDER BY u.name COLLATE NOCASE, u.user_id
            """, transaction, ("$company", companyId.ToString()), ("$role", UserRoles.Staff));
        using var reader = command.ExecuteReader();
        var list = new List<StaffInfo>();
        while (reader.Read())
            list.Add(new StaffInfo(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                ReadPermissions(reader, 3) ?? Array.Empty<string>(), reader.GetInt64(4) != 0, ParseTime(reader.GetString(5)),
                reader.IsDBNull(6) ? null : ParseTime(reader.GetString(6)), reader.GetInt32(7)));
        return list;
    }

    private static string Users(int count) => count == 1 ? "1 user" : $"{count} users";
}
