namespace Mark.Core.Quotes;

/// <summary>How areas are shown on quotations: square feet or square metres.</summary>
public enum AreaUnit
{
    SquareFeet,
    SquareMetres
}

/// <summary>
/// What the company prints on its quotations: its details for the page header, the covering letter, terms and
/// conditions, bank details and the acceptance line, an optional brand (e.g. the profile system it sells) and an
/// optional last page. Set once in Sales › Quotation setup; every quotation PDF uses it.
/// </summary>
public sealed record QuotationSettings
{
    /// <summary>The company name when MARK runs without a licence (with a licence, the licence's name is used).</summary>
    public string CompanyName { get; init; } = "";

    /// <summary>A line above the company name, e.g. "Authorised partner" (empty: none).</summary>
    public string PartnerLabel { get; init; } = "";

    /// <summary>The address, as printed (one line or several).</summary>
    public string Address { get; init; } = "";

    public string Phone { get; init; } = "";
    public string Email { get; init; } = "";
    public string Website { get; init; } = "";
    public string Gstin { get; init; } = "";

    /// <summary>The brand at the top left of every page (e.g. the profile system's maker); empty: none.</summary>
    public string BrandName { get; init; } = "";

    /// <summary>The brand's logo (PNG or JPEG, base64), or null.</summary>
    public string? BrandLogoBase64 { get; init; }

    /// <summary>The covering letter after "Dear Customer,", one paragraph per line.</summary>
    public string Letter { get; init; } = DefaultLetter;

    /// <summary>Terms and conditions, one per line (numbered when printed).</summary>
    public string Terms { get; init; } = DefaultTerms;

    /// <summary>Cancellation policy, one point per line (empty: not printed).</summary>
    public string CancellationPolicy { get; init; } = "";

    /// <summary>What the warranty covers and does not, one point per line (empty: not printed).</summary>
    public string Warranty { get; init; } = "";

    /// <summary>What must be ready on site before the windows are installed, one point per line (empty: not printed).</summary>
    public string InstallationPrerequisites { get; init; } = "";

    public string BankAccountName { get; init; } = "";
    public string BankAccountNumber { get; init; } = "";
    public string BankName { get; init; } = "";
    public string BankIfsc { get; init; } = "";
    public string BankBranch { get; init; } = "";

    /// <summary>The client's acceptance sentence above the signatures.</summary>
    public string Acceptance { get; init; } = DefaultAcceptance;

    /// <summary>Printed under the quote total (empty: just "Notes :").</summary>
    public string Notes { get; init; } = "";

    public AreaUnit AreaUnit { get; init; } = AreaUnit.SquareFeet;

    /// <summary>The money label after amounts, e.g. "Rs.".</summary>
    public string CurrencyLabel { get; init; } = "Rs.";

    /// <summary>A last page with one picture (e.g. care instructions), base64, or null.</summary>
    public string? ExtraPageBase64 { get; init; }

    public const string DefaultLetter =
        "Thank you for considering us for the windows and doors of your premises.\n" +
        "We have prepared this proposal with designs that suit your openings, for comfort, safety and a fine look from inside and outside.\n" +
        "Each design below shows its drawing, specification and value, followed by our terms and conditions.\n" +
        "We look forward to being of service to you.";

    public const string DefaultTerms =
        "Prices are valid for 30 days from the date of this quotation.\n" +
        "Payment: 50% advance with the order and the balance before delivery.\n" +
        "Prices are based on the sizes given; the final value follows the sizes measured on site.\n" +
        "Delivery and installation as agreed when the order is confirmed.\n" +
        "Warranty on profiles, hardware and glass as per the manufacturers' terms.\n" +
        "Taxes as applicable at the time of billing.";

    public const string DefaultCancellationPolicy =
        "An order can be cancelled in writing within 3 days of the advance payment, without any charge.\n" +
        "Once fabrication has started, the cost of the material cut and the work done is kept from the advance.\n" +
        "Windows and doors are made to measure: once made, they cannot be cancelled or returned.\n" +
        "A change of sizes or design after the order is confirmed is treated as a new order and priced again.";

    public const string DefaultWarranty =
        "Profiles: as per the profile maker's warranty against defects in the material.\n" +
        "Hardware and accessories: as per their makers' warranty.\n" +
        "Workmanship and installation: one year from the date of installation.\n" +
        "Glass breakage is not covered by the warranty.\n" +
        "Not covered: damage from misuse, accidents, movement of the building, natural calamities, or repairs by others.";

    public const string DefaultInstallationPrerequisites =
        "The openings must be finished (plaster, sill and levels) and of the sizes measured for the order.\n" +
        "Electricity (220 V) and water must be available on site.\n" +
        "A safe, dry place must be given to store the windows delivered before installation.\n" +
        "Scaffolding, where needed for upper floors, is to be arranged by the customer.\n" +
        "Painting, polishing and other finishing near the openings should be done before installation, or the windows protected.\n" +
        "The site must be clear for our team to work during installation.";

    public const string DefaultAcceptance =
        "I accept this quotation with the prices and specifications above, and have read and agree to the terms and conditions.";

    /// <summary>The non-empty lines of a multi-line text.</summary>
    public static IReadOnlyList<string> LinesOf(string? text)
        => (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}
