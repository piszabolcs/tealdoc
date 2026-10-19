using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

public class MeaningfulNameTests
{
    [Theory]
    // Valódi szót tartalmazó fájlnevek: ezek már beszédesek, NEM szabad átnevezni őket.
    [InlineData("01_Adatlap")]
    [InlineData("Elektromos dok és PLC program 1")]
    [InlineData("2 Elektromos kapcsolási rajz")]
    [InlineData("liste materiel")]
    [InlineData("Kézikönyv")]
    [InlineData("PCI_LINUX_INSTALL")]
    [InlineData("Schraubsystem300_en_es_pt_V3.0")]
    [InlineData("A_4550_Mintagyártó Kft._2024-2 kalibrálás")]
    public void ContainsRealWord_TrueForDescriptiveNames(string name) =>
        Assert.True(MeaningfulName.ContainsRealWord(name));

    [Theory]
    // Vak nevek: csak kód, sorszám vagy tartalom nélküli töltelékszó -> ezeket érdemes átnevezni.
    [InlineData("P-00001233")]
    [InlineData("PRV0670-0024090172-MEB-ANH-001-AA")]
    [InlineData("image2015-07-14-072719_ocr")]
    [InlineData("Documents_ocr")]
    [InlineData("scan0001")]
    [InlineData("IMG_1234")]
    [InlineData("07384_05_4 20200309 mod")]
    [InlineData("R913023370_05")]
    [InlineData("")]
    public void ContainsRealWord_FalseForOpaqueNames(string name) =>
        Assert.False(MeaningfulName.ContainsRealWord(name));

    [Theory]
    // OCR-szemét: a kevert kis/nagybetűs betűkupac nem szó. Ezek a rossz PDF-kinyerésből jövő
    // "címek" egy valódi futás naplójából származnak.
    [InlineData("BEaami[wu")]
    [InlineData("Ji-SID&ÖPIUf")]
    [InlineData("örSZ3HBa©,5EI")]
    [InlineData("33Fåg")]
    [InlineData("wwwE]")]
    public void ContainsRealWord_FalseForOcrGarbage(string text) =>
        Assert.False(MeaningfulName.ContainsRealWord(text));

    [Theory]
    // Címként ennél többet várunk: legalább két valódi szó, vagy egy elég hosszú.
    [InlineData("Rexroth Tightening System 300")]
    [InlineData("Mérési ciklusok használati útmutató")]
    [InlineData("CX-1T külső felület görgőzőgép kezelési útmutató")]
    [InlineData("Kézikönyv")]
    [InlineData("Adatlap")]
    public void IsDescriptiveTitle_TrueForRealTitles(string title) =>
        Assert.True(MeaningfulName.IsDescriptiveTitle(title));

    [Theory]
    // Egyetlen rövid szó nem cím -- ezek a valódi naplóból jött melléfogások.
    [InlineData("Umin")]
    [InlineData("15Umin")]
    [InlineData("BEaami[wu")]
    [InlineData("örSZ3HBa©,5EI")]
    [InlineData("33Fåg")]
    public void IsDescriptiveTitle_FalseForJunk(string title) =>
        Assert.False(MeaningfulName.IsDescriptiveTitle(title));
}
