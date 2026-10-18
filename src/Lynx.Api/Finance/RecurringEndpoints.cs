using Lynx.Application;
using Lynx.Domain.Entities;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Api.Finance;

public record RecurringInput(int? AccountId, string? Payee, decimal? Amount, string? Frequency, DateOnly? NextDue, string? Status);
public record RecurringDto(int Id, int AccountId, string Payee, string Currency, decimal Amount, string Frequency, DateOnly NextDue, string Status, DateOnly? LastSeen, bool IsManual);
public record RecurringList(string[] Currencies, string? Currency, List<RecurringDto> Items);
public record DetectRecurringResult(int Added);
public record ForecastEvent(DateOnly Date, string Payee, decimal Amount);
public record ForecastDto(
    string[] Currencies, string? Currency, int Days, DateOnly StartDate, decimal StartBalance, decimal EndBalance,
    decimal Lowest, DateOnly LowestDate, decimal DailyVariable, List<decimal> Points, List<ForecastEvent> Events);

/// <summary>
/// Recurring bills/income and the balance forecast they drive. Detected items are suggestions; only confirmed ones
/// feed the forecast. Everything is per currency, never converted.
/// </summary>
public static class RecurringEndpoints
{
    private const int VariableWindowDays = 90;

    public static IEndpointRouteBuilder MapRecurringEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/recurring", async (AppDbContext db, string? currency) =>
        {
            var context = await CurrencyContexts.ResolveAsync(db, currency);
            if (context.Selected is not { } selected) return new RecurringList([], null, []);

            var ids = context.Accounts.Select(a => a.Id).ToList();
            var items = await db.RecurringItems.AsNoTracking().Where(r => ids.Contains(r.AccountId)).ToListAsync();
            var today = Today();
            var dtos = items
                .Select(r => ToDto(r, selected, today))
                .OrderBy(r => r.Status == nameof(RecurringStatus.Dismissed)).ThenBy(r => r.NextDue).ThenBy(r => r.Payee).ToList();
            return new RecurringList(context.Currencies, selected, dtos);
        });

        api.MapPost("/recurring/detect", async (AppDbContext db) => new DetectRecurringResult(await DetectAsync(db)));

        api.MapPost("/recurring", async (RecurringInput body, AppDbContext db) =>
        {
            var item = new RecurringItem { IsManual = true, Status = RecurringStatus.Confirmed };
            if (await Apply(item, body, db, creating: true) is { } error) return error;
            db.RecurringItems.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/recurring/{item.Id}", await ToDtoAsync(db, item));
        });

        api.MapPut("/recurring/{id:int}", async (int id, RecurringInput body, AppDbContext db) =>
        {
            var item = await db.RecurringItems.FindAsync(id);
            if (item is null) return Results.NotFound();
            if (await Apply(item, body, db, creating: false) is { } error) return error;
            await db.SaveChangesAsync();
            return Results.Ok(await ToDtoAsync(db, item));
        });

        api.MapDelete("/recurring/{id:int}", async (int id, AppDbContext db) =>
        {
            var item = await db.RecurringItems.FindAsync(id);
            if (item is null) return Results.NotFound();
            db.RecurringItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/forecast", async (AppDbContext db, string? currency, int? days, bool? includeVariable) =>
        {
            var span = Math.Clamp(days ?? 90, 7, 365);
            var context = await CurrencyContexts.ResolveAsync(db, currency);
            var today = Today();
            if (context.Selected is not { } selected)
                return new ForecastDto([], null, span, today, 0, 0, 0, today, 0, [], []);

            var ids = context.Accounts.Select(a => a.Id).ToList();
            var start = context.Accounts.Sum(a => a.OpeningBalanceMinor)
                        + (await db.Transactions.Where(t => ids.Contains(t.AccountId)).SumAsync(t => (long?)t.AmountMinor) ?? 0);

            var confirmed = await db.RecurringItems.AsNoTracking()
                .Where(r => ids.Contains(r.AccountId) && r.Status == RecurringStatus.Confirmed).ToListAsync();

            var end = today.AddDays(span);
            var events = confirmed
                .SelectMany(r => RecurringSchedule.Occurrences(r.NextDue, r.Frequency, today, end).Select(d => (Date: d, Item: r)))
                .OrderBy(e => e.Date).ThenBy(e => e.Item.Payee).ToList();

            // Everyday spending that isn't a confirmed recurring item, averaged over the last 90 days.
            long daily = 0;
            if (includeVariable ?? true)
            {
                var recurringKeys = confirmed.Select(r => (r.AccountId, r.PayeeKey)).ToHashSet();
                var since = today.AddDays(-VariableWindowDays);
                var recent = await db.Transactions.AsNoTracking()
                    .Where(t => ids.Contains(t.AccountId) && !t.IsTransfer && t.AmountMinor < 0 && t.Date > since && t.Date <= today)
                    .Select(t => new { t.AccountId, t.Payee, t.AmountMinor }).ToListAsync();
                var variable = recent.Where(t => !recurringKeys.Contains((t.AccountId, RecurringDetector.KeyFor(t.Payee, t.AmountMinor)))).Sum(t => t.AmountMinor);
                daily = variable / VariableWindowDays;
            }

            var points = new List<decimal>(span + 1);
            var byDay = events.ToLookup(e => e.Date.DayNumber - today.DayNumber);
            long balance = start;
            var lowest = long.MaxValue;
            var lowestDate = today;
            for (var d = 0; d <= span; d++)
            {
                if (d > 0) balance += daily;
                balance += byDay[d].Sum(e => e.Item.AmountMinor);
                points.Add(balance / 100m);
                if (balance < lowest) { lowest = balance; lowestDate = today.AddDays(d); }
            }

            return new ForecastDto(context.Currencies, selected, span, today, start / 100m, balance / 100m, lowest / 100m, lowestDate, daily / 100m, points,
                events.Select(e => new ForecastEvent(e.Date, e.Item.Payee, e.Item.AmountMinor / 100m)).ToList());
        });

        return api;
    }

    /// <summary>Scans transactions for repeating payees; returns how many new suggestions were added.</summary>
    public static async Task<int> DetectAsync(AppDbContext db, DateOnly? today = null)
    {
        var now = today ?? Today();
        var transactions = await db.Transactions.AsNoTracking().Where(t => !t.IsTransfer)
            .Select(t => new DetectorInput(t.AccountId, t.Date, t.AmountMinor, t.Payee)).ToListAsync();
        var found = RecurringDetector.Detect(transactions, now);

        var existing = await db.RecurringItems.ToListAsync();
        var byKey = existing.ToDictionary(e => (e.AccountId, e.PayeeKey));
        var seen = new HashSet<(int, string)>();
        var added = 0;

        foreach (var f in found)
        {
            var key = (f.AccountId, f.PayeeKey);
            seen.Add(key);
            if (!byKey.TryGetValue(key, out var item))
            {
                db.RecurringItems.Add(new RecurringItem
                {
                    AccountId = f.AccountId, Payee = f.Payee, PayeeKey = f.PayeeKey, AmountMinor = f.AmountMinor,
                    Frequency = f.Frequency, NextDue = f.NextDue, LastSeen = f.LastSeen, Status = RecurringStatus.Detected,
                });
                added++;
                continue;
            }

            if (item.Status == RecurringStatus.Dismissed) continue; // the user said no
            item.LastSeen = f.LastSeen;
            if (item.Status == RecurringStatus.Detected)
            {
                item.Payee = f.Payee; item.AmountMinor = f.AmountMinor; item.Frequency = f.Frequency; item.NextDue = f.NextDue;
            }
            else
            {
                // Confirmed: keep the user's amount and frequency, but follow the real payment dates.
                item.NextDue = RecurringSchedule.NextOnOrAfter(f.LastSeen, item.Frequency, now);
            }
        }

        // Suggestions that no longer match the data (cancelled subscription, changed pattern) go away.
        db.RecurringItems.RemoveRange(existing.Where(e => e.Status == RecurringStatus.Detected && !e.IsManual && !seen.Contains((e.AccountId, e.PayeeKey))));
        await db.SaveChangesAsync();
        return added;
    }

    private static async Task<IResult?> Apply(RecurringItem item, RecurringInput body, AppDbContext db, bool creating)
    {
        if (creating || body.AccountId is not null)
        {
            if (body.AccountId is null || !await db.Accounts.AnyAsync(a => a.Id == body.AccountId)) return Invalid("Choose an account.");
            item.AccountId = body.AccountId.Value;
        }
        if (creating || body.Payee is not null)
        {
            var payee = body.Payee?.Trim() ?? "";
            if (payee.Length is 0 or > 200) return Invalid("Payee is required (max 200 characters).");
            item.Payee = payee;
        }
        if (creating || body.Amount is not null)
        {
            if (body.Amount is null or 0 || Math.Abs(body.Amount.Value) > 1_000_000_000m) return Invalid("Amount is required.");
            item.AmountMinor = (long)Math.Round(body.Amount.Value * 100m, MidpointRounding.AwayFromZero);
        }
        if (creating || body.Frequency is not null)
        {
            if (!Enum.TryParse<RecurringFrequency>(body.Frequency, ignoreCase: true, out var frequency) || !Enum.IsDefined(frequency)) return Invalid("Unknown frequency.");
            item.Frequency = frequency;
        }
        if (creating || body.NextDue is not null)
        {
            if (body.NextDue is null) return Invalid("Next due date is required.");
            item.NextDue = body.NextDue.Value;
        }
        if (body.Status is not null)
        {
            if (!Enum.TryParse<RecurringStatus>(body.Status, ignoreCase: true, out var status) || !Enum.IsDefined(status)) return Invalid("Unknown status.");
            item.Status = status;
        }

        item.PayeeKey = RecurringDetector.KeyFor(item.Payee, item.AmountMinor);
        // The (account, payee) pair must stay unique so detection can recognise it later.
        var clash = await db.RecurringItems.AnyAsync(r => r.Id != item.Id && r.AccountId == item.AccountId && r.PayeeKey == item.PayeeKey);
        return clash ? Results.Conflict(new { error = "A recurring item for this payee already exists on that account." }) : null;
    }

    private static async Task<RecurringDto> ToDtoAsync(AppDbContext db, RecurringItem item)
    {
        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == item.AccountId);
        return ToDto(item, CurrencyContexts.Normalize(account.Currency), Today());
    }

    private static RecurringDto ToDto(RecurringItem r, string currency, DateOnly today) =>
        new(r.Id, r.AccountId, r.Payee, currency, r.AmountMinor / 100m, r.Frequency.ToString(),
            RecurringSchedule.NextOnOrAfter(r.NextDue, r.Frequency, today), r.Status.ToString(), r.LastSeen, r.IsManual);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);
    private static IResult Invalid(string message) => Results.BadRequest(new { error = message });
}
