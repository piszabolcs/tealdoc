using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TealDocAiTool.Engine;

/// <summary>
/// Közös validáció a heurisztika és az LLM válaszára is: tiltott fájlnév-karakterek ki,
/// hossz-limit, és egy zajszó-denylist (gyártói logó-szavak, generikus Office-placeholderek),
/// amik gyakran "sikeres" de értéktelen jelöltként jönnének ki a heurisztikából.
/// </summary>
public static class TitleValidator
{
    private const int MinLength = 4;
    private const int MaxLength = 120;

    // Szóköz nélküli futam: a német összetételek legitimen hosszúak
    // ("DistanzleisteEinpressvorrichtungEndmontage" = 42), de egy egész mondat, amiből a
    // kinyerés kiejtette a szóközöket, már nem cím.
    private const int MaxSingleWordLength = 50;
    private const int MaxSymbolsInSingleWord = 2;

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    // Ugyanaz a szöveg kétszer, esetleg elválasztóval ("Windows 2000 Windows 2000",
    // "Síkrajz - Síkrajz"): a dokumentum fejlécében és a törzsében is szerepel, a kinyerés
    // összefűzi. Ez nem hiba, csak egy példány kell belőle.
    private static readonly Regex DuplicatedTitle = new(@"^(.{4,}?)\s*[-–—]?\s*\1$", RegexOptions.Compiled);

    // Négy azonos betű egymás után egyetlen nyelvben sem fordul elő — hibás PDF-font-leképezés.
    // Háromnál igen ("Schifffahrt", "Sauerstoffflasche"), ezért nem lehet szigorúbb.
    private static readonly Regex BrokenFontRun = new(@"(\p{L})\1{3}", RegexOptions.Compiled);

    // Cirill és CJK írásjegyek. A latin szó-kapunk ezekre vak, ezért a heurisztika nem tudja
    // megítélni őket — az idegen nyelvű EULA-kból mondatközepi darabokat vágott ki.
    private static readonly Regex NonLatinScript =
        new(@"[Ѐ-ӿ぀-ヿ一-鿿가-힯]", RegexOptions.Compiled);

    // E-mail-cím: mindig elérhetőség, soha nem cím.
    private static readonly Regex EmailAddress = new(@"[^\s@]+@[^\s@]+\.[^\s@]+", RegexOptions.Compiled);

    // Telefonszám: nemzetközi "+NN (0)" előtag, vagy szóközzel tagolt helyi forma. A
    // kötőjeles elválasztót SZÁNDÉKOSAN nem nézzük — a cikkszámok ("2D069-091-90100") és a
    // Acme-anyagszámok ("3 609 431 446") ugyanígy néznek ki, csak szóköz helyett kötőjellel
    // vagy 3-mal (nem 0-val) kezdődő csoporttal.
    private static readonly Regex PhoneNumber =
        new(@"\+\d{1,3}\s?\(0\)|\b0\d{1,2} \d{3} \d{3,4}\b", RegexOptions.Compiled);

    private static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACME", "SIEMENS", "PHOENIX CONTACT", "PAGE 1", "UNTITLED", "DOCUMENT1", "SHEET1",
    };

    // Jogi lábléc: a PDF-heurisztika (legnagyobb betűméretű sor) néha ezt találja meg a valódi
    // cím helyett. Nyelvenként egy-egy jellegzetes szó elég — ha ilyet látunk, a jelölt nem cím,
    // és inkább az LLM-re bízzuk a fájlt.
    private static readonly string[] BoilerplateMarkers =
    {
        "copyright", "rights reserved", "jog fenntartva", "oltalmi jog", "rechte vorbehalten",
        "all rights", "tous droits",
    };

    /// <summary>
    /// A heurisztika jelöltjére szigorúbb: ha a találat EGYETLEN szó, annak valódi szó alakúnak
    /// kell lennie. A heurisztika vak — a legnagyobb betűméretű sort veszi —, és rossz
    /// szövegkinyerésnél ez egy összefolyt betűkupac ("seiesslsRI", "Zeichnungsnr.Normteil"),
    /// ami a szó-kapun átcsúszik. Egy teljes éles futásban 646 egyszavas heurisztikás címből
    /// 286 volt ilyen. Az LLM-re ez NEM vonatkozik: ott az egyszavas válasz szándékos és jó
    /// ("Klemmstück", "Schlauch_PTFE_d7xD10"), és a modell látja is, amit olvas.
    /// Ami itt elbukik, nem vész el: megy tovább az LLM-hez.
    /// </summary>
    public static string? ValidateHeuristic(string? candidate)
    {
        string? cleaned = Validate(candidate);
        if (cleaned is null) return null;
        if (NonLatinScript.IsMatch(cleaned)) return null;
        if (!cleaned.Any(char.IsWhiteSpace) && !MeaningfulName.IsCleanWord(cleaned)) return null;
        return cleaned;
    }

    // A modell néha visszhangozza a prompt utolsó szavát („Cím: …”).
    private static readonly Regex EchoedLabel = new(
        @"^\s*(cím|cim|típus|tipus|géptípus|geptipus|title)\s*:\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly char[] QuoteChars = { '"', '\'', '„', '”', '“', '«', '»' };

    /// <summary>
    /// Az LLM nyers válaszából a cím-jelölt: CSAK az első nem üres sor (a többsoros válasz
    /// sortöréseinek törlése korábban összeragasztotta a sorokat: „Kezelési útmutatóMagyarázat…”),
    /// a visszhangzott „Cím:” címke és a körülölelő idézőjelek nélkül.
    /// </summary>
    public static string NormalizeLlmAnswer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string firstLine = raw.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return EchoedLabel.Replace(firstLine, "").Trim().Trim(QuoteChars).Trim();
    }

    public static string? Validate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        // Láthatatlan formázó (Cf, pl. U+202E jobbról-balra) és vezérlőkarakter nem kerülhet
        // fájlnévbe: az Intézőben megfordíthatná a látszó kiterjesztést („…fdp.exe”).
        string cleaned = new string(candidate
            .Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            .ToArray()).Trim();
        foreach (char c in InvalidChars) cleaned = cleaned.Replace(c.ToString(), "");
        cleaned = cleaned.Trim(' ', '.', '-');

        var duplicate = DuplicatedTitle.Match(cleaned);
        if (duplicate.Success) cleaned = duplicate.Groups[1].Value.TrimEnd(' ', '-', '–', '—');

        if (cleaned.Length < MinLength) return null;
        if (cleaned.Length > MaxLength) return null; // túl hosszút inkább elutasítunk, mint csonkolunk
        if (Denylist.Contains(cleaned)) return null;
        if (BrokenFontRun.IsMatch(cleaned)) return null;
        if (EmailAddress.IsMatch(cleaned)) return null;
        if (PhoneNumber.IsMatch(cleaned)) return null;

        // Szóköz nélküli cím: itt szigorúbbak vagyunk, mert a hiányzó szóköz vagy a hibás
        // kinyerés jele, vagy azé, hogy a szöveg egyáltalán nem cím volt.
        if (!cleaned.Any(char.IsWhiteSpace))
        {
            if (cleaned.Length > MaxSingleWordLength) return null;
            int symbols = cleaned.Count(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_' and not '.');
            if (symbols > MaxSymbolsInSingleWord) return null;
        }

        foreach (string marker in BoilerplateMarkers)
            if (cleaned.Contains(marker, StringComparison.OrdinalIgnoreCase)) return null;

        // Ismétlődő-glyph kapu: hibás PDF-font-leképezésnél egyetlen karakter uralja a szöveget
        // (pl. "Burnished" után csupa Ű). Ezt a szó-kapu NEM fogná meg, mert az első szó valódi.
        int letters = cleaned.Count(char.IsLetter);
        if (letters > 0)
        {
            int maxSameLetter = cleaned.Where(char.IsLetter)
                .GroupBy(char.ToLowerInvariant)
                .Max(g => g.Count());
            if (letters >= 6 && maxSameLetter > letters / 2) return null;
        }

        // A LEGFONTOSABB kapu: a cím valódi szavakból álljon. Enélkül a rossz PDF-kinyerésből
        // jövő betűkupac ("BEaami[wu", "33Fåg") és a méret-feliratok ("2,6 X 30n") fájlnévvé
        // válnának — miközben az eredeti fájlnév sokszor bőven jobb volt náluk. Ha itt elbukik,
        // a hívó tovább próbál (LLM), vagy Failed lesz és marad az eredeti név.
        if (!MeaningfulName.IsDescriptiveTitle(cleaned)) return null;

        return cleaned;
    }
}
