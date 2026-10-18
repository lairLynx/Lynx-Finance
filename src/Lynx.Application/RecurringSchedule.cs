using Lynx.Domain.Entities;

namespace Lynx.Application;

/// <summary>Date arithmetic for recurring items. Monthly steps are anchored to one date so "31st" doesn't drift to "30th".</summary>
public static class RecurringSchedule
{
    public static DateOnly Step(DateOnly anchor, RecurringFrequency frequency, int count) => frequency switch
    {
        RecurringFrequency.Weekly => anchor.AddDays(7 * count),
        RecurringFrequency.Biweekly => anchor.AddDays(14 * count),
        RecurringFrequency.Monthly => anchor.AddMonths(count),
        RecurringFrequency.Quarterly => anchor.AddMonths(3 * count),
        RecurringFrequency.Yearly => anchor.AddYears(count),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };

    /// <summary>First occurrence on or after <paramref name="from"/>, counting from <paramref name="anchor"/>.</summary>
    public static DateOnly NextOnOrAfter(DateOnly anchor, RecurringFrequency frequency, DateOnly from)
    {
        var date = anchor;
        for (var i = 0; date < from; i++)
        {
            date = Step(anchor, frequency, i + 1);
            if (i > 5000) break; // safety net for absurd anchors
        }
        return date;
    }

    /// <summary>Every occurrence in [from, to], counting from <paramref name="anchor"/> (re-derived each time to avoid month-end drift).</summary>
    public static IEnumerable<DateOnly> Occurrences(DateOnly anchor, RecurringFrequency frequency, DateOnly from, DateOnly to)
    {
        for (var k = 0; k < 100_000; k++)
        {
            var date = Step(anchor, frequency, k);
            if (date > to) yield break;
            if (date >= from) yield return date;
        }
    }
}
