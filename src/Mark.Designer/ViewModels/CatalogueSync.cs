using Mark.Core.Library;
using Mark.Data;
using Mark.Licensing.Client;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Keeps the local library in line with the catalogue the owner gives the company: when the licence's catalogue hash
/// differs from the one last applied, the catalogue is downloaded (and checked against the hash), applied with
/// <see cref="LibraryService.ApplyCatalogue"/> (the company's own prices are kept) and the hash remembered. A licence
/// without a catalogue leaves the library alone. The company's quotation profile (details, brand, bank, last page, set
/// by the admin) is kept in step the same way.
/// </summary>
public sealed class CatalogueSync
{
    private readonly LicenceManager _licence;
    private readonly LocalStore _store;
    private bool _running;

    public CatalogueSync(LicenceManager licence, LocalStore store)
    {
        _licence = licence ?? throw new ArgumentNullException(nameof(licence));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>True when the licence names a catalogue that is not applied yet.</summary>
    public bool IsDue
    {
        get
        {
            try
            {
                return (_licence.CatalogueHash is { } expected && _store.Settings.LoadCatalogueHash() != expected) || ProfileIsDue;
            }
            catch (DataStoreException)
            {
                return false;
            }
        }
    }

    /// <summary>True when the licence's quotation profile differs from the one kept (or one was removed).</summary>
    private bool ProfileIsDue => _licence.Licence is not null && _store.Settings.LoadProfileHash() != _licence.ProfileHash;

    /// <summary>Downloads the quotation profile if it changed (removes it when the admin removed it). Returns an error or null.</summary>
    private async Task<string?> SyncProfileAsync(CancellationToken cancel)
    {
        if (!ProfileIsDue) return null;
        if (_licence.ProfileHash is not { } hash)
        {
            _store.Settings.SaveCompanyProfile(null, null);
            return null;
        }
        var (json, error) = await _licence.DownloadProfileAsync(cancel);
        if (json is null) return $"Your quotation details could not be updated: {error}";
        _store.Settings.SaveCompanyProfile(json, hash);
        return null;
    }

    /// <summary>Applies the catalogue if it changed. Returns a message for the user, or null when nothing changed.</summary>
    public async Task<string?> SyncAsync(CancellationToken cancel = default)
    {
        if (_running || !IsDue) return null;
        string? profileError;
        try
        {
            profileError = await SyncProfileAsync(cancel);
        }
        catch (DataStoreException ex)
        {
            profileError = ex.Message;
        }
        if (_licence.CatalogueHash is not { } expected || _store.Settings.LoadCatalogueHash() == expected) return profileError;
        _running = true;
        try
        {
            var (json, error) = await _licence.DownloadCatalogueAsync(cancel);
            if (json is null) return $"Your product catalogue could not be updated: {error}";
            var result = _store.Library.ApplyCatalogue(LibrarySerializer.Deserialize(json));
            _store.Settings.SaveOwnItems(LibrarySerializer.ReadOwnItems(json));
            _store.Settings.SaveCatalogueHash(expected);
            return result.Changed
                ? $"Your product catalogue was updated: {result.Added.Count} new, {result.Updated.Count} changed, {result.Retired.Count} no longer offered."
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DataStoreException)
        {
            return $"Your product catalogue could not be applied: {ex.Message}";
        }
        finally
        {
            _running = false;
        }
    }
}
