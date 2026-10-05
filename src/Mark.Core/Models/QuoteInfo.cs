namespace Mark.Core.Models;

/// <summary>Where a quote stands with the client.</summary>
public enum QuoteStatus
{
    /// <summary>Being prepared or waiting for the client's answer.</summary>
    Active,

    /// <summary>The client accepted it.</summary>
    Won,

    /// <summary>The client declined it.</summary>
    Lost
}

/// <summary>The client a quote is for, and the site the windows go to. Plain text; checked by <see cref="Quotes.QuoteEditor"/>.</summary>
public sealed class ClientInfo
{
    /// <summary>Salutation, e.g. "Mr.", "Ms.", "Dr.", "M/s." (for a company).</summary>
    public string Title { get; set; } = "";

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Company { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>The client's GSTIN (15 characters) for a business client's tax invoice; empty for a consumer.</summary>
    public string Gstin { get; set; } = "";

    // Site address.
    public string AddressLine1 { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";

    /// <summary>"Mr. Archit Singh", or the company when there is no person; empty if nothing is entered.</summary>
    public string DisplayName
    {
        get
        {
            string person = string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (person.Length == 0) return Company.Trim();
            return string.IsNullOrWhiteSpace(Title) ? person : $"{Title.Trim()} {person}";
        }
    }

    /// <summary>The address on one line ("12 Park Road, Jaipur 302001, Rajasthan").</summary>
    public string AddressText
    {
        get
        {
            string cityLine = string.Join(" ", new[] { City, PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.Join(", ", new[] { AddressLine1, AddressLine2, cityLine, State, Country }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        }
    }

    public ClientInfo Copy() => (ClientInfo)MemberwiseClone();
}

/// <summary>
/// The quote a project is: its number, status, client and notes. The designs (window types) of the quote are the
/// project's frames, each with its <see cref="DesignInfo"/> (reference, quantity).
/// </summary>
public sealed class QuoteInfo
{
    /// <summary>Quote number, e.g. "QT-00012". Given by the database on first save; empty until then.</summary>
    public string Number { get; set; } = "";

    public QuoteStatus Status { get; set; } = QuoteStatus.Active;

    public ClientInfo Client
    {
        get => _client;
        set => _client = value ?? new ClientInfo();
    }

    private ClientInfo _client = new();

    /// <summary>Free text: the client's requirements, site conditions, follow-ups.</summary>
    public string Notes { get; set; } = "";

    /// <summary>The revision: 0 for the first quote, 1 for "R1" and so on (earlier revisions are kept by the database).</summary>
    public int Revision { get; set; }

    /// <summary>The order number ("OR-00003") once the quote became an order; empty until then.</summary>
    public string OrderNumber { get; set; } = "";

    /// <summary>When the quote was converted to an order (UTC), or null.</summary>
    public DateTime? OrderedUtc { get; set; }

    /// <summary>The enquiry the quote was made from, or null.</summary>
    public Guid? EnquiryId { get; set; }

    /// <summary>"QT-00012 R1": the number with its revision.</summary>
    public string NumberText => Revision > 0 && Number.Length > 0 ? $"{Number} R{Revision}" : Number;

    public QuoteInfo Copy()
    {
        var copy = (QuoteInfo)MemberwiseClone();
        copy._client = _client.Copy();
        return copy;
    }
}
