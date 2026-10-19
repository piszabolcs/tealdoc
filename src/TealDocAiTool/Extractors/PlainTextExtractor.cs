using System.IO;
using System.Text;
using TealDocAiTool.Models;

namespace TealDocAiTool.Extractors;

/// <summary>.txt / .csv — nincs heurisztika-lépcső (alacsony volumen, nincs megbízható
/// szerkezeti jel a "cím"-re), egyenesen az LLM kapja a szöveget.</summary>
public sealed class PlainTextExtractor : IDocumentExtractor
{
    private static readonly string[] SupportedExtensions = { ".txt", ".csv" };

    private const int MaxTextChars = 4000;

    /// <summary>Ennyi karaktert olvasunk a fájl elejéről (a vezető üres sorok miatt több, mint
    /// amennyi kell). Egy 64 MB-os CSV-ből is legfeljebb ~64 KB kerül a memóriába — korábban az
    /// egész fájl, 4000 karakterért.</summary>
    private const int HeadChars = 16_000;

    static PlainTextExtractor()
    {
        // A cp1250 a .NET-en regisztrációt igényel (idempotens; a tesztek nem futtatják az App-ot).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool CanHandle(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());

    public ExtractedDocument? Extract(string filePath)
    {
        string text = ReadHead(filePath).Trim();
        if (text.Length == 0) return null;
        return new ExtractedDocument(text.Length > MaxTextChars ? text[..MaxTextChars] : text, Array.Empty<string>());
    }

    private static string ReadHead(string filePath)
    {
        byte[] bytes;
        bool wholeFile;
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int length = (int)Math.Min(stream.Length, HeadChars * 4L + 4);
            bytes = new byte[length];
            stream.ReadExactly(bytes);
            wholeFile = length == stream.Length;
        }
        string text = Decode(bytes, wholeFile);
        return text.Length > HeadChars ? text[..HeadChars] : text;
    }

    /// <summary>
    /// Kódlap: BOM szerint; BOM nélkül SZIGORÚ UTF-8, és ha az érvénytelen, Windows-1250 — a BOM
    /// nélküli magyar szövegfájlok tipikus kódlapja, amelyet UTF-8-ként olvasva az ékezetek
    /// U+FFFD-vé romlottak („M?szak?tad?si jegyz?k?nyv”).
    /// </summary>
    private static string Decode(byte[] bytes, bool wholeFile)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return Encoding.UTF8.GetString(span[3..]);
        if (span.StartsWith(new byte[] { 0xFF, 0xFE })) return Encoding.Unicode.GetString(span[2..^(span.Length % 2)]);
        if (span.StartsWith(new byte[] { 0xFE, 0xFF })) return Encoding.BigEndianUnicode.GetString(span[2..^(span.Length % 2)]);

        try
        {
            // flush=false, ha a fájl hosszabb az olvasottnál: a levágott utolsó (több bájtos)
            // karakter így nem minősül érvénytelennek, és nem vált át tévesen cp1250-re.
            var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
            var chars = new char[bytes.Length];
            int count = decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: wholeFile);
            return new string(chars, 0, count);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1250).GetString(bytes);
        }
    }
}
