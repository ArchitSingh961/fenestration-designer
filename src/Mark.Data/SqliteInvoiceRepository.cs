using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Core.Accounts;

namespace Mark.Data;

/// <summary>GST tax invoices (Milestone 19), stored whole as JSON. Numbers are given when an invoice is first saved.</summary>
public sealed class SqliteInvoiceRepository
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SqliteDatabase _database;
    private readonly Func<DateTime> _utcNow;

    public SqliteInvoiceRepository(SqliteDatabase database, Func<DateTime>? utcNow = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Every invoice, newest first.</summary>
    public IReadOnlyList<Invoice> List() => _database.Guard("read the invoices", () =>
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT invoice_json FROM invoices ORDER BY invoice_date DESC, number DESC";
        using var r = command.ExecuteReader();
        var list = new List<Invoice>();
        while (r.Read())
            if (Read(r.GetString(0)) is { } invoice) list.Add(invoice);
        return (IReadOnlyList<Invoice>)list.AsReadOnly();
    });

    /// <summary>
    /// Saves the invoice; a new one (no number) gets the next number of its series in the same transaction, so two
    /// invoices never share a number.
    /// </summary>
    public void Save(Invoice invoice, AccountsSettings settings) => _database.Guard($"save the invoice {invoice.Number}".Trim(), () =>
    {
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();
        bool isNew = string.IsNullOrWhiteSpace(invoice.Number);
        if (isNew)
        {
            using var numbers = connection.CreateCommand();
            numbers.Transaction = transaction;
            numbers.CommandText = "SELECT number FROM invoices";
            var used = new List<string>();
            using (var r = numbers.ExecuteReader())
                while (r.Read()) used.Add(r.GetString(0));
            invoice.Number = Invoice.NextNumber(settings.InvoicePrefix, settings.NumberByFinancialYear, invoice.Date, used);
            if (invoice.CreatedUtc == default) invoice.CreatedUtc = _utcNow();
        }
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO invoices (id, number, project_id, invoice_date, invoice_json) VALUES ($id, $number, $project, $date, $json)
                ON CONFLICT (id) DO UPDATE SET number = excluded.number, invoice_date = excluded.invoice_date, invoice_json = excluded.invoice_json
                """;
            command.Parameters.AddWithValue("$id", invoice.Id.ToString("D"));
            command.Parameters.AddWithValue("$number", invoice.Number);
            command.Parameters.AddWithValue("$project", invoice.ProjectId.ToString("D"));
            command.Parameters.AddWithValue("$date", invoice.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(invoice, Json));
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch
        {
            if (isNew) invoice.Number = "";
            throw;
        }
    });

    private static Invoice? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Invoice>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
