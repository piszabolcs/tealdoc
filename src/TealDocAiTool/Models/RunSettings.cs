namespace TealDocAiTool.Models;

public sealed record RunSettings(
    string TargetFolder,
    string LogFolder,
    string OllamaHost,
    string OllamaModel,
    int ExtractionWorkers,
    bool Recursive,
    bool DryRun);
