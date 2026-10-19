namespace TealDocAiTool.Models;

public enum LlmOutcome
{
    /// <summary>A modell adott egy címet (a validáció még hátravan).</summary>
    Title,

    /// <summary>A modell szerint nincs értelmes cím a dokumentumban. Ez ÉRVÉNYES válasz.</summary>
    NoTitle,

    /// <summary>Az Ollama nem válaszolt vagy hibát adott. Ez NEM a dokumentumról szól!</summary>
    ServiceError,

    /// <summary>A hívás túllépte az időkorlátot. Külön áll a <see cref="ServiceError"/>-tól, mert
    /// nem jelenti azt, hogy a szolgáltatás halott — egyetlen nehéz dokumentum miatt nem mondunk le
    /// az LLM-ről a maradék több ezer fájlnál.</summary>
    Timeout,
}

/// <summary>
/// Az LLM-hívás kimenetele. A <see cref="LlmOutcome.ServiceError"/> elkülönítése azért kritikus,
/// mert enélkül a napló ugyanazt írja a „nem volt értelmes cím" és a „nem indult el az Ollama"
/// esetre. Egy valódi, éles futásban emiatt 2221 fájl kapott félrevezető Failed-et, miközben a
/// hiba az volt, hogy a telepítésből hiányzott a llama-server binárisa.
/// </summary>
public sealed record LlmResult(LlmOutcome Outcome, string? Title, string? Error)
{
    public static LlmResult FromTitle(string title) => new(LlmOutcome.Title, title, null);
    public static LlmResult NoTitle() => new(LlmOutcome.NoTitle, null, null);
    public static LlmResult ServiceError(string error) => new(LlmOutcome.ServiceError, null, error);
    public static LlmResult Timeout() => new(LlmOutcome.Timeout, null, null);
}
