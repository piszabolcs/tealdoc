using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

public class RunCoordinatorTests
{
    /// <summary>Fake LLM, ami mindig ugyanazt a címet adja — a tesztek többségének ennyi elég.</summary>
    private static Func<LlmRequest, Task<LlmResult>> Llm(string title) =>
        _ => Task.FromResult(LlmResult.FromTitle(title));

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"tealdoc_run_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task RunAsync_DryRun_LogsWithoutRenamingFiles()
    {
        string dir = NewTempDir();
        try
        {
            string filePath = Path.Combine(dir, "P-00012.txt");
            File.WriteAllText(filePath, "Phoenix Contact relé kézikönyv, hosszabb szöveg a heurisztika miatt.");
            string logDir = Path.Combine(dir, "napló");

            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: true);
            var reports = new List<RunProgress>();
            var progress = new Progress<RunProgress>(reports.Add);

            // .txt-nek nincs heurisztikája -> LLM kellene, de dry-run + üres callback esetén
            // "feldolgozatlan"-ként naplózódik, ami pont ezt teszteli: dry-run esetén sem
            // nyúlunk a fájlhoz akkor sem, ha lenne mit átnevezni.
            await new RunCoordinator(Llm("Phoenix Contact relé kézikönyv"))
                .RunAsync(settings, progress, CancellationToken.None);

            Assert.True(File.Exists(filePath)); // az EREDETI fájl nem tűnt el
            string logPath = Path.Combine(logDir, "tealdoc_naplo.csv");
            var entries = TealDocAiTool.Engine.RenameLog.ReadAll(logPath);
            Assert.Single(entries);
            Assert.True(entries[0].DryRun);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_LiveRun_RenamesFileAndLogsIt()
    {
        string dir = NewTempDir();
        try
        {
            string filePath = Path.Combine(dir, "P-00012.txt");
            File.WriteAllText(filePath, "valamilyen dokumentum szöveg a cím-generáláshoz");
            string logDir = Path.Combine(dir, "napló");

            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);
            var progress = new Progress<RunProgress>(_ => { });

            await new RunCoordinator(Llm("Phoenix Contact relé kézikönyv"))
                .RunAsync(settings, progress, CancellationToken.None);

            Assert.False(File.Exists(filePath));
            // Az eredeti (vak) név zárójelben megmarad: cikkszámra is lehet keresni.
            Assert.True(File.Exists(Path.Combine(dir, "Phoenix Contact relé kézikönyv (P-00012).txt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SecondRun_LeavesRenamedFilesAlone()
    {
        // Az átnevezett fájl új neve beszédes, tehát a bemeneti kapu megvédi: se újabb LLM-hívás,
        // se "Cím (Cím (P-00012))" típusú második átnevezés.
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "P-00012.txt"), "szöveg");
            string logDir = Path.Combine(dir, "napló");
            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);

            await new RunCoordinator(Llm("Hidraulikus tömlő cseréje"))
                .RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None);

            string renamed = Path.Combine(dir, "Hidraulikus tömlő cseréje (P-00012).txt");
            Assert.True(File.Exists(renamed));

            var secondReports = new List<RunProgress>();
            await new RunCoordinator(_ => throw new Exception("nem szabadna újra LLM-et hívni"))
                .RunAsync(settings, new Progress<RunProgress>(secondReports.Add), CancellationToken.None);

            Assert.True(File.Exists(renamed)); // ugyanott, ugyanazon a néven
            Assert.All(secondReports, r => Assert.Equal(RenameMethod.Kept, r.LastMethod));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SecondRun_SkipsAlreadyProcessedFiles()
    {
        string dir = NewTempDir();
        try
        {
            // Beszédes nevű fájl: az első futás megtartja (Kept), a NEVE tehát nem változik.
            // Pont ez az eset kerülne újra sorra a második futásban, ha a napló nem szűrné ki —
            // az átnevezetteket már az új nevük is megvédi.
            string filePath = Path.Combine(dir, "Hidraulika kézikönyv.txt");
            File.WriteAllText(filePath, "szöveg");
            string logDir = Path.Combine(dir, "napló");
            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);

            var firstReports = new List<RunProgress>();
            var coordinator = new RunCoordinator(_ => throw new Exception("beszédes névnél nem kell LLM"));
            await coordinator.RunAsync(settings, new Progress<RunProgress>(firstReports.Add), CancellationToken.None);
            Assert.Equal(RenameMethod.Kept, firstReports.Single().LastMethod);

            var secondReports = new List<RunProgress>();
            var secondCoordinator = new RunCoordinator(_ => throw new Exception("nem szabadna újra feldolgozni"));
            await secondCoordinator.RunAsync(settings, new Progress<RunProgress>(secondReports.Add), CancellationToken.None);

            Assert.Empty(secondReports); // nincs feldolgozandó fájl -> nincs progress-jelentés
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_TwoFilesWithSameGeneratedTitle_StayUniqueViaOriginalName()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "első fájl szövege");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "második fájl szövege");
            string logDir = Path.Combine(dir, "napló");
            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 4, Recursive: false, DryRun: false);

            // Mindkét fájl ugyanazt a "cím"-et kapja a fake LLM-től. Ütközés-utótag helyett az
            // eredeti név különbözteti meg őket — ez a valódi futásban 466 fájlt érintett volna.
            await new RunCoordinator(Llm("Ugyanaz A Cím"))
                .RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(dir, "Ugyanaz A Cím (a).txt")));
            Assert.True(File.Exists(Path.Combine(dir, "Ugyanaz A Cím (b).txt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DescriptiveFileName_IsKeptAndNeverProcessed()
    {
        string dir = NewTempDir();
        try
        {
            string descriptive = Path.Combine(dir, "Kezelési útmutató.txt");
            string opaque = Path.Combine(dir, "P-00001233.txt");
            File.WriteAllText(descriptive, "beszédes nevű fájl tartalma");
            File.WriteAllText(opaque, "vak nevű fájl tartalma");
            string logDir = Path.Combine(dir, "napló");
            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);

            // Ha a beszédes nevű fájl mégis eljutna a feldolgozásig, ez a callback elszáll —
            // a kapunak a kinyerés ELŐTT kell fognia (ez spórolja meg az LLM-hívást is).
            Func<LlmRequest, Task<LlmResult>> llm = request => request.DocumentText.Contains("beszédes")
                ? throw new Exception("a beszédes nevű fájlt fel sem szabad dolgozni")
                : Task.FromResult(LlmResult.FromTitle("Generált cím a vak fájlhoz"));

            await new RunCoordinator(llm).RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None);

            Assert.True(File.Exists(descriptive));                                            // érintetlen maradt
            Assert.True(File.Exists(Path.Combine(dir, "Generált cím a vak fájlhoz (P-00001233).txt"))); // a vak nevűt átneveztük
            Assert.False(File.Exists(opaque));

            var entries = TealDocAiTool.Engine.RenameLog.ReadAll(Path.Combine(logDir, "tealdoc_naplo.csv"));
            Assert.Contains(entries, e => e.OriginalPath == descriptive && e.Method == RenameMethod.Kept);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SecondRun_SkipsPreviouslyKeptFiles()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Kezelési útmutató.txt"), "szöveg");
            string logDir = Path.Combine(dir, "napló");
            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);
            var coordinator = new RunCoordinator(Llm("Idáig nem szabadna eljutni"));

            await coordinator.RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None);

            var secondReports = new List<RunProgress>();
            await coordinator.RunAsync(settings, new Progress<RunProgress>(secondReports.Add), CancellationToken.None);

            // Egy megszakadt futás folytatásakor a már megtartott fájlokat sem vizsgáljuk újra.
            Assert.Empty(secondReports);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // C-1 / S4: ha a napló nem írható (pl. Excelben nyitva), a futás el sem indul — korábban a
    // fájlok átneveződtek, a naplóba viszont nem kerültek be, így a Visszavonás sem látta őket.
    [Fact]
    public async Task RunAsync_LogLockedByAnotherProgram_DoesNotStartAndRenamesNothing()
    {
        string dir = NewTempDir();
        try
        {
            string filePath = Path.Combine(dir, "P-00012.txt");
            File.WriteAllText(filePath, "valamilyen dokumentum szöveg a cím-generáláshoz");
            string logDir = Path.Combine(dir, "napló");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "tealdoc_naplo.csv");
            File.WriteAllText(logPath, "");
            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);

            using (new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read)) // mint az Excel
            {
                await Assert.ThrowsAsync<IOException>(() => new RunCoordinator(Llm("Hidraulikus tömlő cseréje"))
                    .RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None));
            }

            Assert.True(File.Exists(filePath)); // egyetlen fájlhoz sem nyúlt
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // T-20: egyfájlos mód (ráhúzás) — a javaslat még NEM nevez át; jóváhagyás után átnevez,
    // ugyanabba a naplóba ír, és a Visszavonás visszaállítja.
    [Fact]
    public async Task ProposeThenApply_RenamesOnlyAfterApprovalAndIsUndoable()
    {
        string dir = NewTempDir();
        try
        {
            string file = Path.Combine(dir, "P-00051.txt");
            File.WriteAllText(file, "valamilyen dokumentum szöveg");

            var proposal = await new RunCoordinator(Llm("Kezelési útmutató")).ProposeAsync(file, CancellationToken.None);
            Assert.Equal("Kezelési útmutató (P-00051).txt", proposal.NewFileName);
            Assert.True(File.Exists(file)); // a javaslat még nem nevezett át

            string logDir = Path.Combine(dir, "napló");
            RunCoordinator.ApplyProposal(proposal, logDir);
            Assert.True(File.Exists(Path.Combine(dir, "Kezelési útmutató (P-00051).txt")));

            Assert.Equal(1, new UndoService().Undo(TealDocAiTool.Engine.RenameLog.PathIn(logDir)).Restored);
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // C-12: egy jogosultság miatt elérhetetlen almappa korábban az egész futást elvitte.
    [Fact]
    public async Task RunAsync_InaccessibleSubfolder_IsSkippedAndTheRunCompletes()
    {
        string dir = NewTempDir();
        string locked = Path.Combine(dir, "tiltott");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(dir, "P-00031.txt"), "elérhető dokumentum szövege");
        var info = new DirectoryInfo(locked);
        var acl = info.GetAccessControl();
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            var settings = new RunSettings(dir, Path.Combine(dir, "napló"), "http://localhost:11434", "qwen3:8b",
                ExtractionWorkers: 2, Recursive: true, DryRun: false);

            await new RunCoordinator(Llm("Hidraulikus tömlő cseréje"))
                .RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(dir, "Hidraulikus tömlő cseréje (P-00031).txt")));
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_LockedFile_LogsFailedAndKeepsProcessingOthers()
    {
        string dir = NewTempDir();
        try
        {
            // Vak fájlnevek: különben a "már beszédes név" kapu kihagyná őket, és nem jutnánk
            // el az átnevezésig, amit ez a teszt vizsgál.
            string lockedPath = Path.Combine(dir, "P-00013.txt");
            string freePath = Path.Combine(dir, "P-00014.txt");
            File.WriteAllText(lockedPath, "zárolt fájl szövege a cím-generáláshoz");
            File.WriteAllText(freePath, "szabad fájl szövege a cím-generáláshoz");
            string logDir = Path.Combine(dir, "napló");
            var settings = new RunSettings(dir, logDir, "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);

            // Tartalom alapján külön cím -> nincs ütközés, ami elfedhetné a zárolás-hibát.
            Func<LlmRequest, Task<LlmResult>> llm = request =>
                Task.FromResult(LlmResult.FromTitle(request.DocumentText.Contains("zárolt") ? "Zárolt dokumentum" : "Szabad dokumentum"));

            // FileShare.Read: az extractor OLVASHATJA a fájlt (a File.ReadAllText is FileShare.Read),
            // de az átnevezéshez DELETE hozzáférés kell, amit ez a share-mód nem enged -> a File.Move
            // megosztási-hibával (IOException) elbukik. Így pont az átnevezés-hibát teszteljük, nem a kinyerést.
            using (var lockHandle = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await new RunCoordinator(llm)
                    .RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None);
            }

            // A szabad fájl átnevezve; a köteg NEM szakadt meg a zárolt fájl miatt.
            Assert.True(File.Exists(Path.Combine(dir, "Szabad dokumentum (P-00014).txt")));
            // A zárolt fájl az eredeti nevén maradt (nem sikerült átnevezni).
            Assert.True(File.Exists(lockedPath));

            // A napló Failed bejegyzést tartalmaz a zárolt fájlra.
            string logPath = Path.Combine(logDir, "tealdoc_naplo.csv");
            var entries = TealDocAiTool.Engine.RenameLog.ReadAll(logPath);
            Assert.Contains(entries, e => e.OriginalPath == lockedPath && e.Method == RenameMethod.Failed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
