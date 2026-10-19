using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using TealDocAiTool.Extractors;

namespace TealDocAiTool.Tests.Extractors;

public class SpreadsheetTextExtractorTests
{
    [Theory]
    [InlineData(".xls")]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    public void CanHandle_AcceptsSupportedExtensions(string ext)
    {
        Assert.True(new SpreadsheetTextExtractor().CanHandle($"minta{ext}"));
    }

    [Fact]
    public void Extract_ReturnsFirstNonEmptyCellAsTopHeuristicCandidate()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.xlsx");
        var workbook = new XSSFWorkbook();
        ISheet sheet = workbook.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("Phoenix Contact relé kézikönyv");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) workbook.Write(fs);

        try
        {
            var result = new SpreadsheetTextExtractor().Extract(path);
            Assert.NotNull(result);
            Assert.Equal("Phoenix Contact relé kézikönyv", result!.HeuristicCandidates[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Extract_SkipsEmptyLeadingRowsAndCells()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.xlsx");
        var workbook = new XSSFWorkbook();
        ISheet sheet = workbook.CreateSheet("Sheet1");
        sheet.CreateRow(0); // üres sor
        var row1 = sheet.CreateRow(1);
        row1.CreateCell(0); // üres cella
        row1.CreateCell(1).SetCellValue("Beszállítói szerződés");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) workbook.Write(fs);

        try
        {
            var result = new SpreadsheetTextExtractor().Extract(path);
            Assert.NotNull(result);
            Assert.Equal("Beszállítói szerződés", result!.HeuristicCandidates[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Extract_ReturnsNullWhenSheetIsEntirelyEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tealdoc_{Guid.NewGuid():N}.xlsx");
        var workbook = new XSSFWorkbook();
        workbook.CreateSheet("Sheet1");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) workbook.Write(fs);

        try
        {
            Assert.Null(new SpreadsheetTextExtractor().Extract(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
