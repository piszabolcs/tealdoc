using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

public class FolderNameParserTests
{
    // Valósághű, anonimizált mappanevek.
    [Theory]
    [InlineData("1401 MINTAGYARTO VARIO 20", "1401", "MINTAGYARTO VARIO 20")]
    [InlineData("1400 TRAUB TNK36 (1657)", "1400", "TRAUB TNK36 (1657)")]
    [InlineData("1203 (M1900003) DEMOTEC L70HDx3000 (TK01-2024)", "1203", "(M1900003) DEMOTEC L70HDx3000 (TK01-2024)")]
    [InlineData("7246 DEMOPRESS présgép 20120626", "7246", "DEMOPRESS présgép 20120626")]
    public void Parse_SplitsNumberFromName(string folder, string number, string name)
    {
        var id = FolderNameParser.Parse(folder);

        Assert.Equal(number, id.Number);
        Assert.Equal(name, id.Name);
        Assert.Equal(folder, id.RawFolderName);
    }

    // A gépszám a legfontosabb keresőkulcs, de nem minden mappa kezdődik vele — ilyenkor a
    // teljes név marad, nem csonkolunk.
    [Theory]
    [InlineData("ABB Robotos cella SOROK")]
    [InlineData("EDMOLIFT emelők")]
    [InlineData("Manual Sleeve Line")]
    [InlineData("01_Szerszámtartók")]
    public void Parse_KeepsTheWholeNameWhenThereIsNoLeadingMachineNumber(string folder)
    {
        var id = FolderNameParser.Parse(folder);

        Assert.Equal(folder, id.Name);
        Assert.Equal(folder, id.RawFolderName);
    }

    // A betűvel kezdődő leltári gépszám ugyanolyan keresőkulcs, mint a sima gépszám — ezt
    // korábban elveszítettük, és a teljes mappanév csúszott a névbe.
    [Theory]
    [InlineData("M0100009 MINTACNC VZ-10 megmunkáló kp (lengyel)", "M0100009", "MINTACNC VZ-10 megmunkáló kp (lengyel)")]
    [InlineData("M2402408 BROTHER TC-R2A", "M2402408", "BROTHER TC-R2A")]
    [InlineData("G5100302 KHZ prés új", "G5100302", "KHZ prés új")]
    public void Parse_ExtractsLetterPrefixedInventoryNumbers(string folder, string number, string name)
    {
        var id = FolderNameParser.Parse(folder);

        Assert.Equal(number, id.Number);
        Assert.Equal(name, id.Name);
    }

    [Fact]
    public void Parse_KeepsEveryInventoryNumberWhenAFolderListsSeveral()
    {
        // „M1200011, M1200012 DEMO HMC500" — mindkét számra kereshetnek.
        var id = FolderNameParser.Parse("M1200011, M1200012 DEMO HMC500");

        Assert.Contains("M1200011", id.Number);
        Assert.Contains("M1200012", id.Number);
        Assert.Equal("DEMO HMC500", id.Name);
    }

    // A sorjel és a leltári gépszám nem keveredhet: a sorjelnél PONTOSAN 2 számjegy áll a betű után.
    [Fact]
    public void Parse_DoesNotMistakeAnInventoryNumberForALineCode()
    {
        Assert.Equal("C3000007", FolderNameParser.Parse("C3000007 - 4C Demo szegecselő D12-D25").Number);
        Assert.Equal("C28", FolderNameParser.Parse("C28 ABNOX zsírzó").Number);
    }

    [Fact]
    public void Parse_KeepsTheRawNameForFileNaming()
    {
        // A RawFolderName-re épül az ütközés-védelem, ezért soha nem szabad megváltoznia.
        var id = FolderNameParser.Parse("  1401 MINTAGYARTO VARIO 20  ");

        Assert.Equal("1401 MINTAGYARTO VARIO 20", id.RawFolderName);
    }
}
