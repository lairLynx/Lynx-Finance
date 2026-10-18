namespace Lynx.Domain.Entities;

public class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public DateOnly Date { get; set; }
    public string Payee { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Signed amount in minor units (cents): negative = money out.</summary>
    public long AmountMinor { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Money moved between the user's own accounts: kept in balances, excluded from income, spending and categories.</summary>
    public bool IsTransfer { get; set; }

    /// <summary>The user decided (marked or unmarked); automatic detection must leave this row alone.</summary>
    public bool TransferLocked { get; set; }

    /// <summary>IBAN of the other party when the statement shows one; used to spot transfers to the user's own accounts.</summary>
    public string? CounterpartyIban { get; set; }

    /// <summary>Identifies an imported row so re-importing an overlapping file doesn't duplicate it. Null for manual entries.</summary>
    public string? ImportKey { get; set; }
}
