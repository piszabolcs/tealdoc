namespace TealDocAiTool.Models;

public sealed record TitleDecision(bool IsResolved, string? Title, RenameMethod Method, string? FailureReason)
{
    public static TitleDecision Success(string title, RenameMethod method) => new(true, title, method, null);
    public static TitleDecision Failed(string reason) => new(false, null, RenameMethod.Failed, reason);
    public static TitleDecision Unsupported() => new(false, null, RenameMethod.Skipped, "Nem támogatott fájltípus.");
    public static TitleDecision Kept() => new(false, null, RenameMethod.Kept, "Az eredeti név már beszédes.");
}
