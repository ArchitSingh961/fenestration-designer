namespace Mark.Licensing.Api;

/// <summary>The calls MARK makes to the licence server (an interface so tests can stand in for the server).</summary>
public interface ILicenceApi
{
    Task<SignInResponse> SignInAsync(SignInRequest request, CancellationToken cancel = default);

    Task<LicenceResponse> CheckInAsync(CheckInRequest request, CancellationToken cancel = default);

    Task<RedeemKeyResponse> RedeemKeyAsync(RedeemKeyRequest request, CancellationToken cancel = default);

    Task SignOutAsync(SignOutRequest request, CancellationToken cancel = default);

    Task<CatalogueResponse> CatalogueAsync(CatalogueRequest request, CancellationToken cancel = default);

    Task<StaffList> StaffAsync(StaffRequest request, CancellationToken cancel = default);

    Task<StaffList> SaveStaffAsync(SaveStaffRequest request, CancellationToken cancel = default);

    Task<StaffList> DeleteStaffAsync(DeleteStaffRequest request, CancellationToken cancel = default);
}

/// <summary>MARK's HTTP client for the licence server.</summary>
public sealed class LicenceApiClient : ILicenceApi, IDisposable
{
    private readonly JsonApi _api;

    public LicenceApiClient(string serverUrl, HttpMessageHandler? handler = null)
    {
        _api = new JsonApi(serverUrl, handler);
    }

    public Task<SignInResponse> SignInAsync(SignInRequest request, CancellationToken cancel = default)
        => _api.SendAsync<SignInResponse>(HttpMethod.Post, "api/client/sign-in", request, cancel);

    public Task<LicenceResponse> CheckInAsync(CheckInRequest request, CancellationToken cancel = default)
        => _api.SendAsync<LicenceResponse>(HttpMethod.Post, "api/client/check-in", request, cancel);

    public Task<RedeemKeyResponse> RedeemKeyAsync(RedeemKeyRequest request, CancellationToken cancel = default)
        => _api.SendAsync<RedeemKeyResponse>(HttpMethod.Post, "api/client/redeem-key", request, cancel);

    public Task SignOutAsync(SignOutRequest request, CancellationToken cancel = default)
        => _api.SendAsync(HttpMethod.Post, "api/client/sign-out", request, cancel);

    public Task<CatalogueResponse> CatalogueAsync(CatalogueRequest request, CancellationToken cancel = default)
        => _api.SendAsync<CatalogueResponse>(HttpMethod.Post, "api/client/catalogue", request, cancel);

    public Task<StaffList> StaffAsync(StaffRequest request, CancellationToken cancel = default)
        => _api.SendAsync<StaffList>(HttpMethod.Post, "api/client/staff", request, cancel);

    public Task<StaffList> SaveStaffAsync(SaveStaffRequest request, CancellationToken cancel = default)
        => _api.SendAsync<StaffList>(HttpMethod.Post, "api/client/staff/save", request, cancel);

    public Task<StaffList> DeleteStaffAsync(DeleteStaffRequest request, CancellationToken cancel = default)
        => _api.SendAsync<StaffList>(HttpMethod.Post, "api/client/staff/delete", request, cancel);

    public void Dispose() => _api.Dispose();
}

/// <summary>MARK Owner's HTTP client for the licence server (admin calls).</summary>
public sealed class OwnerApiClient : IDisposable
{
    private readonly JsonApi _api;

    public OwnerApiClient(string serverUrl, HttpMessageHandler? handler = null)
    {
        _api = new JsonApi(serverUrl, handler);
    }

    public string ServerUrl => _api.BaseUrl;

    /// <summary>The admin signed in with this client, or null.</summary>
    public AdminSession? Session { get; private set; }

    public Task<AdminStatus> StatusAsync(CancellationToken cancel = default)
        => _api.SendAsync<AdminStatus>(HttpMethod.Get, "api/admin/status", null, cancel);

    public async Task<AdminSession> SetUpAsync(AdminSetupRequest request, CancellationToken cancel = default)
        => Use(await _api.SendAsync<AdminSession>(HttpMethod.Post, "api/admin/setup", request, cancel).ConfigureAwait(false));

    public async Task<AdminSession> SignInAsync(AdminSignInRequest request, CancellationToken cancel = default)
        => Use(await _api.SendAsync<AdminSession>(HttpMethod.Post, "api/admin/sign-in", request, cancel).ConfigureAwait(false));

    public async Task SignOutAsync(CancellationToken cancel = default)
    {
        if (Session is null) return;
        try
        {
            await _api.SendAsync(HttpMethod.Post, "api/admin/sign-out", null, cancel).ConfigureAwait(false);
        }
        finally
        {
            Session = null;
            _api.BearerToken = null;
        }
    }

    private AdminSession Use(AdminSession session)
    {
        Session = session;
        _api.BearerToken = session.Token;
        return session;
    }

    // Companies
    public Task<List<CompanySummary>> CompaniesAsync(CancellationToken cancel = default)
        => _api.SendAsync<List<CompanySummary>>(HttpMethod.Get, "api/admin/companies", null, cancel);

    public Task<CompanyDetail> CompanyAsync(Guid id, CancellationToken cancel = default)
        => _api.SendAsync<CompanyDetail>(HttpMethod.Get, $"api/admin/companies/{id}", null, cancel);

    public Task<CompanyDetail> CreateCompanyAsync(CompanyEdit edit, CancellationToken cancel = default)
        => _api.SendAsync<CompanyDetail>(HttpMethod.Post, "api/admin/companies", edit, cancel);

    public Task<CompanyDetail> UpdateCompanyAsync(Guid id, CompanyEdit edit, CancellationToken cancel = default)
        => _api.SendAsync<CompanyDetail>(HttpMethod.Put, $"api/admin/companies/{id}", edit, cancel);

    public Task<CompanyDetail> SetSuspendedAsync(Guid id, bool suspended, CancellationToken cancel = default)
        => _api.SendAsync<CompanyDetail>(HttpMethod.Post, $"api/admin/companies/{id}/suspended", new SetSuspendedRequest(suspended), cancel);

    public Task<CompanyDetail> FreeComputerAsync(Guid companyId, Guid computerId, CancellationToken cancel = default)
        => _api.SendAsync<CompanyDetail>(HttpMethod.Delete, $"api/admin/companies/{companyId}/computers/{computerId}", null, cancel);

    public Task<CompanyDetail> RemoveStaffAsync(Guid companyId, Guid staffId, CancellationToken cancel = default)
        => _api.SendAsync<CompanyDetail>(HttpMethod.Delete, $"api/admin/companies/{companyId}/staff/{staffId}", null, cancel);

    public Task DeleteCompanyAsync(Guid id, CancellationToken cancel = default)
        => _api.SendAsync(HttpMethod.Delete, $"api/admin/companies/{id}", null, cancel);

    // Packages
    public Task<List<PackageInfo>> PackagesAsync(CancellationToken cancel = default)
        => _api.SendAsync<List<PackageInfo>>(HttpMethod.Get, "api/admin/packages", null, cancel);

    public Task<PackageInfo> SavePackageAsync(PackageInfo package, CancellationToken cancel = default)
        => package.Id == Guid.Empty
            ? _api.SendAsync<PackageInfo>(HttpMethod.Post, "api/admin/packages", package, cancel)
            : _api.SendAsync<PackageInfo>(HttpMethod.Put, $"api/admin/packages/{package.Id}", package, cancel);

    public Task DeletePackageAsync(Guid id, CancellationToken cancel = default)
        => _api.SendAsync(HttpMethod.Delete, $"api/admin/packages/{id}", null, cancel);

    // Company types
    public Task<List<CompanyTypeInfo>> CompanyTypesAsync(CancellationToken cancel = default)
        => _api.SendAsync<List<CompanyTypeInfo>>(HttpMethod.Get, "api/admin/company-types", null, cancel);

    public Task<CompanyTypeInfo> SaveCompanyTypeAsync(CompanyTypeInfo type, CancellationToken cancel = default)
        => type.Id == Guid.Empty
            ? _api.SendAsync<CompanyTypeInfo>(HttpMethod.Post, "api/admin/company-types", type, cancel)
            : _api.SendAsync<CompanyTypeInfo>(HttpMethod.Put, $"api/admin/company-types/{type.Id}", type, cancel);

    public Task DeleteCompanyTypeAsync(Guid id, CancellationToken cancel = default)
        => _api.SendAsync(HttpMethod.Delete, $"api/admin/company-types/{id}", null, cancel);

    // Catalogue
    public Task<CatalogueInfo> CatalogueAsync(CancellationToken cancel = default)
        => _api.SendAsync<CatalogueInfo>(HttpMethod.Get, "api/admin/catalogue", null, cancel);

    public Task<CatalogueInfo> PublishCatalogueAsync(string libraryJson, CancellationToken cancel = default)
        => _api.SendAsync<CatalogueInfo>(HttpMethod.Put, "api/admin/catalogue", new PublishCatalogueRequest(libraryJson), cancel);

    // Licence keys
    public Task<List<LicenceKeyInfo>> KeysAsync(CancellationToken cancel = default)
        => _api.SendAsync<List<LicenceKeyInfo>>(HttpMethod.Get, "api/admin/keys", null, cancel);

    public Task<List<LicenceKeyInfo>> GenerateKeysAsync(GenerateKeysRequest request, CancellationToken cancel = default)
        => _api.SendAsync<List<LicenceKeyInfo>>(HttpMethod.Post, "api/admin/keys", request, cancel);

    public Task<LicenceKeyInfo> RevokeKeyAsync(Guid id, CancellationToken cancel = default)
        => _api.SendAsync<LicenceKeyInfo>(HttpMethod.Post, $"api/admin/keys/{id}/revoke", null, cancel);

    public void Dispose() => _api.Dispose();
}
