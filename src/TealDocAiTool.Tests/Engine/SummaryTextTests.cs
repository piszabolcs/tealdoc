using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

/// <summary>
/// A fájl-leírás tisztítása. A tesztesetek egy 3343 leírásos VALÓDI futás átvilágításából
/// származnak — nem kitalált bemenetek.
/// </summary>
public class SummaryTextTests
{
    // 16 leírás kezdődött így: a modell a prompt záró szavát („Összefoglaló:") visszhangozta.
    [Theory]
    [InlineData("Az összefoglaló:  Kezelési utasítás a TRAUB géphez.", "Kezelési utasítás a TRAUB géphez.")]
    [InlineData("Összefoglaló: A dokumentum egy adatlap.", "A dokumentum egy adatlap.")]
    [InlineData("ÖSSZEFOGLALÓ:   Kapcsolási rajz.", "Kapcsolási rajz.")]
    [InlineData("Leírás: Alkatrészlista.", "Alkatrészlista.")]
    public void Clean_StripsTheEchoedPromptLabel(string raw, string expected) =>
        Assert.Equal(expected, SummaryText.Clean(raw));

    // 21 leírásban maradt sortörés; a felsorolásban ez elvágná a sort.
    [Fact]
    public void Clean_CollapsesLineBreaksAndRuns()
    {
        string raw = "Karbantartási útmutató.\nA gép mechanikai részeire\r\n  vonatkozik.";

        Assert.Equal("Karbantartási útmutató. A gép mechanikai részeire vonatkozik.", SummaryText.Clean(raw));
    }

    [Fact]
    public void Clean_RemovesWrappingQuotes() =>
        Assert.Equal("Kapcsolási rajz.", SummaryText.Clean("\"Kapcsolási rajz.\""));

    // A mondat KÖZEPÉN az „összefoglaló" valódi tartalom lehet — csak az elején vágunk.
    [Fact]
    public void Clean_KeepsTheWordWhenItIsPartOfTheSentence()
    {
        string raw = "A dokumentum a mérési eredmények összefoglalója. Tartalmazza a tűréseket.";

        Assert.Equal(raw, SummaryText.Clean(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Összefoglaló:")]
    public void Clean_ReturnsEmptyForNothingUsable(string? raw) =>
        Assert.Equal("", SummaryText.Clean(raw));

    [Fact]
    public void Clean_LeavesAGoodDescriptionUntouched()
    {
        string jo = "Ez egy alkatrészleírás, amely a menetes orsóra vonatkozik. A gyártó az AVENTICS S.a.r.l.";

        Assert.Equal(jo, SummaryText.Clean(jo));
    }
}
