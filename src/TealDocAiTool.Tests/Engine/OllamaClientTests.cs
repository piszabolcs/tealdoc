using System.Net;
using System.Text;
using System.Text.Json;
using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

file sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public FakeHttpMessageHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
    }
}

public class OllamaClientTests
{
    private static LlmRequest TitleRequest(string text = "valamilyen dokumentum szöveg", string folder = "Görgőző gép") =>
        new(text, folder, LlmPromptKind.Title);

    [Fact]
    public async Task GenerateAsync_ReturnsResponseFieldFromJson()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"response":"Phoenix Contact relé kézikönyv","done":true}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        var result = await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.Equal(LlmOutcome.Title, result.Outcome);
        Assert.Equal("Phoenix Contact relé kézikönyv", result.Title);
    }

    [Fact]
    public async Task GenerateAsync_SendsThinkFalseInPayload()
    {
        // Élőben mérve: think:false nélkül a qwen3 ~31s alatt "gondolkodik" egy triviális
        // cím-kérésen is (979 token) — think:false-szal ~1.5s. Ezt a payloadot SOSE hagyd ki.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"response":"Cím","done":true}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        using var payload = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(payload.RootElement.GetProperty("think").GetBoolean());
        Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("qwen3:8b", payload.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("NINCS_CÍM")]
    [InlineData("NINCS_CIM")]
    [InlineData("nincs_cím.")]
    public async Task GenerateAsync_ReturnsNoTitleWhenModelReportsNoTitle(string modelResponse)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new { response = modelResponse, done = true }));
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        var result = await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.Equal(LlmOutcome.NoTitle, result.Outcome);
        Assert.Null(result.Title);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsServiceErrorWithServerMessageOnHttpError()
    {
        // Pontosan ez történt élesben: a szolgáltatás fut, de a runner binárisa hiányzik.
        // Az üzenetnek EL KELL jutnia a naplóba, különben "rossz cím"-nek látszik a hiba.
        const string body = """{"error":"error starting llama-server: llama-server binary not found"}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, body);
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        var result = await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.Equal(LlmOutcome.ServiceError, result.Outcome);
        Assert.Contains("llama-server binary not found", result.Error);
    }

    // C-2 gyökere: egy proxy HTML-hibaoldala nyersen, többsorosan került a hibaüzenetbe (és a naplóba).
    [Fact]
    public async Task GenerateAsync_NonJsonErrorBody_BecomesOneShortLine()
    {
        string html = "<html>\r\n<body>\r\n<h1>502 Bad Gateway</h1>\r\n" + new string('x', 500) + "\r\n</body></html>";
        var client = new OllamaClient(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.BadGateway, html)),
            "http://localhost:11434", "qwen3:8b");

        var result = await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.Equal(LlmOutcome.ServiceError, result.Outcome);
        Assert.DoesNotContain('\n', result.Error!);
        Assert.True(result.Error!.Length <= 201);
    }

    // C-8: az időkorlát hívásonként állítható (a megosztott HttpClient-é végtelen).
    [Fact]
    public async Task GenerateAsync_PerRequestTimeout_IsReportedAsTimeout()
    {
        var client = new OllamaClient(new HttpClient(new HangingHandler()) { Timeout = Timeout.InfiniteTimeSpan },
            "http://localhost:11434", "qwen3:8b", requestTimeout: TimeSpan.FromMilliseconds(150));

        var result = await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.Equal(LlmOutcome.Timeout, result.Outcome);
    }

    // S-6: a dokumentumok szövege nem mehet észrevétlenül távoli gépre.
    [Theory]
    [InlineData("http://localhost:11434", null, false)]
    [InlineData("http://127.0.0.1:11434", null, false)]
    [InlineData("http://192.168.1.20:11434", null, true)]
    [InlineData("localhost:11434", "nem érvényes", false)]
    [InlineData("ftp://localhost", "nem érvényes", false)]
    public void ValidateHost_FlagsInvalidAndRemoteAddresses(string host, string? error, bool remote)
    {
        string? result = OllamaClient.ValidateHost(host, out bool isRemote);

        if (error is null) Assert.Null(result); else Assert.Contains(error, result);
        Assert.Equal(remote, isRemote);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsServiceErrorWhenServerUnreachable()
    {
        var client = new OllamaClient(new HttpClient(new ThrowingHandler()), "http://localhost:11434", "qwen3:8b");

        var result = await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.Equal(LlmOutcome.ServiceError, result.Outcome);
        Assert.Contains("kapcsolat megtagadva", result.Error);
    }

    [Fact]
    public async Task GenerateAsync_TitlePromptOffersNoTitleEscapeAndIncludesFolder()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"response":"Cím","done":true}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        await client.GenerateAsync(TitleRequest(folder: "Demo HD10 teszter"), CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.LastRequestBody!);
        string prompt = payload.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("NINCS_CÍM", prompt);
        Assert.Contains("Demo HD10 teszter", prompt); // a mappa neve a gépet azonosítja
    }

    [Theory]
    // A prompt SAJÁT példái szennyezték a válaszokat: egy teljes éles futásban az LLM-címek
    // 20,1%-a (484 fájl, 91 mappában) tartalmazta a "Klemmstück" szót, miközben a heurisztika
    // — ami nem lát promptot — egyszer sem mondta ki 1800 címből. A modell másolta a mintát,
    // ha nem tudta kiolvasni a valódi megnevezést. Konkrét alkatrésznév nem való a promptba.
    [InlineData("Klemmstück")]
    [InlineData("Führungsblech")]
    [InlineData("Aufnahme Magnete")]
    public async Task GenerateAsync_TitlePromptGivesNoPartNameExamples(string partName)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"response":"Cím","done":true}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.LastRequestBody!);
        string prompt = payload.RootElement.GetProperty("prompt").GetString()!;
        Assert.DoesNotContain(partName, prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_TitlePromptForbidsPostalAddressesAndPeople()
    {
        // A magyar "cím" szó címet ÉS lakcímet is jelent. A prompt magyarul címet kért, a modell
        // pedig a dokumentum "Cím:" mezőjét adta vissza: két fájl kapta a "Kiskérs Dyzsa Gyrgy"
        // nevet egy éles próbán. Személynév és lakcím nem kerülhet fájlnévbe.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"response":"Cím","done":true}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.LastRequestBody!);
        string prompt = payload.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("postai cím", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("személynev", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_TitlePromptStillNamesTheDrawingHeaderField()
    {
        // A példák elhagyása nem veheti el a lényeget: a modellnek tudnia kell, MELYIK mezőt
        // olvassa a rajzfejből — ez adta a legnagyobb minőségi ugrást a rajzokon.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"response":"Cím","done":true}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.LastRequestBody!);
        string prompt = payload.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Benennung", prompt);
        Assert.Contains("szó szerint", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_ClassifyPromptListsDocumentTypes()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"response":"Alkatrészrajz","done":true}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        var result = await client.GenerateAsync(new LlmRequest("112233 Benennung Klemmstück", "Stoesselpresse", LlmPromptKind.Classify), CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.LastRequestBody!);
        string prompt = payload.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Alkatrészrajz", prompt);
        Assert.Contains("Villamos kapcsolási rajz", prompt);
        Assert.Equal("Alkatrészrajz", result.Title);
    }

    [Fact]
    public async Task GenerateAsync_HttpTimeout_IsReportedNotThrown()
    {
        // ÉLES HIBA: egy teljes éles futás 7400/7427-nél összeomlott, mert egy hívás túllépte a
        // HttpClient időkorlátját. A timeout TaskCanceledException-t dob — ugyanazt a típust, mint
        // a felhasználói megszakítás —, így az "ex is not OperationCanceledException" szűrő
        // átengedte, és a kivétel felment a Main-ig. A GUI-ban ez az egész alkalmazást megölné.
        var client = new OllamaClient(
            new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(150) },
            "http://localhost:11434", "qwen3:8b");

        var result = await client.GenerateAsync(TitleRequest(), CancellationToken.None);

        Assert.Equal(LlmOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task GenerateAsync_UserCancellation_StillPropagates()
    {
        // A Megszakítás gombnak MEG KELL állítania a futást — nem szabad "időtúllépésként"
        // elnyelni, különben a program a megszakítás után is tovább dolgozna.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = new OllamaClient(new HttpClient(new HangingHandler()), "http://localhost:11434", "qwen3:8b");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GenerateAsync(TitleRequest(), cts.Token));
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrueOn200()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models":[]}""");
        var client = new OllamaClient(new HttpClient(handler), "http://localhost:11434", "qwen3:8b");

        Assert.True(await client.IsAvailableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseWhenUnreachable()
    {
        var client = new OllamaClient(new HttpClient(new ThrowingHandler()), "http://localhost:11434", "qwen3:8b");
        Assert.False(await client.IsAvailableAsync(CancellationToken.None));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("kapcsolat megtagadva");
    }

    /// <summary>Sosem válaszol — a VALÓDI HttpClient-időkorlát lép működésbe, nem egy általam
    /// kitalált kivétel. Így a teszt azt méri, ami élesben történt.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("ide sosem jutunk el");
        }
    }
}
