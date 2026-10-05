using Mark.Licensing.Api;

namespace Mark.Licensing.Client;

/// <summary>How a check-in went.</summary>
public enum CheckInOutcome
{
    /// <summary>The server sent a new licence (it may say suspended or changed).</summary>
    Updated,

    /// <summary>The server could not be reached; the licence keeps working within the offline grace period.</summary>
    Offline,

    /// <summary>The server no longer knows this computer (freed by the owner, or the account deleted).</summary>
    SignedOut,

    /// <summary>Not signed in, or another error (see the message).</summary>
    Failed
}

public sealed record CheckInResult(CheckInOutcome Outcome, string Message);

/// <summary>
/// MARK's side of licensing: signing in (online, or offline with the saved password hash), the licence of this
/// computer, check-ins, licence keys and signing out. Every licence is verified against the MARK public key and must
/// be for this computer. <see cref="Status"/> is what MARK may do now; <see cref="StatusChanged"/> says when it changes.
/// </summary>
public sealed class LicenceManager
{
    private readonly ILicenceStateStore _store;
    private readonly LicenceVerifier _verifier;
    private readonly Func<string, ILicenceApi> _apiFor;
    private readonly Func<DateTime> _utcNow;
    private LicenceState _state;
    private Licence? _licence;

    /// <param name="machineId">This computer (see MachineIdentity in MARK).</param>
    /// <param name="apiFor">Creates the server client for an address.</param>
    public LicenceManager(ILicenceStateStore store, LicenceVerifier verifier, string machineId, string machineName,
        Func<string, ILicenceApi> apiFor, Func<DateTime>? utcNow = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _apiFor = apiFor ?? throw new ArgumentNullException(nameof(apiFor));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        MachineId = machineId;
        MachineName = machineName;
        _state = store.Load() ?? new LicenceState();
    }

    public string MachineId { get; }

    public string MachineName { get; }

    /// <summary>Sent with sign-ins so the owner can see which version a computer runs.</summary>
    public string? AppVersion { get; set; }

    public string ServerUrl => _state.ServerUrl;

    /// <summary>The User ID that last signed in on this computer (to fill in the sign-in page).</summary>
    public string? RememberedUserId => _state.UserId;

    public bool KeepSignedIn => _state.KeepSignedIn;

    /// <summary>What MARK may do now, or null when nobody is signed in.</summary>
    public LicenceStatus? Status { get; private set; }

    public Licence? Licence => _licence;

    public event Action? StatusChanged;

    /// <summary>
    /// Opens with the saved licence when the user chose to stay signed in and it is valid for this computer. False: the
    /// sign-in page is needed.
    /// </summary>
    public bool TryResume()
    {
        if (!_state.KeepSignedIn || _state.DeviceToken is null) return false;
        var licence = Read(_state.Licence, out _);
        if (licence is null) return false;
        Use(licence);
        return true;
    }

    /// <summary>
    /// Signs in with the server; when the server cannot be reached, the same user who signed in last on this computer
    /// can sign in offline with the saved licence. Returns an error message, or null when signed in.
    /// </summary>
    public async Task<string?> SignInAsync(string serverUrl, string userId, string password, bool keepSignedIn, CancellationToken cancel = default)
    {
        userId = (userId ?? "").Trim();
        if (userId.Length == 0) return "Enter your User ID.";
        if (string.IsNullOrEmpty(password)) return "Enter your password.";
        string url;
        try
        {
            url = JsonApi.NormaliseUrl(serverUrl);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        try
        {
            var response = await _apiFor(url).SignInAsync(new SignInRequest(userId, password, MachineId, MachineName, AppVersion), cancel);
            var licence = Read(response.Licence, out string? error);
            if (licence is null)
                return $"The licence server sent a licence this copy of MARK cannot accept ({error}). The server may belong to a different MARK installation.";

            _state = new LicenceState
            {
                ServerUrl = url,
                Licence = response.Licence,
                DeviceToken = response.DeviceToken,
                UserId = licence.UserId,
                PasswordHash = PasswordHasher.Hash(password),
                KeepSignedIn = keepSignedIn,
                LastSeenUtc = Latest(_state.LastSeenUtc, _utcNow())
            };
            Save();
            Use(licence);
            return null;
        }
        catch (LicenceServerException ex) when (ex.IsConnectionFailure)
        {
            return SignInOffline(userId, password, keepSignedIn) ? null : ex.Message;
        }
        catch (LicenceServerException ex)
        {
            return ex.Message;
        }
    }

    private bool SignInOffline(string userId, string password, bool keepSignedIn)
    {
        if (_state.DeviceToken is null || !string.Equals(_state.UserId, userId, StringComparison.OrdinalIgnoreCase)
                                       || !PasswordHasher.Verify(password, _state.PasswordHash))
            return false;
        var licence = Read(_state.Licence, out _);
        if (licence is null) return false;
        _state = _state with { KeepSignedIn = keepSignedIn };
        Save();
        Use(licence);
        return true;
    }

    /// <summary>Asks the server for the current licence (daily, and on "Check now").</summary>
    /// <summary>The latest MARK release on the licence server, or null when it cannot be asked (offline, not signed in).</summary>
    public async Task<UpdateInfo?> LatestAsync(CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(_state.ServerUrl)) return null;
        try
        {
            return await _apiFor(_state.ServerUrl).LatestAsync(cancel);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or LicenceServerException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public async Task<CheckInResult> CheckInAsync(CancellationToken cancel = default)
    {
        if (_state.DeviceToken is null || _licence is null)
            return new CheckInResult(CheckInOutcome.Failed, "Nobody is signed in.");
        try
        {
            var response = await _apiFor(_state.ServerUrl).CheckInAsync(new CheckInRequest(_state.DeviceToken, MachineId), cancel);
            var licence = Read(response.Licence, out string? error);
            if (licence is null)
                return new CheckInResult(CheckInOutcome.Failed, $"The licence server sent a licence MARK cannot accept: {error}");
            _state = _state with { Licence = response.Licence, LastSeenUtc = Latest(_state.LastSeenUtc, _utcNow()) };
            Save();
            Use(licence);
            return new CheckInResult(CheckInOutcome.Updated, "Your licence is up to date.");
        }
        catch (LicenceServerException ex) when (ex.IsConnectionFailure)
        {
            Refresh();
            return new CheckInResult(CheckInOutcome.Offline, ex.Message);
        }
        catch (LicenceServerException ex) when (ex.Code == ErrorCodes.SignedOut)
        {
            SignedOutByServer(ex.Message);
            return new CheckInResult(CheckInOutcome.SignedOut, ex.Message);
        }
        catch (LicenceServerException ex)
        {
            return new CheckInResult(CheckInOutcome.Failed, ex.Message);
        }
    }

    /// <summary>Uses a licence key from the owner. Returns whether it worked and what happened.</summary>
    public async Task<(bool Ok, string Message)> RedeemKeyAsync(string key, CancellationToken cancel = default)
    {
        if (_state.DeviceToken is null || _licence is null) return (false, "Sign in first.");
        string normalised = Secrets.NormaliseKey(key);
        if (normalised.Length == 0) return (false, "Enter the licence key.");
        try
        {
            var response = await _apiFor(_state.ServerUrl).RedeemKeyAsync(new RedeemKeyRequest(_state.DeviceToken, MachineId, normalised), cancel);
            var licence = Read(response.Licence, out string? error);
            if (licence is null) return (false, $"The licence server sent a licence MARK cannot accept: {error}");
            _state = _state with { Licence = response.Licence, LastSeenUtc = Latest(_state.LastSeenUtc, _utcNow()) };
            Save();
            Use(licence);
            return (true, response.Message);
        }
        catch (LicenceServerException ex) when (ex.Code == ErrorCodes.SignedOut)
        {
            SignedOutByServer(ex.Message);
            return (false, ex.Message);
        }
        catch (LicenceServerException ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>The fingerprint of the company's catalogue in the current licence, or null (the company keeps its own library).</summary>
    public string? CatalogueHash => _licence?.CatalogueHash;

    /// <summary>
    /// Downloads the company's catalogue (library JSON) and checks it against the hash in the signed licence, so a
    /// changed file is never used. Returns the JSON, or null with <paramref name="error"/>.
    /// </summary>
    public async Task<(string? Json, string? Error)> DownloadCatalogueAsync(CancellationToken cancel = default)
    {
        if (_state.DeviceToken is null || _licence?.CatalogueHash is not { } expected)
            return (null, "There is no catalogue for this account.");
        try
        {
            var response = await _apiFor(_state.ServerUrl).CatalogueAsync(new CatalogueRequest(_state.DeviceToken, MachineId), cancel);
            if (Mark.Licensing.CatalogueHash.Of(response.LibraryJson) != expected)
                return (null, "The catalogue from the licence server does not match your licence. Choose Check now and try again.");
            return (response.LibraryJson, null);
        }
        catch (LicenceServerException ex) when (ex.Code == ErrorCodes.SignedOut)
        {
            SignedOutByServer(ex.Message);
            return (null, ex.Message);
        }
        catch (LicenceServerException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>The fingerprint of the company's quotation profile in the current licence, or null (none set).</summary>
    public string? ProfileHash => _licence?.ProfileHash;

    /// <summary>
    /// Downloads the company's quotation profile (details, brand, bank, last page, set by the admin) and checks it against
    /// the hash in the signed licence. Returns the JSON, or null with <paramref name="error"/>.
    /// </summary>
    public async Task<(string? Json, string? Error)> DownloadProfileAsync(CancellationToken cancel = default)
    {
        if (_state.DeviceToken is null || _licence?.ProfileHash is not { } expected)
            return (null, "There are no quotation details for this account.");
        try
        {
            var response = await _apiFor(_state.ServerUrl).ProfileAsync(new CatalogueRequest(_state.DeviceToken, MachineId), cancel);
            if (Mark.Licensing.CatalogueHash.Of(response.ProfileJson) != expected)
                return (null, "The quotation details from the licence server do not match your licence. Choose Check now and try again.");
            return (response.ProfileJson, null);
        }
        catch (LicenceServerException ex) when (ex.Code == ErrorCodes.SignedOut)
        {
            SignedOutByServer(ex.Message);
            return (null, ex.Message);
        }
        catch (LicenceServerException ex)
        {
            return (null, ex.Message);
        }
    }

    // ── Staff logins (the account owner only) ──────────────────────

    /// <summary>True when the account owner is signed in: only they can add, change or remove staff logins.</summary>
    public bool CanManageStaff => _licence is { IsStaff: false } && _state.DeviceToken is not null;

    /// <summary>The company's staff logins. Returns the list, or null with an error message.</summary>
    public Task<(StaffList? List, string? Error)> StaffAsync(CancellationToken cancel = default)
        => StaffCallAsync((api, token) => api.StaffAsync(new StaffRequest(token, MachineId), cancel));

    /// <summary>Adds or changes a staff login (name, User ID, password, features, turned off).</summary>
    public Task<(StaffList? List, string? Error)> SaveStaffAsync(StaffEdit edit, CancellationToken cancel = default)
        => StaffCallAsync((api, token) => api.SaveStaffAsync(new SaveStaffRequest(token, MachineId, edit), cancel));

    /// <summary>Removes a staff login; MARK on its computers is signed out at their next check-in.</summary>
    public Task<(StaffList? List, string? Error)> DeleteStaffAsync(Guid staffId, CancellationToken cancel = default)
        => StaffCallAsync((api, token) => api.DeleteStaffAsync(new DeleteStaffRequest(token, MachineId, staffId), cancel));

    private async Task<(StaffList? List, string? Error)> StaffCallAsync(Func<ILicenceApi, string, Task<StaffList>> call)
    {
        if (_state.DeviceToken is not { } token || _licence is null) return (null, "Sign in first.");
        if (_licence.IsStaff) return (null, "Only the account owner can manage staff logins.");
        try
        {
            return (await call(_apiFor(_state.ServerUrl), token), null);
        }
        catch (LicenceServerException ex) when (ex.Code == ErrorCodes.SignedOut)
        {
            SignedOutByServer(ex.Message);
            return (null, ex.Message);
        }
        catch (LicenceServerException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Signs out of this computer: the server frees it (best effort; offline it stays counted until the owner frees it)
    /// and the licence is removed from the computer. The User ID and server address are remembered.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancel = default)
    {
        if (_state.DeviceToken is { } token)
        {
            try
            {
                await _apiFor(_state.ServerUrl).SignOutAsync(new SignOutRequest(token, MachineId), cancel);
            }
            catch (LicenceServerException)
            {
                // Offline or already signed out: the local sign-out below is what matters.
            }
        }
        _state = _state with { Licence = null, DeviceToken = null, PasswordHash = null };
        Save();
        _licence = null;
        Status = null;
        StatusChanged?.Invoke();
    }

    /// <summary>Re-evaluates the licence at the current time (expiry, grace period, clock) and remembers the time.</summary>
    public LicenceStatus? Refresh()
    {
        if (_licence is null) return Status;
        if (Status is { Mode: LicenceMode.ReadOnly } current && _state.DeviceToken is null)
            return current;                                       // signed out by the server: stays read-only
        var previous = Status;
        Status = Evaluate(_licence);
        if (!SameStatus(previous, Status))
            StatusChanged?.Invoke();
        return Status;
    }

    private void Use(Licence licence)
    {
        _licence = licence;
        Status = Evaluate(licence);
        StatusChanged?.Invoke();
    }

    private LicenceStatus Evaluate(Licence licence)
    {
        var now = _utcNow();
        var status = LicenceEvaluator.Evaluate(licence, now, _state.LastSeenUtc == default ? null : _state.LastSeenUtc);
        if (now > _state.LastSeenUtc)
        {
            _state = _state with { LastSeenUtc = now };
            TrySave();
        }
        return status;
    }

    private void SignedOutByServer(string message)
    {
        _state = _state with { Licence = null, DeviceToken = null, PasswordHash = null };
        TrySave();
        if (_licence is null) return;
        var all = _licence.Features.Select(f => f.FeatureId).ToHashSet();
        Status = LicenceEvaluator.Evaluate(_licence, _utcNow()) with
        {
            Mode = LicenceMode.ReadOnly,
            Reason = $"{message} Your quotes can be viewed but not changed. Restart MARK to sign in again.",
            Warning = null,
            Features = LicenceEvaluator.ForLogin(_licence, all),
            CompanyFeatures = all
        };
        StatusChanged?.Invoke();
    }

    private Licence? Read(SignedLicence? signed, out string? error)
    {
        var licence = _verifier.Verify(signed, out error);
        if (licence is not null && licence.MachineId != MachineId)
        {
            error = "The licence is for another computer.";
            return null;
        }
        return licence;
    }

    private static bool SameStatus(LicenceStatus? a, LicenceStatus? b)
        => a is not null && b is not null && a.Mode == b.Mode && a.Reason == b.Reason && a.Warning == b.Warning
           && a.Features.SetEquals(b.Features) && a.Products.SetEquals(b.Products);

    private static DateTime Latest(DateTime a, DateTime b) => a > b ? a : b;

    private void Save() => _store.Save(_state);

    private void TrySave()
    {
        try
        {
            _store.Save(_state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The state is saved again at the next change; the licence in memory keeps working.
        }
    }
}
