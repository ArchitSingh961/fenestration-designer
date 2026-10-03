using System.IO;
using Mark.LicenceServer;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Licensing.Client;

namespace Mark.Tests.Licensing;

/// <summary>A clock the tests move by hand.</summary>
internal sealed class TestClock
{
    public static readonly DateTime Start = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

    public DateTime Now { get; set; } = Start;

    public void Advance(TimeSpan by) => Now += by;

    public void AdvanceDays(double days) => Now += TimeSpan.FromDays(days);
}

/// <summary>Licence state kept in memory (what MARK keeps in licence.dat).</summary>
internal sealed class MemoryStateStore : ILicenceStateStore
{
    public LicenceState? State { get; set; }

    public int Saves { get; private set; }

    public LicenceState? Load() => State;

    public void Save(LicenceState state)
    {
        State = state;
        Saves++;
    }
}

/// <summary>Stands in for DPAPI: reverses the bytes (enough to prove the file is not plain JSON).</summary>
internal sealed class ReversingProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => data.Reverse().ToArray();

    public byte[] Unprotect(byte[] data) => data.Reverse().ToArray();
}

/// <summary>
/// A licence server in a temporary folder, with its own signing key and a test clock: the service directly, plus
/// helpers to create packages, companies and signed-in computers.
/// </summary>
internal sealed class TestServer : IDisposable
{
    public TestServer()
    {
        Folder = Path.Combine(Path.GetTempPath(), "mark-tests", "licence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Signer = LicenceSigner.CreateNew();
        Verifier = new LicenceVerifier(Signer.PublicKey);
        Service = new LicenceService(LicenceDatabase.Open(Path.Combine(Folder, "licences.db")), Signer, () => Clock.Now);
    }

    public string Folder { get; }

    public TestClock Clock { get; } = new();

    public LicenceSigner Signer { get; }

    public LicenceVerifier Verifier { get; }

    public LicenceService Service { get; }

    public PackageInfo Package(string name) => Service.Packages().Single(p => p.Name == name);

    public CompanyTypeInfo Type(string name) => Service.CompanyTypes().Single(t => t.Name == name);

    /// <summary>The edit for a typical account: uPVC for a year, the Professional package, 2 computers.</summary>
    public CompanyEdit Edit(string name = "Shree Windows", string userId = "shree", string? password = "secret1",
        IReadOnlyList<ProductLicence>? products = null, string package = "Professional", int maxComputers = 2,
        IReadOnlyList<AddOn>? addOns = null, IReadOnlyList<string>? removed = null, DateTime? validUntil = null, string? logo = null)
        => new(name, logo, Type("uPVC fabricator").Id, "Ravi Shah", userId, password,
            products ?? new[] { new ProductLicence(Product.Upvc, Clock.Now.AddYears(1)) },
            Package(package).Id, validUntil ?? Clock.Now.AddYears(1), maxComputers,
            addOns ?? Array.Empty<AddOn>(), removed ?? Array.Empty<string>(), "Sold at the Pune expo");

    public CompanyDetail CreateCompany(CompanyEdit? edit = null) => Service.CreateCompany(edit ?? Edit());

    public SignInResponse SignIn(string userId = "shree", string password = "secret1", string machine = "PC-1")
        => Service.ClientSignIn(new SignInRequest(userId, password, machine, machine + " name", "1.0"));

    public Licence Read(SignedLicence signed) => Verifier.Verify(signed, out string? error) ?? throw new InvalidOperationException(error);

    public void Dispose()
    {
        Signer.Dispose();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp file is harmless.
        }
    }
}

/// <summary>An <see cref="ILicenceApi"/> that calls a <see cref="LicenceService"/> directly (no HTTP), translating its
/// refusals like the endpoints do; <see cref="Offline"/> simulates no connection.</summary>
internal sealed class DirectApi : ILicenceApi
{
    private readonly LicenceService _service;

    public DirectApi(LicenceService service)
    {
        _service = service;
    }

    public bool Offline { get; set; }

    public Task<SignInResponse> SignInAsync(SignInRequest request, CancellationToken cancel = default) => Call(() => _service.ClientSignIn(request));

    public Task<LicenceResponse> CheckInAsync(CheckInRequest request, CancellationToken cancel = default) => Call(() => _service.CheckIn(request));

    public Task<RedeemKeyResponse> RedeemKeyAsync(RedeemKeyRequest request, CancellationToken cancel = default) => Call(() => _service.RedeemKey(request));

    public Task SignOutAsync(SignOutRequest request, CancellationToken cancel = default) => Call(() =>
    {
        _service.ClientSignOut(request);
        return true;
    });

    public Task<CatalogueResponse> CatalogueAsync(CatalogueRequest request, CancellationToken cancel = default)
        => Call(() => _service.ClientCatalogue(request));

    public Task<StaffList> StaffAsync(StaffRequest request, CancellationToken cancel = default) => Call(() => _service.ClientStaff(request));

    public Task<StaffList> SaveStaffAsync(SaveStaffRequest request, CancellationToken cancel = default)
        => Call(() => _service.ClientSaveStaff(request));

    public Task<StaffList> DeleteStaffAsync(DeleteStaffRequest request, CancellationToken cancel = default)
        => Call(() => _service.ClientDeleteStaff(request));

    private Task<T> Call<T>(Func<T> call)
    {
        if (Offline)
            throw new LicenceServerException("connection", "The licence server could not be reached.", isConnectionFailure: true);
        try
        {
            return Task.FromResult(call());
        }
        catch (ApiException ex)
        {
            throw new LicenceServerException(ex.Code, ex.Message, (System.Net.HttpStatusCode)ex.Status);
        }
    }
}
