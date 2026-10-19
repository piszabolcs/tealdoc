using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

/// <summary>
/// A .docx kimenet. Miért Word és nem csak Markdown: sok dokumentum-kereső a szokásos irodai
/// formátumokat (doc/docx/xls/xlsx/ppt/pptx/pdf) olvassa, az .md-t nem.
/// </summary>
public class WordFileWriterTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static MachineIndexRow Row(
        string title = "1401 MINTAGYARTO VARIO 20", string number = "1401",
        string name = "MINTAGYARTO VARIO 20", string machineType = "CNC esztergagép",
        string folderPath = @"C:\Users\demo\Archivum\1401", string sharePointUrl = "",
        MachineEntryKind kind = MachineEntryKind.Machine, string documentList = "",
        IReadOnlyList<MachineDocument>? documents = null) =>
        new(Title: title, Number: number, Name: name, MachineType: machineType, AltNames: "",
            FolderPath: folderPath, DocumentCount: documents?.Count ?? 0, Summary: "Összefoglaló mondat.",
            DocumentList: documentList, Kind: kind, SharePointUrl: sharePointUrl, Documents: documents);

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tealdoc_docx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A .docx-ből a body összes szövegdarabja — ezt látja egy indexelő is.</summary>
    private static string TextOf(string docxPath)
    {
        using var zip = ZipFile.OpenRead(docxPath);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var doc = XDocument.Load(stream);
        return string.Join("\n", doc.Descendants(W + "p").Select(p =>
            string.Concat(p.Descendants(W + "t").Select(t => t.Value))));
    }

    [Fact]
    public void Write_ProducesAValidOpenXmlPackage()
    {
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row());
            string path = Directory.GetFiles(dir, "*.docx").Single();

            using var zip = ZipFile.OpenRead(path);
            // A három kötelező rész — bármelyik hiánya „a fájl sérült" hibát ad a Wordben.
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("_rels/.rels"));
            Assert.NotNull(zip.GetEntry("word/document.xml"));

            using var stream = zip.GetEntry("word/document.xml")!.Open();
            var doc = XDocument.Load(stream);   // parse-olható XML: ez a valódi épség-teszt
            Assert.Equal(W + "document", doc.Root!.Name);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_UsesTheDocxExtensionAndTheSameNameAsTheMarkdownWriter()
    {
        // Ugyanaz a gép ugyanazt a nevet kapja mindkét formátumban — az ütközés-védelem sem
        // lehet formátumfüggő.
        var row = Row(title: "1401 - X", number: "1401", name: "X", folderPath: @"C:\gep\1401 - X");

        string docx = Path.GetFileNameWithoutExtension(WordFileWriter.FilePathFor(@"C:\ki", row));
        string md = Path.GetFileNameWithoutExtension(MarkdownFileWriter.FilePathFor(@"C:\ki", row));

        Assert.Equal(md, docx);
        Assert.EndsWith(".docx", WordFileWriter.FilePathFor(@"C:\ki", row));
    }

    [Fact]
    public void Write_EmitsTheSearchableFieldsAsText()
    {
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row());
            string text = TextOf(Directory.GetFiles(dir, "*.docx").Single());

            Assert.Contains("1401 MINTAGYARTO VARIO 20", text);
            Assert.Contains("Összefoglaló mondat.", text);
            Assert.Contains("Jelleg: gép", text);
            Assert.Contains("Gépszám: 1401", text);
            Assert.Contains("Géptípus: CNC esztergagép", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_CallsAProductionLineALineNotAMachine()
    {
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row(title: "T05 MINTA", number: "T05", name: "MINTA",
                machineType: "szelepszerelő sor", kind: MachineEntryKind.ProductionLine));
            string text = TextOf(Directory.GetFiles(dir, "*.docx").Single());

            Assert.Contains("Jelleg: gyártósor", text);
            Assert.Contains("Sor jele: T05", text);
            Assert.DoesNotContain("Gépszám", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_PrefersTheSharePointUrlOverTheLocalPath()
    {
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row(sharePointUrl: "https://x.sharepoint.com/D/1401"));
            string text = TextOf(Directory.GetFiles(dir, "*.docx").Single());

            Assert.Contains("Dokumentumok helye: https://x.sharepoint.com/D/1401", text);
            Assert.DoesNotContain(@"C:\Users", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_ListsEachDocumentWithItsLinkAsPlainText()
    {
        // A hivatkozás SZÖVEGKÉNT kerül be, nem Word-mezőként: a tudásbázis a szöveget indexeli,
        // és egy asszisztens csak azt tudja idézni, amit lát.
        string dir = TempDir();
        var docs = new[]
        {
            new MachineDocument("kezelesi.pdf", "kezelesi.pdf", "A gép kezelési útmutatója."),
            new MachineDocument("terv.pdf", Path.Combine("Rajzok", "terv.pdf"), null),
        };
        try
        {
            WordFileWriter.Write(dir, Row(sharePointUrl: "https://x.sharepoint.com/D/1401", documents: docs));
            string text = TextOf(Directory.GetFiles(dir, "*.docx").Single());

            Assert.Contains("kezelesi.pdf — A gép kezelési útmutatója. — https://x.sharepoint.com/D/1401/kezelesi.pdf", text);
            Assert.Contains("terv.pdf — https://x.sharepoint.com/D/1401/Rajzok/terv.pdf", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_ConvertsMarkdownLinksFromAnOlderCache()
    {
        // Régi gyorsítótárban csak a renderelt Markdown-lista van meg; a `[név](url)` alak
        // Wordben olvashatatlan lenne.
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row(
                documentList: "- [kezelesi.pdf](https://x.sharepoint.com/D/1401/kezelesi.pdf) — Útmutató."));
            string text = TextOf(Directory.GetFiles(dir, "*.docx").Single());

            Assert.DoesNotContain("](", text);
            Assert.Contains("kezelesi.pdf", text);
            Assert.Contains("https://x.sharepoint.com/D/1401/kezelesi.pdf", text);
            Assert.Contains("Útmutató.", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    // Escape nélkül a .docx MEGNYITHATATLAN lenne — a gépnevekben van `&` (pl. „Fűtés & szellőzés").
    [Fact]
    public void Write_SurvivesXmlSpecialCharactersInNames()
    {
        string dir = TempDir();
        var docs = new[] { new MachineDocument("A<B>&C.pdf", "A<B>&C.pdf", "Leírás \"idézőjellel\".") };
        try
        {
            WordFileWriter.Write(dir, Row(title: "8801 Fűtés & szellőzés", number: "8801",
                name: "Fűtés & szellőzés", documents: docs));
            string path = Directory.GetFiles(dir, "*.docx").Single();
            string text = TextOf(path);   // parse-olna-e egyáltalán

            Assert.Contains("Fűtés & szellőzés", text);
            Assert.Contains("A<B>&C.pdf", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_KeepsHungarianAccentsIntact()
    {
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row(machineType: "fúrógép őrlő ÁRVÍZTŰRŐ"));
            string text = TextOf(Directory.GetFiles(dir, "*.docx").Single());

            Assert.Contains("fúrógép őrlő ÁRVÍZTŰRŐ", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_SaysSoWhenThereIsNothingToList()
    {
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row());
            string text = TextOf(Directory.GetFiles(dir, "*.docx").Single());

            Assert.Contains("Nincs feldolgozható dokumentum", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_RewritingTheSameMachineDoesNotDuplicate()
    {
        // A koordinátor a végén a gyorsítótárból ÚJRA kiír minden gépet.
        string dir = TempDir();
        try
        {
            WordFileWriter.Write(dir, Row());
            WordFileWriter.Write(dir, Row());

            Assert.Single(Directory.GetFiles(dir, "*.docx"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OpenXmlWordWriter_StripsControlCharactersThatWouldBreakTheXml()
    {
        // OCR-es fájlnevekben előfordul vezérlőkarakter; az XML 1.0 nem engedi meg.
        string dir = TempDir();
        string path = Path.Combine(dir, "proba.docx");
        try
        {
            OpenXmlWordWriter.Write(path, new[] { new WordParagraph("elotte\u0007utana") });
            string text = TextOf(path);

            Assert.Equal("elotteutana", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OpenXmlWordWriter_WritesPartsWithoutAByteOrderMark()
    {
        // BOM az OOXML-részben elrontja az XML-fejlécet egyes olvasóknál.
        string dir = TempDir();
        string path = Path.Combine(dir, "proba.docx");
        try
        {
            OpenXmlWordWriter.Write(path, new[] { new WordParagraph("szöveg") });

            using var zip = ZipFile.OpenRead(path);
            using var stream = zip.GetEntry("word/document.xml")!.Open();
            byte[] head = new byte[3];
            stream.ReadExactly(head);

            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, head);
            Assert.Equal("<?x", Encoding.UTF8.GetString(head));
        }
        finally { Directory.Delete(dir, true); }
    }
}
