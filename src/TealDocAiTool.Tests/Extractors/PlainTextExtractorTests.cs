using TealDocAiTool.Extractors;

namespace TealDocAiTool.Tests.Extractors;

public class PlainTextExtractorTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    public void CanHandle_AcceptsSupportedExtensions(string ext)
    {
        Assert.True(new PlainTextExtractor().CanHandle($"minta{ext}"));
    }

    [Fact]
    public void CanHandle_RejectsUnsupportedExtension()
    {
        Assert.False(new PlainTextExtractor().CanHandle("minta.pdf"));
    }

    [Fact]
    public void Extract_ReturnsFullTextWithoutHeuristicCandidates()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "Phoenix Contact relé hibakód lista\nrészletek...");
        try
        {
            var result = new PlainTextExtractor().Extract(path);
            Assert.NotNull(result);
            Assert.Contains("Phoenix Contact", result!.FullText);
            Assert.Empty(result.HeuristicCandidates);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Extract_ReturnsNullForEmptyFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "   ");
        try
        {
            Assert.Null(new PlainTextExtractor().Extract(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // C-11: a BOM nélküli cp1250 magyar szöveg UTF-8-ként olvasva elromlott („M?szak?tad?si”).
    [Fact]
    public void Extract_Cp1250TextWithoutBom_KeepsHungarianAccents()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, System.Text.Encoding.GetEncoding(1250).GetBytes("Műszakátadási jegyzőkönyv – hűtőkör"));
        try
        {
            Assert.Equal("Műszakátadási jegyzőkönyv – hűtőkör", new PlainTextExtractor().Extract(path)!.FullText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Csak a fájl eleje kerül beolvasásra; ha a vágás egy több bájtos UTF-8 karakter közepére esik,
    // az nem „érvénytelen UTF-8”, tehát nem válthat át tévesen cp1250-re.
    [Fact]
    public void Extract_LargeUtf8File_ReadsOnlyTheHeadAndStaysUtf8()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "Árvíztűrő tükörfúrógép;" + string.Concat(Enumerable.Repeat("ő", 2_000_000)),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            var result = new PlainTextExtractor().Extract(path)!;

            Assert.StartsWith("Árvíztűrő tükörfúrógép;őőő", result.FullText);
            Assert.Equal(4000, result.FullText.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
