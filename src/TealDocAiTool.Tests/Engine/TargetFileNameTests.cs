using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

public class TargetFileNameTests
{
    [Fact]
    public void Build_KeepsOriginalNameInParentheses()
    {
        // A cikkszám a legfontosabb keresőkulcs (SAP, rendelés, kézikönyv) — sosem dobjuk el.
        Assert.Equal("Adjust block (P-00001233)", TargetFileName.Build("Adjust block", @"C:\SOROK\gep\P-00001233.pdf"));
    }

    [Fact]
    public void Build_MakesRepeatedPartNamesUnique()
    {
        // A rajzokon az "alkatrésznév" leíró, de nem egyedi: 51 fájl lett volna BRACKET.pdf.
        string first = TargetFileName.Build("BRACKET", @"C:\SOROK\09435-421.pdf");
        string second = TargetFileName.Build("BRACKET", @"C:\SOROK\09435-420.pdf");

        Assert.NotEqual(first, second);
        Assert.Equal("BRACKET (09435-421)", first);
    }

    [Fact]
    public void Build_DoesNotRepeatTheNumberTheTitleAlreadyContains()
    {
        // Ha a rajzszám a cím KÖZEPÉN van, levágni nem lehet — de megduplázni sem szabad,
        // hogy ne legyen "... R450062998_006 rajz (R450062998_006)".
        Assert.Equal("Merevítő R450062998_006 rajz",
            TargetFileName.Build("Merevítő R450062998_006 rajz", @"C:\SOROK\R450062998_006.pdf"));
    }

    [Theory]
    // A modell a cím VÉGÉRE biggyeszti a rajzszámot, és gyakran CSONKÁN — a 8 szavas korlát
    // elvágja. A zárójeles utótag úgyis hozzáteszi a teljeset, tehát a csonkot le kell vágni;
    // enélkül "Camera holder-P-0000 (P-00001388)" lenne belőle. Egy P-számos rajzmappában az
    // LLM-címek 22,6%-a nézett így ki.
    [InlineData("Camera holder-P-0000", @"C:\SOROK\P-00001388.pdf", "Camera holder (P-00001388)")]
    [InlineData("Side block-P-00001", @"C:\SOROK\P-00001081.pdf", "Side block (P-00001081)")]
    [InlineData("Roller bearing-P-00001583", @"C:\SOROK\P-00001583.pdf", "Roller bearing (P-00001583)")]
    [InlineData("Abdeckung - Cover-P-00009", @"C:\SOROK\P-00009609.pdf", "Abdeckung - Cover (P-00009609)")]
    [InlineData("Klemmstück R450013628", @"C:\SOROK\R450013628_04_Lap.pdf", "Klemmstück (R450013628_04_Lap)")]
    public void Build_CutsThePartNumberTheModelAppendedToTheTitle(string title, string path, string expected)
    {
        Assert.Equal(expected, TargetFileName.Build(title, path));
    }

    [Theory]
    // Nem szabad túlbuzgónak lenni: rövid egyezés vagy szám a cím közepén nem vágás-ok.
    [InlineData("Handles holder", @"C:\SOROK\P-00001666.pdf", "Handles holder (P-00001666)")]
    [InlineData("Motor 1234", @"C:\SOROK\1234-5678.pdf", "Motor 1234 (1234-5678)")]
    public void Build_LeavesTitlesThatMerelyEndWithDigits(string title, string path, string expected)
    {
        Assert.Equal(expected, TargetFileName.Build(title, path));
    }

    [Fact]
    public void Build_KeepsTheTitleWhenCuttingWouldLeaveNothing()
    {
        // Ha a cím GYAKORLATILAG a cikkszám, a vágás után nem maradna semmi értelmes —
        // ilyenkor inkább maradjon úgy, ahogy volt (a Kept/Failed út dönt róla).
        Assert.Equal("P-00001388", TargetFileName.Build("P-00001388", @"C:\SOROK\P-00001388.pdf"));
    }

    [Fact]
    public void Build_TruncatesTheTitleButNeverTheOriginalName()
    {
        string longTitle = new string('a', 200);
        string result = TargetFileName.Build(longTitle, @"C:\SOROK\P-00001233.pdf");

        Assert.EndsWith(" (P-00001233)", result);
        Assert.True(result.Length <= 100, $"túl hosszú fájlnév: {result.Length}");
    }

    [Fact]
    public void Build_DoesNotLeaveTrailingSpaceAfterTruncation()
    {
        // A vágás pont a szóközre esik (room = 90), ezért kell a TrimEnd.
        string title = new string('a', 89) + " " + new string('b', 50);
        string result = TargetFileName.Build(title, @"C:\SOROK\P-1.pdf");

        Assert.DoesNotContain("  (", result);
        Assert.EndsWith(" (P-1)", result);
    }

    [Fact]
    public void Build_FallsBackToTitleOnlyWhenOriginalNameLeavesNoRoom()
    {
        // Ilyen a gyakorlatban alig fordul elő (a hosszú név rendszerint beszédes -> Kept),
        // de a fájlnév akkor sem nőhet a végtelenbe.
        string veryLongOriginal = new string('x', 120);
        string result = TargetFileName.Build("Adjust block", $@"C:\SOROK\{veryLongOriginal}.pdf");

        Assert.Equal("Adjust block", result);
    }
}
