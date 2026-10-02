using System.Text.Json.Nodes;

namespace Mark.Core.Serialization;

/// <summary>
/// Project file format versioning and migration.
///
/// When the schema changes in a breaking way:
///   1. Increment <see cref="Current"/>.
///   2. Add a migration step to <see cref="Migrations"/> that upgrades a document
///      from (Current - 1) to Current by editing the raw JSON tree.
/// Older files are then upgraded step by step before being deserialized.
/// </summary>
public static class ProjectFormatVersion
{
    /// <summary>The version written by this build of the application.</summary>
    public const int Current = 1;

    /// <summary>The oldest version this build can read.</summary>
    public const int Minimum = 1;

    /// <summary>
    /// Migration steps keyed by the version they upgrade FROM.
    /// Each step mutates the root JSON object in place; the version field is bumped by the caller.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, Action<JsonObject>> Migrations =
        new Dictionary<int, Action<JsonObject>>
        {
            // Example for the future:
            // [1] = root => { /* rename or add fields to go from v1 to v2 */ },
        };

    /// <summary>
    /// Upgrades <paramref name="root"/> to <see cref="Current"/>.
    /// Throws if the file is newer than this build or older than <see cref="Minimum"/>.
    /// </summary>
    public static void MigrateToCurrent(JsonObject root)
    {
        int version = root["version"]?.GetValue<int>()
            ?? throw new InvalidOperationException("Project file has no 'version' field.");

        if (version > Current)
        {
            throw new InvalidOperationException(
                $"Project file version {version} is newer than this application supports (version {Current}). " +
                "Please update the application.");
        }

        if (version < Minimum)
        {
            throw new InvalidOperationException(
                $"Project file version {version} is too old to open (minimum supported version is {Minimum}).");
        }

        while (version < Current)
        {
            if (!Migrations.TryGetValue(version, out var migrate))
                throw new InvalidOperationException($"No migration defined from project file version {version}.");

            migrate(root);
            version++;
            root["version"] = version;
        }
    }
}
