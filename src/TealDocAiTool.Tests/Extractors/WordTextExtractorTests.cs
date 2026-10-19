using NPOI.XWPF.UserModel;
using TealDocAiTool.Extractors;

namespace TealDocAiTool.Tests.Extractors;

public class WordTextExtractorTests
{
    [Theory]
    [InlineData(".doc")]
    [InlineData(".docx")]
    [InlineData(".docm")]
    public void CanHandle_AcceptsSupportedExtensions(string ext)
    {
        Assert.True(new WordTextExtractor().CanHandle($"minta{ext}"));
    }

    [Fact]
    public void Extract_Docx_ReturnsFirstParagraphsAsCandidates()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.docx");
        var doc = new XWPFDocument();
        doc.CreateParagraph().CreateRun().SetText("Phoenix Contact relé kézikönyv");
        doc.CreateParagraph().CreateRun().SetText("második bekezdés");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) doc.Write(fs);

        try
        {
            var result = new WordTextExtractor().Extract(path);
            Assert.NotNull(result);
            Assert.Equal("Phoenix Contact relé kézikönyv", result!.HeuristicCandidates[0]);
            Assert.Contains("második bekezdés", result.FullText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Extract_Docx_SkipsEmptyLeadingParagraphs()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.docx");
        var doc = new XWPFDocument();
        doc.CreateParagraph(); // üres bekezdés
        doc.CreateParagraph().CreateRun().SetText("Beszállítói szerződés");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) doc.Write(fs);

        try
        {
            var result = new WordTextExtractor().Extract(path);
            Assert.NotNull(result);
            Assert.Equal("Beszállítói szerződés", result!.HeuristicCandidates[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Extract_WhenWordMarkedUnresponsive_SkipsLegacyDocImmediately()
    {
        // Ha egy .doc egyszer megakasztotta a Wordöt (modális ablak, sérült fájl), a további
        // .doc-okat NEM próbáljuk újra: különben minden egyes fájl megvárná az időkorlátot.
        // A kihagyás OKÁT ki kell mondani: egy teljes éles futásban így 34 .doc kapott
        // "Nincs kinyerhető szöveg" bejegyzést, ami a dokumentumot vádolja a Word helyett.
        var extractor = new WordTextExtractor { WordUnresponsive = true };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var hiba = Assert.Throws<InvalidOperationException>(
            () => extractor.Extract(Path.Combine(Path.GetTempPath(), "nemletezik.doc")));
        stopwatch.Stop();

        Assert.Contains("Word", hiba.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"a Word mégis elindult? {stopwatch.Elapsed}");
        // C-17: az állapot futásonkénti — egy új futás (új kinyerő) újra megpróbálja a .doc-ot.
        Assert.False(new WordTextExtractor().WordUnresponsive);
    }

    [Fact]
    public void Extract_Docx_ReturnsNullWhenDocumentIsEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.docx");
        var doc = new XWPFDocument();
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) doc.Write(fs);

        try
        {
            Assert.Null(new WordTextExtractor().Extract(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
