using TealDocAiTool.Engine;
using TealDocAiTool.Extractors;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

file sealed class AnyExtractor : IDocumentExtractor
{
    public bool CanHandle(string filePath) => true;
    public ExtractedDocument? Extract(string filePath) => new("szöveg", new[] { "fejléc" });
}

/// <summary>
/// A fájl-szintű és a gép-szintű jelzés megkülönböztetése. Ez nem kozmetika: a UI a fájl-szintű
/// jelzéseket NEM naplózza, mert egy teljes, valós archívumon 23 000 sor gyűlne a memóriában.
/// </summary>
public class IndexProgressReportingTests
{
    private static IndexSettings Settings() =>
        new(RootFolder: "", OutputMode: IndexOutputMode.MarkdownFiles, OutputCsvPath: "",
            OutputFolder: "", CacheFolder: "", OllamaHost: "", OllamaModel: "", Workers: 1,
            GenerateMachineType: false, GenerateFileSummaries: false,
            MaxFilesPerMachineForSummary: 0);

    private static string MakeMachine(params string[] fileNames)
    {
        string dir = Path.Combine(Path.GetTempPath(), "tealdoc_prog_" + Guid.NewGuid().ToString("N"), "SOROK", "1401 A");
        Directory.CreateDirectory(dir);
        foreach (var f in fileNames) File.WriteAllText(Path.Combine(dir, f), "tartalom");
        return dir;
    }

    private static void Cleanup(string dir) =>
        Directory.Delete(Directory.GetParent(dir)!.Parent!.FullName, true);

    [Fact]
    public async Task BuildRowAsync_MarksPerFileReportsAsFileLevel()
    {
        string dir = MakeMachine("egy.pdf", "ketto.pdf");
        var seen = new List<IndexProgress>();
        try
        {
            var engine = new MachineIndexEngine(
                new ExtractorRegistry(new[] { new AnyExtractor() }),
                _ => throw new Exception("nem kell LLM"),
                new SemaphoreSlim(1));

            await engine.BuildRowAsync(dir, Settings(),
                new Progress<IndexProgress>(p => { lock (seen) seen.Add(p); }),
                CancellationToken.None);

            // A Progress<T> aszinkron marsallozik, ezért megvárjuk a jelzéseket.
            for (int i = 0; i < 50 && seen.Count < 2; i++) await Task.Delay(20);

            lock (seen)
            {
                Assert.NotEmpty(seen);
                Assert.All(seen, p => Assert.True(p.IsFileLevel,
                    "a fájlonkénti jelzésnek IsFileLevel=true-nak kell lennie, különben a UI naplózza"));
            }
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void IndexProgress_DefaultsToMachineLevel()
    {
        // A koordinátor gép-szintű jelentései nem adják meg a jelzőt — azoknak naplózandónak kell lenniük.
        var p = new IndexProgress(1, 10, "1401 A", "kész", 3, 3);

        Assert.False(p.IsFileLevel);
    }

    [Fact]
    public void IndexProgress_FileLevelFlagSurvivesRepacking()
    {
        // A koordinátor újracsomagolja a fájl-jelzést, hogy a gép-számlálókat is vigye. Ha eközben
        // elveszik a jelző, MINDEN fájl naplósort kap — pontosan ez a hiba fordult elő élesben,
        // mert a darabszámokból nem lehet a jelzésre következtetni.
        var fileLevel = new IndexProgress(0, 0, "1401 A", "egy.pdf", 3, 0, IsFileLevel: true);

        var repacked = new IndexProgress(2, 10, fileLevel.CurrentMachine, fileLevel.Message,
            fileLevel.FilesInCurrentMachine, 7, IsFileLevel: fileLevel.IsFileLevel);

        Assert.True(repacked.IsFileLevel);
        Assert.True(repacked.TotalMachines > 0, "a darabszám nem árulja el a jelzés fajtáját");
    }
}
