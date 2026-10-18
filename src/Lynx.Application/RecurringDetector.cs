using System.Text.RegularExpressions;
using Lynx.Domain.Entities;

namespace Lynx.Application;

/// <summary>One transaction as the detector sees it. Transfers must be excluded by the caller.</summary>
public record DetectorInput(int AccountId, DateOnly Date, long AmountMinor, string Payee);

public record DetectedRecurring(
    int AccountId, string Payee, string PayeeKey, long AmountMinor, RecurringFrequency Frequency,
    DateOnly LastSeen, DateOnly NextDue, int Occurrences);

/// <summary>
/// Finds payees that repeat on a regular schedule with a steady amount (rent, subscriptions, salary, ...).
/// Grouping is per account, direction and normalised payee name.
/// </summary>
public static partial class RecurringDetector
{
    private record Period(RecurringFrequency Frequency, int Days, int Tolerance, int MinOccurrences, double AmountSpread);

    // Looser amount spread for monthly/quarterly/yearly bills (utilities vary a bit); tight for weekly (shopping varies a lot).
    private static readonly Period[] Periods =
    [
        new(RecurringFrequency.Weekly, 7, 2, 4, 0.10),
        new(RecurringFrequency.Biweekly, 14, 3, 4, 0.10),
        new(RecurringFrequency.Monthly, 30, 5, 3, 0.20),
        new(RecurringFrequency.Quarterly, 91, 8, 3, 0.20),
        new(RecurringFrequency.Yearly, 365, 15, 2, 0.20),
    ];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    /// <summary>"NETFLIX.COM 12345 NL" and "Netflix.com 99881 NL" become the same key; tokens with digits (terminal ids) are dropped.</summary>
    public static string NormalizePayee(string payee)
    {
        var tokens = NonWord().Split(payee.ToLowerInvariant()).Where(t => t.Length > 0 && !t.Any(char.IsDigit)).Take(3).ToList();
        return tokens.Count > 0 ? string.Join(' ', tokens) : payee.Trim().ToLowerInvariant();
    }

    public static string KeyFor(string payee, long amountMinor) => (amountMinor < 0 ? "-" : "+") + NormalizePayee(payee);

    public static List<DetectedRecurring> Detect(IEnumerable<DetectorInput> transactions, DateOnly today)
    {
        var result = new List<DetectedRecurring>();
        foreach (var group in transactions.Where(t => t.AmountMinor != 0).GroupBy(t => (t.AccountId, Key: KeyFor(t.Payee, t.AmountMinor))))
        {
            var items = group.OrderBy(t => t.Date).ToList();
            if (items.Count < 2) continue;

            var intervals = items.Zip(items.Skip(1), (a, b) => b.Date.DayNumber - a.Date.DayNumber).Where(d => d > 0).ToList();
            if (intervals.Count == 0) continue;
            var median = Median(intervals.Select(i => (double)i).ToList());

            var period = Periods.FirstOrDefault(p => Math.Abs(median - p.Days) <= p.Tolerance);
            if (period is null || items.Count < period.MinOccurrences) continue;
            if (intervals.Count(i => Math.Abs(i - period.Days) <= period.Tolerance) < Math.Ceiling(intervals.Count * 0.7)) continue;

            var amounts = items.Select(t => (double)Math.Abs(t.AmountMinor)).ToList();
            var typical = Median(amounts);
            if (amounts.Count(a => Math.Abs(a - typical) <= typical * period.AmountSpread) < Math.Ceiling(amounts.Count * 0.7)) continue;

            var last = items[^1];
            // A subscription that stopped long ago is no longer recurring.
            if (today.DayNumber - last.Date.DayNumber > period.Days * 2 + period.Tolerance) continue;

            var recent = items.TakeLast(3).Select(t => (double)Math.Abs(t.AmountMinor)).ToList();
            var amount = (long)Math.Round(Median(recent));
            var next = RecurringSchedule.NextOnOrAfter(last.Date, period.Frequency, today);

            result.Add(new DetectedRecurring(
                group.Key.AccountId, last.Payee.Trim(), group.Key.Key, last.AmountMinor < 0 ? -amount : amount,
                period.Frequency, last.Date, next, items.Count));
        }
        return result;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}
