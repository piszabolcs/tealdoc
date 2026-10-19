using System.IO;
using System.Text;
using TealDocAiTool.Extractors;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Egyetlen gépmappát dolgoz fel: kibontja a gépszámot/nevet, összeszedi a dokumentumokat, LLM-mel
/// megállapítja a géptípust, és (ha kérted) fájlonként egy rövid leírást is generál.
/// </summary>
public sealed class MachineIndexEngine
{
    private const int TypeSampleFiles = 4;
    private const int TypeSampleCharsPerFile = 800;
    private const int FileSummaryChars = 3000;

    private readonly ExtractorRegistry _registry;
    private readonly LlmCaller _llm;

    /// <summary>Egy gép LLM-hívásainak állapota: volt-e köztük időtúllépés (akkor a sor hiányos).</summary>
    private sealed class RowLlmState
    {
        public bool Incomplete;
    }

    public MachineIndexEngine(ExtractorRegistry registry, Func<LlmRequest, Task<LlmResult>> llm, SemaphoreSlim llmGate)
    {
        _registry = registry;
        _llm = new LlmCaller(llm, llmGate);
    }

    /// <exception cref="LlmUnavailableException">Az LLM-szolgáltatás nem érhető el (vagy a megszakító
    /// leállította). A félkész sort ilyenkor nem adjuk vissza, hogy ne kerülhessen a gyorsítótárba.</exception>
    public async Task<MachineIndexRow> BuildRowAsync(
        string machineFolder, IndexSettings settings, IProgress<IndexProgress>? fileProgress, CancellationToken ct)
    {
        var llmState = new RowLlmState();
        string trimmed = machineFolder.TrimEnd(Path.DirectorySeparatorChar);
        var identity = FolderNameParser.Parse(Path.GetFileName(trimmed));

        // A géptípus-támpont a felhasználótól jön, NEM a szülőmappa nevéből. Egy valós archívum
        // gyökereiből csak némelyik nevez meg géptípust (pl. `CNC Esztergak`); a többi sorok és
        // berendezések vegyes gyűjteménye. A mappanév automatikus átadása ezért hallucinációt
        // okozott (egy sorgyűjtemény nevéből „CNC precssgép" lett).
        // Üres támpont = a géptípus kizárólag a dokumentumokból jöhet.
        string category = settings.CategoryHint;

        // MINDEN dokumentum bekerül a listába — a .ppt/.pptx/.msg is, amiből szöveget nem tudunk
        // olvasni. Ha kihagynánk, egy kereső nem tudná, hogy léteznek.
        // Egy elérhetetlen almappa (jogosultság) nem viheti el az egész gépet: kimarad, a többi megy.
        var files = Directory.EnumerateFiles(machineFolder, "*", RecursiveTolerant)
            .Where(IndexableDocument.IsIndexable)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var kind = EntryKindClassifier.Classify(identity.RawFolderName);

        string machineType = "";
        if (settings.GenerateMachineType)
            machineType = await ResolveMachineTypeAsync(identity, category, kind, files, llmState, ct);

        var summaryTargets = SelectFilesToSummarise(files, identity, settings);
        var docs = new List<MachineDocument>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(machineFolder, file);
            string? desc = null;
            // Leírást csak ott próbálunk, ahol tudunk szöveget olvasni. A többi fájl ettől még
            // szerepel a listában — csak leírás nélkül.
            if (summaryTargets.Contains(file))
                desc = await SummariseFileAsync(file, identity, llmState, ct);
            docs.Add(new MachineDocument(Path.GetFileName(file), rel, desc));
            fileProgress?.Report(new IndexProgress(0, 0, identity.RawFolderName,
                Path.GetFileName(file), files.Count, 0, IsFileLevel: true));
        }

        // A gép webcíme: a hivatkozás-alaphoz fűzzük a gyökérhez képesti relatív utat. Nem a
        // mappanevet használjuk, mert a gépmappa nem feltétlenül közvetlen gyereke a gyökérnek.
        string machineUrl = SharePointUrlBuilder.ForMachineFolder(
            settings.SharePointBaseUrl, settings.RootFolder, machineFolder);

        var row = Assemble(identity, machineType, machineFolder, docs, kind,
            machineUrl, settings.LinkEachDocument);
        return llmState.Incomplete ? row with { Incomplete = true } : row;
    }

    internal static readonly EnumerationOptions RecursiveTolerant = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0, // a SearchOption-ös túlterheléssel azonos: rejtett fájl is számít
    };

    /// <summary>
    /// Mely fájlok kapjanak LLM-leírást, ha a felhasználó korlátot adott meg.
    ///
    /// A korlát NEM az ábécé-sorrend elejét jelenti: a valódi `1401 MINTAGYARTO VARIO 20`
    /// mappában az első fájlok telepítési ellenőrzőlisták és szállítólevelek, a kézikönyv pedig
    /// egy alkönyvtár mélyén fekszik. Ugyanaz a rangsor dönt, ami a géptípus mintavételénél
    /// bevált: a gép saját kezelési/karbantartási dokumentumai előre, a beszállítói komponensek
    /// és az adminisztratív papírok hátra.
    ///
    /// Korlát nélkül minden olvasható fájl bekerül — ilyenkor a rangsor nem számít.
    /// </summary>
    private HashSet<string> SelectFilesToSummarise(
        List<string> files, MachineIdentity identity, IndexSettings settings)
    {
        if (!settings.GenerateFileSummaries) return new HashSet<string>();

        var readable = files.Where(f => _registry.FindFor(f) is not null);
        if (settings.MaxFilesPerMachineForSummary > 0)
        {
            var nameTokens = ManualLikeness.NameTokens(identity.Name);
            readable = readable
                .OrderByDescending(f => ManualLikeness.Score(f, nameTokens))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxFilesPerMachineForSummary);
        }
        return new HashSet<string>(readable, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveMachineTypeAsync(
        MachineIdentity identity, string category, MachineEntryKind kind, List<string> files,
        RowLlmState llmState, CancellationToken ct)
    {
        // Nem az ábécé-sorrend első fájljait vesszük, hanem a GÉP SAJÁT kézikönyv-gyanús
        // dokumentumait. A gépnév azért kell a pontozáshoz, mert a mappák tele vannak beszállítói
        // komponens-kézikönyvekkel, amelyek NÉV szerint kézikönyvebbek a gép saját doksijánál —
        // ebből lett minden esztergából „Fúrógép" (a FANUC vezérlő kézikönyve nyert).
        // Azonos pontszámnál marad az ábécé-sorrend, hogy a futás megismételhető legyen.
        var nameTokens = ManualLikeness.NameTokens(identity.Name);
        var sampleFiles = files
            .Where(f => _registry.FindFor(f) is not null)
            .OrderByDescending(f => ManualLikeness.Score(f, nameTokens))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(TypeSampleFiles)
            .ToList();

        var sample = new StringBuilder();
        foreach (var file in sampleFiles)
        {
            sample.AppendLine("Fájl: " + Path.GetFileName(file));
            try
            {
                var extracted = _registry.FindFor(file)?.Extract(file);
                if (extracted is { FullText.Length: > 0 })
                {
                    string t = extracted.FullText;
                    sample.AppendLine(t.Length > TypeSampleCharsPerFile ? t[..TypeSampleCharsPerFile] : t);
                }
            }
            catch { }
            sample.AppendLine();
        }

        // Egy gyártósornál nem azt kérdezzük, MILYEN GÉP, hanem hogy MIT szerelnek/gyártanak rajta.
        var promptKind = kind == MachineEntryKind.ProductionLine
            ? LlmPromptKind.ProductionLineType
            : LlmPromptKind.MachineType;

        var result = await CallLlmAsync(
            new LlmRequest(sample.ToString(), identity.RawFolderName, promptKind, category), llmState, ct);
        if (result.Outcome != LlmOutcome.Title || string.IsNullOrWhiteSpace(result.Title)) return "";
        // Elbukott validációnál üres géptípus — a nyers, validálatlan válasz nem kerülhet a kimenetbe.
        return TitleValidator.Validate(TitleValidator.NormalizeLlmAnswer(result.Title)) ?? "";
    }

    private async Task<string?> SummariseFileAsync(string file, MachineIdentity identity, RowLlmState llmState, CancellationToken ct)
    {
        string text;
        try
        {
            var extracted = _registry.FindFor(file)?.Extract(file);
            if (extracted is null || string.IsNullOrWhiteSpace(extracted.FullText)) return null;
            text = extracted.FullText.Length > FileSummaryChars ? extracted.FullText[..FileSummaryChars] : extracted.FullText;
        }
        catch { return null; }

        var result = await CallLlmAsync(new LlmRequest(text, identity.RawFolderName, LlmPromptKind.FileSummary), llmState, ct);
        if (result.Outcome != LlmOutcome.Title || string.IsNullOrWhiteSpace(result.Title)) return null;
        string cleaned = SummaryText.Clean(result.Title);
        return cleaned.Length > 0 ? cleaned : null;
    }

    /// <summary>Szolgáltatás-hibánál (vagy ha a megszakító leállított) a gép feldolgozása megáll;
    /// időtúllépésnél a sor hiányosnak számít, és nem kerül a gyorsítótárba.</summary>
    private async Task<LlmResult> CallLlmAsync(LlmRequest request, RowLlmState llmState, CancellationToken ct)
    {
        var result = await _llm.CallAsync(request, ct);
        if (result.Outcome == LlmOutcome.ServiceError)
            throw new LlmUnavailableException(result.Error ?? "ismeretlen hiba");
        if (result.Outcome == LlmOutcome.Timeout) llmState.Incomplete = true;
        return result;
    }

    private static MachineIndexRow Assemble(
        MachineIdentity identity, string machineType, string folder, List<MachineDocument> docs,
        MachineEntryKind kind, string machineUrl, bool linkEachDocument)
    {
        string title = identity.RawFolderName;
        string altNames = DeriveSynonyms(identity, machineType);

        // A megfogalmazás a JELLEGHEZ igazodik. Egy gyártósorra „gép"-ként hivatkozni félrevezeti
        // a keresőt: a „milyen gép a T05?" és a „mit szerelnek a T05-ön?" nem ugyanaz a kérdés.
        var summary = new StringBuilder();
        switch (kind)
        {
            case MachineEntryKind.ProductionLine when identity.Number.Length > 0:
                summary.Append($"A {identity.Number} jelű gyártósor: {identity.Name}");
                if (machineType.Length > 0) summary.Append($". Ez egy {machineType}");
                summary.Append(". Az ehhez a gyártósorhoz tartozó teljes elérhető dokumentáció ebben a mappában található.");
                break;

            case MachineEntryKind.ProductionLine:
                summary.Append($"A(z) {identity.Name} gyártósor");
                if (machineType.Length > 0) summary.Append($": {machineType}");
                summary.Append(". Az ehhez a gyártósorhoz tartozó teljes elérhető dokumentáció ebben a mappában található.");
                break;

            case MachineEntryKind.Machine:
                summary.Append($"Az {identity.Number}-es számú gép egy {identity.Name}");
                if (machineType.Length > 0) summary.Append($" típusú {machineType}");
                summary.Append(". Az ehhez a géphez tartozó teljes elérhető dokumentáció ebben a mappában található.");
                break;

            // Ismeretlen jelleg: nem állítjuk sem gépnek, sem sornak.
            default:
                summary.Append($"A(z) {identity.Name}");
                if (machineType.Length > 0) summary.Append($": {machineType}");
                summary.Append(". Az ehhez tartozó teljes elérhető dokumentáció ebben a mappában található.");
                break;
        }

        return new MachineIndexRow(
            Title: title,
            Number: identity.Number,
            Name: identity.Name,
            MachineType: machineType,
            AltNames: altNames,
            FolderPath: folder,
            DocumentCount: docs.Count,
            Summary: summary.ToString(),
            DocumentList: DocumentListRenderer.Render(docs, machineUrl, linkEachDocument),
            Kind: kind,
            SharePointUrl: machineUrl,
            // A nyers dokumentumlista a gyorsítótárba is bekerül, hogy egy folytatott futás a
            // felsorolást ÚJRA fel tudja építeni (pl. megváltozott hivatkozás-alap esetén) anélkül,
            // hogy a fájlokat és az LLM-leírásokat újra elő kellene állítani.
            Documents: docs);
    }

    private static string DeriveSynonyms(MachineIdentity identity, string machineType)
    {
        var parts = new List<string>();
        string[] tokens = identity.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0) parts.Add(tokens[0]);
        if (machineType.Length > 0) parts.Add(machineType);
        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
