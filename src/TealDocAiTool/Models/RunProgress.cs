namespace TealDocAiTool.Models;

public sealed record RunProgress(int Processed, int Total, string CurrentFile, string Message, RenameMethod? LastMethod);
