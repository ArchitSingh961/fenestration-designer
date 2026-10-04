using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Core.Quotes;
using Microsoft.Data.Sqlite;

namespace Mark.Data;

/// <summary>An enquiry as listed: number, client, stage, source, salesperson, value, follow-up and its quote.</summary>
public sealed record EnquirySummary(Guid Id, string Number, DateTime CreatedUtc, string CreatedBy, string ClientName, string City,
    string Phone, EnquiryStage Stage, string Source, string Owner, decimal? ExpectedValue, DateTime? FollowUp, Guid? QuoteId,
    string QuoteNumber);

/// <summary>
/// The company's enquiries (Milestone 15). Each is stored whole as JSON, with the columns the list and the sales charts
/// read. A new enquiry gets the next number ("EN-00012") when it is first saved.
/// </summary>
public sealed class SqliteEnquiryRepository
{
    public const string NumberPrefix = "EN-";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SqliteDatabase _database;
    private readonly Func<DateTime> _utcNow;

    public SqliteEnquiryRepository(SqliteDatabase database, Func<DateTime>? utcNow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Who saves from now on (recorded as the creator of new enquiries).</summary>
    public ProjectUser User { get; set; } = ProjectUser.Unknown;

    /// <summary>Inserts or replaces the enquiry; a new one gets its number and creation time (set on <paramref name="enquiry"/>).</summary>
    public void Save(Enquiry enquiry)
    {
        ArgumentNullException.ThrowIfNull(enquiry);
        string now = Time(_utcNow());
        string originalNumber = enquiry.Number;
        try
        {
            _database.Guard($"save the enquiry {enquiry.Client.DisplayName}".Trim(), () =>
            {
                using var connection = _database.Connect();
                using var transaction = connection.BeginTransaction();
                bool exists = Scalar(connection, transaction, "SELECT COUNT(*) FROM enquiries WHERE id = $id", ("$id", Key(enquiry.Id))) > 0;
                if (string.IsNullOrWhiteSpace(enquiry.Number))
                    enquiry.Number = SqliteProjectRepository.NextNumber(connection, transaction, "enquiries", "number", NumberPrefix);
                if (!exists)
                {
                    enquiry.CreatedUtc = _utcNow();
                    if (string.IsNullOrWhiteSpace(enquiry.CreatedBy)) enquiry.CreatedBy = User.DisplayName;
                }
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO enquiries (id, number, created_utc, modified_utc, created_by, stage, source, owner, client_name, client_city,
                        client_phone, expected_value, follow_up, quote_id, document_json)
                    VALUES ($id, $number, $created, $now, $by, $stage, $source, $owner, $client, $city, $phone, $value, $follow, $quote, $doc)
                    ON CONFLICT (id) DO UPDATE SET number = excluded.number, modified_utc = excluded.modified_utc, stage = excluded.stage,
                        source = excluded.source, owner = excluded.owner, client_name = excluded.client_name, client_city = excluded.client_city,
                        client_phone = excluded.client_phone, expected_value = excluded.expected_value, follow_up = excluded.follow_up,
                        quote_id = excluded.quote_id, document_json = excluded.document_json
                    """;
                foreach (var (name, value) in new (string, object?)[]
                         {
                             ("$id", Key(enquiry.Id)), ("$number", enquiry.Number), ("$created", Time(enquiry.CreatedUtc)), ("$now", now),
                             ("$by", enquiry.CreatedBy), ("$stage", enquiry.Stage.ToString()), ("$source", enquiry.Source.Trim()),
                             ("$owner", enquiry.Owner.Trim()), ("$client", enquiry.Client.DisplayName), ("$city", enquiry.Client.City.Trim()),
                             ("$phone", enquiry.Client.Phone.Trim()),
                             ("$value", enquiry.ExpectedValue?.ToString(CultureInfo.InvariantCulture)),
                             ("$follow", enquiry.FollowUpDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                             ("$quote", enquiry.QuoteId is { } q ? Key(q) : null), ("$doc", JsonSerializer.Serialize(enquiry, Json))
                         })
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                command.ExecuteNonQuery();
                transaction.Commit();
            });
        }
        catch
        {
            enquiry.Number = originalNumber;
            throw;
        }
    }

    /// <exception cref="DataStoreException">No such enquiry, or its data is damaged.</exception>
    public Enquiry Load(Guid id)
    {
        string json = _database.Guard("open the enquiry", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT document_json FROM enquiries WHERE id = $id";
            command.Parameters.AddWithValue("$id", Key(id));
            return command.ExecuteScalar() as string ?? throw new DataStoreException("The enquiry is not in the database (it may have been deleted).");
        });
        try
        {
            return JsonSerializer.Deserialize<Enquiry>(json, Json) ?? throw new JsonException("empty");
        }
        catch (JsonException ex)
        {
            throw new DataStoreException($"The saved enquiry is damaged and cannot be opened: {ex.Message}", ex);
        }
    }

    /// <summary>All enquiries, newest first, with the number of the quote made from each.</summary>
    public IReadOnlyList<EnquirySummary> List() => _database.Guard("read the enquiries", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id, e.number, e.created_utc, e.created_by, e.client_name, e.client_city, e.client_phone, e.stage, e.source, e.owner,
                e.expected_value, e.follow_up, e.quote_id, COALESCE(p.quote_number, '')
            FROM enquiries e LEFT JOIN projects p ON p.id = e.quote_id
            ORDER BY e.created_utc DESC, e.number DESC
            """;
        using var r = command.ExecuteReader();
        var list = new List<EnquirySummary>();
        while (r.Read())
            list.Add(new EnquirySummary(Guid.Parse(r.GetString(0)), r.GetString(1), ParseTime(r.GetString(2)), r.GetString(3), r.GetString(4),
                r.GetString(5), r.GetString(6), Enum.TryParse<EnquiryStage>(r.GetString(7), out var stage) ? stage : EnquiryStage.New,
                r.GetString(8), r.GetString(9),
                r.IsDBNull(10) ? null : decimal.Parse(r.GetString(10), NumberStyles.Number, CultureInfo.InvariantCulture),
                r.IsDBNull(11) ? null : DateTime.ParseExact(r.GetString(11), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.IsDBNull(12) ? null : Guid.Parse(r.GetString(12)), r.GetString(13)));
        return (IReadOnlyList<EnquirySummary>)list.AsReadOnly();
    });

    /// <summary>The enquiry a quote was made from, or null.</summary>
    public Enquiry? ForQuote(Guid quoteId)
    {
        string? id = _database.Guard("read the enquiries", () =>
        {
            using var connection = _database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM enquiries WHERE quote_id = $id LIMIT 1";
            command.Parameters.AddWithValue("$id", Key(quoteId));
            return command.ExecuteScalar() as string;
        });
        return id is null ? null : Load(Guid.Parse(id));
    }

    /// <exception cref="DataStoreException">No such enquiry.</exception>
    public void Delete(Guid id) => _database.Guard("delete the enquiry", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM enquiries WHERE id = $id";
        command.Parameters.AddWithValue("$id", Key(id));
        if (command.ExecuteNonQuery() == 0) throw new DataStoreException("The enquiry is not in the database (it may have been deleted).");
    });

    private static long Scalar(SqliteConnection c, SqliteTransaction t, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Key(Guid id) => id.ToString("D");

    private static string Time(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTime(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
