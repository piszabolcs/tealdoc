using DemoCorpus;
using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

/// <summary>
/// Végpontok közti próba a kitalált demó-archívumon (tools/DemoCorpus): valódi PDF/DOCX/XLSX
/// kinyerés, heurisztika, validátor, ütközés, napló — csak az LLM hamis. A PDF-kinyerőt
/// korábban egyetlen teszt sem fedte.
/// </summary>
public class DemoCorpusTests
{
    [Fact]
    public async Task DryRun_OnTheDemoCorpus_ResolvesEachBranchAsExpected()
    {
        string root = Path.Combine(Path.GetTempPath(), "tealdoc_demo_" + Guid.NewGuid().ToString("N"));
        string logDir = root + "_naplo";
        try
        {
            DemoCorpusGenerator.Generate(root);
            var settings = new RunSettings(root, logDir, "http://localhost:11434", "qwen3:8b",
                ExtractionWorkers: 4, Recursive: true, DryRun: true);

            await new RunCoordinator(_ => Task.FromResult(LlmResult.FromTitle("Műszakátadási jegyzőkönyv")))
                .RunAsync(settings, new Progress<RunProgress>(_ => { }), CancellationToken.None);

            var byName = RenameLog.ReadAll(RenameLog.PathIn(logDir)).ToDictionary(e => Path.GetFileName(e.OriginalPath));
            Assert.Equal("Kezelési útmutató – CX-200 eszterga (scan_0001).pdf", byName["scan_0001.pdf"].NewFileName);
            Assert.Equal("Alkatrészlista – CX-200 főorsó egység (1234567).xlsx", byName["1234567.xlsx"].NewFileName);
            Assert.Equal(RenameMethod.Kept, byName["Kezelési útmutató CX-200.pdf"].Method);
            Assert.Equal(RenameMethod.Failed, byName["img_0042.pdf"].Method);   // nincs szövegréteg
            Assert.Equal(RenameMethod.Failed, byName["scan_0002.pdf"].Method);  // sérült PDF
            Assert.NotEqual(byName["P-00011.docx"].NewFileName, byName["P-00012.docx"].NewFileName);
            Assert.StartsWith("Megfelelőségi nyilatkozat", byName["doc_0001.docx"].NewFileName); // elérhetőség kiszűrve
            Assert.DoesNotContain('‮', byName["doc_0002.docx"].NewFileName);
            Assert.Equal(RenameMethod.Llm, byName["jk_0007.txt"].Method);
            Assert.All(Directory.GetFiles(root, "*", SearchOption.AllDirectories), f =>
                Assert.Contains(Path.GetFileName(f), byName.Keys)); // dry-run: semmi nem nevezett át
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (Directory.Exists(logDir)) Directory.Delete(logDir, recursive: true);
        }
    }
}
