using TealDocAiTool.Common;

namespace TealDocAiTool.Tests.Common;

public class InputParsingTests
{
    // C-15: a nem szám vagy a 0 korábban csendben alapértékre esett, felső korlát nem volt.
    [Theory]
    [InlineData("6", 6, false)]
    [InlineData(" 6 ", 6, false)]
    [InlineData("abc", 4, true)]
    [InlineData("0", 1, true)]
    [InlineData("500", 32, true)]
    public void ParseClamped_ClampsAndReportsAdjustments(string text, int expected, bool expectedAdjusted)
    {
        int value = InputParsing.ParseClamped(text, min: 1, max: 32, fallback: 4, out bool adjusted);

        Assert.Equal(expected, value);
        Assert.Equal(expectedAdjusted, adjusted);
    }
}
