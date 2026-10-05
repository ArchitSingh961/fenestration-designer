using System.Globalization;
using System.Security;
using System.Text;

namespace Mark.Core.Accounts;

/// <summary>A payment received, as the accounts see it.</summary>
public sealed record ReceiptEntry(DateTime Date, string Number, string Client, string OrderNumber, decimal Amount, string Mode,
    string Reference, bool IsCash);

/// <summary>
/// The accounts leave MARK as files: registers for Excel (CSV) — invoices, the HSN summary for GST returns, receipts — and
/// a Tally import file (XML) with the client ledgers, a sales voucher per invoice and a receipt voucher per payment.
/// MARK keeps no books itself.
/// </summary>
public static class AccountsExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string InvoicesCsv(IEnumerable<Invoice> invoices)
    {
        var csv = new StringBuilder();
        Row(csv, "Invoice No", "Date", "Client", "Client GSTIN", "Place of supply", "Type", "Order", "Taxable value", "CGST", "SGST",
            "IGST", "Round off", "Invoice total", "Status");
        foreach (var i in invoices.OrderBy(i => i.Date).ThenBy(i => i.Number))
            Row(csv, i.Number, i.Date.ToString("dd-MM-yyyy", Inv), i.ClientName, i.ClientGstin, Gst.StateText(i.PlaceOfSupply),
                i.IsB2B ? "B2B" : "B2C", i.OrderNumber, Money(i.IsCancelled ? 0 : i.Taxable), Money(i.IsCancelled ? 0 : i.Cgst),
                Money(i.IsCancelled ? 0 : i.Sgst), Money(i.IsCancelled ? 0 : i.Igst), Money(i.IsCancelled ? 0 : i.RoundOff),
                Money(i.IsCancelled ? 0 : i.Total), i.IsCancelled ? "Cancelled" : "");
        return csv.ToString();
    }

    /// <summary>Taxable value and tax by HSN/SAC code (for the GST return), cancelled invoices left out.</summary>
    public static string HsnSummaryCsv(IEnumerable<Invoice> invoices)
    {
        var csv = new StringBuilder();
        Row(csv, "HSN/SAC", "Description", "Unit", "Quantity", "Taxable value", "Rate %", "CGST", "SGST", "IGST");
        var rows = invoices.Where(i => !i.IsCancelled)
            .SelectMany(i => i.Lines.Select(l => (Invoice: i, Line: l)))
            .GroupBy(x => (x.Line.Hsn, x.Line.Unit, x.Invoice.TaxPercent))
            .OrderBy(g => g.Key.Hsn);
        foreach (var g in rows)
        {
            decimal taxable = g.Sum(x => x.Line.Taxable);
            decimal cgst = g.Where(x => x.Invoice.IsIntraState).Sum(x => Math.Round(x.Line.Taxable * x.Invoice.TaxPercent / 200m, 2));
            decimal igst = g.Where(x => !x.Invoice.IsIntraState).Sum(x => Math.Round(x.Line.Taxable * x.Invoice.TaxPercent / 100m, 2));
            Row(csv, g.Key.Hsn, string.Join("; ", g.Select(x => x.Line.Description.Split(" · ")[0]).Distinct().Take(3)), g.Key.Unit,
                g.Sum(x => x.Line.Quantity).ToString("0.##", Inv), Money(taxable), g.Key.TaxPercent.ToString("0.##", Inv),
                Money(cgst), Money(cgst), Money(igst));
        }
        return csv.ToString();
    }

    public static string ReceiptsCsv(IEnumerable<ReceiptEntry> receipts)
    {
        var csv = new StringBuilder();
        Row(csv, "Date", "Receipt No", "Client", "Order", "Amount", "Mode", "Reference");
        foreach (var r in receipts.OrderBy(r => r.Date))
            Row(csv, r.Date.ToString("dd-MM-yyyy", Inv), r.Number, r.Client, r.OrderNumber, Money(r.Amount), r.Mode, r.Reference);
        return csv.ToString();
    }

    /// <summary>
    /// A Tally import file: the client ledgers (under the debtors group), a Sales voucher per invoice (client debited with
    /// the total; sales, output GST and round off credited) and a Receipt voucher per payment (cash or bank debited, client
    /// credited). In Tally: Gateway › Import › Transactions. Ledger names come from <paramref name="settings"/>.
    /// </summary>
    public static string TallyXml(IEnumerable<Invoice> invoices, IEnumerable<ReceiptEntry> receipts, AccountsSettings settings)
    {
        var sales = invoices.Where(i => !i.IsCancelled).OrderBy(i => i.Date).ToList();
        var paid = receipts.OrderBy(r => r.Date).ToList();
        var xml = new StringBuilder();
        xml.AppendLine("<ENVELOPE>");
        xml.AppendLine(" <HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER>");
        xml.AppendLine(" <BODY>");
        xml.AppendLine("  <IMPORTDATA>");
        xml.Append("   <REQUESTDESC><REPORTNAME>All Masters</REPORTNAME>");
        if (settings.TallyCompany.Trim().Length > 0)
            xml.Append($"<STATICVARIABLES><SVCURRENTCOMPANY>{X(settings.TallyCompany)}</SVCURRENTCOMPANY></STATICVARIABLES>");
        xml.AppendLine("</REQUESTDESC>");
        xml.AppendLine("   <REQUESTDATA>");

        foreach (var party in sales.Select(i => (i.ClientName, i.ClientGstin, i.PlaceOfSupply)).Concat(paid.Select(r => (r.Client, "", "")))
                     .Where(p => p.Item1.Trim().Length > 0).GroupBy(p => p.Item1.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            xml.AppendLine("    <TALLYMESSAGE xmlns:UDF=\"TallyUDF\">");
            xml.AppendLine($"     <LEDGER NAME=\"{X(party.Item1.Trim())}\" ACTION=\"Create\">");
            xml.AppendLine($"      <NAME>{X(party.Item1.Trim())}</NAME>");
            xml.AppendLine($"      <PARENT>{X(settings.DebtorsGroup)}</PARENT>");
            if (party.Item2.Length > 0) xml.AppendLine($"      <PARTYGSTIN>{X(party.Item2)}</PARTYGSTIN>");
            if (party.Item3.Length > 0 && Gst.States.TryGetValue(party.Item3, out string? state)) xml.AppendLine($"      <LEDSTATENAME>{X(state)}</LEDSTATENAME>");
            xml.AppendLine("      <ISBILLWISEON>Yes</ISBILLWISEON>");
            xml.AppendLine("     </LEDGER>");
            xml.AppendLine("    </TALLYMESSAGE>");
        }

        foreach (var i in sales)
        {
            Voucher(xml, "Sales", i.Date, i.Number, i.ClientName, $"{i.OrderNumber} {i.ProjectName}".Trim(), new List<(string, decimal)>
            {
                (i.ClientName, -i.Total),
                (settings.SalesLedger, i.Taxable),
                (settings.CgstLedger, i.Cgst),
                (settings.SgstLedger, i.Sgst),
                (settings.IgstLedger, i.Igst),
                (settings.RoundOffLedger, i.RoundOff)
            });
        }
        foreach (var r in paid)
        {
            Voucher(xml, "Receipt", r.Date, r.Number, r.Client, $"{r.OrderNumber} {r.Mode} {r.Reference}".Trim(), new List<(string, decimal)>
            {
                (r.IsCash ? settings.CashLedger : settings.BankLedger, -r.Amount),
                (r.Client, r.Amount)
            });
        }

        xml.AppendLine("   </REQUESTDATA>");
        xml.AppendLine("  </IMPORTDATA>");
        xml.AppendLine(" </BODY>");
        xml.AppendLine("</ENVELOPE>");
        return xml.ToString();
    }

    /// <summary>One voucher; in Tally a debit is a negative amount with ISDEEMEDPOSITIVE Yes. Lines of 0 are left out.</summary>
    private static void Voucher(StringBuilder xml, string type, DateTime date, string number, string party, string narration,
        List<(string Ledger, decimal Amount)> entries)
    {
        xml.AppendLine("    <TALLYMESSAGE xmlns:UDF=\"TallyUDF\">");
        xml.AppendLine($"     <VOUCHER VCHTYPE=\"{type}\" ACTION=\"Create\" OBJVIEW=\"Accounting Voucher View\">");
        xml.AppendLine($"      <DATE>{date:yyyyMMdd}</DATE>");
        xml.AppendLine($"      <VOUCHERTYPENAME>{type}</VOUCHERTYPENAME>");
        xml.AppendLine($"      <VOUCHERNUMBER>{X(number)}</VOUCHERNUMBER>");
        xml.AppendLine($"      <PARTYLEDGERNAME>{X(party)}</PARTYLEDGERNAME>");
        xml.AppendLine($"      <NARRATION>{X(narration)}</NARRATION>");
        xml.AppendLine("      <PERSISTEDVIEW>Accounting Voucher View</PERSISTEDVIEW>");
        foreach (var (ledger, amount) in entries.Where(e => e.Amount != 0))
        {
            xml.AppendLine("      <ALLLEDGERENTRIES.LIST>");
            xml.AppendLine($"       <LEDGERNAME>{X(ledger)}</LEDGERNAME>");
            xml.AppendLine($"       <ISDEEMEDPOSITIVE>{(amount < 0 ? "Yes" : "No")}</ISDEEMEDPOSITIVE>");
            xml.AppendLine($"       <AMOUNT>{amount.ToString("0.00", Inv)}</AMOUNT>");
            xml.AppendLine("      </ALLLEDGERENTRIES.LIST>");
        }
        xml.AppendLine("     </VOUCHER>");
        xml.AppendLine("    </TALLYMESSAGE>");
    }

    private static string X(string text) => SecurityElement.Escape(text ?? "") ?? "";

    private static string Money(decimal value) => value.ToString("0.00", Inv);

    private static void Row(StringBuilder csv, params string[] cells)
        => csv.Append(string.Join(",", cells.Select(c =>
        {
            c ??= "";
            // A leading = + - @ would be read by Excel as a formula.
            if (c.Length > 0 && "=+-@".Contains(c[0]) && !decimal.TryParse(c, NumberStyles.Number, Inv, out _)) c = "'" + c;
            return c.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{c.Replace("\"", "\"\"")}\"" : c;
        }))).Append("\r\n");
}
