using Lynx.Domain.Entities;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Api.Finance;

/// <summary>The currencies the user has accounts in, the one being viewed, and the accounts in it.</summary>
public record CurrencyContext(string[] Currencies, string? Selected, List<Account> Accounts);

public static class CurrencyContexts
{
    /// <summary>The Romanian "LEI" label and the ISO code RON are the same currency.</summary>
    public static string Normalize(string code) => code.ToUpperInvariant() is "LEI" or "LEU" ? "RON" : code.ToUpperInvariant();

    /// <summary>Uses the requested currency when the user has an account in it, otherwise the busiest one.</summary>
    public static async Task<CurrencyContext> ResolveAsync(AppDbContext db, string? requested)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        var currencies = accounts.Select(a => Normalize(a.Currency)).Distinct().Order().ToArray();
        if (currencies.Length == 0) return new CurrencyContext([], null, []);

        var wanted = requested?.ToUpperInvariant();
        string selected;
        if (wanted is not null && currencies.Contains(wanted)) selected = wanted;
        else
        {
            var counts = await db.Transactions.GroupBy(t => t.AccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            selected = currencies.OrderByDescending(c => accounts.Where(a => Normalize(a.Currency) == c).Sum(a => counts.GetValueOrDefault(a.Id))).First();
        }
        return new CurrencyContext(currencies, selected, accounts.Where(a => Normalize(a.Currency) == selected).ToList());
    }
}
