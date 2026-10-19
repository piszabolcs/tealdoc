using System.IO;
using System.Text;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Egy gép kimeneti fájlneve — kiterjesztés NÉLKÜL. Közös, mert ugyanaz a gép ugyanazt a nevet
/// kell kapja .md-ként és .docx-ként is; a névütközés elleni védelem sem lehet formátumfüggő.
/// A fájlnév maga is beszédes (gépszám + gépnév), mert a kereső a fájlnevet erősen súlyozza.
/// </summary>
public static class MachineFileName
{
    private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();

    public static string For(MachineIndexRow row)
    {
        string baseName = string.IsNullOrWhiteSpace(row.Number) ? row.Name : $"{row.Number} {row.Name}";
        return UniqueFileName(Sanitize(baseName), row.Title);
    }

    /// <summary>
    /// Az egység mappája a gyökérhez képest. Az egységek a gyökér közvetlen almappái, ezért ez a
    /// mappa neve — a teljes helyi út (benne a Windows-felhasználónévvel) így nem kerül a kimenetbe.
    /// </summary>
    public static string RelativeFolder(MachineIndexRow row) =>
        Path.GetFileName(row.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// Ütközés-védelem. A gépmappák nevei a fájlrendszer miatt szükségszerűen egyediek, a belőlük
    /// KÉPZETT fájlnév viszont nem: a „1401 - X" és a „1401 X" ugyanarra a „1401 X"-re
    /// parse-olódik, és a <see cref="Sanitize"/> is összemoshat két nevet („A/B" és „A_B").
    /// Ilyenkor az egyik gép fájlja NÉMÁN felülírná a másikét.
    ///
    /// A megoldás sorrendtől FÜGGETLEN: nem számoljuk, hányadik ütközés ez (az a felfedezési
    /// sorrendtől függne, és egy folytatott futás más nevet adna), hanem abból indulunk ki, hogy
    /// a nyers mappanév egyedi. Ha a fájlnév eltér tőle, a leképezés veszteséges → a nyers névből
    /// számolt rövid, stabil lenyomatot fűzünk hozzá.
    /// </summary>
    private static string UniqueFileName(string safeName, string rawFolderName) =>
        string.Equals(safeName, rawFolderName, StringComparison.Ordinal)
            ? safeName
            : safeName + "~" + ShortHash(rawFolderName);

    private static string ShortHash(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 3).ToLowerInvariant();
    }

    private static string Sanitize(string name)
    {
        var cleaned = new StringBuilder(name.Length);
        foreach (char c in name)
            cleaned.Append(Array.IndexOf(InvalidFileChars, c) >= 0 ? '_' : c);
        string result = cleaned.ToString().Trim();
        // Windows nem tűri a záró pontot/szóközt fájlnév végén.
        result = result.TrimEnd('.', ' ');
        return result.Length == 0 ? "gep" : result;
    }
}
