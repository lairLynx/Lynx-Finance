using System.Globalization;
using System.Text;

namespace Lynx.Application.Import;

/// <summary>
/// Reads bank CSV exports without a per-bank profile: finds the header row, maps columns by common
/// English/Romanian names (Revolut, ING, BT, BCR, ...) and normalises dates and amounts.
/// </summary>
public static class CsvStatementParser
{
    private static readonly string[] DateHeaders =
        ["completed date", "booking date", "posting date", "transaction date", "date", "data tranzactie", "data procesarii", "data postarii", "data", "datum", "started date", "value date", "data valutei"];
    private static readonly string[] AmountHeaders = ["amount", "suma", "valoare", "valoare tranzactie", "transaction amount"];
    private static readonly string[] DebitHeaders = ["debit", "debit amount", "withdrawal", "withdrawals", "paid out", "money out", "suma debit", "debite"];
    private static readonly string[] CreditHeaders = ["credit", "credit amount", "deposit", "deposits", "paid in", "money in", "suma credit", "credite"];
    private static readonly string[] DescriptionHeaders = ["description", "details", "descriere", "detalii", "detalii tranzactie", "reference", "memo", "narrative", "remittance information", "transaction details", "explicatii"];
    private static readonly string[] PayeeHeaders = ["payee", "counterparty", "merchant", "name", "beneficiar", "beneficiary", "ordonator", "partener", "counterparty name", "nume beneficiar"];
    private static readonly string[] FeeHeaders = ["fee", "comision"];
    private static readonly string[] StatusHeaders = ["state", "status", "stare"];
    private static readonly string[] SkippedStates = ["reverted", "declined", "pending", "failed", "cancelled", "canceled", "refused"];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "dd/MM/yyyy", "dd.MM.yyyy", "dd-MM-yyyy",
        "d/M/yyyy", "d.M.yyyy", "d-M-yyyy", "dd/MM/yyyy HH:mm:ss", "dd.MM.yyyy HH:mm:ss", "yyyy/MM/dd", "yyyyMMdd",
        "dd MMM yyyy", "d MMM yyyy", "dd MMMM yyyy",
    ];

    public static List<ParsedTransaction> Parse(string text)
    {
        var rows = ReadRows(text);
        var (headerIndex, map) = FindHeader(rows);
        var result = new List<ParsedTransaction>();

        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            string Cell(int idx) => idx >= 0 && idx < row.Count ? row[idx].Trim() : "";
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            var date = ParseDate(Cell(map.Date));
            if (date is null) continue; // totals, footers, blank dates

            if (map.Status >= 0 && SkippedStates.Contains(Cell(map.Status).ToLowerInvariant())) continue;

            long? amount;
            if (map.Amount >= 0)
                amount = AmountParser.ParseMinor(Cell(map.Amount));
            else
            {
                var debit = AmountParser.ParseMinor(Cell(map.Debit));
                var credit = AmountParser.ParseMinor(Cell(map.Credit));
                amount = debit is null && credit is null
                    ? null
                    : (credit.HasValue ? Math.Abs(credit.Value) : 0) - (debit.HasValue ? Math.Abs(debit.Value) : 0);
            }
            if (amount is null) continue;
            if (map.Fee >= 0 && AmountParser.ParseMinor(Cell(map.Fee)) is { } fee) amount -= Math.Abs(fee);

            var description = Cell(map.Description);
            var payee = Cell(map.Payee);
            if (payee.Length == 0) { payee = description; description = ""; }

            result.Add(new ParsedTransaction(date.Value, amount.Value, Truncate(payee, 200), Truncate(description, 500), null));
        }

        if (result.Count == 0) throw new ImportFormatException("No transactions found in the file.");
        return result;
    }

    private record ColumnMap(int Date, int Amount, int Debit, int Credit, int Description, int Payee, int Fee, int Status);

    private static (int HeaderIndex, ColumnMap Map) FindHeader(List<List<string>> rows)
    {
        for (var i = 0; i < Math.Min(rows.Count, 30); i++)
        {
            var headers = rows[i].Select(Normalize).ToList();
            var date = IndexOf(headers, DateHeaders);
            var amount = IndexOf(headers, AmountHeaders);
            var debit = IndexOf(headers, DebitHeaders);
            var credit = IndexOf(headers, CreditHeaders);
            if (date < 0 || (amount < 0 && debit < 0 && credit < 0)) continue;

            return (i, new ColumnMap(date, amount, debit, credit,
                IndexOf(headers, DescriptionHeaders), IndexOf(headers, PayeeHeaders),
                IndexOf(headers, FeeHeaders), IndexOf(headers, StatusHeaders)));
        }
        throw new ImportFormatException("Could not find date and amount columns. Expected headers like Date, Description, Amount (or Debit/Credit).");
    }

    // Earlier entries in `names` win, so "Completed Date" beats "Started Date" when both exist.
    private static int IndexOf(List<string> headers, string[] names)
    {
        foreach (var name in names)
        {
            var idx = headers.IndexOf(name);
            if (idx >= 0) return idx;
        }
        // Banks often append units or qualifiers ("Amount (EUR)", "Data tranzactiei").
        foreach (var name in names)
        {
            var idx = headers.FindIndex(h => h.StartsWith(name + " ", StringComparison.Ordinal) || h.StartsWith(name + "(", StringComparison.Ordinal));
            if (idx >= 0) return idx;
        }
        return -1;
    }

    private static string Normalize(string header)
    {
        var decomposed = header.Trim().Trim('﻿').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static DateOnly? ParseDate(string value)
    {
        if (value.Length == 0) return null;
        return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? DateOnly.FromDateTime(dt)
            : null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    internal static List<List<string>> ReadRows(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var sample = string.Join('\n', text.Split('\n').Where(l => l.Trim().Length > 0).Take(8));
        var best = ',';
        var bestCount = -1;
        foreach (var d in new[] { ',', ';', '\t' })
        {
            var count = sample.Count(c => c == d);
            if (count > bestCount) { best = d; bestCount = count; }
        }
        return best;
    }
}
