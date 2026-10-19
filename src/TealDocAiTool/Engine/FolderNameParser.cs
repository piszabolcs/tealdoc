using System.Text.RegularExpressions;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Egy gépmappa nevét bontja szét gépszámra és gépnévre („1401 MINTAGYARTO VARIO 20"). A vezető
/// szám a legfontosabb keresőkulcs, ezért külön tokenként tartjuk. Ha a mappanév nem ilyen alakú,
/// a szám üres, a név a teljes mappanév marad.
/// </summary>
public static class FolderNameParser
{
    private static readonly Regex LeadingCode =
        new(@"^\s*(?<num>\d{2,6}[A-Za-z]?)\s*[-_.]?\s+(?<name>.+?)\s*$", RegexOptions.Compiled);

    /// <summary>A gyártósorok jele 1 betű + 2 számjegy (`T05`, `C28`), esetleg tartományként
    /// (`T17-18-19`). Ez ugyanolyan erős keresőkulcs, mint a gépszám, ezért külön tokenként
    /// tartjuk — a „mi az a T05?" kérdés csak így talál célba.</summary>
    /// <summary>Betűvel kezdődő leltári gépszám: `M0100009`, `C3000007`, `G5100001`. Vesszővel
    /// több is állhat egymás mellett (`M1200011, M1200012 DEMO HMC500`) — ilyenkor az egész
    /// felsorolás az azonosító, mert mindegyikre hivatkozhat a kereső.</summary>
    private static readonly Regex PrefixedCode =
        new(@"^\s*(?<num>[A-Za-z]\d{4,}(?:\s*,\s*[A-Za-z]\d{4,})*)\s*[-_.]?\s+(?<name>.+?)\s*$", RegexOptions.Compiled);

    private static readonly Regex LineCode =
        new(@"^\s*(?<num>[A-Za-z]\d{2}(?:-\d{2})*)(?![0-9])\s*[-_.]?\s+(?<name>.+?)\s*$", RegexOptions.Compiled);

    public static MachineIdentity Parse(string folderName)
    {
        string raw = folderName.Trim();

        // A sorkód ELŐBB: a „C28 teszter" kódja C28, nem pedig szám nélküli név.
        var line = LineCode.Match(raw);
        if (line.Success)
        {
            string lineName = line.Groups["name"].Value.Trim();
            if (lineName.Length > 0)
                return new MachineIdentity(line.Groups["num"].Value, lineName, raw);
        }

        // Betűvel kezdődő leltári gépszám (`M0100009 MINTACNC VZ-10`). Enélkül az azonosító üres
        // maradt, és a teljes mappanév csúszott a névbe.
        var prefixed = PrefixedCode.Match(raw);
        if (prefixed.Success)
        {
            string prefixedName = prefixed.Groups["name"].Value.Trim();
            if (prefixedName.Length > 0)
                return new MachineIdentity(prefixed.Groups["num"].Value, prefixedName, raw);
        }

        var m = LeadingCode.Match(raw);
        if (m.Success)
        {
            string name = m.Groups["name"].Value.Trim();
            if (name.Length > 0)
                return new MachineIdentity(m.Groups["num"].Value, name, raw);
        }
        return new MachineIdentity("", raw, raw);
    }
}
