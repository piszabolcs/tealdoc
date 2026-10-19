using System.IO;
using System.Text;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// CSV-táblázatot ír az egység-sorokból: első sor fejléc, vessző elválasztó, UTF-8 BOM (magyar
/// ékezetek), RFC 4180 idézőjelezés — táblázatkezelőbe és adatbázis-táblába is importálható.
/// </summary>
public static class CsvIndexWriter
{
    private static readonly string[] Header =
    {
        "Title", "Jelleg", "Gepszam", "Gepnev", "Geptipus", "EgyebMegnevezes",
        "Mappa", "HivatkozasUrl", "DokumentumSzam", "Osszefoglalo", "Dokumentumok",
    };

    /// <summary>Emberi olvasásra szánt jelleg-megnevezés — importálva ez szűrhető oszlop lesz.</summary>
    private static string KindLabel(MachineEntryKind kind) => kind switch
    {
        MachineEntryKind.ProductionLine => "gyártósor",
        MachineEntryKind.Machine => "gép",
        _ => "",
    };

    public static void Write(string csvPath, IEnumerable<MachineIndexRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
        using var writer = new StreamWriter(csvPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(',', Header));
        foreach (var r in rows)
        {
            writer.WriteLine(string.Join(',', new[]
            {
                CsvEscape(r.Title),
                CsvEscape(KindLabel(r.Kind)),
                CsvEscape(r.Number),
                CsvEscape(r.Name),
                CsvEscape(r.MachineType),
                CsvEscape(r.AltNames),
                // A teljes helyi út (Windows-felhasználónévvel) nem kerül a táblázatba (S-3).
                CsvEscape(MachineFileName.RelativeFolder(r)),
                CsvEscape(r.SharePointUrl),
                r.DocumentCount.ToString(),
                CsvEscape(r.Summary),
                CsvEscape(CellBullets(r.DocumentList)),
            }));
        }
    }

    /// <summary>A Markdown-felsorolás „- ” jele egy táblázatcellában képletnek látszana (Excel:
    /// #NAME?), ezért a CSV-ben „• ” jelet kap.</summary>
    private static string CellBullets(string? list) =>
        string.Join('\n', (list ?? "").Split('\n').Select(l => l.StartsWith("- ", StringComparison.Ordinal) ? "• " + l[2..] : l));

    private static string CsvEscape(string? value)
    {
        value ??= "";
        value = value.Replace("\r\n", "\n").Replace('\r', '\n');
        // A dokumentumokból származó szöveg (leírás, géptípus) Excelben képletként futna, ha
        // =, +, -, @ jellel kezdődik (S-5) — a ' előtag szövegként tartja.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
