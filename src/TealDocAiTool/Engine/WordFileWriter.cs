using System.IO;
using System.Text.RegularExpressions;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Egységenként EGY .docx „kártyát” ír a kimeneti mappába.
///
/// Miért nem csak Markdown: sok dokumentum-kereső és vállalati tudásbázis csak a szokásos irodai
/// formátumokat (doc/docx/xls/xlsx/ppt/pptx/pdf) olvassa, az .md-t nem. A Word ráadásul kézzel is
/// szerkeszthető, ha egy egységnél javítani kell.
///
/// A tartalom szándékosan ugyanaz, mint a .md-ben: kicsi, önhordó fájl, aminek a fájlneve maga is
/// beszédes — a „milyen gép az 1401?" kérdésre a teljes, ép gép-bejegyzés jön vissza.
/// </summary>
public static class WordFileWriter
{
    /// <summary>Markdown-hivatkozás: `[szöveg](url)`. A .docx-ben nincs értelme, szöveggé bontjuk.</summary>
    private static readonly Regex MarkdownLink = new(@"^\[(?<text>.*?)\]\((?<url>\S+?)\)", RegexOptions.Compiled);

    public static string FilePathFor(string outputFolder, MachineIndexRow row) =>
        Path.Combine(outputFolder, MachineFileName.For(row) + ".docx");

    public static void Write(string outputFolder, MachineIndexRow row)
    {
        Directory.CreateDirectory(outputFolder);
        OpenXmlWordWriter.Write(FilePathFor(outputFolder, row), BuildParagraphs(row));
    }

    private static List<WordParagraph> BuildParagraphs(MachineIndexRow row)
    {
        var paragraphs = new List<WordParagraph>
        {
            // A főcím a legerősebb kulcsszó-jel a keresőnek — a gépszám és a gépnév legyen benne.
            new(row.Title, Level: 1),
            // A természetes nyelvű mondat a szemantikus keresés horgonya.
            new(row.Summary),
        };

        // A jelleg és a mezőnevek a sor/gép megkülönböztetést követik — egy gyártósorra
        // „Gépszám"-ként hivatkozni ugyanúgy félrevezet, mint a szövegben.
        switch (row.Kind)
        {
            case MachineEntryKind.ProductionLine:
                paragraphs.Add(new("Jelleg: gyártósor"));
                paragraphs.Add(new($"Sor jele: {row.Number}"));
                paragraphs.Add(new($"Sor neve: {row.Name}"));
                if (!string.IsNullOrWhiteSpace(row.MachineType))
                    paragraphs.Add(new($"Amit gyártanak rajta: {row.MachineType}"));
                break;

            case MachineEntryKind.Machine:
                paragraphs.Add(new("Jelleg: gép"));
                paragraphs.Add(new($"Gépszám: {row.Number}"));
                paragraphs.Add(new($"Gép neve: {row.Name}"));
                if (!string.IsNullOrWhiteSpace(row.MachineType))
                    paragraphs.Add(new($"Géptípus: {row.MachineType}"));
                break;

            default:
                paragraphs.Add(new($"Azonosító: {row.Number}"));
                paragraphs.Add(new($"Megnevezés: {row.Name}"));
                if (!string.IsNullOrWhiteSpace(row.MachineType))
                    paragraphs.Add(new($"Típus: {row.MachineType}"));
                break;
        }

        if (!string.IsNullOrWhiteSpace(row.AltNames))
            paragraphs.Add(new($"Egyéb megnevezés: {row.AltNames}"));

        // A teljes helyi utat (benne a Windows-felhasználónévvel) sosem írjuk ki (S-3): webcím
        // híján csak a gyökérhez képesti mappanév kerül be.
        paragraphs.Add(string.IsNullOrWhiteSpace(row.SharePointUrl)
            ? new WordParagraph($"Mappa (a gyökérhez képest): {MachineFileName.RelativeFolder(row)}")
            : new WordParagraph($"Dokumentumok helye: {row.SharePointUrl}"));

        paragraphs.Add(new($"Dokumentumok száma: {row.DocumentCount}"));
        paragraphs.Add(new("Elérhető dokumentumok", Level: 2));

        var lines = DocumentLines(row);
        if (lines.Count == 0)
            paragraphs.Add(new("(Nincs feldolgozható dokumentum ebben a mappában.)"));
        else
            paragraphs.AddRange(lines.Select(l => new WordParagraph(l, Bullet: true)));

        return paragraphs;
    }

    /// <summary>
    /// A dokumentumsorok SIMA SZÖVEGKÉNT. A hivatkozás is szövegként kerül be, nem Word-mezőként:
    /// a kereső a szöveget indexeli, és egy asszisztens csak azt tudja idézni, amit lát.
    /// </summary>
    private static List<string> DocumentLines(MachineIndexRow row)
    {
        // Ha megvan a nyers lista, abból építünk — így a hivatkozás akkor is helyes, ha a .md-t
        // más beállítással generáltuk.
        if (row.Documents is { Count: > 0 })
        {
            return row.Documents.Select(d =>
            {
                string line = d.FileName;
                if (!string.IsNullOrWhiteSpace(d.Description)) line += " — " + d.Description;
                string url = SharePointUrlBuilder.Combine(row.SharePointUrl, d.RelativePath);
                if (url.Length > 0) line += " — " + url;
                return line;
            }).ToList();
        }

        // Régi gyorsítótár: csak a renderelt Markdown-lista van meg. A `- ` előtagot levágjuk, a
        // `[név](url)` hivatkozást pedig olvasható szöveggé bontjuk.
        if (string.IsNullOrWhiteSpace(row.DocumentList)) return new List<string>();

        return row.DocumentList
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimStart().TrimStart('-', ' '))
            .Where(l => l.Length > 0)
            .Select(UnwrapMarkdownLink)
            .ToList();
    }

    private static string UnwrapMarkdownLink(string line)
    {
        var m = MarkdownLink.Match(line);
        if (!m.Success) return line;
        string rest = line[m.Length..];
        return m.Groups["text"].Value + rest + " — " + m.Groups["url"].Value;
    }
}
