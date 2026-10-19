using System.Text;
using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

public class CsvIndexWriterTests
{
    private static MachineIndexRow Row(
        string name, string summary = "össz", string docs = "",
        MachineEntryKind kind = MachineEntryKind.Machine) =>
        new(Title: "1401 " + name, Number: "1401", Name: name, MachineType: "CNC esztergagép",
            AltNames: "", FolderPath: @"C:\gep", DocumentCount: 2, Summary: summary,
            DocumentList: docs, Kind: kind);

    private static string WriteToTemp(params MachineIndexRow[] rows)
    {
        string path = Path.Combine(Path.GetTempPath(), "tealdoc_csv_" + Guid.NewGuid().ToString("N"), "ki.csv");
        CsvIndexWriter.Write(path, rows);
        return path;
    }

    [Fact]
    public void Write_StartsWithTheHeaderRow()
    {
        string path = WriteToTemp(Row("A"));
        try
        {
            string first = File.ReadAllLines(path, Encoding.UTF8).First();
            Assert.StartsWith("Title,Jelleg,Gepszam,Gepnev", first);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Write_CarriesTheSharePointUrlAsItsOwnColumn()
    {
        // A helyi útvonal egy megosztott táblázatban sem használható — a webcím az, amire rá
        // lehet kattintani.
        string path = WriteToTemp(Row("A") with { SharePointUrl = "https://x.sharepoint.com/D/1401%20A" });
        try
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);

            Assert.Contains("HivatkozasUrl", lines[0]);
            Assert.Contains("https://x.sharepoint.com/D/1401%20A", lines[1]);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    // A jelleg szűrhető oszlop egy táblázatban: „mutasd az összes gyártósort" csak így megy.
    [Fact]
    public void Write_EmitsTheKindAsAReadableColumn()
    {
        string path = WriteToTemp(
            Row("A", kind: MachineEntryKind.Machine),
            Row("B", kind: MachineEntryKind.ProductionLine),
            Row("C", kind: MachineEntryKind.Unknown));
        try
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.Contains(",gép,", lines[1]);
            Assert.Contains(",gyártósor,", lines[2]);
            Assert.Contains("1401 C,,", lines[3]);   // ismeretlen jelleg = üres, nem tippelünk
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Write_UsesUtf8BomForHungarianAccents()
    {
        string path = WriteToTemp(Row("Fúrógép őrlő"));
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Contains("Fúrógép őrlő", Encoding.UTF8.GetString(bytes));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    // RFC 4180: a vessző és az idézőjel nem törheti szét a sort — különben a táblázat-import
    // elcsúszik, és a mezők átfolynak a szomszédos oszlopba.
    [Fact]
    public void Write_QuotesSeparatorsAndEscapesQuotes()
    {
        string path = WriteToTemp(Row("A", summary: "eszterga, marógép és \"különleges\" gép"));
        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("\"eszterga, marógép és \"\"különleges\"\" gép\"", text);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Write_KeepsAMultiLineDocumentListInsideOneField()
    {
        // A dokumentumlista sortöréseket tartalmaz — ezek nem válhatnak új CSV-sorokká. A „- ”
        // felsorolásjel a cellában képletnek látszana, ezért „• ” lesz belőle.
        string path = WriteToTemp(Row("A", docs: "- egy.pdf\n- ketto.pdf"));
        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("\"• egy.pdf\n• ketto.pdf\"", text);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    // S-5: a dokumentumból jövő „=…” szöveg Excelben képletként futna.
    [Fact]
    public void Write_GuardsFormulaLikeCells()
    {
        string path = WriteToTemp(Row("A") with { MachineType = "=HYPERLINK(\"http://x\") prés" });
        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\") prés\"", text);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Write_EmitsOneRowPerMachine()
    {
        string path = WriteToTemp(Row("A"), Row("B"), Row("C"));
        try
        {
            // Fejléc + 3 gép; a mezőkben nincs sortörés ebben az esetben.
            Assert.Equal(4, File.ReadAllLines(path, Encoding.UTF8).Length);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
