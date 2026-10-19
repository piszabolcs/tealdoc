using TealDocAiTool.Extractors;

namespace TealDocAiTool.Tests.Extractors;

public class ExtractorRegistryTests
{
    [Theory]
    [InlineData("minta.pdf")]
    [InlineData("minta.docx")]
    [InlineData("minta.doc")]
    [InlineData("minta.xlsx")]
    [InlineData("minta.xls")]
    [InlineData("minta.rtf")]
    [InlineData("minta.txt")]
    [InlineData("minta.csv")]
    public void FindFor_ReturnsAnExtractorForSupportedTypes(string fileName)
    {
        Assert.NotNull(new ExtractorRegistry().FindFor(fileName));
    }

    [Theory]
    [InlineData("minta.msg")]
    [InlineData("minta.rar")]
    [InlineData("minta.7z")]
    [InlineData("minta.ppt")]
    [InlineData("minta.pptx")]
    public void FindFor_ReturnsNullForOutOfScopeTypes(string fileName)
    {
        Assert.Null(new ExtractorRegistry().FindFor(fileName));
    }
}
