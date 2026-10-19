using System.IO;
using System.Text;
using UglyToad.PdfPig;
using TealDocAiTool.Models;

namespace TealDocAiTool.Extractors;

/// <summary>pdf — heurisztika: az 1. oldal legnagyobb betűméretű sora (a kézikönyvek/
/// kézikönyvek borítóján ez szinte mindig maga a cím). A betűket Y-koordináta szerint
/// sorokba csoportosítjuk, a legnagyobb PointSize-ú sort választjuk elsőnek.</summary>
public sealed class PdfTextExtractor : IDocumentExtractor
{
    // A kaszkád második köre TÖBB szöveget ad az LLM-nek, ezért itt is többet olvasunk:
    // a kézikönyveknél a tartalmi lényeg gyakran csak a 3-6. oldalon kezdődik. Az első kör
    // továbbra is csak az elejét kapja meg, tehát ez nem lassítja a gyakori eseteket.
    private const int MaxPagesForFullText = 8;
    private const int MaxFullTextChars = 12000;
    private const int MaxCandidates = 5;

    public bool CanHandle(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase);

    public ExtractedDocument? Extract(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        if (document.NumberOfPages == 0) return null;

        int pagesToRead = Math.Min(MaxPagesForFullText, document.NumberOfPages);
        var fullTextBuilder = new StringBuilder();
        for (int i = 1; i <= pagesToRead; i++)
            fullTextBuilder.AppendLine(document.GetPage(i).Text);

        var firstPage = document.GetPage(1);
        var candidates = firstPage.Letters
            .GroupBy(l => Math.Round(l.StartBaseLine.Y, 1))
            .OrderByDescending(g => g.Max(l => l.PointSize))
            .Select(g => string.Concat(g.OrderBy(l => l.StartBaseLine.X).Select(l => l.Value)).Trim())
            .Where(t => t.Length > 0)
            .Take(MaxCandidates)
            .ToList();

        string fullText = fullTextBuilder.ToString().Trim();
        if (fullText.Length == 0 && candidates.Count == 0) return null;

        return new ExtractedDocument(
            fullText.Length > MaxFullTextChars ? fullText[..MaxFullTextChars] : fullText,
            candidates);
    }
}
