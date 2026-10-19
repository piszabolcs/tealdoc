using TealDocAiTool.Engine;
using TealDocAiTool.Extractors;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

file sealed class FakeExtractor : IDocumentExtractor
{
    private readonly ExtractedDocument? _result;
    public FakeExtractor(ExtractedDocument? result) => _result = result;
    public bool CanHandle(string filePath) => true;
    public ExtractedDocument? Extract(string filePath) => _result;
}

file sealed class ThrowingExtractor : IDocumentExtractor
{
    public bool CanHandle(string filePath) => true;
    public ExtractedDocument? Extract(string filePath) => throw new IOException("fájl zárolva");
}

public class TitleEngineTests
{
    private static TitleEngine Engine(IDocumentExtractor extractor, Func<LlmRequest, Task<LlmResult>> llm) =>
        new(new ExtractorRegistry(new[] { extractor }), llm, new SemaphoreSlim(1));

    private static Task<LlmResult> NeverCalled(LlmRequest _) => throw new Exception("nem szabadna LLM-et hívni");

    [Fact]
    public async Task ResolveTitleAsync_UsesHeuristicWhenValid()
    {
        var extractor = new FakeExtractor(new ExtractedDocument("teljes szöveg", new[] { "Phoenix Contact relé kézikönyv" }));

        var decision = await Engine(extractor, NeverCalled).ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.True(decision.IsResolved);
        Assert.Equal("Phoenix Contact relé kézikönyv", decision.Title);
        Assert.Equal(RenameMethod.Heuristic, decision.Method);
    }

    [Fact]
    public async Task ResolveTitleAsync_FallsBackToLlmWhenHeuristicInvalid()
    {
        var extractor = new FakeExtractor(new ExtractedDocument("teljes szöveg", new[] { "ab" }));

        var decision = await Engine(extractor, _ => Task.FromResult(LlmResult.FromTitle("LLM-generált cím")))
            .ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.True(decision.IsResolved);
        Assert.Equal("LLM-generált cím", decision.Title);
        Assert.Equal(RenameMethod.Llm, decision.Method);
    }

    [Fact]
    public async Task ResolveTitleAsync_WhenFirstRoundFindsNoTitle_RetriesWithMoreText()
    {
        // A rövid részletből nem derül ki a cím -> a következő kör TÖBB szöveget kap.
        string longText = new string('x', 4000) + " a lényeg csak itt, a szöveg mélyén derül ki";
        var extractor = new FakeExtractor(new ExtractedDocument(longText, Array.Empty<string>()));
        var received = new List<LlmRequest>();

        var decision = await Engine(extractor, request =>
        {
            received.Add(request);
            return Task.FromResult(received.Count == 1
                ? LlmResult.NoTitle()
                : LlmResult.FromTitle("Hidraulikus tömlő cseréje"));
        }).ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.Equal("Hidraulikus tömlő cseréje", decision.Title);
        Assert.Equal(2, received.Count);
        Assert.True(received[1].DocumentText.Length > received[0].DocumentText.Length,
            "a második körnek több szöveget kell kapnia");
    }

    [Fact]
    public async Task ResolveTitleAsync_WhenNoTitleFound_FallsBackToClassification()
    {
        // Műszaki rajz: cím nincs, de a TÍPUS önmagában is hasznos információ.
        var extractor = new FakeExtractor(new ExtractedDocument("112233 Benennung Maßstab 1.4301", Array.Empty<string>()));
        var kinds = new List<LlmPromptKind>();

        var decision = await Engine(extractor, request =>
        {
            kinds.Add(request.Kind);
            return Task.FromResult(request.Kind == LlmPromptKind.Classify
                ? LlmResult.FromTitle("Alkatrészrajz")
                : LlmResult.NoTitle());
        }).ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.Equal("Alkatrészrajz", decision.Title);
        Assert.Equal(LlmPromptKind.Classify, kinds[^1]);
    }

    [Fact]
    public async Task ResolveTitleAsync_PassesFolderNameToTheModel()
    {
        var extractor = new FakeExtractor(new ExtractedDocument("szöveg", Array.Empty<string>()));
        LlmRequest? captured = null;

        await Engine(extractor, request =>
        {
            captured = request;
            return Task.FromResult(LlmResult.FromTitle("Valamilyen cím"));
        }).ResolveTitleAsync(@"C:\SOROK\Demo HD10 teszter\P-00012.pdf", CancellationToken.None);

        Assert.Equal("Demo HD10 teszter", captured!.FolderName);
    }

    [Fact]
    public async Task ResolveTitleAsync_ServiceError_IsReportedAsServiceProblem()
    {
        // Ez NEM a dokumentumról szól — a naplónak ezt egyértelműen meg kell mondania.
        var extractor = new FakeExtractor(new ExtractedDocument("szöveg", Array.Empty<string>()));

        var decision = await Engine(extractor, _ => Task.FromResult(LlmResult.ServiceError("llama-server binary not found")))
            .ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.False(decision.IsResolved);
        Assert.Equal(RenameMethod.Failed, decision.Method);
        Assert.Contains("Ollama", decision.FailureReason);
        Assert.Contains("llama-server binary not found", decision.FailureReason);
    }

    [Fact]
    public async Task ResolveTitleAsync_AfterServiceError_StopsCallingTheModel()
    {
        // Ha a szolgáltatás halott, ne pörgessünk végig rajta még 7000 fájlt.
        var extractor = new FakeExtractor(new ExtractedDocument("szöveg", Array.Empty<string>()));
        int calls = 0;
        var engine = Engine(extractor, _ =>
        {
            calls++;
            return Task.FromResult(LlmResult.ServiceError("nem indul a modell"));
        });

        await engine.ResolveTitleAsync("P-00012.pdf", CancellationToken.None);
        var second = await engine.ResolveTitleAsync("P-00013.pdf", CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Contains("Ollama", second.FailureReason);
    }

    [Fact]
    public async Task ResolveTitleAsync_Timeout_FailsThisFileButKeepsServiceAlive()
    {
        // Az időtúllépés NEM ugyanaz, mint a halott szolgáltatás: egyetlen lassú dokumentum miatt
        // nem szabad a maradék több ezer fájlnál lemondani az LLM-ről. Ugyanennél a fájlnál viszont
        // nincs értelme tovább próbálkozni: a következő kör MÉG TÖBB szöveget küldene ugyanannak a
        // modellnek, tehát biztosan újabb 5 percet várnánk.
        string longText = new string('x', 4000) + " a lényeg csak itt derül ki";
        var extractor = new FakeExtractor(new ExtractedDocument(longText, Array.Empty<string>()));
        var requests = new List<LlmRequest>();
        var engine = Engine(extractor, request =>
        {
            requests.Add(request);
            return Task.FromResult(requests.Count == 1 ? LlmResult.Timeout() : LlmResult.FromTitle("Görgős vezeték"));
        });

        var first = await engine.ResolveTitleAsync("P-00012.pdf", CancellationToken.None);
        var second = await engine.ResolveTitleAsync("P-00013.pdf", CancellationToken.None);

        Assert.Equal(RenameMethod.Failed, first.Method);
        Assert.Contains("időtúllépés", first.FailureReason);
        Assert.Equal(2, requests.Count);              // fájlonként EGY hívás: az 1. nem kaszkádolt tovább
        Assert.Equal("Görgős vezeték", second.Title); // a 2. fájl viszont megint kapott esélyt
    }

    // C-8 / S12: egy lefagyott Ollama mellett korábban MINDEN fájl kivárta a teljes időkorlátot.
    // Három egymás utáni időtúllépés után a többi fájl azonnal, indoklással bukik.
    [Fact]
    public async Task ResolveTitleAsync_ConsecutiveTimeouts_TripTheBreakerAndLaterFilesFailFast()
    {
        var extractor = new FakeExtractor(new ExtractedDocument("valamilyen szöveg", Array.Empty<string>()));
        int calls = 0;
        var engine = Engine(extractor, _ => { calls++; return Task.FromResult(LlmResult.Timeout()); });

        var decisions = new List<TitleDecision>();
        for (int i = 0; i < 5; i++)
            decisions.Add(await engine.ResolveTitleAsync($"P-0001{i}.pdf", CancellationToken.None));

        Assert.Equal(LlmCaller.MaxConsecutiveTimeouts, calls);
        Assert.All(decisions, d => Assert.Equal(RenameMethod.Failed, d.Method));
        Assert.Contains("egymás utáni időtúllépés", decisions[^1].FailureReason);
    }

    [Fact]
    public async Task ResolveTitleAsync_ReturnsFailedWhenEveryRoundFails()
    {
        var extractor = new FakeExtractor(new ExtractedDocument("értelmezhetetlen zaj", Array.Empty<string>()));

        var decision = await Engine(extractor, _ => Task.FromResult(LlmResult.NoTitle()))
            .ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.False(decision.IsResolved);
        Assert.Equal(RenameMethod.Failed, decision.Method);
    }

    [Fact]
    public async Task ResolveTitleAsync_ReturnsUnsupportedWhenNoExtractorMatches()
    {
        var engine = new TitleEngine(new ExtractorRegistry(Array.Empty<IDocumentExtractor>()), NeverCalled, new SemaphoreSlim(1));

        var decision = await engine.ResolveTitleAsync("P-00012.rar", CancellationToken.None);

        Assert.False(decision.IsResolved);
        Assert.Equal(RenameMethod.Skipped, decision.Method);
    }

    [Fact]
    public async Task ResolveTitleAsync_DescriptiveFileName_ReturnsKeptWithoutExtracting()
    {
        // A ThrowingExtractor elszállna, ha egyáltalán sorra kerülne — a kapunak a kinyerés
        // ELŐTT kell döntenie.
        var decision = await Engine(new ThrowingExtractor(), NeverCalled)
            .ResolveTitleAsync("Kezelési útmutató.pdf", CancellationToken.None);

        Assert.False(decision.IsResolved);
        Assert.Equal(RenameMethod.Kept, decision.Method);
    }

    [Fact]
    public async Task ResolveTitleAsync_UnsupportedFileWithDescriptiveName_IsSkippedNotKept()
    {
        // A .msg/.pptx nem azért marad a nevén, mert a név jó, hanem mert nem tudjuk olvasni —
        // a naplóban ez a különbség számít.
        var engine = new TitleEngine(new ExtractorRegistry(Array.Empty<IDocumentExtractor>()), NeverCalled, new SemaphoreSlim(1));

        var decision = await engine.ResolveTitleAsync("Kaltenbach.pptx", CancellationToken.None);

        Assert.Equal(RenameMethod.Skipped, decision.Method);
    }

    [Fact]
    public async Task ResolveTitleAsync_ReturnsFailedWhenExtractorThrows()
    {
        var decision = await Engine(new ThrowingExtractor(), NeverCalled)
            .ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.False(decision.IsResolved);
        Assert.Equal(RenameMethod.Failed, decision.Method);
        Assert.Contains("fájl zárolva", decision.FailureReason);
    }

    [Fact]
    public async Task ResolveTitleAsync_ReturnsFailedWhenExtractionYieldsNull()
    {
        var decision = await Engine(new FakeExtractor(null), NeverCalled)
            .ResolveTitleAsync("P-00012.pdf", CancellationToken.None);

        Assert.False(decision.IsResolved);
        Assert.Equal(RenameMethod.Failed, decision.Method);
    }
}
