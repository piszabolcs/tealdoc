using TealDocAiTool.Extractors;

namespace TealDocAiTool.Tests.Extractors;

public class RtfTextExtractorTests
{
    [Fact]
    public void CanHandle_AcceptsRtfExtension()
    {
        Assert.True(new RtfTextExtractor().CanHandle("minta.rtf"));
    }

    [Fact]
    public void Extract_StripsControlWordsAndDecodesHungarianAccents()
    {
        // \'e9 = é, \'f6 = ö Windows-1250-ben — élőben leellenőrizve (ne cseréld vissza space-re!).
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.rtf");
        File.WriteAllText(path, @"{\rtf1\ansi\deff0 {\fonttbl{\f0 Arial;}} \f0\fs24 Phoenix Contact rel\'e9 k\'e9zik\'f6nyv}");
        try
        {
            var result = new RtfTextExtractor().Extract(path);
            Assert.NotNull(result);
            Assert.Contains("Phoenix Contact relé kézikönyv", result!.FullText);
            Assert.DoesNotContain("Arial", result.FullText);
            Assert.DoesNotContain(@"\rtf1", result.FullText);
            Assert.Empty(result.HeuristicCandidates);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // C-6: korábban minden `\`-nél a teljes hátralévő szöveg másolódott (O(n²)): 400 KB ~4 s volt,
    // 4 MB percekig tartott volna. Egy több betűtípusos tábla második neve sem szivároghat be.
    [Fact]
    public void Extract_LargeRtfWithEmbeddedPicture_IsLinearAndSkipsNoise()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.rtf");
        var rtf = new System.Text.StringBuilder(@"{\rtf1\ansi {\fonttbl{\f0 Arial;}{\f1 Courier New;}}{\pict\pngblip ");
        for (int i = 0; i < 4 * 1024 * 1024 / 64; i++) rtf.Append(@"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abc\par");
        rtf.Append(@"}\f0 Karbantart\'e1si utas\'edt\'e1s \par v\'e9ge}");
        File.WriteAllText(path, rtf.ToString());
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new RtfTextExtractor().Extract(path);
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"túl lassú: {stopwatch.Elapsed}");
            Assert.Equal("Karbantartási utasítás\nvége", result!.FullText); // a \par sortörés marad
        }
        finally
        {
            File.Delete(path);
        }
    }
}
