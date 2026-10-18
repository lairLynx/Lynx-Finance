namespace Lynx.Domain.Entities;

public enum AccountType
{
    Current = 0,
    Savings = 1,
    CreditCard = 2,
    Cash = 3,
}

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Bank { get; set; }
    public string? Iban { get; set; }
    public AccountType Type { get; set; }
    public string Currency { get; set; } = "EUR";

    /// <summary>Balance before the first stored transaction, in minor units (cents).</summary>
    public long OpeningBalanceMinor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastImportedAt { get; set; }

    public List<Transaction> Transactions { get; set; } = [];
}
