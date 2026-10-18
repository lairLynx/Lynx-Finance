namespace Lynx.Domain.Entities;

public enum RecurringFrequency
{
    Weekly = 0,
    Biweekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
}

public enum RecurringStatus
{
    /// <summary>Found automatically; not used by the forecast until confirmed.</summary>
    Detected = 0,
    Confirmed = 1,
    /// <summary>The user said "not recurring"; detection never brings it back.</summary>
    Dismissed = 2,
}

/// <summary>A bill, subscription or income that repeats on a schedule.</summary>
public class RecurringItem
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public string Payee { get; set; } = "";

    /// <summary>Normalised payee prefixed with the direction ("-" money out, "+" money in); unique per account.</summary>
    public string PayeeKey { get; set; } = "";

    /// <summary>Typical signed amount in minor units (cents).</summary>
    public long AmountMinor { get; set; }

    public RecurringFrequency Frequency { get; set; }
    public DateOnly NextDue { get; set; }
    public DateOnly? LastSeen { get; set; }
    public RecurringStatus Status { get; set; }

    /// <summary>Created by the user rather than found in transactions.</summary>
    public bool IsManual { get; set; }
}
