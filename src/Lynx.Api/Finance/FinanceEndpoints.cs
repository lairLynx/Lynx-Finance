using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lynx.Application.Import;
using Lynx.Domain.Entities;
using Lynx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lynx.Api.Finance;

public record AccountInput(string? Name, string? Bank, string? Iban, string? Type, string? Currency, decimal? OpeningBalance);
public record AccountDto(int Id, string Name, string? Bank, string? Iban, string Type, string Currency, decimal Balance, decimal OpeningBalance,
    int TransactionCount, DateOnly? LastTransactionDate, DateTime? LastImportedAt);

public record CategoryInput(string? Name, string? Color, bool? IsIncome);
public record CategoryDto(int Id, string Name, string Color, bool IsIncome);

public record TransactionInput(int? AccountId, DateOnly? Date, string? Payee, string? Description, decimal? Amount, int? CategoryId, bool? IsTransfer = null);
public record TransactionDto(int Id, int AccountId, DateOnly Date, string Payee, string Description, decimal Amount, int? CategoryId, bool IsTransfer);
public record TransactionPage(List<TransactionDto> Items, int Total, int Page, int PageSize);
public record ImportResult(int Total, int Imported, int Duplicates, int TransfersMarked);
public record TransferChange(bool IsTransfer, bool ApplyToPayee);
public record TransferResult(int Updated, TransactionDto Transaction);
public record DetectResult(int Marked);

public static partial class FinanceEndpoints
{
    private const long MaxImportBytes = 10 * 1024 * 1024;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyCode();

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder api)
    {
        MapAccounts(api);
        MapCategories(api);
        MapTransactions(api);
        return api;
    }

    private static decimal ToMoney(long minor) => minor / 100m;
    private static long ToMinor(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    private static IResult Invalid(string message) => Results.BadRequest(new { error = message });

    // ---------- accounts ----------

    private static void MapAccounts(IEndpointRouteBuilder api)
    {
        api.MapGet("/accounts", async (AppDbContext db) =>
        {
            var stats = await db.Transactions
                .GroupBy(t => t.AccountId)
                .Select(g => new { AccountId = g.Key, Sum = g.Sum(t => t.AmountMinor), Count = g.Count(), Last = g.Max(t => t.Date) })
                .ToDictionaryAsync(x => x.AccountId);
            var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
            return accounts.Select(a => ToDto(a, stats.GetValueOrDefault(a.Id)?.Sum ?? 0, stats.GetValueOrDefault(a.Id)?.Count ?? 0, stats.GetValueOrDefault(a.Id)?.Last));
        });

        api.MapPost("/accounts", async (AccountInput body, AppDbContext db) =>
        {
            var account = new Account();
            if (Apply(account, body) is { } error) return Invalid(error);
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            return Results.Created($"/api/accounts/{account.Id}", ToDto(account, 0, 0, null));
        });

        api.MapPut("/accounts/{id:int}", async (int id, AccountInput body, AppDbContext db) =>
        {
            var account = await db.Accounts.FindAsync(id);
            if (account is null) return Results.NotFound();
            if (Apply(account, body) is { } error) return Invalid(error);
            await db.SaveChangesAsync();
            var sum = await db.Transactions.Where(t => t.AccountId == id).SumAsync(t => (long?)t.AmountMinor) ?? 0;
            var count = await db.Transactions.CountAsync(t => t.AccountId == id);
            var last = await db.Transactions.Where(t => t.AccountId == id).MaxAsync(t => (DateOnly?)t.Date);
            return Results.Ok(ToDto(account, sum, count, last));
        });

        api.MapDelete("/accounts/{id:int}", async (int id, AppDbContext db) =>
        {
            var account = await db.Accounts.FindAsync(id);
            if (account is null) return Results.NotFound();
            db.Accounts.Remove(account);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/accounts/{id:int}/import", async (int id, IFormFile file, AppDbContext db) =>
        {
            var account = await db.Accounts.FindAsync(id);
            if (account is null) return Results.NotFound();
            if (file.Length == 0) return Invalid("The file is empty.");
            if (file.Length > MaxImportBytes) return Invalid("The file is larger than 10 MB.");

            byte[] bytes;
            await using (var stream = file.OpenReadStream())
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            List<ParsedTransaction> parsed;
            try { parsed = StatementParser.Parse(file.FileName, bytes); }
            catch (ImportFormatException ex) { return Invalid(ex.Message); }

            // Same-day identical rows are legitimate (two coffees), so each repeat of a key gets a counter suffix.
            var seen = new Dictionary<string, int>();
            var keyed = parsed.Select(p =>
            {
                var baseKey = p.ExternalId is { Length: > 0 } ext
                    ? "x:" + ext
                    : "h:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{p.Date:yyyy-MM-dd}|{p.AmountMinor}|{p.Payee.ToLowerInvariant()}|{p.Description.ToLowerInvariant()}")))[..24];
                var n = seen.GetValueOrDefault(baseKey);
                seen[baseKey] = n + 1;
                return (Row: p, Key: $"{baseKey}#{n}");
            }).ToList();

            var existing = (await db.Transactions.Where(t => t.AccountId == id && t.ImportKey != null).Select(t => t.ImportKey!).ToListAsync()).ToHashSet();
            var fresh = keyed.Where(k => !existing.Contains(k.Key)).ToList();

            db.Transactions.AddRange(fresh.Select(k => new Transaction
            {
                AccountId = id,
                Date = k.Row.Date,
                Payee = k.Row.Payee,
                Description = k.Row.Description,
                AmountMinor = k.Row.AmountMinor,
                ImportKey = k.Key,
                CounterpartyIban = k.Row.CounterpartyIban,
            }));
            account.LastImportedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var marked = fresh.Count > 0 ? await TransferDetector.DetectAsync(db) : 0;
            if (fresh.Count > 0) await RecurringEndpoints.DetectAsync(db);
            return Results.Ok(new ImportResult(keyed.Count, fresh.Count, keyed.Count - fresh.Count, marked));
        }).DisableAntiforgery(); // CSRF is covered by the required X-Lynx-Request header + SameSite=Strict cookie.
    }

    private static string? Apply(Account account, AccountInput body)
    {
        var name = body.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100) return "Account name is required (max 100 characters).";
        if (!Enum.TryParse<AccountType>(body.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type)) return "Unknown account type.";
        var currency = (body.Currency ?? "EUR").Trim().ToUpperInvariant();
        if (currency is "LEI" or "LEU") currency = "RON"; // statements label Romanian currency "LEI"; the ISO code is RON
        if (!CurrencyCode().IsMatch(currency)) return "Currency must be a 3-letter code such as EUR or RON.";
        var iban = body.Iban?.Replace(" ", "").Trim();
        if (iban is { Length: > 34 }) return "IBAN is too long.";
        var bank = body.Bank?.Trim();
        if (bank is { Length: > 100 }) return "Bank name is too long.";

        account.Name = name;
        account.Bank = string.IsNullOrEmpty(bank) ? null : bank;
        account.Iban = string.IsNullOrEmpty(iban) ? null : iban;
        account.Type = type;
        account.Currency = currency;
        account.OpeningBalanceMinor = ToMinor(body.OpeningBalance ?? 0);
        return null;
    }

    internal static AccountDto ToDto(Account a, long sumMinor, int count, DateOnly? last) =>
        new(a.Id, a.Name, a.Bank, a.Iban, a.Type.ToString(), a.Currency, ToMoney(a.OpeningBalanceMinor + sumMinor), ToMoney(a.OpeningBalanceMinor), count, last, a.LastImportedAt);

    // ---------- categories ----------

    private static void MapCategories(IEndpointRouteBuilder api)
    {
        api.MapGet("/categories", async (AppDbContext db) =>
            await db.Categories.AsNoTracking().OrderBy(c => c.Name).Select(c => new CategoryDto(c.Id, c.Name, c.Color, c.IsIncome)).ToListAsync());

        api.MapPost("/categories", async (CategoryInput body, AppDbContext db) =>
        {
            var category = new Category();
            if (await Apply(category, body, db) is { } error) return error;
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            return Results.Created($"/api/categories/{category.Id}", new CategoryDto(category.Id, category.Name, category.Color, category.IsIncome));
        });

        api.MapPut("/categories/{id:int}", async (int id, CategoryInput body, AppDbContext db) =>
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null) return Results.NotFound();
            if (await Apply(category, body, db) is { } error) return error;
            await db.SaveChangesAsync();
            return Results.Ok(new CategoryDto(category.Id, category.Name, category.Color, category.IsIncome));
        });

        api.MapDelete("/categories/{id:int}", async (int id, AppDbContext db) =>
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null) return Results.NotFound();
            db.Categories.Remove(category); // transactions fall back to uncategorized (FK SET NULL)
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<IResult?> Apply(Category category, CategoryInput body, AppDbContext db)
    {
        var name = body.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) return Invalid("Category name is required (max 60 characters).");
        var color = body.Color?.Trim() ?? "#4f8cff";
        if (!HexColor().IsMatch(color)) return Invalid("Color must look like #4f8cff.");
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != category.Id))
            return Results.Conflict(new { error = "A category with that name already exists." });

        category.Name = name;
        category.Color = color.ToLowerInvariant();
        category.IsIncome = body.IsIncome ?? false;
        return null;
    }

    // ---------- transactions ----------

    private static void MapTransactions(IEndpointRouteBuilder api)
    {
        api.MapGet("/transactions", async (AppDbContext db, int? accountId, int? categoryId, bool? uncategorized, string? q, DateOnly? from, DateOnly? to, bool? transfer, int? page, int? pageSize) =>
        {
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var pageNo = Math.Max(page ?? 1, 1);

            var query = db.Transactions.AsNoTracking().AsQueryable();
            if (accountId is not null) query = query.Where(t => t.AccountId == accountId);
            if (categoryId is not null) query = query.Where(t => t.CategoryId == categoryId);
            if (uncategorized == true) query = query.Where(t => t.CategoryId == null);
            if (transfer is not null) query = query.Where(t => t.IsTransfer == transfer);
            if (from is not null) query = query.Where(t => t.Date >= from);
            if (to is not null) query = query.Where(t => t.Date <= to);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(t => EF.Functions.Like(t.Payee, pattern, "\\") || EF.Functions.Like(t.Description, pattern, "\\"));
            }

            var total = await query.CountAsync();
            var items = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .Skip((pageNo - 1) * size).Take(size).ToListAsync();
            return new TransactionPage(items.Select(ToDto).ToList(), total, pageNo, size);
        });

        api.MapPost("/transactions", async (TransactionInput body, AppDbContext db) =>
        {
            var t = new Transaction();
            if (await Apply(t, body, db) is { } error) return error;
            db.Transactions.Add(t);
            await db.SaveChangesAsync();
            return Results.Created($"/api/transactions/{t.Id}", ToDto(t));
        });

        api.MapPut("/transactions/{id:int}", async (int id, TransactionInput body, AppDbContext db) =>
        {
            var t = await db.Transactions.FindAsync(id);
            if (t is null) return Results.NotFound();
            if (await Apply(t, body, db) is { } error) return error;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(t));
        });

        api.MapPatch("/transactions/{id:int}/transfer", async (int id, TransferChange body, AppDbContext db) =>
        {
            var t = await db.Transactions.FindAsync(id);
            if (t is null) return Results.NotFound();
            var updated = 0;
            var targets = body.ApplyToPayee
                ? await db.Transactions.Where(x => x.Payee == t.Payee).ToListAsync()
                : [t];
            foreach (var target in targets)
            {
                target.IsTransfer = body.IsTransfer;
                target.TransferLocked = true; // the user decided; automatic detection won't change it again
                updated++;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new TransferResult(updated, ToDto(t)));
        });

        api.MapPost("/transfers/detect", async (AppDbContext db) => new DetectResult(await TransferDetector.DetectAsync(db)));

        // Lightweight update used by the category dropdown in the list.
        api.MapPatch("/transactions/{id:int}/category", async (int id, CategoryAssignment body, AppDbContext db) =>
        {
            var t = await db.Transactions.FindAsync(id);
            if (t is null) return Results.NotFound();
            if (body.CategoryId is not null && !await db.Categories.AnyAsync(c => c.Id == body.CategoryId)) return Invalid("Unknown category.");
            t.CategoryId = body.CategoryId;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(t));
        });

        api.MapDelete("/transactions/{id:int}", async (int id, AppDbContext db) =>
        {
            var t = await db.Transactions.FindAsync(id);
            if (t is null) return Results.NotFound();
            db.Transactions.Remove(t);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    public record CategoryAssignment(int? CategoryId);

    private static async Task<IResult?> Apply(Transaction t, TransactionInput body, AppDbContext db)
    {
        if (body.AccountId is null || !await db.Accounts.AnyAsync(a => a.Id == body.AccountId)) return Invalid("Choose an account.");
        if (body.Date is null) return Invalid("Date is required.");
        var payee = body.Payee?.Trim() ?? "";
        if (payee.Length is 0 or > 200) return Invalid("Payee is required (max 200 characters).");
        var description = body.Description?.Trim() ?? "";
        if (description.Length > 500) return Invalid("Description is too long (max 500 characters).");
        if (body.Amount is null or 0) return Invalid("Amount is required.");
        if (Math.Abs(body.Amount.Value) > 1_000_000_000m) return Invalid("Amount is out of range.");
        if (body.CategoryId is not null && !await db.Categories.AnyAsync(c => c.Id == body.CategoryId)) return Invalid("Unknown category.");

        t.AccountId = body.AccountId.Value;
        t.Date = body.Date.Value;
        t.Payee = payee;
        t.Description = description;
        t.AmountMinor = ToMinor(body.Amount.Value);
        t.CategoryId = body.CategoryId;
        if (body.IsTransfer is { } isTransfer && isTransfer != t.IsTransfer)
        {
            t.IsTransfer = isTransfer;
            t.TransferLocked = true;
        }
        return null;
    }

    private static TransactionDto ToDto(Transaction t) =>
        new(t.Id, t.AccountId, t.Date, t.Payee, t.Description, ToMoney(t.AmountMinor), t.CategoryId, t.IsTransfer);
}
