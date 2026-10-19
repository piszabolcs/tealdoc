using System.IO;
using System.Text;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Egységenként EGY kis Markdown-fájlt ír egyetlen kimeneti mappába (nem a forrásdokumentumok
/// közé!). Ezt a mappát egy dokumentum-kereső vagy tudásbázis indexelheti.
/// Miért így jobb, mint egy nagy fájl: egy kicsi, önhordó fájl természetes chunk lesz, tehát a
/// „milyen gép az 1401?" kérdésre a teljes, ép gép-bejegyzés jön vissza, nem egy rossz helyen
/// elvágott szelet. A fájlnév maga is beszédes (gépszám + gépnév), mert a kereső a fájlnevet
/// erősen súlyozza — ez emeli a találati pontszámot.
/// </summary>
public static class MarkdownFileWriter
{
    /// <summary>Egy gép Markdown-fájljának ELÉRÉSI ÚTJA a kimeneti mappában. Publikus, hogy a
    /// koordinátor tudja, melyik fájlt írta (folytathatóság/napló).</summary>
    public static string FilePathFor(string outputFolder, MachineIndexRow row) =>
        Path.Combine(outputFolder, MachineFileName.For(row) + ".md");

    public static void Write(string outputFolder, MachineIndexRow row)
    {
        Directory.CreateDirectory(outputFolder);
        string path = FilePathFor(outputFolder, row);

        var sb = new StringBuilder();
        // A H1 cím a legerősebb kulcsszó-jel a keresőnek — a gépszám és a gépnév legyen benne.
        sb.AppendLine($"# {row.Title}");
        sb.AppendLine();
        // A természetes nyelvű mondat a szemantikus keresés horgonya.
        sb.AppendLine(row.Summary);
        sb.AppendLine();
        // A jelleg és a mezőnevek a sor/gép megkülönböztetést követik — egy gyártósorra
        // „Gépszám"-ként hivatkozni ugyanúgy félrevezet, mint a szövegben.
        switch (row.Kind)
        {
            case MachineEntryKind.ProductionLine:
                sb.AppendLine("- **Jelleg:** gyártósor");
                sb.AppendLine($"- **Sor jele:** {row.Number}");
                sb.AppendLine($"- **Sor neve:** {row.Name}");
                if (!string.IsNullOrWhiteSpace(row.MachineType))
                    sb.AppendLine($"- **Amit gyártanak rajta:** {row.MachineType}");
                break;

            case MachineEntryKind.Machine:
                sb.AppendLine("- **Jelleg:** gép");
                sb.AppendLine($"- **Gépszám:** {row.Number}");
                sb.AppendLine($"- **Gép neve:** {row.Name}");
                if (!string.IsNullOrWhiteSpace(row.MachineType))
                    sb.AppendLine($"- **Géptípus:** {row.MachineType}");
                break;

            default:
                sb.AppendLine($"- **Azonosító:** {row.Number}");
                sb.AppendLine($"- **Megnevezés:** {row.Name}");
                if (!string.IsNullOrWhiteSpace(row.MachineType))
                    sb.AppendLine($"- **Típus:** {row.MachineType}");
                break;
        }
        if (!string.IsNullOrWhiteSpace(row.AltNames))
            sb.AppendLine($"- **Egyéb megnevezés:** {row.AltNames}");
        // A teljes helyi utat (benne a Windows-felhasználónévvel) SOSEM írjuk ki — egy megosztott
        // tudásbázisban használhatatlan és személyes adat (S-3). Webcím híján csak a gyökérhez
        // képesti mappanév kerül be; a webcím viszont kattintható.
        sb.AppendLine(string.IsNullOrWhiteSpace(row.SharePointUrl)
            ? $"- **Mappa (a gyökérhez képest):** {MachineFileName.RelativeFolder(row)}"
            : $"- **Dokumentumok helye:** {row.SharePointUrl}");
        sb.AppendLine($"- **Dokumentumok száma:** {row.DocumentCount}");
        sb.AppendLine();
        sb.AppendLine("## Elérhető dokumentumok");
        sb.AppendLine();
        if (string.IsNullOrWhiteSpace(row.DocumentList))
            sb.AppendLine("_(Nincs feldolgozható dokumentum ebben a mappában.)_");
        else
            sb.AppendLine(row.DocumentList);

        // UTF-8 BOM: a magyar ékezetek biztos helyes felismeréséhez az indexelőnél is.
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
