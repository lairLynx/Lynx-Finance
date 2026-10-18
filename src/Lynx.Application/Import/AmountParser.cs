using System.Globalization;
using System.Text;

namespace Lynx.Application.Import;

public static class AmountParser
{
    /// <summary>
    /// Parses "1.234,56", "1,234.56", "-12,50", "(12.50)", "€ 3 650,00" into signed minor units.
    /// Returns null for blank/unparseable input.
    /// </summary>
    public static long? ParseMinor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var negative = (s.StartsWith('(') && s.EndsWith(')')) || s.StartsWith('-') || s.EndsWith('-') || s.StartsWith('−');

        var sb = new StringBuilder();
        foreach (var c in s)
            if (char.IsAsciiDigit(c) || c is '.' or ',') sb.Append(c);
        var digits = sb.ToString();
        if (!digits.Any(char.IsAsciiDigit)) return null;

        var lastDot = digits.LastIndexOf('.');
        var lastComma = digits.LastIndexOf(',');
        var decimalPos = -1;
        if (lastDot >= 0 && lastComma >= 0)
            decimalPos = Math.Max(lastDot, lastComma);
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var pos = Math.Max(lastDot, lastComma);
            var count = digits.Count(c => c == sep);
            var after = digits.Length - pos - 1;
            var intPart = digits[..pos];
            // A single separator followed by exactly 3 digits is a thousands separator ("1,234"), unless the integer part is 0.
            var thousands = count > 1 || (after == 3 && intPart != "0");
            if (!thousands) decimalPos = pos;
        }

        string whole, frac;
        if (decimalPos >= 0)
        {
            whole = new string(digits[..decimalPos].Where(char.IsAsciiDigit).ToArray());
            frac = new string(digits[(decimalPos + 1)..].Where(char.IsAsciiDigit).ToArray());
        }
        else
        {
            whole = new string(digits.Where(char.IsAsciiDigit).ToArray());
            frac = "";
        }
        if (whole.Length == 0) whole = "0";
        frac = (frac + "00")[..2];

        if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var w) || w > long.MaxValue / 100 - 1) return null;
        var minor = w * 100 + long.Parse(frac, CultureInfo.InvariantCulture);
        return negative ? -minor : minor;
    }
}
