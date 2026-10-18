using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Lynx.Application.Import;

/// <summary>A piece of text placed on a page. Coordinates are PDF points, origin bottom-left.</summary>
public record TextRun(double X, double Y, double Right, string Text);

/// <summary>
/// Minimal text extractor for simple, machine-generated PDFs (Flate content streams, literal-string text
/// operators in single-byte fonts). It is deliberately not a general PDF reader: encrypted files, scans and
/// CID/hex-encoded text are not supported, which is enough for bank statement exports.
/// </summary>
public static class PdfTextExtractor
{
    private const int MaxStreams = 2000;
    private const long MaxInflatedBytes = 50 * 1024 * 1024;

    /// <summary>Returns one list of text runs per content stream, in file order (one per page for statement PDFs).</summary>
    public static List<List<TextRun>> ExtractPages(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        if (!text.StartsWith("%PDF", StringComparison.Ordinal)) throw new ImportFormatException("The file is not a PDF.");
        if (text.Contains("/Encrypt", StringComparison.Ordinal)) throw new ImportFormatException("Encrypted PDFs are not supported.");

        var pages = new List<List<TextRun>>();
        var pos = 0;
        var streams = 0;
        while (streams++ < MaxStreams)
        {
            var start = text.IndexOf("stream", pos, StringComparison.Ordinal);
            if (start < 0) break;
            var dataStart = start + "stream".Length;
            if (start >= 3 && text.AsSpan(start - 3, 3).SequenceEqual("end")) { pos = dataStart; continue; } // "endstream"
            if (dataStart < text.Length && text[dataStart] == '\r') dataStart++;
            if (dataStart < text.Length && text[dataStart] == '\n') dataStart++;
            var end = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (end < 0) break;
            pos = end + "endstream".Length;

            var content = Inflate(pdf.AsSpan(dataStart, end - dataStart));
            if (content is null || !(content.Contains(" Tj", StringComparison.Ordinal) || content.Contains("TJ", StringComparison.Ordinal))) continue;
            var runs = ParseContent(content);
            if (runs.Count > 0) pages.Add(runs);
        }
        return pages;
    }

    private static string? Inflate(ReadOnlySpan<byte> raw)
    {
        // Trim the end-of-line before "endstream" so the zlib checksum lines up.
        try
        {
            using var input = new MemoryStream(raw.ToArray());
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = z.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, n);
                if (output.Length > MaxInflatedBytes) return null;
            }
            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch (InvalidDataException)
        {
            // Not Flate (image data, fonts, or an uncompressed content stream).
            var s = Encoding.Latin1.GetString(raw);
            return s.Contains("BT", StringComparison.Ordinal) ? s : null;
        }
    }

    private static List<TextRun> ParseContent(string s)
    {
        var runs = new List<TextRun>();
        var operands = new List<object>();
        double a = 1, x = 0, y = 0, fontSize = 1;
        var i = 0;

        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(') { operands.Add(ReadLiteral(s, ref i)); continue; }
            if (c == '<')
            {
                if (i + 1 < s.Length && s[i + 1] == '<') { i += 2; continue; } // dictionary start
                var close = s.IndexOf('>', i);
                i = close < 0 ? s.Length : close + 1; // hex strings are CID glyph ids: skipped
                operands.Add(new HexString());
                continue;
            }
            if (c == '>') { i++; continue; }
            if (c == '[') { operands.Add(ReadArray(s, ref i)); continue; }
            if (c == '%') { while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++; continue; }
            if (c == '/') { i++; var st = i; while (i < s.Length && !IsDelimiter(s[i])) i++; operands.Add("/" + s[st..i]); continue; }

            var tokenStart = i;
            while (i < s.Length && !IsDelimiter(s[i])) i++;
            if (i == tokenStart) { i++; continue; }
            var token = s[tokenStart..i];

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                operands.Add(number);
                continue;
            }

            switch (token)
            {
                case "BT":
                    a = 1; x = 0; y = 0;
                    break;
                case "Tm" when operands.Count >= 6 && Numbers(operands, 6) is { } m:
                    a = m[0]; x = m[4]; y = m[5];
                    break;
                case "Td" or "TD" when operands.Count >= 2 && Numbers(operands, 2) is { } d:
                    x += d[0] * a; y += d[1] * a;
                    break;
                case "Tf" when operands.Count >= 1 && operands[^1] is double size:
                    fontSize = size;
                    break;
                case "Tj" when operands.Count >= 1 && operands[^1] is string str:
                    Emit(str);
                    break;
                case "TJ" when operands.Count >= 1 && operands[^1] is List<object> parts:
                    Emit(string.Concat(parts.Select(p => p switch { string t => t, double k when k < -200 => " ", _ => "" })));
                    break;
            }
            operands.Clear();
        }
        return runs;

        void Emit(string text)
        {
            if (text.Length == 0) return;
            var size = Math.Abs(a * fontSize);
            var width = EstimateWidth(text, size);
            if (text.Trim().Length > 0) runs.Add(new TextRun(x, y, x + width, text));
            x += width; // consecutive Tj calls continue along the line
        }
    }

    private static double[]? Numbers(List<object> operands, int count)
    {
        var result = new double[count];
        for (var k = 0; k < count; k++)
        {
            if (operands[^(count - k)] is not double d) return null;
            result[k] = d;
        }
        return result;
    }

    private static bool IsDelimiter(char c) => char.IsWhiteSpace(c) || c is '(' or ')' or '<' or '>' or '[' or ']' or '/' or '%';

    private sealed class HexString;

    private static List<object> ReadArray(string s, ref int i)
    {
        var items = new List<object>();
        i++; // [
        while (i < s.Length && s[i] != ']')
        {
            var c = s[i];
            if (c == '(') items.Add(ReadLiteral(s, ref i));
            else if (c == '<') { var close = s.IndexOf('>', i); i = close < 0 ? s.Length : close + 1; }
            else if (char.IsWhiteSpace(c)) i++;
            else
            {
                var st = i;
                while (i < s.Length && !IsDelimiter(s[i]) && s[i] != ']') i++;
                if (i == st) { i++; continue; }
                if (double.TryParse(s[st..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) items.Add(n);
            }
        }
        i++; // ]
        return items;
    }

    private static string ReadLiteral(string s, ref int i)
    {
        var sb = new StringBuilder();
        var depth = 0;
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                var n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '\r': if (i + 1 < s.Length && s[i + 1] == '\n') i++; break;
                    case '\n': break;
                    case >= '0' and <= '7':
                        var code = n - '0';
                        for (var k = 0; k < 2 && i + 1 < s.Length && s[i + 1] is >= '0' and <= '7'; k++) code = code * 8 + (s[++i] - '0');
                        sb.Append((char)(code & 0xFF));
                        break;
                    default: sb.Append(n); break;
                }
            }
            else if (c == '(') { if (depth++ > 0) sb.Append(c); }
            else if (c == ')') { if (--depth == 0) { i++; return sb.ToString(); } sb.Append(c); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // Helvetica advance widths (per 1000 units) are close enough to place right edges within a couple of points.
    private static double EstimateWidth(string text, double size)
    {
        double units = 0;
        foreach (var ch in text)
            units += ch switch
            {
                ' ' or '.' or ',' or ':' or ';' or '/' or '!' or 'I' or 'i' or 'j' or 'l' or '|' or '\'' => 278,
                'f' or 't' or 'r' or '(' or ')' or '-' or '[' or ']' => 333,
                'm' or 'M' or 'W' => 833,
                'w' => 722,
                >= 'A' and <= 'Z' => 680,
                >= '0' and <= '9' => 556,
                _ => 540,
            };
        return units / 1000 * size;
    }
}
