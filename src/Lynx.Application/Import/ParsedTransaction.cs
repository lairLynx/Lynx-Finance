namespace Lynx.Application.Import;

/// <summary>One statement row, normalised across CSV/OFX/CAMT. Amount is signed minor units.</summary>
public record ParsedTransaction(DateOnly Date, long AmountMinor, string Payee, string Description, string? ExternalId, string? CounterpartyIban = null)
{
    /// <summary>Upper-case, no spaces; null when blank.</summary>
    public static string? NormalizeIban(string? raw)
    {
        var s = raw?.Replace(" ", "").ToUpperInvariant();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}

public class ImportFormatException(string message) : Exception(message);
