using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

public class ManualLikenessTests
{
    [Theory]
    [InlineData("MANUALE DELL'OPERATORE (B-63944IT-1_02_051104).pdf")]
    [InlineData("Betriebsanleitung_VARIO20.pdf")]
    [InlineData("Kézikönyv.docx")]
    [InlineData("kezelesi_utmutato.pdf")]
    [InlineData("MANUALE DI MANUTENZIONE (B-63945IT_02_060207).pdf")]
    [InlineData("Kézikönyv.pdf")]
    [InlineData("karbantartási utas.xls")]
    public void Score_RanksRealManualsHighest(string fileName)
    {
        Assert.Equal(2, ManualLikeness.Score(fileName));
    }

    [Theory]
    [InlineData("Installation_Checklist_Demo Supplier_Acme_Factory_Vario 208.pdf")]
    [InlineData("Műszaki rajz dokumentáció (3019597).PDF")]
    [InlineData("PROTOCOLLO_TEST_ELETTRICI_8.PDF")]
    [InlineData("SMARTKEYS_CODE_8092000325B_.PDF")]
    public void Score_DoesNotFavourIncidentalPaperwork(string fileName)
    {
        Assert.True(ManualLikeness.Score(fileName) < 2);
    }

    // Ez a konkrét fájl vitte félre a mintavételt a valódi 1401-es mappában: ábécé-sorrendben
    // előre került, pedig a hálózati szerviz-szoftver doksija semmit nem mond a gép fajtájáról.
    // A „service" szó ezért szándékosan NINCS a kulcsszavak közt.
    [Fact]
    public void Score_NetserviceIsNotMistakenForAManual()
    {
        Assert.Equal(0, ManualLikeness.Score("NETSERVICE_8092000325B_BOSC.PDF"));
    }

    [Fact]
    public void Score_PrefersTheManualOverTheAlphabeticallyFirstFile()
    {
        // A valódi 1401 mappa ábécé-sorrendben első fájlja vs. a tényleges kézikönyv.
        int first = ManualLikeness.Score("Installation_Checklist_Demo Supplier_Acme_Factory_Vario 208.pdf");
        int manual = ManualLikeness.Score("MANUALE DELL'OPERATORE (B-63944IT-1_02_051104).pdf");

        Assert.True(manual > first, "a kézikönyvnek meg kell előznie a telepítési ellenőrzőlistát");
    }

    [Fact]
    public void Score_IgnoresTheExtension()
    {
        // A kiterjesztés nem szólhat bele: a „.doc" nem tehet egy fájlt „dokumentáció"-vá.
        Assert.Equal(0, ManualLikeness.Score("P-00001233.doc"));
    }

    // A VALÓDI HIBA, amit ez javít: a 1401-es MINTAGYARTO VARIO 20 esztergánál a FANUC VEZÉRLŐ
    // olasz kézikönyve nyert a gép saját doksija ellen, mert abban van a „manuale" szó. Egy
    // vezérlő-kézikönyv esztergálásról, fúrásról és marásról egyaránt beszél — ezért lett minden
    // esztergából „Fúrógép".
    [Fact]
    public void Score_TheMachinesOwnDocumentBeatsAControllerManual()
    {
        var tokens = ManualLikeness.NameTokens("MINTAGYARTO VARIO 20");

        int own = ManualLikeness.Score("VARIO20-8linear-FA-BA-v10.0-de.pdf", tokens);
        int controller = ManualLikeness.Score("MANUALE DELL'OPERATORE (B-63944IT_03_100218).pdf", tokens);

        Assert.True(own > controller,
            $"a gép saját doksija ({own}) nem előzi meg a FANUC vezérlő kézikönyvét ({controller})");
    }

    [Fact]
    public void Score_TheMachinesOwnManualRanksAboveEverything()
    {
        // A 1203-as DEMOTEC-nél ez a fájl került a mintába — és a géptípus helyesen „eszterga" lett.
        var tokens = ManualLikeness.NameTokens("(M1900003) DEMOTEC L70HDx3000 (TK01-2024)");

        int ownManual = ManualLikeness.Score("1.2345.67.0000001_TK01_Betriebsanleitung_L70HD_DE.pdf", tokens);
        int vendorManual = ManualLikeness.Score("Zentralschmierung_MKU_SKF_DE.pdf", tokens);

        Assert.Equal(5, ownManual);
        Assert.True(ownManual > vendorManual);
    }

    [Fact]
    public void Score_ComponentVendorDocumentsAreDemoted()
    {
        var tokens = ManualLikeness.NameTokens("DEMOTEC L70HDx3000");

        Assert.True(ManualLikeness.Score("Filterlüfter_SK_Rittal_DE.pdf", tokens) < 0);
        Assert.True(ManualLikeness.Score("Tauchpumpe_TS12_TS24_Brinkmann_DE.pdf", tokens) < 0);
    }

    // Ha a gép MAGA a beszállító terméke, a doksija a helyes minta — nem szabad levonni érte.
    [Fact]
    public void Score_VendorPenaltyDoesNotApplyWhenTheMachineIsThatVendors()
    {
        var tokens = ManualLikeness.NameTokens("8801 RITTAL kapcsolószekrény");

        Assert.True(ManualLikeness.Score("Rittal_Bedienungsanleitung.pdf", tokens) > 2);
    }

    [Fact]
    public void NameTokens_DropsShortAndNumericParts()
    {
        var tokens = ManualLikeness.NameTokens("MINTAGYARTO VARIO 20");

        Assert.Contains("mintagyarto", tokens);
        Assert.Contains("vario", tokens);
        // A „20" minden második fájlnévre illeszkedne — a mintavétel értelmét venné el.
        Assert.DoesNotContain("20", tokens);
    }

    [Fact]
    public void NameTokens_SplitsOnBracketsAndPunctuation()
    {
        var tokens = ManualLikeness.NameTokens("(M1900003) DEMOTEC L70HDx3000 (TK01-2024)");

        Assert.Contains("demotec", tokens);
        Assert.Contains("l70hdx3000", tokens);
        Assert.Contains("m1900003", tokens);
    }

    [Fact]
    public void Score_PaperworkIsDemotedEvenWhenItNamesTheMachine()
    {
        // A telepítési ellenőrzőlista a gép nevét viseli, de a géptípusról semmit nem mond.
        var tokens = ManualLikeness.NameTokens("MINTAGYARTO VARIO 20");

        int checklist = ManualLikeness.Score("Installation_Checklist_Demo Supplier_Acme_Factory_Vario 208.pdf", tokens);
        int ownDoc = ManualLikeness.Score("VARIO20-8linear-FA-BA-v10.0-de.pdf", tokens);

        Assert.True(ownDoc > checklist);
    }
}
