namespace Lynx.Domain.Entities;

/// <summary>Planned spending for one category in one month, in one currency.</summary>
public class Budget
{
    public int Id { get; set; }

    /// <summary>"yyyy-MM".</summary>
    public string Month { get; set; } = "";

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public string Currency { get; set; } = "EUR";
    public long AmountMinor { get; set; }
}
