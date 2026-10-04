using Mark.Licensing;
using Mark.Licensing.Api;
using Microsoft.Data.Sqlite;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>Company accounts: their owner login, products, package, add-ons, computers and suspension.</summary>
public sealed partial class LicenceService
{
    /// <summary>The role of the account owner, the login the admin sets up (staff logins are <see cref="UserRoles.Staff"/>).</summary>
    public const string OwnerRole = UserRoles.Owner;

    public IReadOnlyList<CompanySummary> Companies()
    {
        using var connection = Connect();
        using var command = Command(connection, $"""
            SELECT {CompanyColumns},
                (SELECT name FROM company_types t WHERE t.id = c.company_type_id),
                (SELECT name FROM packages p WHERE p.id = c.package_id),
                (SELECT user_id FROM users u WHERE u.company_id = c.id AND u.role = '{OwnerRole}' LIMIT 1),
                (SELECT COUNT(DISTINCT machine_id) FROM computers m WHERE m.company_id = c.id),
                (SELECT MAX(last_check_in_utc) FROM computers m WHERE m.company_id = c.id),
                (SELECT COUNT(*) FROM users u WHERE u.company_id = c.id AND u.disabled = 0)
            FROM companies c ORDER BY c.name COLLATE NOCASE
            """, null);
        using var reader = command.ExecuteReader();
        var list = new List<CompanySummary>();
        while (reader.Read())
        {
            var c = ReadCompany(reader);
            const int extra = CompanyColumnCount;                 // the columns after CompanyColumns
            list.Add(new CompanySummary(c.Id, c.Name, c.Logo is not null, reader.IsDBNull(extra) ? null : reader.GetString(extra),
                reader.IsDBNull(extra + 2) ? "" : reader.GetString(extra + 2), c.Products,
                reader.IsDBNull(extra + 1) ? null : reader.GetString(extra + 1),
                c.ValidUntilUtc, c.Suspended, reader.GetInt32(extra + 3), c.MaxComputers,
                reader.IsDBNull(extra + 4) ? null : ParseTime(reader.GetString(extra + 4)),
                reader.GetInt32(extra + 5), c.MaxUsers));
        }
        reader.Close();
        return list.Select(c => c with { OwnItemsSummary = OwnItemsOf(connection, c.Id, null).SummaryText }).ToList();
    }

    public CompanyDetail Company(Guid id)
    {
        using var connection = Connect();
        return Detail(connection, GetCompany(connection, id));
    }

    private static CompanyDetail Detail(SqliteConnection connection, CompanyRow c, SqliteTransaction? transaction = null)
    {
        string ownerName = "", ownerUserId = "";
        using (var command = Command(connection, $"SELECT name, user_id FROM users WHERE company_id = $id AND role = '{OwnerRole}' LIMIT 1",
                   transaction, ("$id", c.Id.ToString())))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                ownerName = reader.GetString(0);
                ownerUserId = reader.GetString(1);
            }
        }

        var computers = new List<ComputerInfo>();
        using (var command = Command(connection, """
                   SELECT m.id, m.machine_name, u.user_id, m.first_seen_utc, m.last_check_in_utc
                   FROM computers m JOIN users u ON u.id = m.user_ref WHERE m.company_id = $id ORDER BY m.first_seen_utc
                   """, transaction, ("$id", c.Id.ToString())))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                computers.Add(new ComputerInfo(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                    ParseTime(reader.GetString(3)), reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4))));
        }

        return new CompanyDetail(c.Id, c.Name, c.Logo, c.TypeId, ownerName, ownerUserId, c.Products, c.PackageId, c.ValidUntilUtc,
            c.MaxComputers, c.AddOns, c.RemovedFeatures, c.Suspended, c.Notes, c.CreatedUtc, computers, c.Catalogue, c.MaxUsers,
            StaffOf(connection, c.Id, transaction), OwnItemsOf(connection, c.Id, transaction).SummaryText,
            ProfileOf(connection, c.Id, transaction));
    }

    /// <summary>A new company account with its owner login (User ID and password set by the admin).</summary>
    public CompanyDetail CreateCompany(CompanyEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        CheckPassword(edit.OwnerPassword);
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var company = Validated(connection, edit, Guid.NewGuid(), Now, suspended: false, transaction, CompanyCatalogue.Empty,
            currentMaxUsers: Math.Max(1, edit.MaxComputers));
        string userId = CheckUserId(edit.OwnerUserId);
        EnsureUserIdFree(connection, userId, exceptCompany: null, transaction);

        WriteCompany(connection, company, transaction, insert: true);
        Execute(connection, """
            INSERT INTO users (id, company_id, user_id, name, role, password_hash, created_utc)
            VALUES ($id, $company, $user, $name, $role, $hash, $now)
            """, transaction,
            ("$id", Guid.NewGuid().ToString()), ("$company", company.Id.ToString()), ("$user", userId),
            ("$name", OwnerName(edit, company.Name)), ("$role", OwnerRole), ("$hash", PasswordHasher.Hash(edit.OwnerPassword!)),
            ("$now", Time(Now)));
        if (edit.Profile is { } profile) SaveProfile(connection, company.Id, profile, transaction);
        var detail = Detail(connection, company, transaction);
        transaction.Commit();
        return detail;
    }

    /// <summary>Changes an account. A non-empty password sets a new one (and unlocks the User ID). Takes effect on the
    /// company's computers at their next check-in.</summary>
    public CompanyDetail UpdateCompany(Guid id, CompanyEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!string.IsNullOrEmpty(edit.OwnerPassword)) CheckPassword(edit.OwnerPassword);
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var existing = GetCompany(connection, id, transaction);
        var company = Validated(connection, edit, id, existing.CreatedUtc, existing.Suspended, transaction, existing.Catalogue,
            existing.MaxUsers);
        int inUse = UsersInUse(connection, id, transaction);
        if (company.MaxUsers < inUse)
            throw ApiException.Invalid($"This account has {inUse} logins in use (the account owner and {inUse - 1} staff). " +
                                       $"Allow at least {inUse}, or remove staff logins first.");
        string userId = CheckUserId(edit.OwnerUserId);
        EnsureUserIdFree(connection, userId, exceptCompany: id, transaction);

        WriteCompany(connection, company, transaction, insert: false);
        Execute(connection, $"UPDATE users SET user_id = $user, name = $name WHERE company_id = $company AND role = '{OwnerRole}'", transaction,
            ("$user", userId), ("$name", OwnerName(edit, company.Name)), ("$company", id.ToString()));
        if (!string.IsNullOrEmpty(edit.OwnerPassword))
            Execute(connection, $"""
                UPDATE users SET password_hash = $hash, failed_attempts = 0, locked_until_utc = NULL
                WHERE company_id = $company AND role = '{OwnerRole}'
                """, transaction, ("$hash", PasswordHasher.Hash(edit.OwnerPassword)), ("$company", id.ToString()));
        if (edit.Profile is { } profile) SaveProfile(connection, id, profile, transaction);
        var detail = Detail(connection, company, transaction);
        transaction.Commit();
        return detail;
    }

    /// <summary>Suspends (MARK becomes read-only at the next check-in) or reactivates an account.</summary>
    public CompanyDetail SetSuspended(Guid id, bool suspended)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var company = GetCompany(connection, id, transaction) with { Suspended = suspended };
        WriteCompany(connection, company, transaction, insert: false);
        var detail = Detail(connection, company, transaction);
        transaction.Commit();
        return detail;
    }

    /// <summary>Signs a computer out: it no longer counts towards the limit, and MARK there must sign in again.</summary>
    public CompanyDetail FreeComputer(Guid companyId, Guid computerId)
    {
        using var connection = Connect();
        var company = GetCompany(connection, companyId);
        Execute(connection, "DELETE FROM computers WHERE id = $id AND company_id = $company", null,
            ("$id", computerId.ToString()), ("$company", companyId.ToString()));
        return Detail(connection, company);
    }

    /// <summary>Deletes an account with its users and computers; its unused keys are revoked. MARK on its computers
    /// becomes read-only at the next check-in.</summary>
    public void DeleteCompany(Guid id)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        GetCompany(connection, id, transaction);
        Execute(connection, "UPDATE licence_keys SET state = $revoked WHERE company_id = $id AND state = $unused", transaction,
            ("$revoked", KeyState.Revoked.ToString()), ("$unused", KeyState.Unused.ToString()), ("$id", id.ToString()));
        Execute(connection, "DELETE FROM companies WHERE id = $id", transaction, ("$id", id.ToString()));
        transaction.Commit();
    }

    // ── Quotation profile ───────────────────────────────────────────

    /// <summary>The last page's picture may be larger than a logo.</summary>
    public const int MaxExtraPageBytes = 1536 * 1024;

    private static QuotationProfile ProfileOf(SqliteConnection connection, Guid companyId, SqliteTransaction? transaction)
        => ProfileJsonOf(connection, companyId, transaction) is { } json
            ? System.Text.Json.JsonSerializer.Deserialize<QuotationProfile>(json, LicenceJson.Options) ?? QuotationProfile.Empty
            : QuotationProfile.Empty;

    /// <summary>The stored profile JSON (what MARK downloads and its hash covers), or null when none is set.</summary>
    private static string? ProfileJsonOf(SqliteConnection connection, Guid companyId, SqliteTransaction? transaction)
        => Scalar<string?>(connection, "SELECT profile_json FROM company_profiles WHERE company_id = $id", transaction,
            ("$id", companyId.ToString()));

    /// <summary>Checks and stores (or, when empty, removes) a company's quotation profile.</summary>
    private void SaveProfile(SqliteConnection connection, Guid companyId, QuotationProfile profile, SqliteTransaction transaction)
    {
        static string Text(string? value, string what, int max = 300)
        {
            string text = (value ?? "").Trim();
            if (text.Length > max) throw ApiException.Invalid($"The {what} can have at most {max} characters.");
            return text;
        }
        var clean = new QuotationProfile
        {
            PartnerLabel = Text(profile.PartnerLabel, "partner line", 80), Address = Text(profile.Address, "address", 400),
            Phone = Text(profile.Phone, "contact number", 80), Email = Text(profile.Email, "e-mail", 120),
            Website = Text(profile.Website, "website", 120), Gstin = Text(profile.Gstin, "GSTIN", 20).ToUpperInvariant(),
            BrandName = Text(profile.BrandName, "brand name", 80), BrandLogoBase64 = CheckImage(profile.BrandLogoBase64, "brand logo", MaxLogoBytes),
            BankAccountName = Text(profile.BankAccountName, "account name", 120), BankAccountNumber = Text(profile.BankAccountNumber, "account number", 40),
            BankName = Text(profile.BankName, "bank name", 120), BankIfsc = Text(profile.BankIfsc, "IFSC", 20).ToUpperInvariant(),
            BankBranch = Text(profile.BankBranch, "branch", 120),
            ExtraPageBase64 = CheckImage(profile.ExtraPageBase64, "last page picture", MaxExtraPageBytes)
        };
        if (clean.IsEmpty)
        {
            Execute(connection, "DELETE FROM company_profiles WHERE company_id = $id", transaction, ("$id", companyId.ToString()));
            return;
        }
        Execute(connection, """
            INSERT INTO company_profiles (company_id, profile_json, updated_utc) VALUES ($id, $json, $now)
            ON CONFLICT (company_id) DO UPDATE SET profile_json = excluded.profile_json, updated_utc = excluded.updated_utc
            """, transaction, ("$id", companyId.ToString()), ("$json", ToJson(clean)), ("$now", Time(Now)));
    }

    /// <summary>The company's quotation profile for a signed-in computer.</summary>
    public ProfileResponse ClientProfile(CatalogueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var connection = Connect();
        var computer = Computer(connection, request.DeviceToken, request.MachineId);
        return new ProfileResponse(ProfileJsonOf(connection, computer.CompanyId, null)
                                   ?? throw ApiException.NotFound("Quotation details for your account"));
    }

    private static string OwnerName(CompanyEdit edit, string companyName)
        => string.IsNullOrWhiteSpace(edit.OwnerName) ? companyName : edit.OwnerName.Trim();

    /// <summary>The User ID must not belong to anyone else (another company, or a staff login) than the account owner of <paramref name="exceptCompany"/>.</summary>
    private static void EnsureUserIdFree(SqliteConnection connection, string userId, Guid? exceptCompany, SqliteTransaction transaction)
    {
        long taken = Scalar<long>(connection,
            $"SELECT COUNT(*) FROM users WHERE user_id = $user AND NOT (company_id = $company AND role = '{OwnerRole}')", transaction,
            ("$user", userId), ("$company", (exceptCompany ?? Guid.Empty).ToString()));
        if (taken > 0) throw ApiException.Conflict($"The User ID \"{userId}\" is already in use. Choose another.");
    }

    /// <param name="currentCatalogue">Kept when the edit does not say what the company gets.</param>
    /// <param name="currentMaxUsers">Kept when the edit does not say how many logins the company may have.</param>
    private static CompanyRow Validated(SqliteConnection connection, CompanyEdit edit, Guid id, DateTime createdUtc, bool suspended,
        SqliteTransaction transaction, CompanyCatalogue currentCatalogue, int currentMaxUsers)
    {
        string name = Required(edit.Name, "company name");
        if (edit.MaxComputers is < 1 or > 1000) throw ApiException.Invalid("The number of computers must be between 1 and 1000.");
        int maxUsers = edit.MaxUsers ?? currentMaxUsers;
        if (maxUsers is < 1 or > 1000) throw ApiException.Invalid("The number of users must be between 1 and 1000.");

        var products = (edit.Products ?? Array.Empty<ProductLicence>()).ToList();
        if (products.Count == 0) throw ApiException.Invalid("Choose at least one product (uPVC or Aluminium).");
        if (products.Select(p => p.Product).Distinct().Count() != products.Count) throw ApiException.Invalid("A product is listed twice.");

        if (edit.PackageId is not { } packageId) throw ApiException.Invalid("Choose a package.");
        if (FindPackage(connection, packageId, transaction) is null) throw ApiException.NotFound("The package");
        if (edit.CompanyTypeId is { } typeId
            && Scalar<long>(connection, "SELECT COUNT(*) FROM company_types WHERE id = $id", transaction, ("$id", typeId.ToString())) == 0)
            throw ApiException.NotFound("The company type");

        var addOns = (edit.AddOns ?? Array.Empty<AddOn>()).ToList();
        var removed = (edit.RemovedFeatures ?? Array.Empty<string>()).Distinct().ToList();
        var unknown = addOns.Select(a => a.FeatureId).Concat(removed).Where(f => !FeatureCatalog.Exists(f)).Distinct().ToList();
        if (unknown.Count > 0) throw ApiException.Invalid($"Unknown feature: {string.Join(", ", unknown)}.");
        if (addOns.Select(a => a.FeatureId).Distinct().Count() != addOns.Count) throw ApiException.Invalid("An add-on is listed twice.");
        if (removed.Any(f => FeatureCatalog.Find(f)!.IsCore))
            throw ApiException.Invalid("Quotes and the frame designer are always included and cannot be removed.");

        string? notes = string.IsNullOrWhiteSpace(edit.Notes) ? null : edit.Notes.Trim();
        var catalogue = edit.Catalogue is { } chosen
            ? new CompanyCatalogue(chosen.SystemIds.Distinct().ToList(), chosen.ItemIds.Distinct().ToList())
            : currentCatalogue;
        return new CompanyRow(id, name, CheckLogo(edit.LogoBase64), edit.CompanyTypeId, packageId, edit.ValidUntilUtc, edit.MaxComputers,
            suspended, products, addOns, removed, notes, createdUtc, catalogue, maxUsers);
    }
}
