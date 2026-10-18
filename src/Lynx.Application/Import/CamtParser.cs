using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Lynx.Application.Import;

/// <summary>Reads ISO 20022 camt.052/053/054 statements (namespace-agnostic).</summary>
public static class CamtParser
{
    public static List<ParsedTransaction> Parse(string text)
    {
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new ImportFormatException($"The file is not valid XML: {ex.Message}");
        }

        var result = new List<ParsedTransaction>();
        foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName == "Ntry"))
        {
            var amountText = Child(entry, "Amt");
            var amount = AmountParser.ParseMinor(amountText);
            if (amount is null) continue;
            var debit = string.Equals(Child(entry, "CdtDbtInd"), "DBIT", StringComparison.OrdinalIgnoreCase);
            var signed = debit ? -Math.Abs(amount.Value) : Math.Abs(amount.Value);

            var dateText = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "BookgDt")?.Descendants().FirstOrDefault(e => e.Name.LocalName is "Dt" or "DtTm")?.Value
                           ?? entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "ValDt")?.Descendants().FirstOrDefault(e => e.Name.LocalName is "Dt" or "DtTm")?.Value;
            if (dateText is null || dateText.Length < 10
                || !DateOnly.TryParseExact(dateText[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;

            // For money out the counterparty is the creditor; for money in, the debtor.
            var party = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == (debit ? "Cdtr" : "Dbtr"))
                ?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Nm")?.Value ?? "";
            var remittance = string.Join(" ", entry.Descendants().Where(e => e.Name.LocalName == "Ustrd").Select(e => e.Value.Trim()));
            if (remittance.Length == 0) remittance = Child(entry, "AddtlNtryInf") ?? "";
            var payee = party.Trim();
            if (payee.Length == 0) { payee = remittance; remittance = ""; }

            var accountParty = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == (debit ? "CdtrAcct" : "DbtrAcct"));
            var iban = accountParty?.Descendants().FirstOrDefault(e => e.Name.LocalName == "IBAN")?.Value.Trim();

            var reference = Child(entry, "AcctSvcrRef");
            result.Add(new ParsedTransaction(date, signed, Truncate(payee, 200), Truncate(remittance, 500), string.IsNullOrWhiteSpace(reference) ? null : reference, ParsedTransaction.NormalizeIban(iban)));
        }

        if (result.Count == 0) throw new ImportFormatException("No transactions found in the CAMT file.");
        return result;
    }

    private static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
