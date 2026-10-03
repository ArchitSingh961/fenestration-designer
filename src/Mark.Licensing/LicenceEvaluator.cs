using System.Globalization;

namespace Mark.Licensing;

/// <summary>Full: everything in the licence works. ReadOnly: quotes can be opened, viewed and exported, but not saved.</summary>
public enum LicenceMode
{
    Full,
    ReadOnly
}

/// <summary>What a licence allows right now (see <see cref="LicenceEvaluator"/>).</summary>
public sealed record LicenceStatus
{
    public required Licence Licence { get; init; }

    public LicenceMode Mode { get; init; }

    /// <summary>Why MARK is read-only (null when <see cref="LicenceMode.Full"/>).</summary>
    public string? Reason { get; init; }

    /// <summary>Something the user should know soon, e.g. "Your licence ends on 12 Oct 2026 (9 days)."</summary>
    public string? Warning { get; init; }

    /// <summary>Features that work now. In read-only mode: every feature of the licence, for viewing.</summary>
    public IReadOnlySet<string> Features { get; init; } = new HashSet<string>();

    /// <summary>Product lines that are valid now.</summary>
    public IReadOnlySet<Product> Products { get; init; } = new HashSet<Product>();

    public bool IsReadOnly => Mode == LicenceMode.ReadOnly;

    public bool Allows(string featureId) => Features.Contains(featureId);
}

/// <summary>
/// Decides what a verified licence allows at a given time. Read-only when: the clock was moved back, the account is
/// suspended or expired, no product is valid, or MARK has not reached the server for longer than the offline grace
/// period (counted from <see cref="Licence.IssuedUtc"/>, which is signed, so it cannot be moved).
/// </summary>
public static class LicenceEvaluator
{
    /// <summary>The computer clock may be this far behind the last time MARK saw before it counts as moved back.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(2);

    /// <summary>Warn this many days before something ends.</summary>
    public const int WarnDays = 14;

    /// <param name="nowUtc">The computer's clock.</param>
    /// <param name="lastSeenUtc">The latest time MARK has seen on this computer (to notice a clock moved back).</param>
    public static LicenceStatus Evaluate(Licence licence, DateTime nowUtc, DateTime? lastSeenUtc = null)
    {
        ArgumentNullException.ThrowIfNull(licence);
        var validProducts = licence.Products.Where(p => p.ValidUntilUtc >= nowUtc).Select(p => p.Product).ToHashSet();

        string? reason = ReadOnlyReason(licence, nowUtc, lastSeenUtc, validProducts);
        if (reason is not null)
        {
            return new LicenceStatus
            {
                Licence = licence,
                Mode = LicenceMode.ReadOnly,
                Reason = reason,
                Features = licence.Features.Select(f => f.FeatureId).ToHashSet(),
                Products = licence.Products.Select(p => p.Product).ToHashSet()
            };
        }

        return new LicenceStatus
        {
            Licence = licence,
            Mode = LicenceMode.Full,
            Warning = Warning(licence, nowUtc),
            Features = licence.Features.Where(f => f.ValidUntilUtc >= nowUtc).Select(f => f.FeatureId).ToHashSet(),
            Products = validProducts
        };
    }

    private static string? ReadOnlyReason(Licence licence, DateTime nowUtc, DateTime? lastSeenUtc, IReadOnlySet<Product> validProducts)
    {
        if (nowUtc < licence.IssuedUtc - ClockTolerance || (lastSeenUtc is { } seen && nowUtc < seen - ClockTolerance))
            return "The date on this computer is earlier than it was before. Correct the computer's date and time, " +
                   "then connect to the internet so MARK can check your licence.";
        if (licence.Suspended)
            return "Your MARK account is suspended. Your quotes can be viewed but not changed. Contact your MARK supplier.";
        if (nowUtc > licence.ValidUntilUtc)
            return $"Your MARK licence ended on {LicenceDates.Format(licence.ValidUntilUtc)}. Your quotes can be viewed but " +
                   "not changed. Contact your MARK supplier to renew, or enter a licence key.";
        if (validProducts.Count == 0)
            return licence.Products.Count == 0
                ? "Your licence has no product (uPVC or Aluminium). Contact your MARK supplier."
                : $"Your {string.Join(" and ", licence.Products.Select(p => p.Product.DisplayName()))} licence has ended. " +
                  "Contact your MARK supplier to renew, or enter a licence key.";
        if (nowUtc > licence.IssuedUtc.AddDays(licence.OfflineGraceDays))
            return $"MARK has not been able to check your licence for more than {licence.OfflineGraceDays} days. " +
                   "Connect this computer to the internet and choose Check now.";
        return null;
    }

    private static string? Warning(Licence licence, DateTime nowUtc)
    {
        int daysLeft = DaysLeft(licence.ValidUntilUtc, nowUtc);
        if (daysLeft <= WarnDays)
            return $"Your MARK licence ends on {LicenceDates.Format(licence.ValidUntilUtc)} ({Days(daysLeft)}).";

        foreach (var product in licence.Products.Where(p => p.ValidUntilUtc >= nowUtc).OrderBy(p => p.ValidUntilUtc))
        {
            int productDays = DaysLeft(product.ValidUntilUtc, nowUtc);
            if (productDays <= WarnDays)
                return $"Your {product.Product.DisplayName()} licence ends on {LicenceDates.Format(product.ValidUntilUtc)} ({Days(productDays)}).";
        }

        var offline = nowUtc - licence.IssuedUtc;
        if (offline > TimeSpan.FromDays(2))
        {
            int left = Math.Max(0, (int)Math.Ceiling((licence.IssuedUtc.AddDays(licence.OfflineGraceDays) - nowUtc).TotalDays));
            return $"MARK has not reached the licence server for {(int)offline.TotalDays} days. Connect to the internet " +
                   $"within {Days(left)} to keep working.";
        }
        return null;
    }

    private static int DaysLeft(DateTime untilUtc, DateTime nowUtc) => Math.Max(0, (int)Math.Ceiling((untilUtc - nowUtc).TotalDays));

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";
}

/// <summary>Licence dates as shown to people: the local date, e.g. "3 Oct 2027".</summary>
public static class LicenceDates
{
    public static string Format(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The end of a local calendar day as UTC: a licence "valid until 3 Oct 2027" works all of that day.</summary>
    public static DateTime EndOfLocalDayUtc(DateTime localDate)
        => DateTime.SpecifyKind(localDate.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Local).ToUniversalTime();

    /// <summary>The local calendar day a UTC end time falls on (inverse of <see cref="EndOfLocalDayUtc"/>).</summary>
    public static DateTime LocalDate(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().Date;
}
