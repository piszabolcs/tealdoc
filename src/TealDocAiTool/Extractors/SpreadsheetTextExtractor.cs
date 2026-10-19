using System.IO;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using TealDocAiTool.Models;

namespace TealDocAiTool.Extractors;

/// <summary>xls/xlsx/xlsm — heurisztika: az 1. munkalap első néhány nem üres cellája,
/// dokumentum-sorrendben (nem metaadat-Title, mert az valós fájloknál gyakran üres/"Munka1"
/// jellegű szemét — élő tapasztalat alapján döntve).</summary>
public sealed class SpreadsheetTextExtractor : IDocumentExtractor
{
    private static readonly string[] SupportedExtensions = { ".xls", ".xlsx", ".xlsm" };
    private const int MaxCandidates = 5;

    public bool CanHandle(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());

    public ExtractedDocument? Extract(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        IWorkbook workbook = Path.GetExtension(filePath).Equals(".xls", StringComparison.OrdinalIgnoreCase)
            ? new HSSFWorkbook(stream)
            : new XSSFWorkbook(stream);

        if (workbook.NumberOfSheets == 0) return null;
        ISheet sheet = workbook.GetSheetAt(0);

        var candidates = new List<string>();
        for (int r = sheet.FirstRowNum; r <= sheet.LastRowNum && candidates.Count < MaxCandidates; r++)
        {
            IRow? row = sheet.GetRow(r);
            if (row is null) continue;

            for (int c = row.FirstCellNum; c < row.LastCellNum && candidates.Count < MaxCandidates; c++)
            {
                NPOI.SS.UserModel.ICell? cell = row.GetCell(c);
                if (cell is null) continue;

                string value = (cell.CellType == CellType.String ? cell.StringCellValue : cell.ToString() ?? "").Trim();
                if (value.Length > 0) candidates.Add(value);
            }
        }

        if (candidates.Count == 0) return null;
        return new ExtractedDocument(string.Join("\n", candidates), candidates);
    }
}
