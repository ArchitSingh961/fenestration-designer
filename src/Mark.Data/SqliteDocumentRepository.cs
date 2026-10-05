using System.Globalization;
using Mark.Core.Quotes;

namespace Mark.Data;

/// <summary>
/// Files kept with each quote (its Documents tab): uploads such as a site survey or a signed contract, and the quotation
/// and margin PDFs MARK writes. The file is stored in the database with its details, so it travels with the database.
/// </summary>
public sealed class SqliteDocumentRepository
{
    private readonly SqliteDatabase _database;

    public SqliteDocumentRepository(SqliteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>Keeps <paramref name="content"/> as <paramref name="document"/> (its size is taken from the content).</summary>
    /// <exception cref="DataStoreException">Too large, or the database cannot be written.</exception>
    public ProjectDocument Add(ProjectDocument document, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(content);
        if (content.LongLength > ProjectDocument.MaxSize)
            throw new DataStoreException($"'{document.FileName}' is {ProjectDocument.SizeText(content.LongLength)}; a document can be at most {ProjectDocument.SizeText(ProjectDocument.MaxSize)}.");
        var saved = document with { Size = content.LongLength };
        return _database.Guard($"keep '{document.FileName}'", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO project_documents (id, project_id, category, name, file_name, added_utc, added_by, size, note, content)
                VALUES ($id, $project, $category, $name, $file, $added, $by, $size, $note, $content)
                """;
            command.Parameters.AddWithValue("$id", Key(saved.Id));
            command.Parameters.AddWithValue("$project", Key(saved.ProjectId));
            command.Parameters.AddWithValue("$category", saved.Category.ToString());
            command.Parameters.AddWithValue("$name", saved.Name);
            command.Parameters.AddWithValue("$file", saved.FileName);
            command.Parameters.AddWithValue("$added", DateTime.SpecifyKind(saved.AddedUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$by", saved.AddedBy);
            command.Parameters.AddWithValue("$size", saved.Size);
            command.Parameters.AddWithValue("$note", saved.Note);
            command.Parameters.AddWithValue("$content", content);
            command.ExecuteNonQuery();
            return saved;
        });
    }

    /// <summary>The documents of a quote (without their files), newest first.</summary>
    public IReadOnlyList<ProjectDocument> ForProject(Guid projectId) => _database.Guard("read the quote's documents", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, project_id, category, name, file_name, added_utc, added_by, size, note
            FROM project_documents WHERE project_id = $project ORDER BY added_utc DESC
            """;
        command.Parameters.AddWithValue("$project", Key(projectId));
        using var r = command.ExecuteReader();
        var list = new List<ProjectDocument>();
        while (r.Read())
        {
            if (!Enum.TryParse(r.GetString(2), out DocumentCategory category)) category = DocumentCategory.Others;
            list.Add(new ProjectDocument(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), category, r.GetString(3),
                r.GetString(4), DateTime.Parse(r.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                r.GetString(6), r.GetInt64(7), r.GetString(8)));
        }
        return (IReadOnlyList<ProjectDocument>)list.AsReadOnly();
    });

    /// <summary>How many documents a quote has in each category.</summary>
    public IReadOnlyDictionary<DocumentCategory, int> Counts(Guid projectId)
        => ForProject(projectId).GroupBy(d => d.Category).ToDictionary(g => g.Key, g => g.Count());

    /// <exception cref="DataStoreException">The document is not there any more.</exception>
    public byte[] Content(Guid id) => _database.Guard("open the document", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM project_documents WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        return command.ExecuteScalar() as byte[] ?? throw new DataStoreException("The document is not in the database (it may have been deleted).");
    });

    public void Delete(Guid id) => _database.Guard("delete the document", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM project_documents WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        command.ExecuteNonQuery();
    });

    private static string Key(Guid id) => id.ToString("D");
}
