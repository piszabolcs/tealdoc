using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Közös LLM-hívó mindkét motorhoz: párhuzamossági kapu, a kivételek kimenetellé alakítása, és
/// MEGSZAKÍTÓ. Ha a szolgáltatás egyszer szolgáltatás-hibát adott, vagy
/// <see cref="MaxConsecutiveTimeouts"/>-szor egymás után túllépte az időkorlátot, a további
/// hívások azonnal <see cref="LlmOutcome.ServiceError"/>-t kapnak: egy lefagyott Ollama mellett
/// különben minden fájl külön kivárná a teljes időkorlátot.
/// </summary>
public sealed class LlmCaller
{
    public const int MaxConsecutiveTimeouts = 3;

    private readonly Func<LlmRequest, Task<LlmResult>> _llm;
    private readonly SemaphoreSlim _gate;
    private int _consecutiveTimeouts;
    private volatile string? _serviceError;

    public LlmCaller(Func<LlmRequest, Task<LlmResult>> llm, SemaphoreSlim gate)
    {
        _llm = llm;
        _gate = gate;
    }

    /// <summary>Nem null, ha a szolgáltatást halottnak tekintjük; ilyenkor ez az ok.</summary>
    public string? ServiceError => _serviceError;

    public async Task<LlmResult> CallAsync(LlmRequest request, CancellationToken ct)
    {
        if (_serviceError is { } down) return LlmResult.ServiceError(down);

        await _gate.WaitAsync(ct);
        LlmResult result;
        try
        {
            result = await _llm(request);
        }
        // A HttpClient időtúllépése ugyanolyan OperationCanceledException, mint a felhasználói
        // megszakítás — csak a token állapota különbözteti meg őket.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result = LlmResult.Timeout();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = LlmResult.ServiceError(ex.Message);
        }
        finally
        {
            _gate.Release();
        }

        switch (result.Outcome)
        {
            case LlmOutcome.ServiceError:
                _serviceError ??= result.Error ?? "ismeretlen hiba";
                break;
            case LlmOutcome.Timeout:
                if (Interlocked.Increment(ref _consecutiveTimeouts) >= MaxConsecutiveTimeouts)
                    _serviceError ??= $"{MaxConsecutiveTimeouts} egymás utáni időtúllépés — az Ollama nem válaszol időben";
                break;
            default:
                Interlocked.Exchange(ref _consecutiveTimeouts, 0);
                break;
        }
        return result;
    }
}

/// <summary>Az LLM-szolgáltatás nem érhető el (vagy a megszakító leállította). Az Index futása ilyenkor
/// megáll, és a félkész gép NEM kerül a gyorsítótárba — különben egy későbbi, működő Ollamás futás
/// kihagyná.</summary>
public sealed class LlmUnavailableException(string reason)
    : Exception($"Az Ollama nem érhető el vagy nem válaszol: {reason}");
