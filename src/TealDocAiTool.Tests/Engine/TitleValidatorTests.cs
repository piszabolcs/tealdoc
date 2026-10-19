using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

public class TitleValidatorTests
{
    [Fact]
    public void Validate_TrimsAndReturnsCleanTitle()
    {
        Assert.Equal("Phoenix Contact relé kézikönyv", TitleValidator.Validate("  Phoenix Contact relé kézikönyv  "));
    }

    [Fact]
    public void Validate_StripsInvalidFileNameCharacters()
    {
        var result = TitleValidator.Validate("Cím: \"idézet\" / teszt");
        Assert.DoesNotContain('"', result);
        Assert.DoesNotContain('/', result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public void Validate_RejectsEmptyOrTooShort(string? input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Theory]
    [InlineData("ACME")]
    [InlineData("acme")]
    [InlineData("Document1")]
    [InlineData("Page 1")]
    public void Validate_RejectsDenylistedNoise(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Fact]
    public void Validate_RejectsOverlyLongTitles()
    {
        // Túl hosszút NEM csonkolunk mondattöredékké — elutasítjuk (a hívó tovább próbál / Failed).
        string longTitle = new string('a', 200);
        Assert.Null(TitleValidator.Validate(longTitle));
    }

    [Theory]
    [InlineData("26.5")]          // csak szám (OCR-melléfogás)
    [InlineData("M 1  2")]        // 1 valódi betű
    [InlineData("2,6 X 30n")]     // méret-felirat, főleg szám/szimbólum
    [InlineData("121212121212")]  // csupa szám (LLM számsor-melléfogás)
    public void Validate_RejectsMostlyNonLetterJunk(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Fact]
    public void Validate_RejectsRepeatedGlyphArtifact()
    {
        // Hibás PDF-font-leképezés: egy valódi szó után ugyanaz a glyph ismétlődik végig.
        Assert.Null(TitleValidator.Validate("BurnishedŰ ŰŰŰ Ű Ű  ŰŰ    Ű   Ű  Ű"));
    }

    [Fact]
    public void Validate_RejectsLlmDescriptionInsteadOfTitle()
    {
        // Az LLM cím helyett leírást adott — ne csonkoljuk, utasítsuk el (>120 karakter).
        string sentence = "A szöveg egy naplóbejegyzés vagy hibaüzenet sorozata, amelyek valószínűleg egy számítógépes rendszer vagy alkalmazás működésére utalnak";
        Assert.Null(TitleValidator.Validate(sentence));
    }

    [Theory]
    // Egy valódi futás naplójából: rossz PDF-kinyerésből jött betűkupacok, amiket eddig elfogadtunk.
    [InlineData("BEaami[wu")]
    [InlineData("wwwE]")]
    [InlineData("33Fåg")]
    [InlineData("örSZ3HBa©,5EI")]
    [InlineData("Ji-SID&ÖPIUf")]
    public void Validate_RejectsOcrGarbageWithoutRealWords(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Theory]
    // Egyetlen rövid szó nem ér fel egy meglévő fájlnévvel — inkább maradjon az eredeti.
    [InlineData("Umin")]
    [InlineData("15Umin")]
    [InlineData("Linie")]
    public void Validate_RejectsSingleShortWord(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Theory]
    // Jogi lábléc / copyright — a PDF-heurisztika néha ezt találja a legnagyobb betűs sornak.
    // Valódi találatok egy éles futásból; ilyenkor inkább menjen tovább az LLM-hez.
    [InlineData("Copyright  Hilscher Gesellschaft für Systemautomation  FAQISA#03D")]
    [InlineData("© Oltalmi jog bejelentése esetén is minden jog a AVENTICS S.a.r.l.-t illeti")]
    [InlineData("All rights reserved including the right of reproduction")]
    [InlineData("Minden jog fenntartva, beleértve a másolás jogát")]
    [InlineData("Alle Rechte vorbehalten")]
    public void Validate_RejectsCopyrightAndLegalBoilerplate(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Theory]
    // Valódi találatok egy éles futásból. A PDF-heurisztika a legnagyobb betűs sort veszi; rossz
    // szövegkinyerésnél ez egyetlen összefolyt betűkupac lesz, ami "szónak" látszik.
    [InlineData("iEsesibInd.stilast.beRkReb")]
    [InlineData("erirr0rena")]
    [InlineData("©eeyeol")]
    [InlineData("urnmute}")]
    [InlineData("205Rexrotpnwstangnapasinhylsline")]
    [InlineData("UtgåvaUpplagdOrsak")]
    [InlineData("seiesslsRI")]
    [InlineData("aSEENET")]
    [InlineData("ElectricDrivesHydraulicsLinearMotionandPneumaticsServiceexrota")]
    [InlineData("[8277-532-700]Mecman[")]
    // Ez nem zaj, hanem a rajzfej CÍMKÉJE — a mezők neve, nem a tartalmuk.
    [InlineData("Zeichnungsnr.Normteil")]
    public void ValidateHeuristic_RejectsSingleWordGarbage(string input)
    {
        Assert.Null(TitleValidator.ValidateHeuristic(input));
    }

    [Theory]
    // Ugyanezek a rajzok az LLM-től rendes alkatrésznevet kapnak — a szigorítás CSAK a vak
    // heurisztikára vonatkozik, különben ezeket a jó címeket is eldobnánk.
    [InlineData("Schlauch_PTFE_d7xD10")]
    [InlineData("Kugel_eindruecken")]
    [InlineData("Kabelverschraubung_M16x15")]
    [InlineData("Rögzítőcsavar_GRLZ-M5-QS-4")]
    public void Validate_AcceptsLlmSingleWordTitlesWithCodes(string input)
    {
        Assert.Equal(input, TitleValidator.Validate(input));
    }

    [Theory]
    // Egyetlen szó a heurisztikából is bőven jó, ha valódi alkatrésznév alakú.
    [InlineData("Abdeckung")]
    [InlineData("BRACKET")]
    [InlineData("Schlauchanschluß")]
    [InlineData("OPC-Server")]
    [InlineData("EVU-Gestell")]
    [InlineData("AIRPAD-MACHINE")]
    [InlineData("Kaliberwerkzeug-ZSB")]
    public void ValidateHeuristic_KeepsCleanPartNames(string input)
    {
        Assert.Equal(input, TitleValidator.ValidateHeuristic(input));
    }

    [Fact]
    public void ValidateHeuristic_MultiWordTitlesKeepTheCommonRules()
    {
        // Több szó esetén a cikkszámok és rövidítések rendben vannak — ott a szövegkörnyezet
        // mutatja, hogy valódi címet találtunk, nem egy betűkupacot.
        string good = "Programm zur Erstellung von Erstmusterprüfberichten (EMPB) nach QSP0515 Bemusterung";
        Assert.Equal(good, TitleValidator.ValidateHeuristic(good));
    }

    [Theory]
    // Zebra nyomtató-kézikönyvek idegen nyelvű EULA-i: a heurisztika a mondat KÖZEPÉBŐL vág ki
    // egy darabot. A szó-kapunk latin szavakat keres, a japán/koreai/kínai/orosz szövegben a
    // latin cégnév átcsúszik rajta. A heurisztika itt nem tud megítélni semmit — menjen az LLM-hez,
    // ami legalább érti, mit lát.
    [InlineData("ません ) と Zebra Technologies International,  LLC (以下「Z ebra」といいます)  の間で締")]
    [InlineData("프트웨어 ) 에  대해  귀하 ( 개인  또는  단일  개체 ) 와  Zebra Technologies International, LLC")]
    [InlineData("A. 保証および保証期間 Zebra Technologies International, LLC")]
    [InlineData("B. Гарантийные обязательства компании Zebra")]
    [InlineData("키, 미디어, 리본, POCKET EYE")]
    public void ValidateHeuristic_RejectsNonLatinSentenceFragments(string input)
    {
        Assert.Null(TitleValidator.ValidateHeuristic(input));
    }

    [Theory]
    // A dokumentum kétszer tartalmazza ugyanazt a sort (fejléc + tartalom), és a kinyerés
    // összefűzi. Ez nem elutasítandó — csak az egyik példány kell.
    [InlineData("Windows 2000 Windows 2000", "Windows 2000")]
    [InlineData("Síkrajz - Síkrajz", "Síkrajz")]
    [InlineData("Adatlap  Adatlap", "Adatlap")]
    public void Validate_KeepsOnlyOneCopyOfADuplicatedTitle(string input, string expected)
    {
        Assert.Equal(expected, TitleValidator.Validate(input));
    }

    [Theory]
    // Az elveszett szóközök miatt egy egész mondat lesz egyetlen "szó". A német összetételek
    // legitimek, de egy 60+ karakteres futam már nem alkatrésznév, hanem összefolyt szöveg.
    [InlineData("Atanúsítványújbólkibocsátásáranincslehetőségemiattkérjükaszoftvertermékkelegyüt")]
    [InlineData("Start—LuckaFastspäningTS600FixturTightFineleakGrossleakTestfinishedRea")]
    [InlineData("RitadKopKontrStandGodkSkalaErsatterErsattavperaceeaeeeeFastekolvsting")]
    public void Validate_RejectsRunTogetherSentences(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Theory]
    // Valódi LLM-válaszok ugyanebből a futásból: hosszúak és szóköz nélküliek, de értelmesek.
    [InlineData("DistanzleisteEinpressvorrichtungEndmontage")]
    [InlineData("Drosselrueckschlagventil_GRLZ-M5-QS-41LFC")]
    [InlineData("RollenhalterIm6Trichterhalteblech")]
    public void Validate_KeepsLongButMeaningfulCompounds(string input)
    {
        Assert.Equal(input, TitleValidator.Validate(input));
    }

    [Theory]
    // Négy azonos betű egymás után egyetlen nyelvben sem fordul elő — ez hibás PDF-font-leképezés.
    // (Háromnál igen: "Schifffahrt", "Sauerstoffflasche".)
    [InlineData("ccccoooommmmpppplllleeeetttteeee    ddddooooccccuuuummmmeeeenntt")]
    [InlineData("Baaaanhof")]
    public void Validate_RejectsBrokenFontRuns(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Fact]
    public void Validate_KeepsGermanTripleLetterCompound()
    {
        Assert.Equal("Sauerstoffflasche", TitleValidator.Validate("Sauerstoffflasche"));
    }

    [Fact]
    public void Validate_RejectsSingleWordFullOfSymbols()
    {
        // A modell néha visszaadja a nyers zajt, amit a rajzon lát.
        Assert.Null(TitleValidator.Validate("LLA)18=f5lanlan3_=&ImIaolomfom[dYT"));
    }

    [Fact]
    public void Validate_KeepsGenuineLongDescriptiveTitle()
    {
        // Ne legyen túl agresszív: valódi, tartalmas (de <120 karakteres) német cím maradjon meg.
        string good = "Programm zur Erstellung von Erstmusterprüfberichten (EMPB) nach QSP0515 Bemusterung";
        Assert.Equal(good, TitleValidator.Validate(good));
    }

    [Theory]
    // Valódi találat egy éles futásból (Heuristic): egy CE-tanúsítvány fejléce. Az IsDescriptiveTitle
    // simán átengedte, mert a település és a cégnév alakilag teljesen rendes szónak néz ki. Csak a szerkezet
    // (kukac, telefonos előtag) árulja el, hogy ez elérhetőség, nem cím.
    [InlineData("H-1234 MINTAVAROS info@pelda-gyarto.example +36 (0)1-234 567 89 Cegj. 01-09-123456 (CE)")]
    [InlineData("072 311 8404, El Minta 42")]
    public void Validate_RejectsContactInformation(string input)
    {
        Assert.Null(TitleValidator.Validate(input));
    }

    [Theory]
    // A telefonminta SZÁNDÉKOSAN csak szóközös elválasztásra szűkül. A Acme-féle
    // anyagszámok ("3 609 431 446") ugyanúgy szóközzel tagolt számcsoportok, és gyakoriak
    // a valódi fájlnevekben - ha 0-val kezdődő csoportra nem szűkítenénk, ezeket is
    // elutasítanánk.
    [InlineData("Strebenprofil 45x90L Acme-Linear 3 609 431 446")]
    [InlineData("Gewindeeinsatz Ensat-SH M5 2D069-091-90100")]
    public void Validate_KeepsPartNumbersThatLookLikePhonePatterns(string input)
    {
        Assert.Equal(input, TitleValidator.Validate(input));
    }

    // S-4: a dokumentumból jövő láthatatlan formázó karakter (U+202E: jobbról-balra) megfordítaná
    // az Intézőben a látszó kiterjesztést.
    [Fact]
    public void Validate_RemovesInvisibleFormatCharacters()
    {
        Assert.Equal("Szerelési rajz fdp.exe", TitleValidator.Validate("Szerelési rajz ‮fdp.exe​"));
    }

    // C-10: a többsoros LLM-válasz sortöréseinek törlése összeragasztotta a sorokat.
    [Theory]
    [InlineData("Kezelési útmutató\nMagyarázat: a dokumentum…", "Kezelési útmutató")]
    [InlineData("\n  Cím: „Hidraulikus tömlő cseréje”\n", "Hidraulikus tömlő cseréje")]
    [InlineData("\"Alkatrészlista\"", "Alkatrészlista")]
    public void NormalizeLlmAnswer_KeepsOnlyTheFirstLineWithoutLabelAndQuotes(string raw, string expected)
    {
        Assert.Equal(expected, TitleValidator.NormalizeLlmAnswer(raw));
    }
}
