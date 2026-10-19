namespace TealDocAiTool.Models;

/// <summary>
/// Egy gépmappa nevéből kibontott azonosítók. A „1401 MINTAGYARTO VARIO 20" mappából
/// Number = "1401", Name = "MINTAGYARTO VARIO 20". Ha nincs vezető szám, a Number üres,
/// a Name pedig a teljes mappanév.
/// </summary>
public sealed record MachineIdentity(string Number, string Name, string RawFolderName);

/// <summary>Egy dokumentum a gépmappában: a (lehetőleg beszédes) fájlnév és egy opcionális,
/// LLM-mel generált egymondatos leírás.</summary>
public sealed record MachineDocument(string FileName, string RelativePath, string? Description);

/// <summary>Gyártósor vagy önálló gép? Egy vegyes gyökér alatt mindkettő van, és egy sorra
/// „gép"-ként hivatkozni félrevezeti a keresőt. `Unknown`, ha a jelölés nem dönti el — ilyenkor
/// semleges megfogalmazást használunk, nem tippelünk.</summary>
public enum MachineEntryKind
{
    Unknown,
    Machine,
    ProductionLine,
}

/// <summary>
/// Egyetlen sor: egy gép vagy gyártósor minden kigyűjtött adata. Ez lesz a CSV egy sora VAGY az
/// adott egység .md fájlja.
/// </summary>
public sealed record MachineIndexRow(
    string Title,
    string Number,
    string Name,
    string MachineType,
    string AltNames,
    string FolderPath,
    int DocumentCount,
    string Summary,
    string DocumentList,
    MachineEntryKind Kind = MachineEntryKind.Unknown,

    /// <summary>
    /// A mappa webes címe (pl. egy SharePoint-dokumentumtárban), ha a felhasználó megadta a gyökér
    /// hivatkozás-alap URL-jét. Ez kerül a kimenetbe; nélküle csak a gyökérhez képesti mappanév.
    /// </summary>
    string SharePointUrl = "",

    /// <summary>
    /// A dokumentumok NYERS listája (a <see cref="DocumentList"/> ebből renderelt szöveg). A
    /// gyorsítótárba is bekerül, hogy egy folytatott futás a felsorolást újra fel tudja építeni —
    /// pl. ha közben megadtad a hivatkozás-alap URL-t — anélkül, hogy az LLM-leírásokat órák alatt újra
    /// legenerálnánk. Egy RÉGI (e mező előtti) gyorsítótárban null: ilyenkor a mappa-hivatkozás
    /// még pótolható, a fájlonkénti már nem.
    /// </summary>
    IReadOnlyList<MachineDocument>? Documents = null)
{
    /// <summary>
    /// Igaz, ha a sor valamelyik LLM-hívása időtúllépés miatt kimaradt. Az ilyen sor kiírható, de
    /// NEM kerül a gyorsítótárba — különben egy későbbi, működő Ollamás futás sosem pótolná.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Incomplete { get; init; }
}

/// <summary>Egy gép feldolgozásának állapotjelentése a UI felé.</summary>
/// <param name="IsFileLevel">
/// Igaz, ha ez EGY FÁJLRÓL szóló jelzés, nem egy kész gépről. A hívó ezeket nem naplózza, csak az
/// állapotsorba írja: egy teljes, valós archívumon 23 000 ilyen jelzés érkezne, és annyi sor a
/// memóriában tartott napló-dobozban fölösleges teher.
///
/// Miért külön mező: a darabszámokból NEM lehet rá következtetni. A koordinátor a fájl-szintű
/// jelzést újracsomagolja, és beleírja az aktuális gép-számlálókat — így a `TotalMachines` a
/// fájl-jelzéseknél is nullánál nagyobb lesz.
/// </param>
public sealed record IndexProgress(
    int ProcessedMachines,
    int TotalMachines,
    string CurrentMachine,
    string Message,
    int FilesInCurrentMachine,
    int ProcessedFilesTotal,
    bool IsFileLevel = false);

/// <summary>Milyen kimenetet gyártson az Index mód.</summary>
public enum IndexOutputMode
{
    /// <summary>Egyetlen CSV-táblázat, egységenként egy sorral (táblázatkezelőhöz, adatbázis-importhoz).</summary>
    Csv,

    /// <summary>Egységenként egy kis Markdown-fájl egyetlen kimeneti mappába.</summary>
    MarkdownFiles,

    /// <summary>
    /// Egységenként egy .docx („kártya”) egyetlen mappába. Ez az alapértelmezés: sok dokumentum-
    /// kereső (pl. vállalati tudásbázisok) a Word-fájlt olvassa, a Markdownt nem.
    /// </summary>
    WordFiles,
}

/// <summary>Az Index mód futásának beállításai.</summary>
public sealed record IndexSettings(
    string RootFolder,
    IndexOutputMode OutputMode,
    string OutputCsvPath,
    string OutputFolder,
    string CacheFolder,
    string OllamaHost,
    string OllamaModel,
    int Workers,
    bool GenerateMachineType,
    bool GenerateFileSummaries,
    int MaxFilesPerMachineForSummary,

    /// <summary>
    /// Mi a KÖZÖS jellege a gyökér alatti gépeknek — a felhasználó írja be, nem találgatjuk.
    ///
    /// Miért nem a szülőmappa nevéből: egy valós archívum gyökereiből csak némelyiknél jelöl a név
    /// géptípust (pl. `CNC Esztergak`). Más gyökerek sorok és berendezések vegyes gyűjteményei —
    /// ezekre a mappanév támpontnak HASZNÁLHATATLAN. Amikor mégis odaadtuk, a modell egy ilyen
    /// gyűjtőnévből „CNC precssgép"-et hallucinált.
    ///
    /// Üresen hagyva nincs támpont: a géptípus kizárólag a dokumentumokból jön.
    /// </summary>
    string CategoryHint = "",

    /// <summary>
    /// Opcionális hivatkozás-alap URL: annak a webes mappának (pl. SharePoint-dokumentumtár) a címe,
    /// amely a <see cref="RootFolder"/>-nek FELEL MEG. Ehhez fűzzük a gépek és a dokumentumok relatív
    /// útját. Üresen hagyva a kimenetbe csak a gyökérhez képesti mappanév kerül.
    /// </summary>
    string SharePointBaseUrl = "",

    /// <summary>
    /// Kapjon-e MINDEN dokumentum saját hivatkozást a felsorolásban.
    ///
    /// Bekapcsolva egy kereső/asszisztens konkrét fájlra tud mutatni. Az ára: egy 42 dokumentumos gépnél az
    /// URL-ek több karaktert tesznek ki, mint a hasznos tartalom, és ez a szemantikus kereséshez
    /// használt beágyazást hígítja. Ha a keresés minősége romlana, ezt kell először kikapcsolni —
    /// a mappa-hivatkozás akkor is megmarad.
    /// </summary>
    bool LinkEachDocument = true);
