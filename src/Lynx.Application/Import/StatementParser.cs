using System.Text;

namespace Lynx.Application.Import;

public static class StatementParser
{
    static StatementParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Picks a parser from the file contents (falling back to the extension) and returns normalised rows.</summary>
    public static List<ParsedTransaction> Parse(string fileName, byte[] content)
    {
        if (content.Length > 4 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F')
            return ParsePdf(content);

        var text = Decode(content);
        var head = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (head.Contains("<Ntry>", StringComparison.Ordinal) || head.Contains("<Ntry ", StringComparison.Ordinal) || head.Contains("BkToCstmrStmt", StringComparison.Ordinal) || head.Contains("camt.05", StringComparison.Ordinal))
            return CamtParser.Parse(text);
        if (head.StartsWith("OFXHEADER", StringComparison.OrdinalIgnoreCase) || head.Contains("<OFX>", StringComparison.OrdinalIgnoreCase) || ext is ".ofx" or ".qfx")
            return OfxParser.Parse(text);
        if (ext == ".xml")
            return CamtParser.Parse(text);
        if (ext is ".csv" or ".txt" or ".tsv" or "")
        {
            if (IngRoStatementParser.Looks(text)) return IngRoStatementParser.Parse(text);
            return CsvStatementParser.Parse(text);
        }

        throw new ImportFormatException("Unsupported file type. Use CSV, OFX or CAMT (XML).");
    }

    private static List<ParsedTransaction> ParsePdf(byte[] content)
    {
        var pages = PdfTextExtractor.ExtractPages(content);
        if (BtPdfStatementParser.Looks(pages)) return BtPdfStatementParser.Parse(pages);
        throw new ImportFormatException("This PDF layout isn't supported. Banca Transilvania statements work; for other banks export CSV, OFX or CAMT.");
    }

    /// <summary>UTF-8 when valid, otherwise Windows-1250 (common for Central/Eastern European bank exports).</summary>
    private static string Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1250).GetString(content);
        }
    }
}
