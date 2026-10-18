namespace Lynx.Domain.Entities;

public class Goal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public long TargetMinor { get; set; }
    public DateOnly? TargetDate { get; set; }

    /// <summary>When set, progress is the live balance of this account and contributions are not used.</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Manually tracked savings (ignored when linked to an account).</summary>
    public long SavedMinor { get; set; }

    /// <summary>Amount saved when the goal was created; the "on track" schedule runs from here to the target.</summary>
    public long StartMinor { get; set; }

    public DateOnly CreatedOn { get; set; }
}
