using Lynx.Application.Import;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Api.Finance;

/// <summary>
/// Finds money moved between the user's own accounts so it isn't counted as both spending and income.
/// Two rules, applied only to rows the user hasn't decided on (TransferLocked == false):
/// 1. Pairing: an outgoing and an incoming row of the same amount and currency on different accounts, at most
///    <see cref="MaxDaysApart"/> days apart.
/// 2. Own IBAN: the statement names a counterparty IBAN that belongs to another of the user's accounts.
/// </summary>
public static class TransferDetector
{
    public const int MaxDaysApart = 3;

    public static async Task<int> DetectAsync(AppDbContext db, CancellationToken ct = default)
    {
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
        var currency = accounts.ToDictionary(a => a.Id, a => Currency(a.Currency));
        var ibanOwner = accounts
            .Where(a => ParsedTransaction.NormalizeIban(a.Iban) is not null)
            .GroupBy(a => ParsedTransaction.NormalizeIban(a.Iban)!)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var candidates = await db.Transactions.Where(t => !t.IsTransfer && !t.TransferLocked).ToListAsync(ct);
        var marked = 0;

        // Rule 1: pair opposite amounts, closest dates first, each row used at most once.
        var incoming = candidates.Where(t => t.AmountMinor > 0)
            .GroupBy(t => (Currency: currency.GetValueOrDefault(t.AccountId, ""), t.AmountMinor))
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Date).ToList());
        foreach (var outgoing in candidates.Where(t => t.AmountMinor < 0).OrderBy(t => t.Date))
        {
            if (outgoing.IsTransfer) continue;
            var key = (Currency: currency.GetValueOrDefault(outgoing.AccountId, ""), AmountMinor: -outgoing.AmountMinor);
            if (!incoming.TryGetValue(key, out var matches)) continue;

            var match = matches
                .Where(m => !m.IsTransfer && m.AccountId != outgoing.AccountId
                            && Math.Abs(m.Date.DayNumber - outgoing.Date.DayNumber) <= MaxDaysApart)
                .OrderBy(m => Math.Abs(m.Date.DayNumber - outgoing.Date.DayNumber))
                .FirstOrDefault();
            if (match is null) continue;

            outgoing.IsTransfer = true;
            match.IsTransfer = true;
            marked += 2;
        }

        // Rule 2: counterparty is another of the user's own accounts.
        foreach (var t in candidates.Where(t => !t.IsTransfer && t.CounterpartyIban is not null))
        {
            if (ibanOwner.TryGetValue(t.CounterpartyIban!, out var ownerId) && ownerId != t.AccountId)
            {
                t.IsTransfer = true;
                marked++;
            }
        }

        if (marked > 0) await db.SaveChangesAsync(ct);
        return marked;
    }

    private static string Currency(string code) => code.ToUpperInvariant() is "LEI" or "LEU" ? "RON" : code.ToUpperInvariant();
}
