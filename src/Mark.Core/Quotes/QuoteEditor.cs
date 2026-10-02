using System.Text.RegularExpressions;
using Mark.Core.Design;
using Mark.Core.Models;

namespace Mark.Core.Quotes;

/// <summary>
/// Validated changes to a quote (project name, status, client, notes). Same pattern as <see cref="FrameEditor"/>:
/// <c>TryXxx</c> returns an <see cref="EditResult"/>, <c>Xxx</c> throws <see cref="DesignValidationException"/>; a
/// rejected change leaves the project untouched. Text is trimmed. The quote number is never edited here.
/// </summary>
public static partial class QuoteEditor
{
    public const int MaxNameLength = 120;
    public const int MaxFieldLength = 200;
    public const int MaxNotesLength = 4000;

    public static void SetQuote(Project project, string name, QuoteInfo quote) => TrySetQuote(project, name, quote).ThrowIfFailed();

    public static EditResult TrySetQuote(Project project, string name, QuoteInfo quote)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(quote);

        string cleanName = (name ?? "").Trim();
        if (cleanName.Length == 0)
            return EditResult.Fail("Enter a project name.");
        if (cleanName.Length > MaxNameLength)
            return EditResult.Fail($"The project name can be at most {MaxNameLength} characters.");
        if (!Enum.IsDefined(quote.Status))
            return EditResult.Fail("Unknown quote status.");

        var c = quote.Client;
        var client = new ClientInfo
        {
            Title = Clean(c.Title), FirstName = Clean(c.FirstName), LastName = Clean(c.LastName), Company = Clean(c.Company),
            Phone = Clean(c.Phone), Email = Clean(c.Email), AddressLine1 = Clean(c.AddressLine1),
            AddressLine2 = Clean(c.AddressLine2), City = Clean(c.City), State = Clean(c.State),
            PostalCode = Clean(c.PostalCode), Country = Clean(c.Country)
        };
        string notes = (quote.Notes ?? "").Trim();

        foreach (var (label, value) in new[]
                 {
                     ("Title", client.Title), ("First name", client.FirstName), ("Last name", client.LastName),
                     ("Company", client.Company), ("Phone", client.Phone), ("Email", client.Email),
                     ("Address", client.AddressLine1), ("Address line 2", client.AddressLine2), ("City", client.City),
                     ("State", client.State), ("Postal code", client.PostalCode), ("Country", client.Country)
                 })
        {
            if (value.Length > MaxFieldLength)
                return EditResult.Fail($"{label} can be at most {MaxFieldLength} characters.");
        }
        if (notes.Length > MaxNotesLength)
            return EditResult.Fail($"The notes can be at most {MaxNotesLength} characters.");
        if (client.Email.Length > 0 && !EmailPattern().IsMatch(client.Email))
            return EditResult.Fail("Enter a valid email address (like name@example.com), or leave it empty.");
        if (client.Phone.Length > 0 && !PhonePattern().IsMatch(client.Phone))
            return EditResult.Fail("A phone number may contain digits, spaces, +, - and brackets only.");

        project.Name = cleanName;
        project.Quote = new QuoteInfo
        {
            Number = project.Quote.Number,
            Status = quote.Status,
            Client = client,
            Notes = notes
        };
        return EditResult.Ok;

        static string Clean(string? s) => (s ?? "").Trim();
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[0-9+\-() ]{3,25}$")]
    private static partial Regex PhonePattern();
}

/// <summary>Size of a quote in designs, pieces and window area (each design counted as often as its quantity).</summary>
public readonly record struct QuoteTotals(int Designs, int Quantity, double AreaM2)
{
    public static QuoteTotals Of(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        int quantity = 0;
        double area = 0;
        foreach (var frame in project.Frames)
        {
            int q = Math.Max(1, frame.Design.Quantity);
            quantity += q;
            area += frame.Width * frame.Height / 1_000_000.0 * q;
        }
        return new QuoteTotals(project.Frames.Count, quantity, area);
    }
}
