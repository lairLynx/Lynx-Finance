using System.Globalization;
using System.Text.RegularExpressions;

namespace Lynx.Application.Import;

/// <summary>
/// Banca Transilvania "EXTRAS CONT" PDF. Rows have no ruling lines, so debit vs credit is decided by the
/// horizontal position of the amount relative to the "Debit" and "Credit" column headers.
/// Each transaction starts on the line that carries its amount; following lines (name;IBAN;BIC;, REF:) are details.
/// </summary>
public static partial class BtPdfStatementParser
{
    // Page furniture (letterhead, footer) lives outside this band.
    private const double TopLimit = 745, BottomLimit = 45;
    private const double LineTolerance = 2.5;

    [GeneratedRegex(@"^\d{1,3}(,\d{3})*\.\d{2}$")]
    private static partial Regex AmountText();

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}$")]
    private static partial Regex DateText();

    [GeneratedRegex(@"^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$")]
    private static partial Regex IbanText();

    [GeneratedRegex(@"^TID:\S+\s+")]
    private static partial Regex TidPrefix();

    [GeneratedRegex(@"\s+\d{6,}$")]
    private static partial Regex TrailingNumber();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static bool Looks(List<List<TextRun>> pages) =>
        pages.Any(p => p.Any(r => r.Text.Contains("BANCA TRANSILVANIA", StringComparison.Ordinal))
                       && p.Any(r => r.Text.Contains("EXTRAS CONT", StringComparison.Ordinal)));

    private sealed class Pending
    {
        public DateOnly Date;
        public string Type = "";
        public long Amount;
        public List<string> Details = [];
    }

    public static List<ParsedTransaction> Parse(List<List<TextRun>> pages)
    {
        var result = new List<ParsedTransaction>();
        double debitRight = 474, creditRight = 572;
        var capturing = false;
        DateOnly? date = null;
        Pending? current = null;

        void Flush()
        {
            if (current is not null) result.Add(Build(current));
            current = null;
        }

        foreach (var page in pages)
        {
            var runs = page.Where(r => r.Y is < TopLimit and > BottomLimit).ToList();
            foreach (var line in GroupLines(runs))
            {
                var texts = line.Select(r => r.Text.Trim()).ToList();

                var debitHeader = line.FirstOrDefault(r => r.Text.Trim() == "Debit");
                var creditHeader = line.FirstOrDefault(r => r.Text.Trim() == "Credit");
                if (debitHeader is not null && creditHeader is not null && texts.Contains("Descriere"))
                {
                    Flush();
                    debitRight = debitHeader.Right;
                    creditRight = creditHeader.Right;
                    capturing = true;
                    continue;
                }
                if (!capturing) continue;

                var split = (debitRight + creditRight) / 2;
                var dateRun = line.FirstOrDefault(r => r.X < 85 && DateText().IsMatch(r.Text.Trim()));
                var amountRun = line.FirstOrDefault(r => r.Right >= debitRight - 25 && AmountText().IsMatch(r.Text.Trim()));
                var description = string.Join(" ", line
                    .Where(r => r != dateRun && r != amountRun && r.X >= 85 && r.X < debitRight - 25)
                    .Select(r => r.Text.Trim()).Where(t => t.Length > 0));

                if (description.StartsWith("SUME BLOCATE", StringComparison.Ordinal)) { Flush(); capturing = false; continue; }
                if (description.StartsWith("SOLD", StringComparison.Ordinal) || description.StartsWith("RULAJ", StringComparison.Ordinal))
                {
                    Flush();
                    if (description.StartsWith("SOLD FINAL CONT", StringComparison.Ordinal)) capturing = false;
                    continue;
                }

                if (dateRun is not null &&
                    DateOnly.TryParseExact(dateRun.Text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    date = parsedDate;

                if (amountRun is not null && description.Length > 0 && date is { } day)
                {
                    Flush();
                    var minor = AmountParser.ParseMinor(amountRun.Text.Trim());
                    if (minor is null) continue;
                    var isDebit = amountRun.Right < split;
                    current = new Pending { Date = day, Type = description, Amount = isDebit ? -Math.Abs(minor.Value) : Math.Abs(minor.Value) };
                }
                else if (current is not null && description.Length > 0)
                {
                    current.Details.Add(description);
                }
            }
        }
        Flush();

        if (result.Count == 0) throw new ImportFormatException("No transactions found in the PDF.");
        return result;
    }

    // Runs on the same visual line can differ by a point or so in baseline (the date sits slightly below its row).
    private static List<List<TextRun>> GroupLines(List<TextRun> runs)
    {
        var lines = new List<List<TextRun>>();
        var lineY = new List<double>();
        foreach (var run in runs.OrderByDescending(r => r.Y))
        {
            var idx = lineY.FindIndex(y => Math.Abs(y - run.Y) <= LineTolerance);
            if (idx < 0) { lines.Add([run]); lineY.Add(run.Y); }
            else lines[idx].Add(run);
        }
        return lines.Select(l => l.OrderBy(r => r.X).ToList()).ToList();
    }

    private static ParsedTransaction Build(Pending p)
    {
        var details = p.Details.Where(d => !d.StartsWith("REF:", StringComparison.OrdinalIgnoreCase)).ToList();
        string payee;
        string? note = null;
        string? counterpartyIban = null;

        if (p.Type.StartsWith("Comision", StringComparison.OrdinalIgnoreCase))
        {
            payee = "Banca Transilvania";
        }
        else
        {
            var parts = string.Join(" ", details).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var ibanAt = Array.FindIndex(parts, s => IbanText().IsMatch(s));
            if (ibanAt >= 1)
            {
                // "name;IBAN;BIC;" (incoming) or "note;code;name;IBAN;BIC;" (outgoing): the name sits just before the IBAN.
                payee = parts[ibanAt - 1];
                counterpartyIban = parts[ibanAt];
                if (ibanAt >= 2) note = parts[0];
            }
            else
            {
                // Card payments etc.: strip terminal ids and the trailing merchant number.
                var line = details.FirstOrDefault() ?? p.Type;
                line = TrailingNumber().Replace(TidPrefix().Replace(line, ""), "");
                payee = Spaces().Replace(line, " ").Trim();
            }
        }
        if (payee.Length == 0) payee = p.Type;

        var description = note is { Length: > 0 } ? $"{p.Type} — {note}" : p.Type;
        if (string.Equals(payee, description, StringComparison.OrdinalIgnoreCase)) description = "";

        return new ParsedTransaction(p.Date, p.Amount, Truncate(payee, 200), Truncate(description, 500), null, ParsedTransaction.NormalizeIban(counterpartyIban));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
