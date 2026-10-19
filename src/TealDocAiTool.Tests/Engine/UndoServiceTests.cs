using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

public class UndoServiceTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"tealdoc_undo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Undo_RestoresRenamedFileToOriginalName()
    {
        string dir = NewTempDir();
        try
        {
            string originalPath = Path.Combine(dir, "123.pdf");
            string newPath = Path.Combine(dir, "Phoenix Contact relé kézikönyv.pdf");
            File.WriteAllText(newPath, "tartalom");

            string logPath = Path.Combine(dir, "napló.csv");
            RenameLog.Append(logPath, new RenameLogEntry(
                DateTimeOffset.Now, originalPath, "Phoenix Contact relé kézikönyv.pdf",
                RenameMethod.Heuristic, "", false));

            var result = new UndoService().Undo(logPath);

            Assert.Equal(1, result.Restored);
            Assert.True(File.Exists(originalPath));
            Assert.False(File.Exists(newPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Undo_SkipsEntryWhenRenamedFileNoLongerExists()
    {
        string dir = NewTempDir();
        try
        {
            string originalPath = Path.Combine(dir, "123.pdf");
            string logPath = Path.Combine(dir, "napló.csv");
            // a "Cím.pdf" sosem jött létre ténylegesen (pl. kézzel törölték utólag)
            RenameLog.Append(logPath, new RenameLogEntry(
                DateTimeOffset.Now, originalPath, "Cím.pdf", RenameMethod.Heuristic, "", false));

            var result = new UndoService().Undo(logPath);

            Assert.Equal(0, result.Restored);
            Assert.Equal(1, result.SkippedMissing);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static RunSettings LiveRun(string dir) =>
        new(dir, Path.Combine(dir, "napló"), "http://localhost:11434", "qwen3:8b", ExtractionWorkers: 2, Recursive: false, DryRun: false);

    private static Task RunWithTitle(string dir, string title) =>
        new RunCoordinator(_ => Task.FromResult(LlmResult.FromTitle(title)))
            .RunAsync(LiveRun(dir), new Progress<RunProgress>(_ => { }), CancellationToken.None);

    // C-4 / S3: a visszavonás jelöli a naplóban, mit állított vissza — a második Visszavonásnak
    // nincs mit ígérnie, az újrafuttatás pedig a visszaállított fájlokat ismét feldolgozza.
    [Fact]
    public async Task Undo_MarksRevertedEntries_SecondUndoFindsNothingAndRerunProcessesAgain()
    {
        string dir = NewTempDir();
        try
        {
            for (int i = 1; i <= 3; i++) File.WriteAllText(Path.Combine(dir, $"P-0001{i}.txt"), $"dokumentum {i} szövege");
            await RunWithTitle(dir, "Szerelési útmutató");
            string logPath = RenameLog.PathIn(Path.Combine(dir, "napló"));

            var first = new UndoService().Undo(logPath);
            var second = new UndoService().Undo(logPath);

            Assert.Equal(3, first.Restored);
            Assert.True(File.Exists(Path.Combine(dir, "P-00011.txt")));
            Assert.Equal(0, second.Restored + second.SkippedMissing);
            Assert.Empty(RenameLog.ActiveRenames(RenameLog.ReadAll(logPath)));

            await RunWithTitle(dir, "Szerelési útmutató");

            Assert.True(File.Exists(Path.Combine(dir, "Szerelési útmutató (P-00013).txt")));
            Assert.Equal(3, RenameLog.ActiveRenames(RenameLog.ReadAll(logPath)).Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Undo_LastRunOnly_KeepsEarlierRunsRenamed()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "P-00021.txt"), "első futás dokumentuma");
            await RunWithTitle(dir, "Kezelési útmutató");
            File.WriteAllText(Path.Combine(dir, "P-00022.txt"), "második futás dokumentuma");
            await RunWithTitle(dir, "Kezelési útmutató");
            string logPath = RenameLog.PathIn(Path.Combine(dir, "napló"));

            string lastRun = RenameLog.LastRunId(RenameLog.ReadAll(logPath))!;
            var result = new UndoService().Undo(logPath, lastRun);

            Assert.Equal(1, result.Restored);
            Assert.True(File.Exists(Path.Combine(dir, "P-00022.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "Kezelési útmutató (P-00021).txt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // S-9: egy kézzel szerkesztett napló ne mozgathasson tetszőleges fájlt.
    [Fact]
    public void Undo_RejectsEntryWhoseNewNameContainsAPath()
    {
        string dir = NewTempDir();
        try
        {
            string victim = Path.Combine(dir, "masik", "fontos.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(victim)!);
            File.WriteAllText(victim, "nem szabad elmozdítani");
            string logPath = Path.Combine(dir, "napló.csv");
            RenameLog.Append(logPath, new RenameLogEntry(
                DateTimeOffset.Now, Path.Combine(dir, "a.pdf"), @"masik\fontos.txt", RenameMethod.Heuristic, "", false));

            var result = new UndoService().Undo(logPath);

            Assert.Single(result.Errors);
            Assert.True(File.Exists(victim));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Undo_IgnoresDryRunEntries()
    {
        string dir = NewTempDir();
        try
        {
            string logPath = Path.Combine(dir, "napló.csv");
            RenameLog.Append(logPath, new RenameLogEntry(
                DateTimeOffset.Now, Path.Combine(dir, "123.pdf"), "Cím.pdf", RenameMethod.Heuristic, "", DryRun: true));

            var result = new UndoService().Undo(logPath);

            Assert.Equal(0, result.Restored);
            Assert.Equal(0, result.SkippedMissing);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
