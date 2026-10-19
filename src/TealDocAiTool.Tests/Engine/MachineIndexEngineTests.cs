using TealDocAiTool.Engine;
using TealDocAiTool.Extractors;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

/// <summary>Csak azt a kiterjesztést kezeli, amit megadunk — így modellezzük, hogy a valódi
/// kinyerők nem tudnak .ppt/.pptx/.msg-t olvasni.</summary>
file sealed class ExtensionExtractor : IDocumentExtractor
{
    private readonly string[] _exts;
    public ExtensionExtractor(params string[] exts) => _exts = exts;
    public bool CanHandle(string filePath) =>
        _exts.Contains(Path.GetExtension(filePath).ToLowerInvariant());
    public ExtractedDocument? Extract(string filePath) =>
        new ExtractedDocument("szöveg innen: " + Path.GetFileName(filePath), new[] { "fejléc" });
}

public class MachineIndexEngineTests
{
    private static IndexSettings Settings(
        bool machineType = false, bool summaries = false, int maxFiles = 0, string categoryHint = "",
        string root = "", string sharePointUrl = "", bool linkDocs = true) =>
        new(RootFolder: root, OutputMode: IndexOutputMode.MarkdownFiles, OutputCsvPath: "",
            OutputFolder: "", CacheFolder: "", OllamaHost: "", OllamaModel: "", Workers: 1,
            GenerateMachineType: machineType, GenerateFileSummaries: summaries,
            MaxFilesPerMachineForSummary: maxFiles, CategoryHint: categoryHint,
            SharePointBaseUrl: sharePointUrl, LinkEachDocument: linkDocs);

    private static MachineIndexEngine Engine(
        IDocumentExtractor extractor, Func<LlmRequest, Task<LlmResult>>? llm = null) =>
        new(new ExtractorRegistry(new[] { extractor }),
            llm ?? (_ => throw new Exception("nem szabadna LLM-et hívni")),
            new SemaphoreSlim(1));

    private static string MakeMachine(string category, string machineFolder, params string[] fileNames)
    {
        string root = Path.Combine(Path.GetTempPath(), "tealdoc_idx_" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(root, category, machineFolder);
        Directory.CreateDirectory(dir);
        foreach (var f in fileNames)
        {
            string full = Path.Combine(dir, f);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "tartalom");
        }
        return dir;
    }

    private static void Cleanup(string machineDir) =>
        Directory.Delete(Directory.GetParent(machineDir)!.Parent!.FullName, true);

    // A JAVÍTOTT HIBA: a .pptx/.msg-ből nem tudunk szöveget olvasni, de a gép dokumentációjának
    // része. Korábban a motor kidobta őket, így a darabszám alulszámolt és a fájl nem is szerepelt.
    [Fact]
    public async Task BuildRowAsync_ListsDocumentsWeCannotReadText()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A",
            "kezikonyv.pdf", "oktatas.pptx", "hibajelentes.msg");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"))
                .BuildRowAsync(dir, Settings(), null, CancellationToken.None);

            Assert.Equal(3, row.DocumentCount);
            Assert.Contains("oktatas.pptx", row.DocumentList);
            Assert.Contains("hibajelentes.msg", row.DocumentList);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_StillExcludesNonDocuments()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A", "kezikonyv.pdf", "setup.exe", "alkatresz.step");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"))
                .BuildRowAsync(dir, Settings(), null, CancellationToken.None);

            Assert.Equal(1, row.DocumentCount);
            Assert.DoesNotContain("setup.exe", row.DocumentList);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_DoesNotTryToSummariseWhatItCannotRead()
    {
        // A .msg bekerül a listába, de leírás nélkül — nem szabad emiatt LLM-et hívni vagy elhasalni.
        string dir = MakeMachine("CNC Esztergak", "1401 A", "level.msg");
        int llmCalls = 0;
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"), _ =>
            {
                llmCalls++;
                return Task.FromResult(LlmResult.FromTitle("leírás"));
            }).BuildRowAsync(dir, Settings(summaries: true), null, CancellationToken.None);

            Assert.Equal(0, llmCalls);
            Assert.Equal(1, row.DocumentCount);
            Assert.Contains("level.msg", row.DocumentList);
            Assert.DoesNotContain("—", row.DocumentList);
        }
        finally { Cleanup(dir); }
    }

    // A géptípus mintavétele: a kézikönyvnek kell bekerülnie, nem az ábécé-sorrend első fájljának.
    [Fact]
    public async Task BuildRowAsync_SamplesTheManualNotTheAlphabeticallyFirstFile()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A",
            "Installation_Checklist.pdf", "NETSERVICE_8092.pdf", "Zzz_Betriebsanleitung.pdf");
        string seenText = "";
        try
        {
            await Engine(new ExtensionExtractor(".pdf"), req =>
            {
                seenText = req.DocumentText;
                return Task.FromResult(LlmResult.FromTitle("CNC esztergagép"));
            }).BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.Contains("Zzz_Betriebsanleitung.pdf", seenText);
        }
        finally { Cleanup(dir); }
    }

    // A géptípus-támpont a FELHASZNÁLÓTÓL jön. A modell világtudása nem ismeri a konkrét
    // típusneveket („MINTAGYARTO VARIO 20"), ezért ez sokat ér — de csak ott, ahol tényleg
    // egyfajta gép van.
    [Fact]
    public async Task BuildRowAsync_PassesTheUserSuppliedCategoryHintToTheModel()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 MINTAGYARTO VARIO 20", "kezikonyv.pdf");
        string category = "";
        try
        {
            await Engine(new ExtensionExtractor(".pdf"), req =>
            {
                category = req.CategoryName;
                return Task.FromResult(LlmResult.FromTitle("CNC esztergagép"));
            }).BuildRowAsync(dir, Settings(machineType: true, categoryHint: "CNC esztergagép"),
                null, CancellationToken.None);

            Assert.Equal("CNC esztergagép", category);
        }
        finally { Cleanup(dir); }
    }

    // A JAVÍTOTT REGRESSZIÓ: korábban a SZÜLŐMAPPA nevét adtuk át automatikusan. A `SOROK` viszont
    // gyártósorok gyűjteménye, nem géptípus — ebből lett a présgépből „CNC precssgép".
    // Támpont nélkül semmilyen kategórianév nem juthat a modellhez.
    [Fact]
    public async Task BuildRowAsync_SendsNoCategoryWhenTheUserGaveNoHint()
    {
        string dir = MakeMachine("SOROK", "7246 DEMOPRESS présgép", "kezikonyv.pdf");
        string category = "nem hívták meg";
        try
        {
            await Engine(new ExtensionExtractor(".pdf"), req =>
            {
                category = req.CategoryName;
                return Task.FromResult(LlmResult.FromTitle("présgép"));
            }).BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.Equal("", category);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_NeverLeaksTheParentFolderNameAsACategory()
    {
        // Biztosíték a visszacsúszás ellen: a mappaszerkezetből SEMMILYEN úton nem szivároghat
        // kategória a promptba.
        string dir = MakeMachine("Egyeb", "9778 MintaClean mosó", "kezikonyv.pdf");
        string category = "";
        try
        {
            await Engine(new ExtensionExtractor(".pdf"), req =>
            {
                category = req.CategoryName;
                return Task.FromResult(LlmResult.FromTitle("mosógép"));
            }).BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.DoesNotContain("Egyeb", category);
        }
        finally { Cleanup(dir); }
    }

    // A gyártósor NEM gép: a mondat, a mezőnevek és a feltett kérdés is más kell legyen.
    [Fact]
    public async Task BuildRowAsync_CallsAProductionLineALineNotAMachine()
    {
        string dir = MakeMachine("SOROK", "T05 DEMOAUTO 3_2 Hosszu-Rovid MINTA 07001", "kezikonyv.pdf");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"),
                _ => Task.FromResult(LlmResult.FromTitle("szelepszerelő sor")))
                .BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.Equal(MachineEntryKind.ProductionLine, row.Kind);
            Assert.Equal("T05", row.Number);
            Assert.Contains("gyártósor", row.Summary);
            Assert.DoesNotContain("számú gép", row.Summary);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_AsksWhatAProductionLineProducesNotWhatMachineItIs()
    {
        string dir = MakeMachine("SOROK", "T09 5_2 MINTA szerelés", "kezikonyv.pdf");
        LlmPromptKind kind = LlmPromptKind.Title;
        try
        {
            await Engine(new ExtensionExtractor(".pdf"), req =>
            {
                kind = req.Kind;
                return Task.FromResult(LlmResult.FromTitle("MINTA szelepszerelés"));
            }).BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.Equal(LlmPromptKind.ProductionLineType, kind);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_MachinesStillGetTheMachineTypeQuestion()
    {
        string dir = MakeMachine("SOROK", "7246 DEMOPRESS présgép 20120626", "kezikonyv.pdf");
        LlmPromptKind kind = LlmPromptKind.Title;
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"), req =>
            {
                kind = req.Kind;
                return Task.FromResult(LlmResult.FromTitle("présgép"));
            }).BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.Equal(LlmPromptKind.MachineType, kind);
            Assert.Equal(MachineEntryKind.Machine, row.Kind);
            Assert.Contains("számú gép", row.Summary);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_MakesNoClaimWhenTheCodeDoesNotDecide()
    {
        string dir = MakeMachine("SOROK", "ABB Robotos cella SOROK", "kezikonyv.pdf");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"))
                .BuildRowAsync(dir, Settings(), null, CancellationToken.None);

            Assert.Equal(MachineEntryKind.Unknown, row.Kind);
            Assert.DoesNotContain("számú gép", row.Summary);
            Assert.DoesNotContain("gyártósor", row.Summary);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_SummaryMentionsNumberNameAndType()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 MINTAGYARTO VARIO 20", "kezikonyv.pdf");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"),
                _ => Task.FromResult(LlmResult.FromTitle("CNC esztergagép")))
                .BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.Equal("1401", row.Number);
            Assert.Equal("MINTAGYARTO VARIO 20", row.Name);
            Assert.Contains("1401", row.Summary);
            Assert.Contains("MINTAGYARTO VARIO 20", row.Summary);
            Assert.Contains("CNC esztergagép", row.Summary);
        }
        finally { Cleanup(dir); }
    }

    // C-5: korábban a kiesett Ollama mellett is „sikeres”, üres géptípusú sor készült, és a
    // gyorsítótárba került — egy későbbi, működő futás így kihagyta. Most a gép feldolgozása megáll.
    [Fact]
    public async Task BuildRowAsync_ModelDown_ThrowsInsteadOfReturningAnEmptyRow()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A", "kezikonyv.pdf");
        try
        {
            await Assert.ThrowsAsync<LlmUnavailableException>(() => Engine(new ExtensionExtractor(".pdf"),
                    _ => throw new HttpRequestException("Nem hozható létre kapcsolat"))
                .BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None));
        }
        finally { Cleanup(dir); }
    }

    // C-9: elbukott validációnál a nyers LLM-válasz nem kerülhet géptípusként a kimenetbe.
    [Fact]
    public async Task BuildRowAsync_InvalidMachineTypeAnswer_LeavesTypeEmpty()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A", "kezikonyv.pdf");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"),
                    _ => Task.FromResult(LlmResult.FromTitle("x@y.example")))
                .BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.Equal("", row.MachineType);
            Assert.False(row.Incomplete);
        }
        finally { Cleanup(dir); }
    }

    // Egy időtúllépés miatt kimaradt LLM-hívás hiányossá teszi a sort: kiírható, de nem cache-elhető.
    [Fact]
    public async Task BuildRowAsync_TimedOutCall_MarksTheRowIncomplete()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A", "kezikonyv.pdf");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"), _ => Task.FromResult(LlmResult.Timeout()))
                .BuildRowAsync(dir, Settings(machineType: true), null, CancellationToken.None);

            Assert.True(row.Incomplete);
            Assert.Equal(1, row.DocumentCount);
        }
        finally { Cleanup(dir); }
    }

    // A kézikönyvek a SharePointon élnek, a generálás egy helyi másolaton fut. A .md-be a
    // SharePoint-címnek kell kerülnie, különben a tudásbázis egy `C:\Users\…` útra hivatkozik.
    [Fact]
    public async Task BuildRowAsync_MapsTheLocalPathToTheSharePointUrl()
    {
        string dir = MakeMachine("CNC Esztergak", "1203 DEMOTEC L70HD", "kezikonyv.pdf");
        string root = Directory.GetParent(dir)!.FullName;
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf")).BuildRowAsync(
                dir, Settings(root: root, sharePointUrl: "https://x.sharepoint.com/sites/S/Docs/CNC%20Esztergak"),
                null, CancellationToken.None);

            Assert.Equal("https://x.sharepoint.com/sites/S/Docs/CNC%20Esztergak/1203%20DEMOTEC%20L70HD",
                row.SharePointUrl);
            Assert.Contains("(https://x.sharepoint.com/sites/S/Docs/CNC%20Esztergak/1203%20DEMOTEC%20L70HD/kezikonyv.pdf)",
                row.DocumentList);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_LeavesTheUrlEmptyWhenNoSharePointRootIsConfigured()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A", "kezikonyv.pdf");
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"))
                .BuildRowAsync(dir, Settings(), null, CancellationToken.None);

            Assert.Equal("", row.SharePointUrl);
            Assert.Contains("- kezikonyv.pdf", row.DocumentList);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_LinkEachDocumentOffStillKeepsTheFolderUrl()
    {
        // A hosszú URL-ek hígítják a beágyazást; kikapcsolva a mappa-hivatkozás megmarad, a
        // fájlok viszont puszta névvel szerepelnek.
        string dir = MakeMachine("CNC Esztergak", "1401 A", "kezikonyv.pdf");
        string root = Directory.GetParent(dir)!.FullName;
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf")).BuildRowAsync(
                dir, Settings(root: root, sharePointUrl: "https://x.sharepoint.com/D", linkDocs: false),
                null, CancellationToken.None);

            Assert.Equal("https://x.sharepoint.com/D/1401%20A", row.SharePointUrl);
            Assert.DoesNotContain("https://", row.DocumentList);
        }
        finally { Cleanup(dir); }
    }

    // A nyers dokumentumlista a gyorsítótárba is bekerül — enélkül egy folytatott futás nem tudná
    // újraépíteni a hivatkozásokat, és a kész gépek órányi LLM-munkáját kellene eldobni.
    [Fact]
    public async Task BuildRowAsync_KeepsTheRawDocumentListForTheCache()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A",
            "kezikonyv.pdf", Path.Combine("Rajzok", "terv.pdf"));
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"))
                .BuildRowAsync(dir, Settings(), null, CancellationToken.None);

            Assert.NotNull(row.Documents);
            Assert.Equal(2, row.Documents!.Count);
            Assert.Contains(row.Documents, d => d.RelativePath == Path.Combine("Rajzok", "terv.pdf"));
        }
        finally { Cleanup(dir); }
    }

    // A leírás-korlát a FONTOS fájlokra vonatkozzon, ne az ábécé-sorrend elejére. A valódi
    // 1401-es mappában az ábécé eleje telepítési ellenőrzőlista és szállítólevél; a kézikönyv
    // egy alkönyvtárban fekszik. Korláttal épp azt veszítenénk el, amiért a leírás készül.
    [Fact]
    public async Task BuildRowAsync_SummaryCapPicksTheImportantFilesNotTheFirstAlphabetically()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 VARIO",
            "AAA_szallitolevel.pdf", "BBB_arajanlat.pdf", "ZZZ_Betriebsanleitung.pdf");
        var leirtak = new List<string>();
        try
        {
            await Engine(new ExtensionExtractor(".pdf"), req =>
            {
                lock (leirtak) leirtak.Add(req.DocumentText);
                return Task.FromResult(LlmResult.FromTitle("leírás"));
            }).BuildRowAsync(dir, Settings(summaries: true, maxFiles: 1), null, CancellationToken.None);

            Assert.Single(leirtak);
            Assert.Contains("ZZZ_Betriebsanleitung.pdf", leirtak[0]);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_WithoutACapEveryReadableFileGetsADescription()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A", "egy.pdf", "ketto.pdf", "level.msg");
        int hivasok = 0;
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"), _ =>
            {
                Interlocked.Increment(ref hivasok);
                return Task.FromResult(LlmResult.FromTitle("leírás"));
            }).BuildRowAsync(dir, Settings(summaries: true), null, CancellationToken.None);

            // A .msg-t nem tudjuk olvasni: bekerül a listába, de nem hívunk rá LLM-et.
            Assert.Equal(2, hivasok);
            Assert.Equal(3, row.DocumentCount);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task BuildRowAsync_FindsDocumentsInSubfolders()
    {
        string dir = MakeMachine("CNC Esztergak", "1401 A",
            Path.Combine("Backup dokumentumok", "DOCUMENTATIONS", "SCHEMA.PDF"));
        try
        {
            var row = await Engine(new ExtensionExtractor(".pdf"))
                .BuildRowAsync(dir, Settings(), null, CancellationToken.None);

            Assert.Equal(1, row.DocumentCount);
            Assert.Contains("SCHEMA.PDF", row.DocumentList);
        }
        finally { Cleanup(dir); }
    }
}
