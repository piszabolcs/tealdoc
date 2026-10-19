using System.IO;
using TealDocAiTool.Extractors;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

public sealed class TitleEngine
{
    /// <summary>Az első LLM-kör ennyi karaktert kap. Ha ebből nem derül ki a cím, a második
    /// kör a teljes kinyert szöveget megkapja — a kézikönyveknél a lényeg gyakran a 3-4. oldalon van.</summary>
    private const int FirstRoundChars = 4000;

    private readonly ExtractorRegistry _registry;

    // Ha az Ollama egyszer szolgáltatás-hibát adott (vagy többször egymás után túllépte az
    // időkorlátot), a többi fájlnál meg sem próbáljuk: a hiba a szolgáltatásban van, nem a
    // dokumentumban, és 7000 fájlon végigpörgetni értelmetlen. Ezt a közös hívó tartja számon.
    private readonly LlmCaller _llm;

    /// <param name="llmCallback">A tényleges Ollama-hívás (produkcióban <c>OllamaClient.GenerateAsync</c>
    /// becsomagolva) — így a teszt nem hív valódi HTTP-t.</param>
    /// <param name="llmGate">Az egyidejű LLM-hívások száma korlátozva — CSAK a callback köré fon,
    /// hogy a tisztán heurisztikával megoldott fájlok sose várakozzanak feleslegesen.</param>
    public TitleEngine(ExtractorRegistry registry, Func<LlmRequest, Task<LlmResult>> llmCallback, SemaphoreSlim llmGate)
    {
        _registry = registry;
        _llm = new LlmCaller(llmCallback, llmGate);
    }

    public async Task<TitleDecision> ResolveTitleAsync(string filePath, CancellationToken ct)
    {
        var extractor = _registry.FindFor(filePath);
        if (extractor is null) return TitleDecision.Unsupported();

        // BEMENETI KAPU — a legolcsóbb és legfontosabb lépés. Ha a fájlnév már hordoz értelmet,
        // hozzá sem nyúlunk: se kinyerés, se LLM-hívás. Egy valódi, éles futás szerint ez az összes
        // fájl 40%-a, és pont ezeknél volt a javasolt cím ROSSZABB az eredetinél (szkennelt PDF-ek
        // OCR-zaja). Mellékhatásként a köteg sokkal gyorsabb, és a Word COM-ot is ritkábban indítjuk.
        // A támogatottság-vizsgálat UTÁN áll, hogy a .msg/.pptx továbbra is Skipped legyen, ne Kept.
        if (MeaningfulName.ContainsRealWord(Path.GetFileNameWithoutExtension(filePath)))
            return TitleDecision.Kept();

        ExtractedDocument? extracted;
        try
        {
            extracted = extractor.Extract(filePath);
        }
        catch (Exception ex)
        {
            // A könyvtárak (PdfPig, NPOI) angol kivételszövegét magyar előtag vezeti be a naplóban.
            return TitleDecision.Failed("Kinyerési hiba: " + ex.Message);
        }
        if (extracted is null) return TitleDecision.Failed("Nincs kinyerhető szöveg.");

        foreach (var candidate in extracted.HeuristicCandidates)
        {
            var valid = TitleValidator.ValidateHeuristic(candidate);
            if (valid is not null) return TitleDecision.Success(valid, RenameMethod.Heuristic);
        }

        if (string.IsNullOrWhiteSpace(extracted.FullText))
            return TitleDecision.Failed("Nincs kinyerhető szöveg az LLM-hez sem.");

        if (_llm.ServiceError is { } down) return TitleDecision.Failed(ServiceErrorReason(down));

        return await ResolveWithLlmAsync(filePath, extracted.FullText, ct);
    }

    /// <summary>Kaszkád: rövid szöveg → teljes szöveg → típus-osztályozás. Minden lépcső akkor
    /// indul, ha az előző nem adott ÉRVÉNYES címet — így egy sikertelen próbálkozás után nem
    /// marad ott az eredeti név anélkül, hogy tényleg mindent megpróbáltunk volna.</summary>
    private async Task<TitleDecision> ResolveWithLlmAsync(string filePath, string fullText, CancellationToken ct)
    {
        string folderName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "";
        string firstRoundText = fullText.Length > FirstRoundChars ? fullText[..FirstRoundChars] : fullText;

        var rounds = new List<LlmRequest> { new(firstRoundText, folderName, LlmPromptKind.Title) };
        if (fullText.Length > firstRoundText.Length)
            rounds.Add(new LlmRequest(fullText, folderName, LlmPromptKind.Title));
        rounds.Add(new LlmRequest(fullText, folderName, LlmPromptKind.Classify));

        foreach (var round in rounds)
        {
            var result = await _llm.CallAsync(round, ct);

            if (result.Outcome == LlmOutcome.ServiceError)
                return TitleDecision.Failed(ServiceErrorReason(result.Error ?? "ismeretlen hiba"));

            // Időtúllépésnél ennél a fájlnál abbahagyjuk, de a szolgáltatást NEM temetjük el: a
            // következő kör még több szöveget küldene ugyanannak a modellnek, tehát csak újabb
            // perceket várnánk — a többi fájl viszont továbbra is kap esélyt.
            if (result.Outcome == LlmOutcome.Timeout)
                return TitleDecision.Failed("Az LLM időtúllépés miatt nem válaszolt erre a fájlra.");

            if (result.Outcome == LlmOutcome.NoTitle) continue; // a modell nem tudta — jöhet a következő kör

            var valid = TitleValidator.Validate(TitleValidator.NormalizeLlmAnswer(result.Title));
            if (valid is not null) return TitleDecision.Success(valid, RenameMethod.Llm);
        }

        return TitleDecision.Failed("Az LLM sem talált értelmes címet (több körben sem).");
    }

    private static string ServiceErrorReason(string error) =>
        $"Az Ollama nem válaszol, ez NEM a dokumentum hibája: {error}";
}
