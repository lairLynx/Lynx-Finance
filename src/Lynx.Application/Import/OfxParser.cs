using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Lynx.Application.Import;

/// <summary>Reads OFX 1.x (SGML) and 2.x (XML) statements by scanning STMTTRN blocks.</summary>
public static partial class OfxParser
{
    [GeneratedRegex(@"<STMTTRN>(.*?)(?:</STMTTRN>|(?=<STMTTRN>)|(?=</BANKTRANLIST>)|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();

    public static List<ParsedTransaction> Parse(string text)
    {
        var result = new List<ParsedTransaction>();
        foreach (Match block in BlockRegex().Matches(text))
        {
            var body = block.Groups[1].Value;
            var posted = Tag(body, "DTPOSTED");
            var amount = AmountParser.ParseMinor(Tag(body, "TRNAMT"));
            if (posted is null || amount is null || posted.Length < 8) continue;
            if (!DateOnly.TryParseExact(posted[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;

            var name = Tag(body, "NAME") ?? Tag(body, "PAYEE") ?? "";
            var memo = Tag(body, "MEMO") ?? "";
            if (name.Length == 0) { name = memo; memo = ""; }
            result.Add(new ParsedTransaction(date, amount.Value, Truncate(name, 200), Truncate(memo, 500), Tag(body, "FITID")));
        }
        if (result.Count == 0) throw new ImportFormatException("No transactions found in the OFX file.");
        return result;
    }

    private static string? Tag(string block, string tag)
    {
        var m = Regex.Match(block, $@"<{tag}>\s*([^<\r\n]*)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var value = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
        return value.Length == 0 ? null : value;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
