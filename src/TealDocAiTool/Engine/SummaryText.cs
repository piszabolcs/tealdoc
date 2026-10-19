using System.Text.RegularExpressions;

namespace TealDocAiTool.Engine;

/// <summary>
/// A fájl-leírás megtisztítása, mielőtt a dokumentumba kerül.
///
/// Két valódi hibát javít, amelyet egy 3343 leírásos futás átvilágítása hozott elő:
/// a modell néha a prompt utolsó szavával kezdi a választ („Az összefoglaló:  Kezelési
/// utasítás…"), és néha sortöréseket hagy benne. Egyik sem tartalmi hiba, de a listában
/// szemétnek látszik, és a keresés is a felesleges szavakat indexeli.
/// </summary>
public static class SummaryText
{
    /// <summary>A prompt záró szava, amit a modell visszhangozhat. Csak a szöveg ELEJÉN vágjuk —
    /// a mondat közepén szereplő „összefoglaló" szó valódi tartalom lehet.</summary>
    private static readonly Regex EchoedLabel = new(
        @"^\s*(az\s+)?(összefoglaló|összefoglalás|leírás|válasz)\s*:\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string text = EchoedLabel.Replace(raw, "");
        // A sortörés a felsorolásban törné a sort; a leírás egyetlen bekezdés.
        text = Whitespace.Replace(text, " ").Trim();
        // Az idézőjelbe zárt válasz is előfordul; a záró-nyitó pár nélkül nem nyúlunk hozzá.
        if (text.Length > 1 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1].Trim();
        return text;
    }
}
