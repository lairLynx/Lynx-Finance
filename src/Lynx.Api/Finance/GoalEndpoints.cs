using Lynx.Domain.Entities;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Api.Finance;

public record GoalInput(string? Name, string? Currency, decimal? Target, DateOnly? TargetDate, int? AccountId, decimal? Saved);
public record ContributionInput(decimal? Amount);
public record GoalDto(
    int Id, string Name, string Currency, decimal Target, decimal Saved, decimal Remaining, DateOnly? TargetDate,
    int? AccountId, decimal? MonthlyNeeded, string Status);

/// <summary>
/// Savings goals. Each goal lives in one currency (never converted). Progress is either the live balance of a linked
/// account or a manually adjusted amount.
/// </summary>
public static class GoalEndpoints
{
    public static IEndpointRouteBuilder MapGoalEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/goals", async (AppDbContext db) =>
        {
            var goals = await db.Goals.AsNoTracking().OrderBy(g => g.TargetDate == null).ThenBy(g => g.TargetDate).ThenBy(g => g.Id).ToListAsync();
            var balances = await AccountBalances(db, goals.Where(g => g.AccountId is not null).Select(g => g.AccountId!.Value).ToList());
            return goals.Select(g => ToDto(g, g.AccountId is { } id ? balances.GetValueOrDefault(id) : g.SavedMinor)).ToList();
        });

        api.MapPost("/goals", async (GoalInput body, AppDbContext db) =>
        {
            var goal = new Goal { CreatedOn = DateOnly.FromDateTime(DateTime.Today) };
            if (await Apply(goal, body, db) is { } error) return error;

            var saved = goal.AccountId is { } id ? (await AccountBalances(db, [id])).GetValueOrDefault(id) : goal.SavedMinor;
            goal.StartMinor = saved;
            db.Goals.Add(goal);
            await db.SaveChangesAsync();
            return Results.Created($"/api/goals/{goal.Id}", ToDto(goal, saved));
        });

        api.MapPut("/goals/{id:int}", async (int id, GoalInput body, AppDbContext db) =>
        {
            var goal = await db.Goals.FindAsync(id);
            if (goal is null) return Results.NotFound();
            if (await Apply(goal, body, db) is { } error) return error;
            await db.SaveChangesAsync();
            var saved = goal.AccountId is { } accountId ? (await AccountBalances(db, [accountId])).GetValueOrDefault(accountId) : goal.SavedMinor;
            return Results.Ok(ToDto(goal, saved));
        });

        api.MapDelete("/goals/{id:int}", async (int id, AppDbContext db) =>
        {
            var goal = await db.Goals.FindAsync(id);
            if (goal is null) return Results.NotFound();
            db.Goals.Remove(goal);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Adds (or, when negative, withdraws) manually tracked savings.
        api.MapPost("/goals/{id:int}/contribute", async (int id, ContributionInput body, AppDbContext db) =>
        {
            var goal = await db.Goals.FindAsync(id);
            if (goal is null) return Results.NotFound();
            if (goal.AccountId is not null) return Invalid("This goal follows an account balance; add money to the account instead.");
            if (body.Amount is null or 0 || Math.Abs(body.Amount.Value) > 1_000_000_000m) return Invalid("Enter a non-zero amount.");
            goal.SavedMinor = Math.Max(0, goal.SavedMinor + ToMinor(body.Amount.Value));
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(goal, goal.SavedMinor));
        });

        return api;
    }

    private static async Task<IResult?> Apply(Goal goal, GoalInput body, AppDbContext db)
    {
        var name = body.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100) return Invalid("Goal name is required (max 100 characters).");
        if (body.Target is null or <= 0 || body.Target > 1_000_000_000m) return Invalid("Target must be greater than zero.");

        string currency;
        if (body.AccountId is { } accountId)
        {
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
            if (account is null) return Invalid("Unknown account.");
            currency = CurrencyContexts.Normalize(account.Currency); // a linked goal always uses the account's currency
        }
        else
        {
            currency = CurrencyContexts.Normalize((body.Currency ?? "").Trim());
            if (currency.Length != 3) return Invalid("Choose a currency.");
        }

        goal.Name = name;
        goal.Currency = currency;
        goal.TargetMinor = ToMinor(body.Target.Value);
        goal.TargetDate = body.TargetDate;
        goal.AccountId = body.AccountId;
        if (body.AccountId is null && body.Saved is { } saved)
        {
            if (saved < 0 || saved > 1_000_000_000m) return Invalid("Saved amount is out of range.");
            goal.SavedMinor = ToMinor(saved);
        }
        if (body.AccountId is not null) goal.SavedMinor = 0;
        return null;
    }

    private static async Task<Dictionary<int, long>> AccountBalances(AppDbContext db, List<int> ids)
    {
        if (ids.Count == 0) return [];
        var sums = await db.Transactions.Where(t => ids.Contains(t.AccountId)).GroupBy(t => t.AccountId)
            .Select(g => new { g.Key, Sum = g.Sum(t => t.AmountMinor) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
        var openings = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.OpeningBalanceMinor);
        return ids.Distinct().ToDictionary(id => id, id => openings.GetValueOrDefault(id) + sums.GetValueOrDefault(id));
    }

    internal static GoalDto ToDto(Goal g, long savedMinor, DateOnly? today = null)
    {
        var now = today ?? DateOnly.FromDateTime(DateTime.Today);
        var remaining = Math.Max(0, g.TargetMinor - savedMinor);
        decimal? monthly = null;
        string status;

        if (savedMinor >= g.TargetMinor) status = "Completed";
        else if (g.TargetDate is not { } due) status = "NoDate";
        else if (due < now) status = "Behind"; // deadline passed
        else
        {
            var monthsLeft = Math.Max(1, (int)Math.Ceiling((due.DayNumber - now.DayNumber) / 30.4375));
            monthly = Math.Ceiling(remaining / (decimal)monthsLeft) / 100m;

            // On track = at or above a straight line from the starting amount (at creation) to the target (at the due date).
            var totalDays = Math.Max(1, due.DayNumber - g.CreatedOn.DayNumber);
            var elapsed = Math.Clamp(now.DayNumber - g.CreatedOn.DayNumber, 0, totalDays);
            var expected = g.StartMinor + (g.TargetMinor - g.StartMinor) * elapsed / (decimal)totalDays;
            status = savedMinor >= expected ? "OnTrack" : "Behind";
        }

        return new GoalDto(g.Id, g.Name, g.Currency, g.TargetMinor / 100m, savedMinor / 100m, remaining / 100m, g.TargetDate, g.AccountId, monthly, status);
    }

    private static IResult Invalid(string message) => Results.BadRequest(new { error = message });
    private static long ToMinor(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
