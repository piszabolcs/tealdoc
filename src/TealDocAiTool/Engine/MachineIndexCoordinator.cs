using System.IO;
using System.Text;
using System.Text.Json;
using TealDocAiTool.Extractors;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Az Index mód karmestere. Bejárja a gyökér alatti gépmappákat, mindegyikre felépíti a sort, és a
/// végén a választott módban kiírja: egy CSV-táblázat, VAGY egységenként egy .docx / .md fájl egy mappába.
/// A 71 GB miatt kritikus a folytathatóság: minden kész gépet azonnal gyorsítótárazunk (JSONL).
/// </summary>
public sealed class MachineIndexCoordinator
{
    private const string CacheFileName = "index_cache.jsonl";

    private readonly Func<LlmRequest, Task<LlmResult>> _llm;
    private readonly object _cacheLock = new();

    public MachineIndexCoordinator(Func<LlmRequest, Task<LlmResult>> llm) => _llm = llm;

    public async Task RunAsync(IndexSettings settings, IProgress<IndexProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(settings.CacheFolder);
        string cachePath = Path.Combine(settings.CacheFolder, CacheFileName);

        var registry = new ExtractorRegistry();
        // Az LLM-hívások párhuzamossága a felhasználó „Párhuzamos" beállítása. Korábban fixen 2
        // volt, akármit írt a mezőbe — így a mező csak a szövegkinyerést gyorsította, miközben az
        // LLM a szűk keresztmetszet. Fájlonkénti leírásnál ez 22 885 híváson dől el.
        int llmParallelism = Math.Max(1, settings.Workers);
        var llmGate = new SemaphoreSlim(llmParallelism);
        var engine = new MachineIndexEngine(registry, _llm, llmGate);

        var machineFolders = Directory.EnumerateDirectories(settings.RootFolder, "*",
                new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 })
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cachedRows = LoadCache(cachePath);
        var done = new HashSet<string>(cachedRows.Select(r => r.FolderPath), StringComparer.OrdinalIgnoreCase);
        var pending = machineFolders.Where(f => !done.Contains(f)).ToList();

        int processed = done.Count;
        int filesTotal = 0;
        int total = machineFolders.Count;

        // Ha az LLM-szolgáltatás kiesik, a többi gépet sem kezdjük el: egy halott Ollama mellett
        // minden gép üres géptípussal, „sikeresen” készülne el (és korábban a gyorsítótárba is került).
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        LlmUnavailableException? serviceFailure = null;
        // A hiányos (időtúllépéses) sorok nem kerülnek a gyorsítótárba, de a kimenetbe igen.
        var incompleteRows = new System.Collections.Concurrent.ConcurrentBag<MachineIndexRow>();

        var workerGate = new SemaphoreSlim(Math.Max(1, settings.Workers));
        var tasks = pending.Select(folder => Task.Run(async () =>
        {
            await workerGate.WaitAsync(abort.Token);
            try
            {
                abort.Token.ThrowIfCancellationRequested();
                // A fájl-szintű jelzést kiegészítjük a gép-számlálókkal, de az IsFileLevel jelzőt
                // MEG KELL TARTANI — abból tudja a UI, hogy ezt nem kell naplózni.
                var fileProgress = new Progress<IndexProgress>(fp =>
                    progress.Report(new IndexProgress(processed, total, fp.CurrentMachine, fp.Message,
                        fp.FilesInCurrentMachine, filesTotal, IsFileLevel: fp.IsFileLevel)));

                var row = await engine.BuildRowAsync(folder, settings, fileProgress, abort.Token);
                if (row.Incomplete) incompleteRows.Add(row);
                else AppendCache(cachePath, row);
                // Fájl-módban azonnal ki is írjuk a gép fájlját (nem kell megvárni a végét).
                WritePerMachineFile(settings, row);

                Interlocked.Add(ref filesTotal, row.DocumentCount);
                int p = Interlocked.Increment(ref processed);
                progress.Report(new IndexProgress(p, total, row.Title, RowSummary(row, settings), row.DocumentCount, filesTotal));
            }
            catch (LlmUnavailableException ex)
            {
                Interlocked.CompareExchange(ref serviceFailure, ex, null);
                abort.Cancel();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new IndexProgress(processed, total, Path.GetFileName(folder), "HIBA: " + ex.Message, 0, filesTotal));
            }
            finally
            {
                workerGate.Release();
            }
        }, abort.Token));

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (serviceFailure is not null && !ct.IsCancellationRequested)
        {
            // a megszakítást a szolgáltatás-hiba okozta — az alábbi throw jelzi a valódi okot
        }
        if (serviceFailure is not null) throw serviceFailure;

        var allRows = LoadCache(cachePath)
            .Concat(incompleteRows)
            .Select(r => Rehydrate(r, settings))
            .OrderBy(r => ParseNum(r.Number))
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.OutputMode == IndexOutputMode.Csv)
        {
            CsvIndexWriter.Write(settings.OutputCsvPath, allRows);
        }
        else
        {
            // Végső biztosítás: a gyorsítótárból minden gép fájlját (újra)írjuk, hogy egy
            // folytatott futásból se maradjon ki egy sem.
            foreach (var row in allRows) WritePerMachineFile(settings, row);
        }
    }

    private static string RowSummary(MachineIndexRow row, IndexSettings settings)
    {
        string text = $"{row.DocumentCount} dokumentum";
        if (settings.GenerateMachineType)
            text += " · " + (row.MachineType.Length > 0 ? row.MachineType : "típus: nem állapítható meg");
        if (row.Incomplete)
            text += " · HIÁNYOS (LLM-időtúllépés) — a következő futás pótolja";
        return text;
    }

    private static void WritePerMachineFile(IndexSettings settings, MachineIndexRow row)
    {
        switch (settings.OutputMode)
        {
            case IndexOutputMode.WordFiles:
                WordFileWriter.Write(settings.OutputFolder, row);
                break;
            case IndexOutputMode.MarkdownFiles:
                MarkdownFileWriter.Write(settings.OutputFolder, row);
                break;
        }
    }

    /// <summary>
    /// A gyorsítótárból visszatöltött sort az AKTUÁLIS beállításokhoz igazítja. Enélkül egy
    /// korábbi futás gépei a régi (vagy hiányzó) webes hivatkozással íródnának ki, és a
    /// javításuk teljes újragenerálást kívánna — több ezer LLM-hívást.
    ///
    /// A hivatkozás determinisztikus (útvonalból számolt), tehát bátran újraszámolható; az
    /// LLM-től származó mezőkhöz (géptípus, leírások) NEM nyúlunk.
    /// </summary>
    private static MachineIndexRow Rehydrate(MachineIndexRow row, IndexSettings settings)
    {
        // Egy gyorsítótárba több gyökér is futhat — a valódi archívum négy fájának MÁS-MÁS
        // hivatkozás-alapja van. Egy másik gyökérhez tartozó géphez ebből a futásból nem tudunk
        // címet számolni, a sajátját viszont nem szabad felülírnunk.
        if (settings.SharePointBaseUrl.Length > 0 &&
            !SharePointUrlBuilder.IsUnderRoot(settings.RootFolder, row.FolderPath))
            return row;

        string url = SharePointUrlBuilder.ForMachineFolder(
            settings.SharePointBaseUrl, settings.RootFolder, row.FolderPath);

        // Régi gyorsítótár: nincs meg a nyers dokumentumlista, így a fájlonkénti hivatkozás nem
        // pótolható — a mappa-hivatkozás viszont igen, és az önmagában is használható.
        if (row.Documents is not { Count: > 0 })
            return url == row.SharePointUrl ? row : row with { SharePointUrl = url };

        return row with
        {
            SharePointUrl = url,
            DocumentList = DocumentListRenderer.Render(row.Documents, url, settings.LinkEachDocument),
        };
    }

    private static long ParseNum(string s) =>
        long.TryParse(new string(s.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : long.MaxValue;

    private List<MachineIndexRow> LoadCache(string cachePath)
    {
        var rows = new List<MachineIndexRow>();
        if (!File.Exists(cachePath)) return rows;
        foreach (var line in File.ReadLines(cachePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var row = JsonSerializer.Deserialize<MachineIndexRow>(line);
                if (row is not null) rows.Add(row);
            }
            catch (JsonException) { }
        }
        return rows;
    }

    private void AppendCache(string cachePath, MachineIndexRow row)
    {
        string json = JsonSerializer.Serialize(row);
        lock (_cacheLock)
        {
            File.AppendAllText(cachePath, json + Environment.NewLine, Encoding.UTF8);
        }
    }
}
