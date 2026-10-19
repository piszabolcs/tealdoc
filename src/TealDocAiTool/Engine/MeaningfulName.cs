using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TealDocAiTool.Engine;

/// <summary>
/// Eldönti, hogy egy szövegben (fájlnévben vagy generált címben) van-e VALÓDI SZÓ.
/// Ez a motor legfontosabb fékje: egy éles előnézeti futás alapján az átnevezésre jelölt fájlok
/// kétharmadának már beszédes neve volt, és pont ezeknél lett szemét a javaslat.
/// Szótár nélkül dolgozik (15 000 fájl, 4 nyelv), pusztán a szó ALAKJÁBÓL:
/// az OCR-hulladékot ("BEaami[wu", "ÖPIUf") a kevert kis/nagybetűs minta leplezi le.
/// </summary>
public static class MeaningfulName
{
    private const int MinWordLength = 4;              // "rajz" még szó, "mod" már nem
    private const int MinAcronymLength = 5;           // csupa nagybetű gyanús: "LINUX" igen, "ISIR" nem
    private const int MinStandaloneTitleWordLength = 6; // egyetlen szó csak akkor cím, ha elég hosszú

    // camelCase-határ: enélkül a "BEaami" és a "wwwE" egyetlen tokennek látszana.
    private static readonly Regex CamelBoundary = new(@"(?<=\p{Ll})(?=\p{Lu})", RegexOptions.Compiled);
    private static readonly Regex LetterRun = new(@"\p{L}+", RegexOptions.Compiled);

    // Egy valódi szó alakja: legfeljebb egy kezdő nagybetű + kisbetűk ("Adatlap", "rajz"),
    // VAGY csupa nagybetűs rövidítés ("LINUX"). Minden más betűkupac: OCR-zaj vagy cikkszám.
    private const string WordShapePattern = @"(\p{Lu}?\p{Ll}+|\p{Lu}+)";
    private static readonly Regex WordShape = new($"^{WordShapePattern}$", RegexOptions.Compiled);

    // Ugyanez, de kötőjeles/aláhúzásos összetételt is elfogad ("OPC-Server", "Kaliberwerkzeug-ZSB").
    private static readonly Regex CompoundWordShape =
        new($"^{WordShapePattern}([-_]{WordShapePattern})*$", RegexOptions.Compiled);

    // Alaki szűrőn átmenő, de információt NEM hordozó szavak. Szándékosan szűk a lista:
    // kétség esetén inkább hagyjuk békén a fájlt, mint hogy elrontsunk egy jó nevet.
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "scan", "scans", "scannen", "img", "image", "images", "doc", "docs", "document",
        "documents", "dokument", "dokumentum", "file", "files", "copy", "kopie", "masolat",
        "new", "neu", "final", "ocr", "temp", "tmp", "untitled", "page", "pages", "oldal",
        "seite", "skm", "print", "output", "pdf", "xls", "xlsx", "docx", "kep", "bild",
    };

    /// <summary>Bemeneti kapu: a fájlnév már hordoz értelmet -> nem nyúlunk hozzá.</summary>
    public static bool ContainsRealWord(string? text) => RealWords(text).Count > 0;

    /// <summary>Kimeneti kapu: a generált cím tényleg címnek néz-e ki. Szigorúbb, mint a
    /// bemeneti: egyetlen rövid szó ("Umin", "rodex") nem ér fel egy meglévő fájlnévvel.</summary>
    public static bool IsDescriptiveTitle(string? text)
    {
        var words = RealWords(text);
        if (words.Count >= 2) return true;
        return words.Count == 1 && words[0].Length >= MinStandaloneTitleWordLength;
    }

    /// <summary>Egyetlen összefüggő egység alaki vizsgálata: valódi alkatrésznév
    /// ("Grundplatte", "BRACKET", "OPC-Server") vagy összefolyt betűkupac ("seiesslsRI",
    /// "UtgåvaUpplagdOrsak", "erirr0rena")? A <see cref="RealWords"/> ezeket még átengedi, mert
    /// camelCase-bontás után az egyes darabok szó alakúak — itt viszont az EGÉSZ egységnek annak
    /// kell lennie. Számjegy, írásjel, szóköz nélküli nagybetű-váltás: mind kizáró ok.</summary>
    public static bool IsCleanWord(string? text) =>
        !string.IsNullOrWhiteSpace(text) && CompoundWordShape.IsMatch(text.Trim());

    private static List<string> RealWords(string? text)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return words;

        foreach (Match match in LetterRun.Matches(CamelBoundary.Replace(text, " ")))
        {
            string token = match.Value;
            if (!WordShape.IsMatch(token)) continue;

            bool acronym = token.All(char.IsUpper);
            if (token.Length < (acronym ? MinAcronymLength : MinWordLength)) continue;

            // Az ékezetek levétele kell a magánhangzó- és a denylist-vizsgálathoz is
            // (magyar/svéd/német szavak: "útmutató", "Utgåva", "Prüfbericht").
            string plain = RemoveDiacritics(token).ToLowerInvariant();
            if (Generic.Contains(plain)) continue;
            if (!plain.Any(c => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y')) continue; // magánhangzó nélkül: kód

            words.Add(token);
        }

        return words;
    }

    private static string RemoveDiacritics(string value) =>
        new(value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
}
