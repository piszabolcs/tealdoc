using System.IO;

namespace TealDocAiTool.Engine;

/// <summary>
/// Mi számít DOKUMENTUMNAK az Index módban.
///
/// Szándékosan TÁGABB kör, mint amit a szövegkinyerők kezelnek: a <c>.ppt/.pptx/.msg</c>
/// tartalmát nem tudjuk olvasni, de a gép dokumentációjának ugyanúgy része. Ha kihagynánk őket,
/// egy kereső azt sem tudná, hogy LÉTEZNEK — pedig a valódi archívum szűrőszabálya kifejezetten
/// megtartja őket, és a <c>.msg</c> levelek hordozzák a kontakt/probléma információt.
/// Ezért: a listába és a darabszámba minden ilyen fájl bekerül, az extractor csak az OPCIONÁLIS
/// leírás generálásához kell.
///
/// Allowlist (nem denylist), mert a gépmappák alatt szoftvertelepítők és CAD-natívok is
/// előfordulhatnak — azokat továbbra sem akarjuk az indexben.
/// </summary>
public static class IndexableDocument
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".doc", ".docx", ".docm",
        ".xls", ".xlsx", ".xlsm",
        ".ppt", ".pptx",
        ".msg",
        ".rtf", ".csv", ".txt",
    };

    public static bool IsIndexable(string filePath) =>
        Extensions.Contains(Path.GetExtension(filePath));
}
