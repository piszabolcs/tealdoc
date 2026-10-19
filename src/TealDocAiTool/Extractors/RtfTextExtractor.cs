using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TealDocAiTool.Models;

namespace TealDocAiTool.Extractors;

/// <summary>
/// .rtf — kis (43 fájlos) volumen miatt nincs teljes RTF-parser könyvtár behúzva, de a
/// naiv "regex mindent kiszed" megközelítés ÉLŐBEN ELLENŐRIZVE elrontja a magyar ékezetes
/// karaktereket (a \'XX hexa-escape-t space-re cserélné decode helyett) és beszivárogtatja
/// a betűkészlet-tábla tartalmát (pl. "Arial;") a szövegbe. Ezért mélység-követéssel skip-eli
/// a zajos csoportokat (fonttbl/colortbl/stb.) és cp1250-ként dekódolja a hexa-escape-eket.
/// </summary>
public sealed class RtfTextExtractor : IDocumentExtractor
{
    // A cp1250 codepage a .NET Core-on regisztrációt igényel. Az App.xaml.cs induláskor
    // regisztrálja, de az egységtesztek nem futtatják az App-ot — ezért itt is regisztráljuk
    // (idempotens), hogy az extractor önállóan, teszt-kontextusban is működjön.
    static RtfTextExtractor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Szöveget jelentő vezérlőszavak.</summary>
    private static readonly Dictionary<string, string> ControlWordText = new()
    {
        ["par"] = "\n", ["line"] = "\n", ["tab"] = " ", ["endash"] = "–", ["emdash"] = "—", ["bullet"] = "•",
    };

    private static readonly string[] NoisyDestinations =
        { "fonttbl", "colortbl", "stylesheet", "info", "generator", "pict", "object" };

    public bool CanHandle(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".rtf", StringComparison.OrdinalIgnoreCase);

    private const int MaxTextChars = 4000;

    // Egy beágyazott képes RTF több MB is lehet; a szöveg a fájl elején van. Ennél többet nem
    // olvasunk be (korábban a teljes fájl a memóriába került).
    private const int MaxReadChars = 8 * 1024 * 1024;

    // \G: a minta pontosan a megadott pozíción illeszkedjen. Korábban minden `\`-nél a teljes
    // hátralévő szöveg másolódott (`rtf[i..]`) → O(n²): 400 KB már ~4 s volt.
    private static readonly Regex HexEscape = new(@"\G\\'([0-9a-fA-F]{2})", RegexOptions.Compiled);
    private static readonly Regex ControlWord = new(@"\G\\([a-zA-Z]+)(-?\d+)? ?", RegexOptions.Compiled);
    // A sortörés megmarad (a bekezdések szerkezete az LLM-nek is támpont), a vízszintes
    // térközök és az üres sorok összevonódnak.
    private static readonly Regex HorizontalSpace = new(@"[^\S\n]+", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\s*\n\s*", RegexOptions.Compiled);

    public ExtractedDocument? Extract(string filePath)
    {
        string rtf = ReadHead(filePath);
        string text = StripRtf(rtf);
        if (text.Length == 0) return null;
        return new ExtractedDocument(text.Length > MaxTextChars ? text[..MaxTextChars] : text, Array.Empty<string>());
    }

    private static string ReadHead(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[(int)Math.Min(MaxReadChars, new FileInfo(filePath).Length + 1)];
        int read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    private static string StripRtf(string rtf)
    {
        var cp1250 = Encoding.GetEncoding(1250);
        var output = new StringBuilder();
        int i = 0;
        int depth = 0;
        int skipDepth = -1; // -1 = nem vagyunk zajos csoportban

        // A whitespace-összevonás előtti nyers szövegből ennyi bőven elég a 4000 karakterhez.
        while (i < rtf.Length && output.Length < MaxTextChars * 4)
        {
            char c = rtf[i];

            if (c == '{') { depth++; i++; continue; }
            if (c == '}')
            {
                if (skipDepth == depth) skipDepth = -1;
                depth--;
                i++;
                continue;
            }
            if (c == '\\' && i + 1 < rtf.Length)
            {
                var hexMatch = HexEscape.Match(rtf, i);
                if (hexMatch.Success)
                {
                    if (skipDepth == -1) output.Append(cp1250.GetString(new[] { Convert.ToByte(hexMatch.Groups[1].Value, 16) }));
                    i += hexMatch.Length;
                    continue;
                }

                var wordMatch = ControlWord.Match(rtf, i);
                if (wordMatch.Success)
                {
                    string word = wordMatch.Groups[1].Value;
                    // A zajos csoport az őt NYITÓ kapcsos zárójel párjáig tart (a belső csoportokkal
                    // együtt) — korábban a mélység eggyel elcsúszott, és egy több betűtípusos
                    // táblából a második név már a szövegbe szivárgott.
                    if (skipDepth == -1 && NoisyDestinations.Contains(word)) skipDepth = depth;
                    // A bekezdés- és sorvég jelentést hordoz: nélküle két bekezdés összeragadt
                    // („…beállításaA tengelyek…”).
                    else if (skipDepth == -1 && ControlWordText.TryGetValue(word, out string? text)) output.Append(text);
                    i += wordMatch.Length;
                    continue;
                }

                i++; // ismeretlen escape (pl. \{ \} \\) -> a soron következő karakter szó szerinti
                if (i < rtf.Length && skipDepth == -1) output.Append(rtf[i]);
                i++;
                continue;
            }

            if (skipDepth == -1) output.Append(c);
            i++;
        }

        string flattened = HorizontalSpace.Replace(output.ToString().Replace("\r", ""), " ");
        return BlankLines.Replace(flattened, "\n").Trim();
    }
}
