using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

public class IndexableDocumentTests
{
    // Ezekből nem tudunk szöveget olvasni, de a gép dokumentációjának RÉSZEI. Ha kimaradnának,
    // egy kereső azt sem tudná, hogy léteznek. Egy valós archívumban 337 ilyen fájl volt.
    [Theory]
    [InlineData("levelezes.msg")]
    [InlineData("oktatas.ppt")]
    [InlineData("Training Vario 20-8.pptx")]
    public void IsIndexable_IncludesDocumentsWeCannotReadText(string fileName)
    {
        Assert.True(IndexableDocument.IsIndexable(fileName));
    }

    [Theory]
    [InlineData("kezikonyv.pdf")]
    [InlineData("Görgőző.doc")]
    [InlineData("Kézikönyv.docx")]
    [InlineData("V01_Arbeitsanweisung.docm")]
    [InlineData("karbantartási utas.xls")]
    [InlineData("Bronkhorst.xlsx")]
    [InlineData("R424B17137.xlsm")]
    [InlineData("CDM 2 08 02 Release Info.rtf")]
    [InlineData("griddata.csv")]
    [InlineData("readme.txt")]
    public void IsIndexable_IncludesEveryRetainedDocumentType(string fileName)
    {
        Assert.True(IndexableDocument.IsIndexable(fileName));
    }

    // Allowlist, nem denylist: a gépmappák alatt szoftvertelepítők és CAD-natívok is vannak,
    // azok nem valók az indexbe.
    [Theory]
    [InlineData("setup.exe")]
    [InlineData("alkatresz.step")]
    [InlineData("rajz.dwg")]
    [InlineData("backup.zip")]
    [InlineData("kep.jpg")]
    [InlineData("kiterjesztes_nelkul")]
    public void IsIndexable_ExcludesNonDocuments(string fileName)
    {
        Assert.False(IndexableDocument.IsIndexable(fileName));
    }

    [Fact]
    public void IsIndexable_IsCaseInsensitive()
    {
        // Egy valós archívumban a szkennelt anyag nagy része NAGYBETŰS kiterjesztésű.
        Assert.True(IndexableDocument.IsIndexable("SCHEMA_CDS_8092000325B_BOSC.PDF"));
        Assert.True(IndexableDocument.IsIndexable("PCI_LINUX_INSTALL.DOC"));
    }
}
