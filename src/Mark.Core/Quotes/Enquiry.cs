using Mark.Core.Models;

namespace Mark.Core.Quotes;

/// <summary>Where an enquiry stands: new, contacted, site visit, quoted, then won or lost.</summary>
public enum EnquiryStage
{
    New,
    Contacted,
    SiteVisit,
    Quoted,
    Won,
    Lost
}

/// <summary>
/// A sales enquiry (Milestone 15): who asked (the client and site), where it came from, who looks after it, what it may
/// be worth and when to follow up. It becomes a quote ("Create quote"), and the quote's result (won, lost) is its result.
/// </summary>
public sealed class Enquiry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>"EN-00012", given by the database on first save.</summary>
    public string Number { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    public string CreatedBy { get; set; } = "";

    public ClientInfo Client
    {
        get => _client;
        set => _client = value ?? new ClientInfo();
    }
    private ClientInfo _client = new();

    public EnquiryStage Stage { get; set; } = EnquiryStage.New;

    /// <summary>Where it came from, e.g. "Referral" (see <see cref="Sources"/>).</summary>
    public string Source { get; set; } = "";

    /// <summary>The salesperson who looks after it.</summary>
    public string Owner { get; set; } = "";

    public decimal? ExpectedValue { get; set; }

    /// <summary>The local day the client expects to decide.</summary>
    public DateTime? ExpectedCloseDate { get; set; }

    /// <summary>The local day of the next follow-up.</summary>
    public DateTime? FollowUpDate { get; set; }

    /// <summary>What the client needs: rooms, window types, budget, timing.</summary>
    public string Requirements { get; set; } = "";

    public string LostReason { get; set; } = "";

    /// <summary>The quote made from it, or null.</summary>
    public Guid? QuoteId { get; set; }

    /// <summary>The usual sources of enquiries (free text is allowed too).</summary>
    public static IReadOnlyList<string> Sources { get; } = new[]
    {
        "Walk-in", "Phone call", "Website", "Referral", "Architect", "Builder", "Dealer", "Exhibition", "Social media", "Other"
    };

    /// <summary>"Site visit" for <see cref="EnquiryStage.SiteVisit"/>, the name otherwise.</summary>
    public static string StageName(EnquiryStage stage) => stage == EnquiryStage.SiteVisit ? "Site visit" : stage.ToString();

    public Enquiry Copy()
    {
        var copy = (Enquiry)MemberwiseClone();
        copy._client = _client.Copy();
        return copy;
    }
}
