using System.Globalization;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Api.Finance;

public record MonthPoint(string Month, decimal Income, decimal Expense);
public record BalancePoint(string Month, decimal Balance);
public record CategoryTotal(int? CategoryId, string Name, string Color, decimal Amount);
public record PayeeTotal(string Payee, decimal Amount, int Count);

/// <summary>
/// Everything the dashboard and reports need for ONE currency. Currencies are never converted or summed together:
/// the caller picks a currency and only accounts in that currency contribute.
/// </summary>
public record OverviewDto(
    string[] Currencies, string? Currency, string Month, string? FirstMonth, string? LastMonth,
    decimal Income, decimal Expense, decimal NetWorth, decimal PreviousNetWorth, int TransactionCount, int UncategorizedCount,
    decimal RangeIncome, decimal RangeExpense, decimal Transfers,
    List<MonthPoint> Months, List<BalancePoint> Balances,
    List<CategoryTotal> Categories, List<PayeeTotal> TopPayees,
    List<AccountDto> Accounts, List<TransactionDto> Recent);

public static class OverviewEndpoints
{
    private const string UncategorizedColor = "#6b7280";

    public static IEndpointRouteBuilder MapOverviewEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/overview", async (AppDbContext db, string? currency, string? month, int? months) =>
        {
            var span = Math.Clamp(months ?? 12, 1, 36);
            var context = await CurrencyContexts.ResolveAsync(db, currency);
            var currencies = context.Currencies;
            if (context.Selected is not { } selected)
                return Results.Ok(new OverviewDto([], null, CurrentMonth(), null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], [], [], [], []));

            var inCurrency = context.Accounts;
            var ids = inCurrency.Select(a => a.Id).ToList();
            var txs = await db.Transactions.AsNoTracking().Where(t => ids.Contains(t.AccountId))
                .Select(t => new Row(t.AccountId, t.Date, t.AmountMinor, t.CategoryId, t.Payee, t.IsTransfer)).ToListAsync();

            var firstKey = txs.Count > 0 ? txs.Min(t => Key(t.Date)) : (int?)null;
            var lastKey = txs.Count > 0 ? txs.Max(t => Key(t.Date)) : (int?)null;
            var monthKey = ParseMonth(month) ?? lastKey ?? Key(DateOnly.FromDateTime(DateTime.Today));

            // Per-month sums, then cumulative balance up to the end of each month.
            // Transfers between own accounts stay in the balance but are not income or spending.
            var sums = txs.Where(t => !t.IsTransfer).GroupBy(t => Key(t.Date)).ToDictionary(g => g.Key, g => (
                Income: g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                Expense: -g.Where(t => t.Amount < 0).Sum(t => t.Amount)));
            var openingMinor = inCurrency.Sum(a => a.OpeningBalanceMinor);
            long BalanceAt(int key) => openingMinor + txs.Where(t => Key(t.Date) <= key).Sum(t => t.Amount);

            var series = Enumerable.Range(0, span).Select(i => monthKey - (span - 1 - i)).ToList();
            var monthPoints = series.Select(k =>
            {
                var s = sums.GetValueOrDefault(k);
                return new MonthPoint(Label(k), ToMoney(s.Income), ToMoney(s.Expense));
            }).ToList();

            // One pass for the cumulative balance instead of rescanning per month.
            var ordered = txs.OrderBy(t => Key(t.Date)).ToList();
            var balances = new List<BalancePoint>();
            long running = openingMinor;
            var idx = 0;
            foreach (var k in series)
            {
                while (idx < ordered.Count && Key(ordered[idx].Date) <= k) running += ordered[idx++].Amount;
                balances.Add(new BalancePoint(Label(k), ToMoney(running)));
            }
            var netWorth = balances[^1].Balance;
            var previous = ToMoney(BalanceAt(monthKey - 1));

            var inMonth = txs.Where(t => Key(t.Date) == monthKey && !t.IsTransfer).ToList();
            var transfers = txs.Where(t => Key(t.Date) == monthKey && t.IsTransfer && t.Amount > 0).Sum(t => t.Amount);
            var cats = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id);
            var categoryTotals = inMonth.Where(t => t.Amount < 0).GroupBy(t => t.CategoryId)
                .Select(g =>
                {
                    var c = g.Key is { } id ? cats.GetValueOrDefault(id) : null;
                    return new CategoryTotal(g.Key, c?.Name ?? "Uncategorized", c?.Color ?? UncategorizedColor, ToMoney(-g.Sum(t => t.Amount)));
                })
                .OrderByDescending(c => c.Amount).ToList();
            var payees = inMonth.Where(t => t.Amount < 0).GroupBy(t => t.Payee.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new PayeeTotal(g.Key, ToMoney(-g.Sum(t => t.Amount)), g.Count()))
                .OrderByDescending(p => p.Amount).Take(8).ToList();

            var endOfMonth = new DateOnly(monthKey / 12, monthKey % 12 + 1, 1).AddMonths(1).AddDays(-1);
            var recent = await db.Transactions.AsNoTracking()
                .Where(t => ids.Contains(t.AccountId) && t.Date <= endOfMonth)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(6).ToListAsync();

            var stats = txs.GroupBy(t => t.AccountId).ToDictionary(g => g.Key, g => (Sum: g.Sum(t => t.Amount), Count: g.Count(), Last: g.Max(t => t.Date)));
            var accountDtos = inCurrency.Select(a =>
            {
                var s = stats.GetValueOrDefault(a.Id);
                return FinanceEndpoints.ToDto(a, s.Sum, s.Count, stats.ContainsKey(a.Id) ? s.Last : null);
            }).ToList();

            var monthSums = sums.GetValueOrDefault(monthKey);
            var rangeIncome = series.Sum(k => sums.GetValueOrDefault(k).Income);
            var rangeExpense = series.Sum(k => sums.GetValueOrDefault(k).Expense);

            return Results.Ok(new OverviewDto(
                currencies, selected, Label(monthKey), firstKey is { } f ? Label(f) : null, lastKey is { } l ? Label(l) : null,
                ToMoney(monthSums.Income), ToMoney(monthSums.Expense), netWorth, previous, inMonth.Count, inMonth.Count(t => t.CategoryId is null),
                ToMoney(rangeIncome), ToMoney(rangeExpense), ToMoney(transfers),
                monthPoints, balances, categoryTotals, payees, accountDtos,
                recent.Select(t => new TransactionDto(t.Id, t.AccountId, t.Date, t.Payee, t.Description, ToMoney(t.AmountMinor), t.CategoryId, t.IsTransfer)).ToList()));
        });

        return api;
    }

    private sealed record Row(int AccountId, DateOnly Date, long Amount, int? CategoryId, string Payee, bool IsTransfer);


    private static int Key(DateOnly d) => d.Year * 12 + d.Month - 1;
    private static string Label(int key) => $"{key / 12:0000}-{key % 12 + 1:00}";
    private static string CurrentMonth() => Label(Key(DateOnly.FromDateTime(DateTime.Today)));
    private static decimal ToMoney(long minor) => minor / 100m;

    private static int? ParseMonth(string? month) =>
        DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? Key(d) : null;
}
