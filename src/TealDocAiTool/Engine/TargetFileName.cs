using System.IO;

namespace TealDocAiTool.Engine;

/// <summary>
/// Összeállítja az új fájlnevet: <c>cím (eredeti név)</c>.
/// Az eredeti nevet SOSEM dobjuk el, mert vak névnél az rendszerint cikkszám vagy rajzszám —
/// a SAP-ban, a rendelésen és a kézikönyvben az szerepel, tehát a legfontosabb keresőkulcs.
/// Ez oldja meg az ütközéseket is: egy valódi, éles futásban 51 fájl kapta volna a "BRACKET"
/// címet (a rajzokon az alkatrésznév leíró, de nem egyedi) — a rajzszámmal együtt viszont
/// mindegyik egyedi és beszédes marad.
/// </summary>
public static class TargetFileName
{
    private const int MaxFileNameLength = 100; // a valódi archívum mappái mélyek, hagyunk helyet az útvonalnak
    private const int MinTitleRoom = 15;       // ennél kevesebb hely már nem ér semmit
    private const int MinRepeatCheckLength = 6; // ennél rövidebb nevet nem tekintünk cikkszámnak
    private const int MinTrailingCodeLength = 5; // ennél rövidebb egyezés lehet véletlen szám a címben
    private const int MinTitleAfterCut = 4;      // a vágás után is maradjon értelmes cím

    public static string Build(string title, string originalPath)
    {
        string original = Path.GetFileNameWithoutExtension(originalPath);
        title = CutTrailingPartNumber(title, original);

        // A modell néha magától beleírja a rajzszámot a címbe — ne szerepeljen kétszer.
        // A hosszkorlát azért kell, mert egy rövid „eredeti név" (pl. "a") véletlenül is
        // előfordul bármelyik címben; a valódi cikkszámok ennél jóval hosszabbak.
        if (original.Length >= MinRepeatCheckLength && title.Contains(original, StringComparison.OrdinalIgnoreCase))
            return title;

        string suffix = $" ({original})";
        int room = MaxFileNameLength - suffix.Length - Path.GetExtension(originalPath).Length;

        // Hosszú eredeti név mellett nem fér bele mindkettő. Ilyenkor a cím marad — ez a helyzet
        // a gyakorlatban alig fordul elő, mert a hosszú név rendszerint beszédes (-> Kept).
        if (room < MinTitleRoom) return title;

        return title.Length <= room
            ? title + suffix
            : title[..room].TrimEnd() + suffix;
    }

    /// <summary>
    /// A modell a cím VÉGÉRE biggyeszti a rajzszámot, hiába tiltja a prompt — és gyakran csonkán,
    /// mert a 8 szavas korlát elvágja ("Camera holder-P-0000" a "P-00001388"-hoz). A zárójeles
    /// utótag úgyis hozzáteszi a teljes számot, tehát a csonk csak zaj: egy P-számos rajzmappában
    /// az LLM-címek 22,6%-a nézett így ki. A leghosszabb egyezéssel kezdünk, hogy a teljes
    /// rajzszámot vágjuk le, ne csak a felét.
    /// </summary>
    private static string CutTrailingPartNumber(string title, string original)
    {
        for (int length = Math.Min(original.Length, title.Length); length >= MinTrailingCodeLength; length--)
        {
            if (!title.EndsWith(original[..length], StringComparison.OrdinalIgnoreCase)) continue;

            string cut = title[..^length].TrimEnd(' ', '-', '_', '.', ',', ':');
            // Ha a cím gyakorlatilag MAGA a cikkszám, a vágás után nem maradna semmi értelmes.
            return cut.Length >= MinTitleAfterCut ? cut : title;
        }

        return title;
    }
}
