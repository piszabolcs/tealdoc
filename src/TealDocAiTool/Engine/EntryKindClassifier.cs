using System.Text.RegularExpressions;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Gyártósor vagy gép? Egy vegyes gyökér alatt mindkettő előfordul, és ez nem
/// mindegy: egy sorról azt írni, hogy „az 1401-es számú GÉP", félrevezeti a keresőt és az asszisztenst.
///
/// A szabály a jelölésből jön (a felhasználótól):
/// - <b>Gyártósor</b>: 1 betű + 2 számjegy — `T05`, `C28`, `V01`, `S01`. Tartományt is jelölhet
///   (`T17-18-19`).
/// - <b>Gép</b>: legalább 4 számjegy, betű nélkül — `7246`, `0229`, `53600001`.
/// - Minden más: <b>Unknown</b>. Itt SZÁNDÉKOSAN nem tippelünk — a `G510`, `H134`, `T010`
///   (1 betű + 3 számjegy), a `404x`/`976x` (x-es maszk) és a `C3000007` (betű + 7 számjegy)
///   nem illik egyik szabályba sem, és a rossz besorolás rosszabb, mint a semmilyen.
/// </summary>
public static class EntryKindClassifier
{
    /// <summary>1 betű + pontosan 2 számjegy, esetleg további „-NN" tagokkal. A lezárás azért kell,
    /// hogy a `C3000007` (betű + 7 számjegy) NE minősüljön sornak.</summary>
    private static readonly Regex LineCode =
        new(@"^\s*[A-Za-z]\d{2}(-\d{2})*(?![0-9])", RegexOptions.Compiled);

    /// <summary>Legalább 4 vezető számjegy, betű nélkül.</summary>
    private static readonly Regex MachineNumber =
        new(@"^\s*\d{4,}(?![0-9])", RegexOptions.Compiled);

    /// <summary>Betűvel kezdődő leltári gépszám, legalább 4 számjeggyel: `M0100009`, `C3000007`,
    /// `G5100001`. A sorjelöléstől egyértelműen elválik (az 1 betű + PONTOSAN 2 számjegy), és a
    /// „legalább 4 számjegy = gép" szabály szellemét követi. Enélkül ez a 13 mappa elvesztette az
    /// azonosítóját — épp azt a tokent, amire a SAP és a rendelés hivatkozik.</summary>
    private static readonly Regex PrefixedMachineNumber =
        new(@"^\s*[A-Za-z]\d{4,}", RegexOptions.Compiled);

    public static MachineEntryKind Classify(string folderName)
    {
        if (LineCode.IsMatch(folderName)) return MachineEntryKind.ProductionLine;
        if (MachineNumber.IsMatch(folderName)) return MachineEntryKind.Machine;
        if (PrefixedMachineNumber.IsMatch(folderName)) return MachineEntryKind.Machine;
        return MachineEntryKind.Unknown;
    }
}
