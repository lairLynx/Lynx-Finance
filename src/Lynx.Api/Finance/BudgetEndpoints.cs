using System.Globalization;
using System.Text.RegularExpressions;
using Lynx.Domain.Entities;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Api.Finance;

public record BudgetInput(string? Month, int? CategoryId, string? Currency, decimal? Amount);
public record BudgetCopyInput(string? FromMonth, string? ToMonth, string? Currency);
public record BudgetLine(int Id, int CategoryId, string Name, string Color, decimal Budget, decimal Spent);
public record BudgetsDto(
    string[] Currencies, string? Currency, string Month, string? FirstMonth, string? LastMonth,
    decimal Budgeted, decimal Spent, decimal TotalSpent, bool CanCopy,
    List<BudgetLine> Lines, List<CategoryTotal> Unbudgeted);

/// <summary>Monthly category budgets per currency; "spent" is real non-transfer spending in accounts of that currency.</summary>
public static partial class BudgetEndpoints
{
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex MonthText();

    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/budgets", async (AppDbContext db, string? currency, string? month) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var monthText = month is not null && MonthText().IsMatch(month) ? month : $"{today:yyyy-MM}";
            var context = await CurrencyContexts.ResolveAsync(db, currency);
            if (context.Selected is not { } selected)
                return new BudgetsDto([], null, monthText, null, null, 0, 0, 0, false, [], []);

            var ids = context.Accounts.Select(a => a.Id).ToList();
            var start = DateOnly.ParseExact(monthText + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = start.AddMonths(1).AddDays(-1);

            var spending = await db.Transactions.AsNoTracking()
                .Where(t => ids.Contains(t.AccountId) && !t.IsTransfer && t.AmountMinor < 0 && t.Date >= start && t.Date <= end)
                .GroupBy(t => t.CategoryId).Select(g => new { CategoryId = g.Key ?? 0, Sum = -g.Sum(t => t.AmountMinor) }) // 0 = uncategorized
                .ToDictionaryAsync(x => x.CategoryId, x => x.Sum);

            var budgets = await db.Budgets.AsNoTracking().Include(b => b.Category)
                .Where(b => b.Month == monthText && b.Currency == selected).OrderBy(b => b.Category.Name).ToListAsync();
            var lines = budgets.Select(b => new BudgetLine(b.Id, b.CategoryId, b.Category.Name, b.Category.Color,
                b.AmountMinor / 100m, spending.GetValueOrDefault(b.CategoryId) / 100m)).ToList();

            var budgetedIds = budgets.Select(b => b.CategoryId).ToHashSet();
            var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id);
            var unbudgeted = spending.Where(kv => !budgetedIds.Contains(kv.Key))
                .Select(kv =>
                {
                    var c = categories.GetValueOrDefault(kv.Key);
                    return new CategoryTotal(kv.Key == 0 ? null : kv.Key, c?.Name ?? "Uncategorized", c?.Color ?? "#6b7280", kv.Value / 100m);
                })
                .OrderByDescending(c => c.Amount).ToList();

            var previous = start.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var previousCategories = await db.Budgets.AsNoTracking().Where(b => b.Month == previous && b.Currency == selected).Select(b => b.CategoryId).ToListAsync();
            var canCopy = previousCategories.Any(id => !budgetedIds.Contains(id));

            var firstTx = await db.Transactions.Where(t => ids.Contains(t.AccountId)).MinAsync(t => (DateOnly?)t.Date);
            var lastTx = await db.Transactions.Where(t => ids.Contains(t.AccountId)).MaxAsync(t => (DateOnly?)t.Date);
            var thisMonth = $"{today:yyyy-MM}";
            var lastMonth = lastTx is { } l && $"{l:yyyy-MM}".CompareTo(thisMonth) > 0 ? $"{l:yyyy-MM}" : thisMonth; // planning may look at the current month

            return new BudgetsDto(context.Currencies, selected, monthText,
                firstTx is { } f ? $"{f:yyyy-MM}" : thisMonth, lastMonth,
                lines.Sum(l2 => l2.Budget), lines.Sum(l2 => l2.Spent), spending.Values.Sum() / 100m, canCopy, lines, unbudgeted);
        });

        // Creates or updates the budget for (month, category, currency); an amount of 0 removes it.
        api.MapPut("/budgets", async (BudgetInput body, AppDbContext db) =>
        {
            if (body.Month is null || !MonthText().IsMatch(body.Month)) return Invalid("Month must look like 2026-10.");
            var currency = CurrencyContexts.Normalize((body.Currency ?? "").Trim());
            if (currency.Length != 3) return Invalid("Choose a currency.");
            if (body.CategoryId is null || !await db.Categories.AnyAsync(c => c.Id == body.CategoryId)) return Invalid("Choose a category.");
            var amount = body.Amount ?? 0;
            if (amount < 0 || amount > 1_000_000_000m) return Invalid("Amount is out of range.");

            var existing = await db.Budgets.FirstOrDefaultAsync(b => b.Month == body.Month && b.CategoryId == body.CategoryId && b.Currency == currency);
            if (amount == 0)
            {
                if (existing is not null) db.Budgets.Remove(existing);
            }
            else if (existing is null)
            {
                db.Budgets.Add(new Budget { Month = body.Month, CategoryId = body.CategoryId.Value, Currency = currency, AmountMinor = ToMinor(amount) });
            }
            else
            {
                existing.AmountMinor = ToMinor(amount);
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Copies budgets from one month to another without touching categories that already have one.
        api.MapPost("/budgets/copy", async (BudgetCopyInput body, AppDbContext db) =>
        {
            if (body.FromMonth is null || body.ToMonth is null || !MonthText().IsMatch(body.FromMonth) || !MonthText().IsMatch(body.ToMonth))
                return Invalid("Months must look like 2026-10.");
            var currency = CurrencyContexts.Normalize((body.Currency ?? "").Trim());
            var source = await db.Budgets.AsNoTracking().Where(b => b.Month == body.FromMonth && b.Currency == currency).ToListAsync();
            var have = await db.Budgets.Where(b => b.Month == body.ToMonth && b.Currency == currency).Select(b => b.CategoryId).ToListAsync();
            var fresh = source.Where(b => !have.Contains(b.CategoryId)).ToList();
            db.Budgets.AddRange(fresh.Select(b => new Budget { Month = body.ToMonth, CategoryId = b.CategoryId, Currency = currency, AmountMinor = b.AmountMinor }));
            await db.SaveChangesAsync();
            return Results.Ok(new { copied = fresh.Count });
        });

        return api;
    }

    private static IResult Invalid(string message) => Results.BadRequest(new { error = message });
    private static long ToMinor(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
