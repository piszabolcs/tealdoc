using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

public sealed class OllamaClient
{
    private const int MaxPromptChars = 12000;

    // Ékezet nélküli változat is, mert a modell nem mindig teszi ki a hosszú í-t.
    private static readonly string[] NoTitleMarkers = { "NINCS_CÍM", "NINCS_CIM" };

    /// <summary>Az alapértelmezett időkorlát egy generálásra (a felületen állítható).</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Az elérhetőség-ellenőrzés rövid: egy nem futó vagy lefagyott szolgáltatást gyorsan
    /// kell jelezni, nem a generálás időkorlátja után.</summary>
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Az egész app EGY HttpClient-et használ (hívásonként új példány a socketeket pazarolja). Az
    /// időkorlátot hívásonként a token adja, ezért itt végtelen.
    /// </summary>
    public static HttpClient SharedHttp { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly string _host;
    private readonly string _model;
    private readonly TimeSpan _requestTimeout;

    public OllamaClient(HttpClient http, string host, string model, TimeSpan? requestTimeout = null)
    {
        _http = http;
        _host = host.TrimEnd('/');
        _model = model;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    /// <summary>
    /// A felhasználó által megadott Ollama-cím ellenőrzése. Hibaüzenetet ad, ha nem http(s) cím;
    /// <paramref name="isRemote"/> igaz, ha nem a helyi gépre mutat — ilyenkor a dokumentumok
    /// szövege titkosítatlanul egy másik gépre menne, erről a felhasználónak döntenie kell.
    /// </summary>
    public static string? ValidateHost(string? host, out bool isRemote)
    {
        isRemote = false;
        if (!Uri.TryCreate(host?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "Az Ollama címe nem érvényes http(s) cím (pl. http://localhost:11434).";
        isRemote = !uri.IsLoopback;
        return null;
    }

    /// <summary>Csak azt mondja meg, hogy a szolgáltatás VÁLASZOL — azt nem, hogy tud is
    /// generálni. (Élesben előfordult, hogy a /api/tags rendben felelt, de a modell futtatásához
    /// szükséges binárisok hiányoztak a telepítésből.) A valódi ellenőrzés a <see cref="ProbeAsync"/>.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(AvailabilityTimeout);
            using var response = await _http.GetAsync($"{_host}/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Futás előtti próba: válaszol-e a szolgáltatás, és tud-e a megadott modell generálni (egy
    /// egytokenes kéréssel — így a hiányzó modell azonnal kiderül, nem több ezer fájl után).
    /// </summary>
    /// <returns><c>null</c>, ha minden rendben; különben a hiba oka magyarul.</returns>
    public async Task<string?> ProbeAsync(CancellationToken ct)
    {
        if (!await IsAvailableAsync(ct))
            return $"a szolgáltatás nem válaszol ezen a címen: {_host}";

        var result = await SendAsync(new OllamaGenerateRequest(_model, "Válaszolj egy szóval: OK", Stream: false, Think: false,
            new OllamaOptions(NumPredict: 1, Temperature: 0)), ct);
        return result.Outcome switch
        {
            LlmOutcome.ServiceError => result.Error,
            LlmOutcome.Timeout => $"a(z) {_model} modell nem válaszolt {_requestTimeout.TotalSeconds:0} másodpercen belül",
            _ => null,
        };
    }

    public Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken ct)
    {
        // A fájl-leírás a leghosszabb kimenet (200–400 karakter); a többi prompt egy-két szót kér,
        // ott a szűk korlát véd az elszabadult válasz ellen.
        int maxTokens = request.Kind == LlmPromptKind.FileSummary ? 300 : 60;
        return SendAsync(new OllamaGenerateRequest(_model, BuildPrompt(request), Stream: false, Think: false,
            new OllamaOptions(maxTokens, Temperature: 0.2)), ct);
    }

    private async Task<LlmResult> SendAsync(OllamaGenerateRequest payload, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_requestTimeout);
            using var response = await _http.PostAsJsonAsync($"{_host}/api/generate", payload, timeout.Token);
            string body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
                return LlmResult.ServiceError(ReadErrorField(body) ?? $"HTTP {(int)response.StatusCode}");

            string? raw;
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("error", out var error))
                    return LlmResult.ServiceError(error.GetString() ?? "ismeretlen Ollama-hiba");

                raw = json.RootElement.TryGetProperty("response", out var value) ? value.GetString() : null;
            }
            catch (JsonException ex)
            {
                return LlmResult.ServiceError($"értelmezhetetlen válasz: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(raw)) return LlmResult.NoTitle();

            foreach (string marker in NoTitleMarkers)
                if (raw.Contains(marker, StringComparison.OrdinalIgnoreCase)) return LlmResult.NoTitle();

            return LlmResult.FromTitle(raw);
        }
        // A HttpClient időtúllépése ugyanolyan TaskCanceledException, mint a felhasználói
        // megszakítás — egyedül a token állapota különbözteti meg őket. Ha nem MI kértük a leállást,
        // akkor időtúllépés volt: azt jelenteni kell, nem továbbdobni. (Egy ilyen kivétel vitte el a
        // teljes éles futást 7400/7427-nél.)
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return LlmResult.Timeout();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LlmResult.ServiceError(OneLine(ex.Message));
        }
    }

    private static string? ReadErrorField(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("error", out var error) ? OneLine(error.GetString()) : null;
        }
        catch (JsonException)
        {
            // Nem JSON (pl. egy proxy HTML-hibaoldala): egyetlen rövid sor — a nyers, többsoros
            // törzs korábban a naplót is olvashatatlanná tette.
            return OneLine(body);
        }
    }

    private static string OneLine(string? text)
    {
        string flat = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return flat.Length > 200 ? flat[..200] + "…" : flat;
    }

    /// <summary>Publikus, hogy a diagnosztikai eszközök PONTOSAN ugyanazt a promptot tudják
    /// elküldeni, amit az éles út — különben a hibakeresés más rendszert vizsgálna.</summary>
    public static string BuildPrompt(LlmRequest request)
    {
        string text = request.DocumentText.Length > MaxPromptChars
            ? request.DocumentText[..MaxPromptChars]
            : request.DocumentText;

        return request.Kind switch
        {
            LlmPromptKind.Classify => BuildClassifyPrompt(text, request.FolderName),
            LlmPromptKind.MachineType => BuildMachineTypePrompt(text, request.FolderName, request.CategoryName),
            LlmPromptKind.ProductionLineType => BuildProductionLinePrompt(text, request.FolderName),
            LlmPromptKind.FileSummary => BuildFileSummaryPrompt(text, request.FolderName),
            _ => BuildTitlePrompt(text, request.FolderName),
        };
    }

    /// <summary>Index mód: a gépsorból + a gépmappa nevéből + a dokumentum-mintából egy rövid,
    /// természetes nyelvű géptípus-megnevezés. Ez válaszol a „milyen gép az 1401?" kérdésre.
    ///
    /// SZÁNDÉKOSAN NINCS BENNE PÉLDA a kívánt válaszra. A projekt egyszer már megfizette ezt:
    /// a Title-prompt „pl. Klemmstück" példájától az LLM-címek 20,1%-a lett `Klemmstück`,
    /// valahányszor a modell bizonytalan volt. Nyílt kimenetnél a példa nem segít, hanem diktál.
    /// A megengedett kiút a NINCS_CÍM.</summary>
    private static string BuildMachineTypePrompt(string text, string folderName, string categoryName)
    {
        // A támpontot a felhasználó adja meg (nem mappanévből vesszük), tehát már helyes magyar
        // géptípus. Ettől még NEM biztos, hogy erre a konkrét gépre illik, ezért nem parancs,
        // hanem alapértelmezés: ha a dokumentumok mást mondanak, azok az erősebbek.
        string categoryLine = string.IsNullOrWhiteSpace(categoryName)
            ? ""
            : $"""
              Támpont: a gyűjtemény, amelyben ez a gép van, jellemzően ilyen gépeket tartalmaz:
              "{categoryName}". Ha a dokumentumok nem mondanak ellent, ezt használd kiindulásnak.
              Ha viszont a dokumentumokból egyértelműen MÁS fajta gép látszik, azt írd.

              """;

        return $"""
        Egy dokumentum-archívumban berendezések (gépek, eszközök) dokumentációját rendszerezzük.
        A gép mappájának neve: "{folderName}" — ez rendszerint a gyártó és a típusnév,
        de önmagában NEM mondja meg, MILYEN FAJTA gépről van szó.

        {categoryLine}Mondd meg RÖVIDEN, magyarul, MILYEN FAJTA gép ez.

        Szabályok:
        - Csak a géptípust írd, 2-5 szóban. Ne ismételd a gyártó nevét vagy a típusszámot.
        - MAGYARUL válaszolj. Ha a gép neve idegen nyelvű (pl. "EINROLMASCHINE"), akkor sem azt
          másold vissza, hanem a magyar megfelelőjét írd.
        - NE ÍRJ olyan jelzőt (pl. „centrifugális", „hidraulikus", „automata"), amit a dokumentumok
          nem támasztanak alá. Bizonytalanság esetén a rövidebb, általánosabb megnevezés a helyes.
        - NE TALÁLD KI. Ha sem a gépsor, sem a dokumentumok nem árulják el, mi ez a gép,
          akkor a helyes válasz: NINCS_CÍM
        - Használj helyes magyar ékezeteket.

        Néhány dokumentum a gép mappájából:
        {text}

        Géptípus:
        """;
    }

    /// <summary>Index mód, GYÁRTÓSOR: nem géptípust kérünk, hanem hogy mit szerelnek/gyártanak
    /// rajta. A sorok mappaneve rendszerint a terméket vagy a vevőt hordozza
    /// („T05 DEMOAUTO 3_2 Hosszu-Rovid", „T09 5_2 MINTA szerelés"), a benne lévő gépek típusa pedig
    /// vegyes — ezért a „milyen típusú gép ez?" kérdés itt értelmetlen volna.
    /// Példa itt sincs: nyílt kimenetnél a példa diktál.</summary>
    private static string BuildProductionLinePrompt(string text, string folderName) =>
        $"""
        Egy dokumentum-archívumban GYÁRTÓSOROK dokumentációját rendszerezzük.
        A gyártósor mappájának neve: "{folderName}" — ebben rendszerint a sor jele, a vevő
        (pl. autógyár) és a gyártott alkatrész szerepel.

        Mondd meg RÖVIDEN, magyarul, MIT SZERELNEK vagy GYÁRTANAK ezen a soron.

        Szabályok:
        - Egyetlen tömör kifejezés, 2-6 szó. Ne ismételd a sor jelét.
        - NE TALÁLD KI. Ha a névből és a dokumentumokból nem derül ki, válaszold: NINCS_CÍM
        - Használj helyes magyar ékezeteket.

        Néhány dokumentum a gyártósor mappájából:
        {text}

        Amit ezen a soron gyártanak:
        """;

    /// <summary>
    /// Index mód, fájl-szintű leírás: összefüggő, 200–400 karakteres magyar összefoglaló EGY
    /// fájlról. Ebből tudja egy kereső vagy asszisztens megmondani, MELYIK dokumentumban keresse a választ.
    ///
    /// A hosszt szavakban IS megadjuk, mert a modell karaktert nem tud számolni.
    /// SZÁNDÉKOSAN NINCS PÉLDA a kívánt válaszra — a projekt egyszer már megfizette ezt
    /// (484 fájl kapta a prompt példájából a `Klemmstück` nevet). Helyette azt írjuk elő, MILYEN
    /// KÉRDÉSEKRE feleljen a leírás; így a szerkezet adott, a szavak viszont a dokumentumból jönnek.
    /// </summary>
    private static string BuildFileSummaryPrompt(string text, string folderName) =>
        $"""
        Egy berendezés ("{folderName}") dokumentációjának egyetlen dokumentumát kell magyarul összefoglalnod.

        Az összefoglaló ezekre feleljen, összefüggő mondatokban:
        - Milyen FAJTA dokumentum ez (kezelési utasítás, kapcsolási rajz, alkatrészlista,
          jegyzőkönyv, adatlap, tanúsítvány, árajánlat, szerelési útmutató…)?
        - A gép melyik RÉSZÉRE vagy milyen RÉSZEGYSÉGRE vonatkozik?
        - Mi a konkrét tartalma: milyen műveletet, adatot, alkatrészt vagy eljárást ír le?
        - Ha a dokumentumban gyártó, típusjel vagy cikkszám szerepel, azt is említsd meg.

        FIGYELEM a gyártóra: a gépmappában rengeteg BESZÁLLÍTÓI komponens dokumentuma van
        (vezérlő, szivattyú, szűrő, kapcsoló, szállítószalag). Ezek gyártója NEM a gép gyártója.
        Csak azt a gyártót írd le, amelyik magában a dokumentumban szerepel — a mappa nevéből
        SOHA ne vedd át.

        Szabályok:
        - 2–4 mondat, körülbelül 200–400 karakter (nagyjából 30–60 szó). Ne írj ennél hosszabbat.
        - CSAK a dokumentum szövegéből dolgozz. Amit nem látsz benne, azt ne találd ki.
        - Ne kezdd azzal, hogy „Ez a dokumentum…" — mondd meg egyből, mi az.
        - Ne sorold fel a fájl nevét, és ne írj bevezetőt, magyarázatot vagy felsorolásjeleket.
        - Használj helyes magyar ékezeteket akkor is, ha a forrásszöveg ékezet nélküli.
        - Idegen nyelvű szakkifejezést vagy típusnevet hagyj az eredeti alakjában.
        - Ha a szöveg értelmezhetetlen zaj (OCR-szemét, csak számok), válaszold: NINCS_CÍM

        Dokumentum szövege:
        {text}

        Összefoglaló:
        """;

    private static string BuildTitlePrompt(string text, string folderName) =>
        $"""
        Nevezd meg röviden, magyarul, hogy MIRŐL SZÓL ez a dokumentum — fájlnévbe illő módon.
        A dokumentum egy berendezés (gép, eszköz) dokumentációjából származik.
        A tartalmazó mappa neve: "{folderName}" — ez rendszerint a gép vagy berendezés neve.

        FIGYELEM: a magyar „cím" szó postai címet is jelent, de MOST NEM AZT KÉRJÜK.
        Postai címet, települést, utcanevet, személynevet, ügyfélnevet, telefonszámot és
        e-mail-címet SOHA ne adj vissza — akkor sem, ha ezek a dokumentum legfeltűnőbb adatai.
        A dokumentum TÁRGYA kell, nem az, hogy kinek vagy hová szól.

        Szabályok: csak a megnevezést írd, semmi mást (ne magyarázd, ne tegyél idézőjelet).
        Használj helyes magyar ékezeteket akkor is, ha a forrásszöveg ékezet nélküli.
        Max 8 szó. Ne használj / \ : * ? " < > | karaktereket.
        A rajzszámot vagy cikkszámot NE írd bele a címbe — azt a program úgyis hozzáfűzi.

        Ha MŰSZAKI RAJZOT látsz (rajzfej-mezők: Benennung, Zeichnungsnr., Werkstoff, Maßstab,
        Denomination, Drawing No.), akkor a MEGNEVEZÉS mező (Benennung / Denomination) értékét
        másold ki SZÓ SZERINT, pontosan úgy, ahogy a dokumentumban áll — ne fordítsd le, ne
        egészítsd ki, és ne írj helyette mást. Ha ez a mező nincs meg vagy üres, akkor NINCS_CÍM.
        A rajzfej CÍMKÉIT, a szabványszámokat (ISO, DIN), a cégneveket és a copyright-szöveget
        SOHA ne add vissza címként.

        Ha a szöveg csak számokból, kódokból vagy hibás szkennelésből származó zajból áll, és semmi
        értelmeset nem tudsz kiolvasni belőle, akkor pontosan ezt válaszold: NINCS_CÍM
        Ne találj ki címet, és ne adj általános megnevezést (pl. "Technikai megjegyzések",
        "Összegzés", "Dokumentum") csak azért, hogy legyen válasz.

        Dokumentum szövege:
        {text}

        Cím:
        """;

    /// <summary>A kaszkád utolsó lépcsője. Ha címet nem sikerült kiolvasni, a TÍPUS akkor is
    /// hasznos információ — zárt listából választva a modell nem tud sablont kitalálni.</summary>
    private static string BuildClassifyPrompt(string text, string folderName) =>
        $"""
        Ez egy berendezés dokumentációjának egy fájlja, címet nem sikerült kiolvasni belőle.
        A tartalmazó mappa neve: "{folderName}" — ez rendszerint a gép vagy berendezés neve.

        Döntsd el, MILYEN TÍPUSÚ dokumentum ez, KIZÁRÓLAG ebből a listából:
        Alkatrészrajz
        Összeállítási rajz
        Villamos kapcsolási rajz
        Pneumatikus kapcsolási rajz
        Hidraulikus kapcsolási rajz
        Alkatrészlista
        Kezelési útmutató
        Karbantartási utasítás
        Adatlap
        Mérési jegyzőkönyv
        Megfelelőségi nyilatkozat
        Paraméterlista
        Beállítási utasítás
        Szállítói dokumentum

        Támpontok:
        - méretek, tűrések, anyagminőség (1.4301, S235), Maßstab, Benennung → Alkatrészrajz
        - több alkatrész egy ábrán, tételszámok, robbantott nézet → Összeállítási rajz
        - K1/Q1/X1 jelölések, sorkapcsok, 24V/230V, vezetékszámok → Villamos kapcsolási rajz
        - szelep- és hengerjelek, bar, MPa, légelőkészítő → Pneumatikus kapcsolási rajz
        - tételszám + darabszám + megnevezés táblázat → Alkatrészlista
        - lépésenkénti leírás, figyelmeztetések, kezelőszervek → Kezelési útmutató

        Ha a szöveg teljesen értelmezhetetlen zaj, válaszold: NINCS_CÍM
        Csak a típus nevét írd, semmi mást.

        Dokumentum szövege:
        {text}

        Típus:
        """;
}

// think:false NÉLKÜL a qwen3 (thinking-képes modell) élőben mérve ~20x lassabb egy triviális
// cím-kérésnél is (~31s vs ~1.5s) — lásd a terv Kontextus szekcióját. Ne hagyd ki.
internal sealed record OllamaGenerateRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("think")] bool Think,
    [property: JsonPropertyName("options")] OllamaOptions Options);

/// <summary>
/// A generálás korlátai. Kettő számít nekünk:
///
/// <c>num_predict</c>: felső korlát a kimeneti tokenekre. A futásidő EGYENESEN arányos vele —
/// egy elszabaduló válasz egyetlen fájlnál percekbe kerülhet, 23 000 fájlnál ez a futás halála.
/// 300 token bőven elég egy 400 karakteres magyar összefoglalóhoz.
///
/// <c>temperature</c>: az alapértelmezett 0.8 műszaki összefoglalóhoz túl kreatív — ez az a
/// szabadság, amiből a `Preszesszű gép` és a `Háztartási eszköz tartózkodó` született. Itt nem
/// változatosságot akarunk, hanem azt, hogy a modell a LÁTOTT szövegnél maradjon.
/// </summary>
internal sealed record OllamaOptions(
    [property: JsonPropertyName("num_predict")] int NumPredict,
    [property: JsonPropertyName("temperature")] double Temperature);

