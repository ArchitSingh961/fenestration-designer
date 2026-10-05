using System.Globalization;
using System.Text;

namespace Mark.Core.Accounts;

/// <summary>
/// Indian GST basics for invoices: GSTIN checks (format and check character), the state codes, whether a sale is within
/// the state (CGST + SGST) or between states (IGST), and an amount in words ("Rupees Eleven Thousand Eight Hundred Only").
/// </summary>
public static class Gst
{
    private const string Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>The GST state codes and names.</summary>
    public static IReadOnlyDictionary<string, string> States { get; } = new Dictionary<string, string>
    {
        ["01"] = "Jammu and Kashmir", ["02"] = "Himachal Pradesh", ["03"] = "Punjab", ["04"] = "Chandigarh",
        ["05"] = "Uttarakhand", ["06"] = "Haryana", ["07"] = "Delhi", ["08"] = "Rajasthan", ["09"] = "Uttar Pradesh",
        ["10"] = "Bihar", ["11"] = "Sikkim", ["12"] = "Arunachal Pradesh", ["13"] = "Nagaland", ["14"] = "Manipur",
        ["15"] = "Mizoram", ["16"] = "Tripura", ["17"] = "Meghalaya", ["18"] = "Assam", ["19"] = "West Bengal",
        ["20"] = "Jharkhand", ["21"] = "Odisha", ["22"] = "Chhattisgarh", ["23"] = "Madhya Pradesh", ["24"] = "Gujarat",
        ["26"] = "Dadra and Nagar Haveli and Daman and Diu", ["27"] = "Maharashtra", ["29"] = "Karnataka", ["30"] = "Goa",
        ["31"] = "Lakshadweep", ["32"] = "Kerala", ["33"] = "Tamil Nadu", ["34"] = "Puducherry",
        ["35"] = "Andaman and Nicobar Islands", ["36"] = "Telangana", ["37"] = "Andhra Pradesh", ["38"] = "Ladakh"
    };

    /// <summary>A well-formed GSTIN: 2-digit state code, PAN (5 letters, 4 digits, 1 letter), entity number, Z, check character.</summary>
    public static bool IsGstin(string? gstin)
    {
        string g = (gstin ?? "").Trim().ToUpperInvariant();
        if (g.Length != 15 || !States.ContainsKey(g[..2])) return false;
        for (int i = 2; i < 7; i++) if (!char.IsAsciiLetterUpper(g[i])) return false;
        for (int i = 7; i < 11; i++) if (!char.IsAsciiDigit(g[i])) return false;
        if (!char.IsAsciiLetterUpper(g[11]) || Characters.IndexOf(g[12]) < 1 || g[13] != 'Z') return false;
        return g[14] == CheckCharacter(g[..14]);
    }

    /// <summary>The check character of the first 14 characters of a GSTIN (alternating weights 1, 2 in base 36).</summary>
    public static char CheckCharacter(string first14)
    {
        int sum = 0;
        for (int i = 0; i < 14; i++)
        {
            int value = Characters.IndexOf(char.ToUpperInvariant(first14[i]));
            int product = value * (i % 2 == 0 ? 1 : 2);
            sum += product / 36 + product % 36;
        }
        return Characters[(36 - sum % 36) % 36];
    }

    /// <summary>The state code of a GSTIN ("08"), or null.</summary>
    public static string? StateCodeOfGstin(string? gstin)
    {
        string g = (gstin ?? "").Trim();
        return g.Length >= 2 && States.ContainsKey(g[..2]) ? g[..2] : null;
    }

    /// <summary>The state code of a state name ("Rajasthan" → "08"; also "RJ"-free names like "delhi"), or null.</summary>
    public static string? StateCodeOfName(string? state)
    {
        string s = (state ?? "").Trim();
        if (s.Length == 0) return null;
        if (s.Length == 2 && States.ContainsKey(s)) return s;
        string Plain(string t) => new(t.ToLowerInvariant().Where(char.IsLetter).ToArray());
        string wanted = Plain(s.Replace("&", "and"));
        return States.FirstOrDefault(p => Plain(p.Value) == wanted || wanted == "newdelhi" && p.Key == "07" || wanted == "orissa" && p.Key == "21").Key;
    }

    /// <summary>"Rajasthan (08)".</summary>
    public static string StateText(string? code) => code is not null && States.TryGetValue(code, out string? name) ? $"{name} ({code})" : "";

    /// <summary>"Rupees Eleven Thousand Eight Hundred and Fifty Paise Only" (Indian system: thousand, lakh, crore).</summary>
    public static string AmountInWords(decimal amount)
    {
        amount = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        long rupees = (long)Math.Floor(amount);
        int paise = (int)((amount - rupees) * 100);
        var text = new StringBuilder("Rupees ");
        text.Append(rupees == 0 ? "Zero" : Words(rupees));
        if (paise > 0) text.Append(" and ").Append(Words(paise)).Append(" Paise");
        return text.Append(" Only").ToString();
    }

    private static readonly string[] Ones =
    {
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen",
        "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    };

    private static readonly string[] Tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

    private static string Words(long n)
    {
        var parts = new List<string>();
        void Add(long value, string unit)
        {
            if (value > 0) parts.Add(Below1000((int)value) + (unit.Length > 0 ? " " + unit : ""));
        }
        long crore = n / 10_000_000;
        if (crore > 0) parts.Add(Words(crore) + " Crore");
        n %= 10_000_000;
        Add(n / 100_000, "Lakh");
        n %= 100_000;
        Add(n / 1000, "Thousand");
        n %= 1000;
        Add(n, "");
        return string.Join(" ", parts);
    }

    private static string Below1000(int n)
    {
        var parts = new List<string>();
        if (n >= 100)
        {
            parts.Add(Ones[n / 100] + " Hundred");
            n %= 100;
        }
        if (n >= 20)
        {
            parts.Add(n % 10 == 0 ? Tens[n / 10] : $"{Tens[n / 10]} {Ones[n % 10]}");
        }
        else if (n > 0)
        {
            parts.Add(Ones[n]);
        }
        return string.Join(" ", parts);
    }

    /// <summary>"2026-27": the Indian financial year (April to March) of a date.</summary>
    public static string FinancialYear(DateTime date)
    {
        int start = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{start}-{((start + 1) % 100).ToString("D2", CultureInfo.InvariantCulture)}";
    }
}
