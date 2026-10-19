namespace TealDocAiTool.Models;

public enum RenameMethod
{
    Heuristic,
    Llm,
    /// <summary>Az eredeti fájlnév már beszédes volt — szándékosan nem nyúltunk hozzá.</summary>
    Kept,
    Skipped,
    Failed,
    /// <summary>Egy korábbi átnevezés visszavonva (a fájl visszakapta az eredeti nevét). A sor
    /// ugyanazt az eredeti utat és új nevet hordozza, mint a visszavont bejegyzés.</summary>
    Undone,
}
