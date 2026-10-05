using System.Text.RegularExpressions;
using Mark.Licensing;
using Mark.Licensing.Api;
using static Mark.LicenceServer.LicenceDatabase;

namespace Mark.LicenceServer;

/// <summary>
/// The latest MARK version the owner has released (Milestone 20): set in MARK Owner, asked for by every MARK, which
/// offers the download when it is newer than itself.
/// </summary>
public sealed partial class LicenceService
{
    private const string LatestKey = "latest-mark";

    /// <summary>The latest release, or an empty one (no version) when none was published.</summary>
    public UpdateInfo Latest()
    {
        using var connection = Connect();
        using var command = Command(connection, "SELECT value FROM server_settings WHERE key = $key", null, ("$key", LatestKey));
        return command.ExecuteScalar() is string json
            ? System.Text.Json.JsonSerializer.Deserialize<UpdateInfo>(json, LicenceJson.Options) ?? UpdateInfo.None
            : UpdateInfo.None;
    }

    /// <summary>Publishes a release: a version (1.2 or 1.2.3), an http(s) download link and what is new.</summary>
    public UpdateInfo SetLatest(UpdateInfo update)
    {
        string version = (update.Version ?? "").Trim();
        string url = (update.DownloadUrl ?? "").Trim();
        string notes = (update.Notes ?? "").Trim();
        if (version.Length == 0 && url.Length == 0)
        {
            using var clear = Connect();
            using var delete = Command(clear, "DELETE FROM server_settings WHERE key = $key", null, ("$key", LatestKey));
            delete.ExecuteNonQuery();
            return UpdateInfo.None;
        }
        if (!Regex.IsMatch(version, @"^\d{1,4}(\.\d{1,5}){1,3}$"))
            throw ApiException.Invalid("Enter the version as numbers, e.g. 1.2 or 1.2.3.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw ApiException.Invalid("Enter the download link as a web address (https://…).");
        if (notes.Length > 2000) throw ApiException.Invalid("What is new can have at most 2,000 characters.");
        var saved = new UpdateInfo(version, url, notes, _utcNow());
        using var connection = Connect();
        using var command = Command(connection, """
            INSERT INTO server_settings (key, value) VALUES ($key, $value)
            ON CONFLICT (key) DO UPDATE SET value = excluded.value
            """, null, ("$key", LatestKey), ("$value", ToJson(saved)));
        command.ExecuteNonQuery();
        return saved;
    }
}
