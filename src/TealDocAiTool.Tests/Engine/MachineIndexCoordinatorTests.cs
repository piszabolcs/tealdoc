using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

/// <summary>
/// A gyorsítótár és az AKTUÁLIS beállítások összhangja. A tét nagy: egy teljes archívum
/// generálása több ezer LLM-hívás. Ha egy beállítás megváltozása csak teljes újragenerálással
/// érvényesülne, a felhasználó órákat veszít — vagy vegyes kimenetet kap.
/// </summary>
public class MachineIndexCoordinatorTests
{
    /// <summary>A munkaterület gyökere. A gépek a `gepek` alkönyvtárban vannak, a kimenet és a
    /// gyorsítótár MELLETTE — a koordinátor a gyökér minden alkönyvtárát gépnek tekinti.</summary>
    private sealed record Workspace(string Base, string Root, string Out, string Cache);

    private static Workspace MakeWorkspace(params string[] machineFolders)
    {
        string basePath = Path.Combine(Path.GetTempPath(), "tealdoc_coord_" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(basePath, "gepek");
        foreach (string m in machineFolders)
        {
            string dir = Path.Combine(root, m);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "kezikonyv.pdf"), "tartalom");
        }
        return new Workspace(basePath, root, Path.Combine(basePath, "ki"), Path.Combine(basePath, "cache"));
    }

    private static IndexSettings Settings(string root, string outFolder, string cache, string spUrl = "",
        IndexOutputMode mode = IndexOutputMode.MarkdownFiles) =>
        new(RootFolder: root, OutputMode: mode, OutputCsvPath: "",
            OutputFolder: outFolder, CacheFolder: cache, OllamaHost: "", OllamaModel: "", Workers: 1,
            GenerateMachineType: false, GenerateFileSummaries: false, MaxFilesPerMachineForSummary: 0,
            SharePointBaseUrl: spUrl);

    private static readonly IProgress<IndexProgress> Silent = new Progress<IndexProgress>(_ => { });

    // A SharePoint-tudásforrás az .md-t nem olvassa, ezért a Word az alapértelmezett kimenet.
    // A gyorsítótár közös: a formátumváltás NEM kívánhat teljes újragenerálást.
    // C-5 / S10: kiesett Ollama mellett a futás érthető hibával áll meg, és semmi nem kerül a
    // gyorsítótárba — így egy későbbi, működő Ollamás futás tényleg kitölti a géptípust.
    [Fact]
    public async Task RunAsync_OllamaDown_FailsWithoutCachingThenWorkingRunFillsMachineType()
    {
        var w = MakeWorkspace("1401 A", "1402 B");
        var settings = Settings(w.Root, w.Out, w.Cache) with { GenerateMachineType = true };
        try
        {
            await Assert.ThrowsAsync<LlmUnavailableException>(() => new MachineIndexCoordinator(
                    _ => throw new HttpRequestException("Nem hozható létre kapcsolat"))
                .RunAsync(settings, Silent, CancellationToken.None));
            string cache = Path.Combine(w.Cache, "index_cache.jsonl");
            Assert.True(!File.Exists(cache) || File.ReadAllText(cache).Trim().Length == 0);

            await new MachineIndexCoordinator(_ => Task.FromResult(LlmResult.FromTitle("CNC esztergagép")))
                .RunAsync(settings, Silent, CancellationToken.None);

            Assert.All(Directory.GetFiles(w.Out, "*.md"), f => Assert.Contains("CNC esztergagép", File.ReadAllText(f)));
        }
        finally { Directory.Delete(w.Base, true); }
    }

    [Fact]
    public async Task RunAsync_SwitchingToWordReusesTheCacheAndWritesDocx()
    {
        var w = MakeWorkspace("1401 A");
        var llm = new Func<LlmRequest, Task<LlmResult>>(_ => throw new Exception("nem kellhet LLM"));
        try
        {
            await new MachineIndexCoordinator(llm)
                .RunAsync(Settings(w.Root, w.Out, w.Cache), Silent, CancellationToken.None);
            Assert.Single(Directory.GetFiles(w.Out, "*.md"));

            await new MachineIndexCoordinator(llm).RunAsync(
                Settings(w.Root, w.Out, w.Cache, mode: IndexOutputMode.WordFiles),
                Silent, CancellationToken.None);

            Assert.Single(Directory.GetFiles(w.Out, "*.docx"));
        }
        finally { Directory.Delete(w.Base, true); }
    }

    [Fact]
    public async Task RunAsync_AddingTheSharePointUrlLaterFixesAlreadyCachedMachines()
    {
        var w = MakeWorkspace("1203 DEMOTEC L70HD");
        var llm = new Func<LlmRequest, Task<LlmResult>>(_ => throw new Exception("nem kellhet LLM"));
        try
        {
            // Első futás: nincs webcím, a .md csak a gyökérhez képesti mappanevet írja.
            await new MachineIndexCoordinator(llm)
                .RunAsync(Settings(w.Root, w.Out, w.Cache), Silent, CancellationToken.None);
            string before = File.ReadAllText(Directory.GetFiles(w.Out, "*.md").Single());
            Assert.Contains("Mappa (a gyökérhez képest):** 1203 DEMOTEC L70HD", before);

            // Második futás UGYANAZZAL a gyorsítótárral, most már címmel. A gépet nem generáljuk
            // újra (az LLM hívása kivételt dobna) — a hivatkozásnak mégis meg kell jelennie.
            await new MachineIndexCoordinator(llm).RunAsync(
                Settings(w.Root, w.Out, w.Cache, "https://x.sharepoint.com/sites/S/Docs/CNC%20Esztergak"),
                Silent, CancellationToken.None);
            string after = File.ReadAllText(Directory.GetFiles(w.Out, "*.md").Single());

            Assert.Contains("https://x.sharepoint.com/sites/S/Docs/CNC%20Esztergak/1203%20DEMOTEC%20L70HD", after);
            Assert.DoesNotContain("Mappa (a gyökérhez képest)", after);
        }
        finally { Directory.Delete(w.Base, true); }
    }

    [Fact]
    public async Task RunAsync_CachedDocumentLinksAreRebuiltToo()
    {
        var w = MakeWorkspace("1401 A");
        var llm = new Func<LlmRequest, Task<LlmResult>>(_ => throw new Exception("nem kellhet LLM"));
        try
        {
            await new MachineIndexCoordinator(llm)
                .RunAsync(Settings(w.Root, w.Out, w.Cache), Silent, CancellationToken.None);

            await new MachineIndexCoordinator(llm).RunAsync(
                Settings(w.Root, w.Out, w.Cache, "https://x.sharepoint.com/D"), Silent, CancellationToken.None);
            string text = File.ReadAllText(Directory.GetFiles(w.Out, "*.md").Single());

            Assert.Contains("[kezikonyv.pdf](https://x.sharepoint.com/D/1401%20A/kezikonyv.pdf)", text);
        }
        finally { Directory.Delete(w.Base, true); }
    }

    // A VALÓS eset: mind a négy gyökér (`CNC Esztergak`, `SOROK`, …) EGY gyorsítótárba futott.
    // Amikor a SOROK gyökeret generáljuk a saját címével, a korábbi gyökér gépeinek címét nem szabad
    // sem felülírni, sem `..` szegmenses szeméttel elrontani.
    [Fact]
    public async Task RunAsync_DoesNotTouchMachinesFromAnotherRootInASharedCache()
    {
        var a = MakeWorkspace("1203 DEMOTEC L70HD");
        string otherRoot = Path.Combine(a.Base, "masik_gyoker");
        Directory.CreateDirectory(Path.Combine(otherRoot, "T05 DEMOAUTO"));
        File.WriteAllText(Path.Combine(otherRoot, "T05 DEMOAUTO", "kezikonyv.pdf"), "tartalom");
        var llm = new Func<LlmRequest, Task<LlmResult>>(_ => throw new Exception("nem kellhet LLM"));
        try
        {
            // Első gyökér a saját címével.
            await new MachineIndexCoordinator(llm).RunAsync(
                Settings(a.Root, a.Out, a.Cache, "https://x.sharepoint.com/D/Esztergak"),
                Silent, CancellationToken.None);

            // Második gyökér, KÖZÖS gyorsítótárral és MÁS címmel.
            await new MachineIndexCoordinator(llm).RunAsync(
                Settings(otherRoot, a.Out, a.Cache, "https://x.sharepoint.com/D/SOROK"),
                Silent, CancellationToken.None);

            string eszterga = File.ReadAllText(Path.Combine(a.Out, "1203 DEMOTEC L70HD.md"));
            string sorok = File.ReadAllText(Path.Combine(a.Out, "T05 DEMOAUTO.md"));

            Assert.Contains("https://x.sharepoint.com/D/Esztergak/1203%20DEMOTEC%20L70HD", eszterga);
            Assert.Contains("https://x.sharepoint.com/D/SOROK/T05%20DEMOAUTO", sorok);
            Assert.DoesNotContain("..", eszterga);
        }
        finally { Directory.Delete(a.Base, true); }
    }

    [Fact]
    public async Task RunAsync_RemovingTheSharePointUrlFallsBackToTheRelativeFolder()
    {
        var w = MakeWorkspace("1401 A");
        var llm = new Func<LlmRequest, Task<LlmResult>>(_ => throw new Exception("nem kellhet LLM"));
        try
        {
            await new MachineIndexCoordinator(llm).RunAsync(
                Settings(w.Root, w.Out, w.Cache, "https://x.sharepoint.com/D"), Silent, CancellationToken.None);

            await new MachineIndexCoordinator(llm)
                .RunAsync(Settings(w.Root, w.Out, w.Cache), Silent, CancellationToken.None);
            string text = File.ReadAllText(Directory.GetFiles(w.Out, "*.md").Single());

            Assert.DoesNotContain("https://x.sharepoint.com", text);
            Assert.Contains("Mappa (a gyökérhez képest):** 1401 A", text);
            Assert.DoesNotContain(w.Root, text); // S-3: a helyi út nem kerül a kimenetbe
        }
        finally { Directory.Delete(w.Base, true); }
    }
}
