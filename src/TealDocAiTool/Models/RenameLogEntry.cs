namespace TealDocAiTool.Models;

/// <param name="RunId">Melyik futás írta a sort. Ez teszi lehetővé az „utolsó futás visszavonását”.
/// A régi (e mező előtti) naplósoroknál üres.</param>
public sealed record RenameLogEntry(
    DateTimeOffset Timestamp,
    string OriginalPath,
    string NewFileName,
    RenameMethod Method,
    string Detail,
    bool DryRun,
    string RunId = "");
