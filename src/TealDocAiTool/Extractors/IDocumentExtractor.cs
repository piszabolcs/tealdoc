using TealDocAiTool.Models;

namespace TealDocAiTool.Extractors;

public interface IDocumentExtractor
{
    bool CanHandle(string filePath);

    /// <summary>Null, ha a fájl nem nyitható meg vagy nincs benne kinyerhető szöveg.</summary>
    ExtractedDocument? Extract(string filePath);
}
