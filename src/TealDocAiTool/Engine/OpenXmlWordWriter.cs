using System.IO;
using System.IO.Compression;
using System.Text;

namespace TealDocAiTool.Engine;

/// <summary>Egy bekezdés a Word-dokumentumban. A <paramref name="Level"/> 0 = normál szöveg,
/// 1 = főcím, 2 = alcím; a felsorolás elemeit a hívó jelöli.</summary>
public sealed record WordParagraph(string Text, int Level = 0, bool Bullet = false);

/// <summary>
/// Minimális .docx (OOXML) írása KÜLSŐ CSOMAG NÉLKÜL — a .docx egy ZIP három XML-lel.
///
/// Miért nem csak Markdown: sok dokumentum-kereső és tudásbázis az .md fájlokat nem olvassa (a
/// szokásos lista doc/docx/xls/xlsx/ppt/pptx/pdf).
///
/// Miért nem Word COM-mal: 250+ dokumentumot egy rejtett Word-példánnyal legyártani lassú és
/// törékeny — ez a projekt már megszenvedte (lásd a `.doc` kinyerés időtúllépéseit). Miért nem
/// DocumentFormat.OpenXml: egy sima szöveges dokumentumhoz nem éri meg új függőséget behozni.
/// </summary>
public static class OpenXmlWordWriter
{
    private const string ContentTypes =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
        <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
        <Default Extension="xml" ContentType="application/xml"/>
        <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    private const string PackageRels =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    public static void Write(string path, IEnumerable<WordParagraph> paragraphs)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Előbb memóriába építünk: egy félbeszakadt írás ne hagyjon sérült .docx-et a kimenetben.
        byte[] package = BuildPackage(paragraphs);
        File.WriteAllBytes(path, package);
    }

    private static byte[] BuildPackage(IEnumerable<WordParagraph> paragraphs)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", ContentTypes);
            AddEntry(zip, "_rels/.rels", PackageRels);
            AddEntry(zip, "word/document.xml", BuildDocumentXml(paragraphs));
        }
        return buffer.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        // BOM NÉLKÜLI UTF-8: az OOXML-részekbe a BOM egyes olvasóknál elrontja az XML-fejlécet.
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string BuildDocumentXml(IEnumerable<WordParagraph> paragraphs)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>""");

        foreach (var p in paragraphs) AppendParagraph(sb, p);

        sb.Append("<w:sectPr/></w:body></w:document>");
        return sb.ToString();
    }

    private static void AppendParagraph(StringBuilder sb, WordParagraph p)
    {
        // Stíluslap helyett közvetlen formázás: így nem kell styles.xml rész, és a dokumentum
        // ugyanúgy néz ki bármelyik Wordben.
        int halfPoints = p.Level switch { 1 => 32, 2 => 26, _ => 22 };  // 16 / 13 / 11 pt
        bool bold = p.Level > 0;

        sb.Append("<w:p>");
        if (p.Bullet)
        {
            // Behúzás — valódi listaszámozás helyett, mert ahhoz numbering.xml kellene, és a
            // tartalom kinyerése szempontjából (ez a lényeg) semmit nem adna hozzá.
            sb.Append("""<w:pPr><w:ind w:left="360"/></w:pPr>""");
        }

        sb.Append("<w:r><w:rPr>");
        if (bold) sb.Append("<w:b/>");
        sb.Append($"""<w:sz w:val="{halfPoints}"/><w:szCs w:val="{halfPoints}"/>""");
        sb.Append("</w:rPr>");
        sb.Append($"""<w:t xml:space="preserve">{XmlEscape(p.Text)}</w:t>""");
        sb.Append("</w:r></w:p>");
    }

    /// <summary>
    /// A gépnevek tele vannak `&amp;`-tel és `&lt;`-tel; escape nélkül a .docx MEGNYITHATATLAN
    /// lenne. A vezérlőkaraktereket is ki kell szűrni: az OCR-es fájlnevekben előfordulnak, és
    /// az XML 1.0 nem engedi meg őket.
    /// </summary>
    private static string XmlEscape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default:
                    if (c == '\t' || c == '\n' || c == '\r' || c >= 0x20) sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
