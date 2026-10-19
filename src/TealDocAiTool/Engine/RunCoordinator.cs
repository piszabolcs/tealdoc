using System.Collections.Concurrent;
using System.IO;
using TealDocAiTool.Extractors;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

public sealed class RunCoordinator
{
    private const int MaxConcurrentLlmCalls = 2;

    private readonly Func<LlmRequest, Task<LlmResult>> _llmCallback;

    /// <param name="llmCallback">Produkcióban <c>new OllamaClient(...).GenerateAsync</c>;
    /// tesztben egy fake delegate — így a teszt nem hív valódi HTTP-t.</param>
    public RunCoordinator(Func<LlmRequest, Task<LlmResult>> llmCallback)
    {
        _llmCallback = llmCallback;
    }

    public async Task RunAsync(RunSettings settings, IProgress<RunProgress> progress, CancellationToken ct)
    {
        string logPath = RenameLog.PathIn(settings.LogFolder);

        // A naplót ELŐSZÖR nyitjuk meg, és a futás végéig nyitva tartjuk. Ha nem írható (pl. az
        // Excel tartja nyitva), a futás el sem indul — korábban ilyenkor a fájlok átneveződtek,
        // a naplóba viszont nem kerültek be, így a Visszavonás sem látta őket.
        using var log = RenameLog.OpenWriter(logPath);
        string runId = NewRunId();

        var registry = new ExtractorRegistry();
        var llmGate = new SemaphoreSlim(MaxConcurrentLlmCalls);
        var engine = new TitleEngine(registry, _llmCallback, llmGate);

        // Egy elérhetetlen almappa (jogosultság) nem viheti el a teljes futást: kimarad, a többi megy.
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = settings.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = 0, // a SearchOption-ös túlterheléssel azonos viselkedés
        };
        var allFiles = Directory.EnumerateFiles(settings.TargetFolder, "*", enumeration)
            .Where(f => !IsInside(f, settings.LogFolder)) // a napló maga sosem dokumentum
            .ToList();

        // Folytathatóság: kihagyjuk (a) a szándékosan megtartott nevű fájlokat, ÉS (b) a még
        // érvényes átnevezések EREDMÉNY-fájljait — különben egy átnevezett fájl ("123.pdf" ->
        // "Cím.pdf") a következő futáskor új, feldolgozandó fájlként jelenne meg. Ami az eredeti
        // nevén van (visszavont, vagy az áthelyezés meg sem történt), az újra sorra kerül.
        var history = RenameLog.ReadAll(logPath);
        var skip = new HashSet<string>(
            history.Where(e => !e.DryRun && e.Method == RenameMethod.Kept).Select(e => e.OriginalPath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var e in RenameLog.ActiveRenames(history))
            skip.Add(Path.Combine(Path.GetDirectoryName(e.OriginalPath) ?? "", e.NewFileName));

        var pending = allFiles.Where(f => !skip.Contains(f)).ToList();

        var takenNamesByDir = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);
        ConcurrentDictionary<string, byte> TakenNamesFor(string dir) =>
            takenNamesByDir.GetOrAdd(dir, d =>
            {
                var set = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.EnumerateFiles(d)) set.TryAdd(Path.GetFileName(f).ToLowerInvariant(), 0);
                return set;
            });

        int processed = 0;
        var extractionGate = new SemaphoreSlim(Math.Max(1, settings.ExtractionWorkers));

        var tasks = pending.Select(filePath => Task.Run(async () =>
        {
            await extractionGate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var decision = await engine.ResolveTitleAsync(filePath, ct);
                var outcome = ApplyDecision(settings, filePath, decision, TakenNamesFor(Path.GetDirectoryName(filePath)!), log, runId);

                int done = Interlocked.Increment(ref processed);
                progress.Report(new RunProgress(done, pending.Count, filePath, outcome.Message, outcome.Method));
            }
            finally
            {
                extractionGate.Release();
            }
        }, ct));

        await Task.WhenAll(tasks);
    }

    private static (RenameMethod Method, string Message) ApplyDecision(
        RunSettings settings, string filePath, TitleDecision decision,
        ConcurrentDictionary<string, byte> takenNames, RenameLogWriter log, string runId)
    {
        if (!decision.IsResolved)
        {
            log.Append(new RenameLogEntry(DateTimeOffset.Now, filePath, "", decision.Method, decision.FailureReason ?? "", settings.DryRun, runId));
            return (decision.Method, decision.FailureReason ?? "");
        }

        string extension = Path.GetExtension(filePath);
        string baseName = TargetFileName.Build(decision.Title!, filePath);
        string newFileName = CollisionResolver.ResolveUniqueName(baseName, extension, takenNames);

        if (settings.DryRun)
        {
            log.Append(new RenameLogEntry(DateTimeOffset.Now, filePath, newFileName, decision.Method, "", DryRun: true, runId));
            return (decision.Method, decision.Title!);
        }

        string? failure = MoveWithLog(log, filePath, newFileName, decision.Method, runId);
        return failure is null ? (decision.Method, decision.Title!) : (RenameMethod.Failed, failure);
    }

    /// <summary>
    /// Az átnevezés egyetlen, közös útja (köteg és egyfájlos mód). WRITE-AHEAD: a naplósor az
    /// áthelyezés ELŐTT kerül a lemezre. Ha a folyamatot épp az áthelyezés körül lövik ki, a napló
    /// legfeljebb egy meg-nem-történt átnevezést tartalmaz (a Visszavonás ezt felismeri) — de
    /// átnevezett fájl napló nélkül nem maradhat.
    /// </summary>
    /// <returns><c>null</c> siker esetén, különben a hiba oka (a naplóban Failed sorral).</returns>
    private static string? MoveWithLog(RenameLogWriter log, string filePath, string newFileName, RenameMethod method, string runId)
    {
        log.Append(new RenameLogEntry(DateTimeOffset.Now, filePath, newFileName, method, "", DryRun: false, runId));
        try
        {
            File.Move(filePath, Path.Combine(Path.GetDirectoryName(filePath)!, newFileName));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Egy zárolt (pl. épp megnyitott PDF) vagy jogosultság-védett fájl NEM szakíthatja meg
            // a köteget. A Failed sor ugyanarra az (eredeti, új név) párra érvényteleníti az előbbi
            // szándék-sort, így a Visszavonás nem keresi, a következő futás pedig újra megpróbálja.
            string reason = $"Átnevezés sikertelen: {ex.Message}";
            log.Append(new RenameLogEntry(DateTimeOffset.Now, filePath, newFileName, RenameMethod.Failed, reason, DryRun: false, runId));
            return reason;
        }
    }

    private static string NewRunId() => DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];

    // ===================== Egyfájlos mód (ráhúzás) =====================

    /// <summary>Egyetlen fájl átnevezési javaslata — még NEM nevez át. <see cref="NewFileName"/>
    /// null, ha nincs javaslat (a fájl neve már beszédes, vagy nem sikerült címet találni);
    /// az ok ilyenkor a <see cref="Decision"/>-ben.</summary>
    public sealed record SingleFileProposal(string FilePath, string? NewFileName, TitleDecision Decision);

    public async Task<SingleFileProposal> ProposeAsync(string filePath, CancellationToken ct)
    {
        var engine = new TitleEngine(new ExtractorRegistry(), _llmCallback, new SemaphoreSlim(MaxConcurrentLlmCalls));
        var decision = await engine.ResolveTitleAsync(filePath, ct);
        if (!decision.IsResolved) return new SingleFileProposal(filePath, null, decision);

        var taken = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(filePath)!))
            taken.TryAdd(Path.GetFileName(f).ToLowerInvariant(), 0);
        string newFileName = CollisionResolver.ResolveUniqueName(
            TargetFileName.Build(decision.Title!, filePath), Path.GetExtension(filePath), taken);
        return new SingleFileProposal(filePath, newFileName, decision);
    }

    /// <summary>A jóváhagyott javaslat végrehajtása — ugyanazzal a write-ahead naplóval, mint a
    /// kötegelt futás, tehát a Visszavonás („legutóbbi futás”) ezt is visszaállítja.</summary>
    /// <exception cref="IOException">A napló nem írható, vagy az átnevezés nem sikerült.</exception>
    public static void ApplyProposal(SingleFileProposal proposal, string logFolder)
    {
        if (proposal.NewFileName is null) throw new InvalidOperationException("Nincs jóváhagyható javaslat.");
        using var log = RenameLog.OpenWriter(RenameLog.PathIn(logFolder));
        string? failure = MoveWithLog(log, proposal.FilePath, proposal.NewFileName, proposal.Decision.Method, NewRunId());
        if (failure is not null) throw new IOException(failure);
    }

    private static bool IsInside(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        string rel = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));
        return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }
}
