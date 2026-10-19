using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

public class EntryKindClassifierTests
{
    // Gyártósor = 1 betű + 2 számjegy. Valósághű, anonimizált mappanevek.
    [Theory]
    [InlineData("T05 DEMOAUTO 3_2 Hosszu-Rovid MINTA 07001")]
    [InlineData("T01 2252 DEMO HMC55")]
    [InlineData("T09 5_2 MINTA szerelés")]
    [InlineData("T19 UNI1")]
    [InlineData("C28 ABNOX zsírzó")]
    [InlineData("C07 - ISO Dugattyúcsavarozó")]
    [InlineData("C19")]
    [InlineData("C36 P16-P17-P20 PET")]
    public void Classify_OneLetterAndTwoDigitsIsAProductionLine(string folder)
    {
        Assert.Equal(MachineEntryKind.ProductionLine, EntryKindClassifier.Classify(folder));
    }

    [Fact]
    public void Classify_ARangeOfLinesIsStillALine()
    {
        Assert.Equal(MachineEntryKind.ProductionLine, EntryKindClassifier.Classify("T17-18-19 8934 ESD rework"));
    }

    // Gép = legalább 4 számjegy, betű nélkül.
    [Theory]
    [InlineData("7246 DEMOPRESS présgép 20120626")]
    [InlineData("9778 MintaClean mosó")]
    [InlineData("0229 MNI fojtó prés")]
    [InlineData("4214 Kaltenbach RKL551 (MOE1)")]
    [InlineData("53600001 ISO sor ORT RP18 görgőzőgép (Szalóki)")]
    [InlineData("1401 MINTAGYARTO VARIO 20")]
    [InlineData("3432")]
    public void Classify_FourOrMoreDigitsIsAMachine(string folder)
    {
        Assert.Equal(MachineEntryKind.Machine, EntryKindClassifier.Classify(folder));
    }

    // A rossz besorolás rosszabb, mint a semmilyen — ezekre SZÁNDÉKOSAN nem mondunk semmit.
    // Betűvel kezdődő leltári gépszám — ez is gép. A sorjeltől az különbözteti meg, hogy ott
    // PONTOSAN 2 számjegy áll a betű után.
    [Theory]
    [InlineData("M0100009 MINTACNC VZ-10 megmunkáló kp (lengyel)")]
    [InlineData("M2402408 BROTHER TC-R2A")]
    [InlineData("C3000007 - 4C Demo szegecselő D12-D25")]
    [InlineData("G5100001 MINTA prés")]
    public void Classify_LetterPrefixedInventoryNumberIsAMachine(string folder)
    {
        Assert.Equal(MachineEntryKind.Machine, EntryKindClassifier.Classify(folder));
    }

    [Theory]
    [InlineData("T010 2402 DEMO MARO 700")]                  // betű + 3 számjegy
    [InlineData("G510 4C ládaszállító pálya")]                 // betű + 3 számjegy
    [InlineData("H134 mosó")]                                  // betű + 3 számjegy
    [InlineData("404x GPC teszter MINTA 02001")]               // maszkolt szám
    [InlineData("740_Automata_Csavarozó")]                     // csak 3 számjegy
    [InlineData("4C görgősor")]                                // szám + betű
    [InlineData("ABB Robotos cella SOROK")]
    [InlineData("EDMOLIFT emelők")]
    [InlineData("Manual Sleeve Line")]
    [InlineData("NC3 gyűjtőlista")]
    public void Classify_DoesNotGuessWhenTheCodeDoesNotDecide(string folder)
    {
        Assert.Equal(MachineEntryKind.Unknown, EntryKindClassifier.Classify(folder));
    }

    [Fact]
    public void Classify_MachineNumberIsNotConfusedWithALineCode()
    {
        // A gépszám sosem kezdődik betűvel, a sorkód mindig igen — ez a két szabály nem fedheti egymást.
        Assert.NotEqual(
            EntryKindClassifier.Classify("7246 DEMOPRESS présgép"),
            EntryKindClassifier.Classify("T05 DEMOAUTO"));
    }
}
