using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mark.Core.Models;
using Mark.Core.Utilities;

namespace Mark.Core.Serialization;

/// <summary>
/// Handles JSON serialization and deserialization of <see cref="ProjectFile"/>.
/// Uses System.Text.Json with custom converters for geometry types.
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Derived geometry (Length, Angle, Bounds, Dimension.Value, ...) is get-only and must
            // never be persisted — it is recomputed from the stored source geometry.
            IgnoreReadOnlyProperties = true
        };
        options.Converters.Add(new Point2DJsonConverter());
        options.Converters.Add(new Rectangle2DJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Serializes a project to a JSON string.</summary>
    public static string Serialize(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var file = new ProjectFile
        {
            Version = ProjectFormatVersion.Current,
            Project = project
        };
        return JsonSerializer.Serialize(file, SerializerOptions);
    }

    /// <summary>Deserializes a project from a JSON string, migrating older formats first.</summary>
    /// <exception cref="InvalidOperationException">Thrown if the file version is unsupported or the content is invalid.</exception>
    public static Project Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("Project file root must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Project file is not valid JSON.", ex);
        }

        ProjectFormatVersion.MigrateToCurrent(root);

        try
        {
            var file = root.Deserialize<ProjectFile>(SerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize project file.");

            // Geometry types validate themselves (e.g. Rectangle2D rejects negative sizes);
            // this catches the rest, such as zero-length profiles or out-of-range frame sizes.
            ValidationHelper.EnsureValidProject(file.Project);
            return file.Project;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"Project file contains invalid data: {ex.Message}", ex);
        }
    }

    /// <summary>Saves a project to a file on disk.</summary>
    public static async Task SaveAsync(Project project, string filePath)
    {
        string json = Serialize(project);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>Loads a project from a file on disk.</summary>
    public static async Task<Project> LoadAsync(string filePath)
    {
        string json = await File.ReadAllTextAsync(filePath);
        return Deserialize(json);
    }

    /// <summary>
    /// Returns a deep copy of <paramref name="project"/> that preserves every Id.
    /// Used to hand an isolated snapshot to consumers such as the calculation engine.
    /// </summary>
    public static Project Snapshot(Project project) => Deserialize(Serialize(project));
}
