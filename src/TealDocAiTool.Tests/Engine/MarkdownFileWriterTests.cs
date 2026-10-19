using System.Text;
using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

public class MarkdownFileWriterTests
{
    private static MachineIndexRow Row(
        string title, string number, string name, string machineType = "", int docCount = 0,
        string documentList = "", string folderPath = @"C:\gep", string sharePointUrl = "") =>
        new(Title: title, Number: number, Name: name, MachineType: machineType, AltNames: "",
            FolderPath: folderPath, DocumentCount: docCount, Summary: "összefoglaló",
            DocumentList: documentList, SharePointUrl: sharePointUrl);

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tealdoc_sp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void FilePathFor_UsesTheMachineNumberAndNameUnchanged()
    {
        // A tipikus eset: a fájlnév maga a mappanév. Ez a keresés legerősebb jele, nem szabad
        // se csonkolni, se lenyomattal megtoldani.
        var row = Row("1401 MINTAGYARTO VARIO 20", "1401", "MINTAGYARTO VARIO 20");

        string path = MarkdownFileWriter.FilePathFor(@"C:\ki", row);

        Assert.Equal("1401 MINTAGYARTO VARIO 20.md", Path.GetFileName(path));
    }

    // A HIBA, amit javítunk: a „1401 - X" és a „1401 X" mappa ugyanarra a névre parse-olódik,
    // így az egyik gép .md-je NÉMÁN felülírta volna a másikét.
    [Fact]
    public void FilePathFor_DistinctMachinesNeverShareAFileName()
    {
        var a = Row("1401 X", "1401", "X", folderPath: @"C:\gep\1401 X");
        var b = Row("1401 - X", "1401", "X", folderPath: @"C:\gep\1401 - X");

        string pa = MarkdownFileWriter.FilePathFor(@"C:\ki", a);
        string pb = MarkdownFileWriter.FilePathFor(@"C:\ki", b);

        Assert.NotEqual(pa, pb);
    }

    [Fact]
    public void FilePathFor_SanitisationCollisionIsAlsoResolved()
    {
        // A Sanitize a „/"-t „_"-ra cseréli, tehát az „A_B" és az „A/B" egybeeshetne.
        var a = Row("A_B", "", "A_B", folderPath: @"C:\gep\A_B");
        var b = Row("A/B", "", "A/B", folderPath: @"C:\gep\AB");

        Assert.NotEqual(
            MarkdownFileWriter.FilePathFor(@"C:\ki", a),
            MarkdownFileWriter.FilePathFor(@"C:\ki", b));
    }

    // Kritikus a folytathatóság miatt: egy megszakadt és újraindított futásnak UGYANAZT a fájlnevet
    // kell adnia. Ezért nem sorszámozunk (az a felfedezési sorrendtől függne), hanem a nyers
    // mappanévből számolunk stabil lenyomatot.
    [Fact]
    public void FilePathFor_IsDeterministicAcrossRuns()
    {
        var row = Row("1401 - X", "1401", "X", folderPath: @"C:\gep\1401 - X");

        string first = MarkdownFileWriter.FilePathFor(@"C:\ki", row);
        string second = MarkdownFileWriter.FilePathFor(@"C:\ki", row);

        Assert.Equal(first, second);
    }

    [Fact]
    public void FilePathFor_FallsBackWhenTheNameIsUnusable()
    {
        var row = Row("...", "", "...", folderPath: @"C:\gep\...");

        string name = Path.GetFileName(MarkdownFileWriter.FilePathFor(@"C:\ki", row));

        Assert.EndsWith(".md", name);
        Assert.DoesNotContain("..md", name);
    }

    [Fact]
    public void Write_ProducesOneFilePerMachine()
    {
        string dir = TempDir();
        try
        {
            MarkdownFileWriter.Write(dir, Row("1401 A", "1401", "A", docCount: 86));
            MarkdownFileWriter.Write(dir, Row("1402 B", "1402", "B", docCount: 12));

            Assert.Equal(2, Directory.GetFiles(dir, "*.md").Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_EmitsTheMachineNumberAndTypeAsSearchableText()
    {
        string dir = TempDir();
        try
        {
            var row = Row("1401 MINTAGYARTO VARIO 20", "1401", "MINTAGYARTO VARIO 20",
                machineType: "CNC esztergagép", docCount: 86, documentList: "- kezikonyv.pdf");

            MarkdownFileWriter.Write(dir, row);
            string text = File.ReadAllText(Directory.GetFiles(dir, "*.md").Single());

            Assert.Contains("# 1401 MINTAGYARTO VARIO 20", text);
            Assert.Contains("CNC esztergagép", text);
            Assert.Contains("- kezikonyv.pdf", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_UsesUtf8BomSoHungarianAccentsSurviveIndexing()
    {
        string dir = TempDir();
        try
        {
            MarkdownFileWriter.Write(dir, Row("1401 Ő", "1401", "Ő", machineType: "fúrógép"));
            byte[] bytes = File.ReadAllBytes(Directory.GetFiles(dir, "*.md").Single());

            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Contains("fúrógép", Encoding.UTF8.GetString(bytes));
        }
        finally { Directory.Delete(dir, true); }
    }

    // A HIBA, amit javítunk: a .md-be a generáló GÉP helyi útja került (`C:\Users\demo\…`).
    // A dokumentumok egy webes tárban vannak, tehát ez egy megosztott tudásbázisban használhatatlan,
    // és egy asszisztens bemásolhatná a válaszába — a felhasználó egy nem létező útvonalat kapna.
    [Fact]
    public void Write_PrefersTheSharePointUrlOverTheLocalPath()
    {
        string dir = TempDir();
        try
        {
            var row = Row("1401 A", "1401", "A", folderPath: @"C:\Users\demo\Desktop\Archivum\1401 A",
                sharePointUrl: "https://x.sharepoint.com/sites/S/Docs/1401%20A");

            MarkdownFileWriter.Write(dir, row);
            string text = File.ReadAllText(Directory.GetFiles(dir, "*.md").Single());

            Assert.Contains("https://x.sharepoint.com/sites/S/Docs/1401%20A", text);
            Assert.DoesNotContain(@"C:\Users", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_WithoutUrl_NamesTheFolderRelativeToTheRootNotTheLocalPath()
    {
        // S-3: a teljes helyi út (benne a Windows-felhasználónévvel) nem kerülhet a kimenetbe.
        string dir = TempDir();
        try
        {
            MarkdownFileWriter.Write(dir, Row("1401 A", "1401", "A", folderPath: @"C:\Users\demo\Archivum\1401 A"));
            string text = File.ReadAllText(Directory.GetFiles(dir, "*.md").Single());

            Assert.Contains("Mappa (a gyökérhez képest):** 1401 A", text);
            Assert.DoesNotContain(@"C:\Users", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_RewritingTheSameMachineDoesNotDuplicate()
    {
        // A koordinátor a végén a gyorsítótárból ÚJRA kiír minden gépet — ez nem hozhat létre
        // második fájlt ugyanarra a gépre.
        string dir = TempDir();
        try
        {
            var row = Row("1401 A", "1401", "A");
            MarkdownFileWriter.Write(dir, row);
            MarkdownFileWriter.Write(dir, row);

            Assert.Single(Directory.GetFiles(dir, "*.md"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
