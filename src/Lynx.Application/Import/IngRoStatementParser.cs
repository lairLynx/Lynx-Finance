using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lynx.Application.Import;

/// <summary>
/// ING Romania "Extras de cont" CSV: one transaction spans several rows. The first row has the date
/// ("04 octombrie 2026"), the type ("Incasare", "Cumparare POS", ...) and the amount under Debit or Credit;
/// the rows below carry "Key:Value" details (Ordonator, Beneficiar, Tranzactie la, Detalii, ...).
/// Column positions drift between pages, so debit vs credit is decided by the gap between type and amount.
/// </summary>
public static partial class IngRoStatementParser
{
    private static readonly string[] Months =
        ["ianuarie", "februarie", "martie", "aprilie", "mai", "iunie", "iulie", "august", "septembrie", "octombrie", "noiembrie", "decembrie"];

    [GeneratedRegex(@"^(\d{1,2})\s+([A-Za-zÀ-ɏ]+)\s+(\d{4})$")]
    private static partial Regex DateCell();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static bool Looks(string text) =>
        text.Contains("Detalii tranzactie", StringComparison.OrdinalIgnoreCase)
        && text.Contains("Titular cont", StringComparison.OrdinalIgnoreCase)
        && DateCellAnywhere().IsMatch(text);

    [GeneratedRegex(@"(?m)^\d{1,2} (ianuarie|februarie|martie|aprilie|mai|iunie|iulie|august|septembrie|octombrie|noiembrie|decembrie) \d{4},")]
    private static partial Regex DateCellAnywhere();

    private sealed class Pending
    {
        public DateOnly Date;
        public string Type = "";
        public long Amount;
        public Dictionary<string, string> Details = new(StringComparer.OrdinalIgnoreCase);
    }

    public static List<ParsedTransaction> Parse(string text)
    {
        var rows = CsvStatementParser.ReadRows(text);
        var result = new List<ParsedTransaction>();
        Pending? current = null;
        int debitGap = 3, creditGap = 5; // defaults; refreshed from each page header

        void Flush()
        {
            if (current is null) return;
            result.Add(Build(current));
            current = null;
        }

        foreach (var row in rows)
        {
            var normalized = row.Select(c => Normalize(c.Trim())).ToList();
            var detailCol = normalized.IndexOf("detalii tranzactie");
            if (detailCol >= 0)
            {
                Flush();
                var debitCol = normalized.IndexOf("debit");
                var creditCol = normalized.IndexOf("credit");
                if (debitCol > detailCol) debitGap = debitCol - detailCol;
                if (creditCol > detailCol) creditGap = creditCol - detailCol;
                continue;
            }

            var first = row.Count > 0 ? row[0].Trim() : "";
            if (ParseDate(first) is { } date)
            {
                Flush();
                var filled = Enumerable.Range(1, Math.Max(row.Count - 1, 0)).Where(i => row[i].Trim().Length > 0).ToList();
                if (filled.Count < 2) continue;
                // The cell after the type is the amount; any further cell is a running balance and must be ignored.
                var typeCol = filled[0];
                var amountCol = filled[1];
                var amount = AmountParser.ParseMinor(row[amountCol]);
                if (amount is null) continue;

                var gap = amountCol - typeCol;
                var isDebit = Math.Abs(gap - debitGap) <= Math.Abs(gap - creditGap);
                current = new Pending { Date = date, Type = row[typeCol].Trim(), Amount = isDebit ? -Math.Abs(amount.Value) : Math.Abs(amount.Value) };
                continue;
            }

            if (current is null || first.Length > 0) continue;
            var line = row.Select(c => c.Trim()).FirstOrDefault(c => c.Length > 0);
            var colon = line?.IndexOf(':') ?? -1;
            if (line is null || colon <= 0) continue;
            current.Details.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
        }
        Flush();

        if (result.Count == 0) throw new ImportFormatException("No transactions found in the file.");
        return result;
    }

    private static ParsedTransaction Build(Pending p)
    {
        string Detail(string key) => p.Details.GetValueOrDefault(key, "");

        var payee = p.Type;
        if (Detail("Tranzactie la") is { Length: > 0 } merchant) payee = Spaces().Replace(merchant, " ");
        else if (Detail("Ordonator") is { Length: > 0 } sender) payee = sender;
        else if (Detail("Beneficiar") is { Length: > 0 } recipient) payee = recipient;

        var note = Detail("Detalii");
        var description = note.Length > 0 && !string.Equals(note, p.Type, StringComparison.OrdinalIgnoreCase) ? $"{p.Type} — {note}" : p.Type;
        if (string.Equals(payee, description, StringComparison.OrdinalIgnoreCase)) description = "";

        var counterparty = Detail("In contul") is { Length: > 0 } to ? to : Detail("Din contul");
        return new ParsedTransaction(p.Date, p.Amount, Truncate(payee, 200), Truncate(description, 500), null, ParsedTransaction.NormalizeIban(counterparty));
    }

    private static DateOnly? ParseDate(string cell)
    {
        var m = DateCell().Match(cell);
        if (!m.Success) return null;
        var month = Array.IndexOf(Months, Normalize(m.Groups[2].Value)) + 1;
        if (month == 0) return null;
        return DateOnly.TryParseExact($"{m.Groups[1].Value}-{month}-{m.Groups[3].Value}", "d-M-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
