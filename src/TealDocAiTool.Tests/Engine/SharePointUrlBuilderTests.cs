using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

/// <summary>
/// A helyi útvonal → webes (SharePoint-) cím leképezés. Ez nem kozmetika: a helyi út egy
/// megosztott tudásbázisban használhatatlan.
/// </summary>
public class SharePointUrlBuilderTests
{
    [Fact]
    public void NormalizeBase_KeepsCanonicalFolderUrl()
    {
        string result = SharePointUrlBuilder.NormalizeBase(
            "https://contoso.sharepoint.com/sites/Demo/Shared%20Documents/Archivum/CNC%20Esztergak");

        Assert.Equal(
            "https://contoso.sharepoint.com/sites/Demo/Shared%20Documents/Archivum/CNC%20Esztergak",
            result);
    }

    [Fact]
    public void NormalizeBase_DropsTrailingSlash()
    {
        string result = SharePointUrlBuilder.NormalizeBase("https://x.sharepoint.com/sites/S/Docs/");

        Assert.Equal("https://x.sharepoint.com/sites/S/Docs", result);
    }

    [Fact]
    public void NormalizeBase_ExtractsPathFromLibraryViewUrl()
    {
        // Ezt mutatja a böngésző címsora, amikor a mappában állsz — a valódi út az `id` paraméter.
        string result = SharePointUrlBuilder.NormalizeBase(
            "https://contoso.sharepoint.com/sites/Demo/Shared%20Documents/Forms/AllItems.aspx" +
            "?id=%2Fsites%2FDemo%2FShared%20Documents%2FArchivum%2FCNC%20Esztergak&viewid=abc123");

        Assert.Equal(
            "https://contoso.sharepoint.com/sites/Demo/Shared%20Documents/Archivum/CNC%20Esztergak",
            result);
    }

    [Fact]
    public void NormalizeBase_DropsQueryStringFromCanonicalUrl()
    {
        // A `?e=AbCd12` bennmaradva a hozzáfűzött fájlnév a QUERY STRINGBE kerülne, nem az útba.
        string result = SharePointUrlBuilder.NormalizeBase("https://x.sharepoint.com/sites/S/Docs/Gep?e=AbCd12");

        Assert.Equal("https://x.sharepoint.com/sites/S/Docs/Gep", result);
    }

    [Fact]
    public void NormalizeBase_RejectsSharingLink()
    {
        // A „Megosztás” gombbal másolt link a mappát TOKENNEL azonosítja — gyerek-elem címét nem
        // lehet belőle képezni. Némán elfogadva több ezer .md kapna halott hivatkozást.
        string link = "https://contoso.sharepoint.com/:f:/s/Demo/IgAAAAAAAAAAAAAAAAAAAAAAAA?e=AbCd12";

        Assert.True(SharePointUrlBuilder.IsSharingLink(link));
        Assert.Equal("", SharePointUrlBuilder.NormalizeBase(link));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nem-url")]
    [InlineData("ftp://x.sharepoint.com/sites/S")]
    public void NormalizeBase_ReturnsEmptyForUnusableInput(string input) =>
        Assert.Equal("", SharePointUrlBuilder.NormalizeBase(input));

    [Fact]
    public void IsSharingLink_IsFalseForCanonicalUrl() =>
        Assert.False(SharePointUrlBuilder.IsSharingLink("https://x.sharepoint.com/sites/S/Docs/Gep"));

    [Fact]
    public void Combine_EncodesSpacesAndAccents()
    {
        string result = SharePointUrlBuilder.Combine(
            "https://x.sharepoint.com/sites/S/Docs", "01_Szerszámtartók/Spec.30-as kulcs_ocr.pdf");

        Assert.Equal(
            "https://x.sharepoint.com/sites/S/Docs/01_Szersz%C3%A1mtart%C3%B3k/Spec.30-as%20kulcs_ocr.pdf",
            result);
    }

    [Fact]
    public void Combine_TreatsBackslashAsSeparator()
    {
        // A relatív út a Windowsról jön, tehát `\` — ha nem elválasztóként kezelnénk, egyetlen
        // szegmensként `%5C`-vé kódolódna, és a hivatkozás halott lenne.
        string result = SharePointUrlBuilder.Combine("https://x.sharepoint.com/D", @"alkonyvtar\rajz.pdf");

        Assert.Equal("https://x.sharepoint.com/D/alkonyvtar/rajz.pdf", result);
    }

    [Fact]
    public void Combine_ReturnsFolderUrlForEmptyRelativePath() =>
        Assert.Equal("https://x.sharepoint.com/D", SharePointUrlBuilder.Combine("https://x.sharepoint.com/D", ""));

    [Fact]
    public void Combine_ReturnsEmptyWithoutBase() =>
        Assert.Equal("", SharePointUrlBuilder.Combine("", "barmi.pdf"));

    [Fact]
    public void ForMachineFolder_UsesPathRelativeToRoot()
    {
        string result = SharePointUrlBuilder.ForMachineFolder(
            "https://x.sharepoint.com/sites/S/Docs/CNC%20Esztergak",
            @"C:\Archivum\CNC Esztergak",
            @"C:\Archivum\CNC Esztergak\1203 DEMOTEC L70HD");

        Assert.Equal("https://x.sharepoint.com/sites/S/Docs/CNC%20Esztergak/1203%20DEMOTEC%20L70HD", result);
    }

    [Fact]
    public void ForMachineFolder_ToleratesTrailingSeparatorOnMachineFolder()
    {
        string result = SharePointUrlBuilder.ForMachineFolder(
            "https://x.sharepoint.com/D", @"C:\Gyoker", @"C:\Gyoker\1401 GEP\");

        Assert.Equal("https://x.sharepoint.com/D/1401%20GEP", result);
    }

    // Egy gyorsítótárba több gyökér is futhat. Egy MÁSIK gyökér gépére a relatív út `..`
    // szegmenseket tartalmazna — abból csak halott hivatkozás lenne, ezért inkább nem adunk semmit.
    [Fact]
    public void ForMachineFolder_RefusesFoldersOutsideTheRoot() =>
        Assert.Equal("", SharePointUrlBuilder.ForMachineFolder(
            "https://x.sharepoint.com/D", @"C:\Archivum\CNC Esztergak", @"C:\Archivum\SOROK\T05"));

    [Theory]
    [InlineData(@"C:\Gyoker", @"C:\Gyoker\1401", true)]
    [InlineData(@"C:\Gyoker", @"C:\Gyoker\alkonyvtar\1401", true)]
    [InlineData(@"C:\Gyoker", @"C:\Masik\1401", false)]
    [InlineData(@"C:\Gyoker", @"D:\Gyoker\1401", false)]
    [InlineData("", @"C:\Barmi\1401", true)]
    public void IsUnderRoot_DetectsFoldersOutsideTheRoot(string root, string path, bool expected) =>
        Assert.Equal(expected, SharePointUrlBuilder.IsUnderRoot(root, path));

    [Fact]
    public void ForMachineFolder_ReturnsEmptyWithoutBaseUrl() =>
        Assert.Equal("", SharePointUrlBuilder.ForMachineFolder("", @"C:\Gyoker", @"C:\Gyoker\1401"));
}
