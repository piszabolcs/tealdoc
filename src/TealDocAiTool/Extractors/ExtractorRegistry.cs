namespace TealDocAiTool.Extractors;

public sealed class ExtractorRegistry
{
    private readonly IReadOnlyList<IDocumentExtractor> _extractors;

    public ExtractorRegistry() : this(new IDocumentExtractor[]
    {
        new PdfTextExtractor(),
        new WordTextExtractor(),
        new SpreadsheetTextExtractor(),
        new PlainTextExtractor(),
        new RtfTextExtractor(),
    })
    { }

    /// <summary>Tesztekhez — fake extractor-listával, a valódi PdfPig/NPOI/Word-kód nélkül
    /// (lásd Task 12: TitleEngineTests).</summary>
    public ExtractorRegistry(IReadOnlyList<IDocumentExtractor> extractors)
    {
        _extractors = extractors;
    }

    /// <summary>Null, ha a kiterjesztés nincs ebben a körben lefedve (pl. .msg, .rar, .7z,
    /// .ppt/.pptx) — ezeket a hívó "nem támogatott"-ként naplózza, a fájl érintetlen marad.</summary>
    public IDocumentExtractor? FindFor(string filePath) =>
        _extractors.FirstOrDefault(e => e.CanHandle(filePath));
}
