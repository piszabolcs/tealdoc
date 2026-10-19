using System.Text;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// A dokumentum-felsorolást állítja elő a .md-be.
///
/// Miért külön osztály: a felsorolást a generálás UTÁN is elő kell tudni állítani. A kész gépeket
/// gyorsítótárazzuk, és egy folytatott futás onnan írja a .md-t — ha a hivatkozás-építés csak a
/// generálás közben létezne, a gyorsítótárból visszatöltött gépek örökre hivatkozás nélkül
/// maradnának, holott az újragenerálásuk órányi LLM-idő.
/// </summary>
public static class DocumentListRenderer
{
    public static string Render(IEnumerable<MachineDocument> docs, string machineUrl, bool linkEachDocument)
    {
        bool withLinks = linkEachDocument && !string.IsNullOrWhiteSpace(machineUrl);
        var sb = new StringBuilder();
        foreach (var d in docs)
        {
            sb.Append("- ");
            if (withLinks)
                sb.Append('[').Append(EscapeLinkText(d.FileName)).Append("](")
                    .Append(SharePointUrlBuilder.Combine(machineUrl, d.RelativePath)).Append(')');
            else
                sb.Append(d.FileName);
            if (!string.IsNullOrWhiteSpace(d.Description)) sb.Append(" — ").Append(d.Description);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>A szögletes zárójel a link SZÖVEGÉBEN elvágná a Markdown-hivatkozást — márpedig
    /// fájlnévben előfordul (pl. „Rajz [rev2].pdf”).</summary>
    private static string EscapeLinkText(string text) =>
        text.Replace("[", "\\[").Replace("]", "\\]");
}
