using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Licensing;
using Mark.Licensing.Api;
using Microsoft.AspNetCore.Http.Json;

namespace Mark.LicenceServer;

/// <summary>Where the server keeps its data and which address it listens on.</summary>
public sealed record ServerOptions
{
    /// <summary>The default data folder: <c>%LOCALAPPDATA%\MARK Licence Server</c>.</summary>
    public static string DefaultDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MARK Licence Server");

    public const string DefaultUrls = "http://localhost:5180";

    public string DataFolder { get; init; } = DefaultDataFolder;

    /// <summary>The private signing key (PEM). Default: <c>signing-key.pem</c> in the data folder; created if missing.</summary>
    public string? SigningKeyPath { get; init; }

    public string Urls { get; init; } = DefaultUrls;

    public string DatabasePath => Path.Combine(DataFolder, "licences.db");

    public string KeyPath => SigningKeyPath ?? Path.Combine(DataFolder, "signing-key.pem");
}

/// <summary>Builds the licence server: the service, JSON settings, error handling and the HTTP endpoints.</summary>
public static class LicenceServerApp
{
    public static WebApplication Create(ServerOptions options, string[]? args = null, Func<DateTime>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
        builder.WebHost.UseUrls(options.Urls);
        builder.Services.Configure<JsonOptions>(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        Directory.CreateDirectory(options.DataFolder);
        var signer = LoadOrCreateKey(options.KeyPath, out bool created);
        var service = new LicenceService(LicenceDatabase.Open(options.DatabasePath), signer, utcNow);
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.Use(HandleErrors);
        MapEndpoints(app, service);

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var log = app.Logger;
            log.LogInformation("MARK Licence Server listening on {Urls}", string.Join(", ", app.Urls));
            log.LogInformation("Data folder: {Folder}", options.DataFolder);
            if (created)
                log.LogWarning("A new signing key was created at {Path}. Keep a backup of it: licences can only be issued with it.", options.KeyPath);
            if (!service.PublicKeyMatchesMark)
                log.LogWarning("This server's signing key is not the one MARK is built with, so MARK will not accept its licences. " +
                               "Public key: {Key}", service.PublicKey);
        });
        return app;
    }

    /// <summary>The signing key from <paramref name="path"/>; a new one is created there on first run.</summary>
    public static LicenceSigner LoadOrCreateKey(string path, out bool created)
    {
        created = false;
        if (File.Exists(path)) return LicenceSigner.FromPem(File.ReadAllText(path));
        var signer = LicenceSigner.CreateNew();
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        File.WriteAllText(path, signer.ExportPrivateKeyPem());
        created = true;
        return signer;
    }

    private static async Task HandleErrors(HttpContext context, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (ApiException ex)
        {
            await WriteError(context, ex.Status, new ApiError(ex.Code, ex.Message));
        }
        catch (BadHttpRequestException)
        {
            await WriteError(context, 400, new ApiError(ErrorCodes.Invalid, "The request could not be read."));
        }
    }

    private static Task WriteError(HttpContext context, int status, ApiError error)
    {
        context.Response.Clear();
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(error, LicenceJson.Options);
    }

    private static bool IsLocal(HttpContext context)
        => context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);

    private static LicenceService.AdminIdentity RequireAdmin(HttpContext context, LicenceService service)
    {
        string? header = context.Request.Headers.Authorization;
        string? token = header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
        return service.Authenticate(token)
               ?? throw new ApiException(401, ErrorCodes.Unauthorized, "Your MARK Owner session has ended. Sign in again.");
    }

    private static void MapEndpoints(WebApplication app, LicenceService service)
    {
        app.MapGet("/", () => Results.Text("MARK Licence Server is running."));

        // MARK (client companies)
        app.MapPost("/api/client/sign-in", (SignInRequest r) => service.ClientSignIn(r));
        app.MapPost("/api/client/check-in", (CheckInRequest r) => service.CheckIn(r));
        app.MapGet("/api/client/latest", () => service.Latest());
        app.MapPost("/api/client/redeem-key", (RedeemKeyRequest r) => service.RedeemKey(r));
        app.MapPost("/api/client/catalogue", (CatalogueRequest r) => service.ClientCatalogue(r));
        app.MapPost("/api/client/profile", (CatalogueRequest r) => service.ClientProfile(r));
        app.MapPost("/api/client/staff", (StaffRequest r) => service.ClientStaff(r));
        app.MapPost("/api/client/staff/save", (SaveStaffRequest r) => service.ClientSaveStaff(r));
        app.MapPost("/api/client/staff/delete", (DeleteStaffRequest r) => service.ClientDeleteStaff(r));
        app.MapPost("/api/client/sign-out", (SignOutRequest r) =>
        {
            service.ClientSignOut(r);
            return Results.NoContent();
        });

        // MARK Owner: no session needed
        app.MapGet("/api/admin/status", (HttpContext c) => service.Status(IsLocal(c)));
        app.MapPost("/api/admin/setup", (HttpContext c, AdminSetupRequest r) =>
        {
            if (!IsLocal(c))
                throw new ApiException(403, ErrorCodes.Forbidden, "The admin account can only be set up on the server's own computer.");
            return service.SetUp(r);
        });
        app.MapPost("/api/admin/sign-in", (AdminSignInRequest r) => service.SignIn(r));

        // MARK Owner: signed-in admin only
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (context, next) =>
        {
            RequireAdmin(context.HttpContext, service);
            return await next(context);
        });
        admin.MapPost("/sign-out", (HttpContext c) =>
        {
            string? header = c.Request.Headers.Authorization;
            service.SignOut(header?["Bearer ".Length..].Trim());
            return Results.NoContent();
        });

        admin.MapGet("/companies", () => service.Companies());
        admin.MapGet("/companies/{id:guid}", (Guid id) => service.Company(id));
        admin.MapPost("/companies", (CompanyEdit e) => service.CreateCompany(e));
        admin.MapPut("/companies/{id:guid}", (Guid id, CompanyEdit e) => service.UpdateCompany(id, e));
        admin.MapPost("/companies/{id:guid}/suspended", (Guid id, SetSuspendedRequest r) => service.SetSuspended(id, r.Suspended));
        admin.MapDelete("/companies/{id:guid}/computers/{computer:guid}", (Guid id, Guid computer) => service.FreeComputer(id, computer));
        admin.MapDelete("/companies/{id:guid}/staff/{staff:guid}", (Guid id, Guid staff) => service.RemoveStaff(id, staff));
        admin.MapGet("/companies/{id:guid}/own-items", (Guid id) => service.CompanyItems(id));
        admin.MapPut("/companies/{id:guid}/own-items", (Guid id, SaveCompanyItemsRequest r) => service.SaveCompanyItems(id, r.ItemsJson));
        admin.MapDelete("/companies/{id:guid}", (Guid id) =>
        {
            service.DeleteCompany(id);
            return Results.NoContent();
        });

        admin.MapGet("/packages", () => service.Packages());
        admin.MapPost("/packages", (PackageInfo p) => service.SavePackage(p with { Id = Guid.Empty }));
        admin.MapPut("/packages/{id:guid}", (Guid id, PackageInfo p) => service.SavePackage(p with { Id = id }));
        admin.MapDelete("/packages/{id:guid}", (Guid id) =>
        {
            service.DeletePackage(id);
            return Results.NoContent();
        });

        admin.MapGet("/company-types", () => service.CompanyTypes());
        admin.MapPost("/company-types", (CompanyTypeInfo t) => service.SaveCompanyType(t with { Id = Guid.Empty }));
        admin.MapPut("/company-types/{id:guid}", (Guid id, CompanyTypeInfo t) => service.SaveCompanyType(t with { Id = id }));
        admin.MapDelete("/company-types/{id:guid}", (Guid id) =>
        {
            service.DeleteCompanyType(id);
            return Results.NoContent();
        });

        admin.MapGet("/latest", () => service.Latest());
        admin.MapPut("/latest", (UpdateInfo u) => service.SetLatest(u));
        admin.MapGet("/catalogue", () => service.Catalogue());
        admin.MapPut("/catalogue", (PublishCatalogueRequest r) => service.PublishCatalogue(r.LibraryJson));

        admin.MapGet("/keys", () => service.Keys());
        admin.MapPost("/keys", (GenerateKeysRequest r) => service.GenerateKeys(r));
        admin.MapPost("/keys/{id:guid}/revoke", (Guid id) => service.RevokeKey(id));
    }
}
