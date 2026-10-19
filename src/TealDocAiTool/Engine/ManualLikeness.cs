using System.IO;

namespace TealDocAiTool.Engine;

/// <summary>
/// Mennyire valószínű, hogy egy fájl ENNEK A GÉPNEK a kezelési/karbantartási dokumentuma —
/// pusztán a fájlnévből. Ez dönti el, miből vegyünk mintát a géptípus megállapításához.
///
/// Miért kell: az eredeti mintavétel az ábécé-sorrendben első néhány fájlt vette. A valódi
/// „1401 MINTAGYARTO VARIO 20" mappában ez telepítési ellenőrzőlista + műszaki rajz +
/// hálózati szerviz doksi volt — a tényleges kézikönyv sokkal mélyebben fekszik.
///
/// A MÁSODIK, súlyosabb hiba, amit javít: a gépmappák tele vannak BESZÁLLÍTÓI komponensek
/// kézikönyveivel, és azok neve pont a keresett szavakat tartalmazza. A 1401-es esztergánál a
/// „MANUALE DELL'OPERATORE (B-63944IT)" — a FANUC VEZÉRLŐ olasz kézikönyve — nyert a gép saját
/// „VARIO20-8linear-FA-BA-v10.0-de.pdf" doksija ellen, amiben nincs egyetlen kulcsszó sem.
/// Egy vezérlő-kézikönyv esztergálásról, fúrásról és marásról egyaránt beszél, ezért lett minden
/// esztergából „Fúrógép". A gép SAJÁT dokumentumát a gépnévvel való egyezés ismeri fel.
/// </summary>
public static class ManualLikeness
{
    /// <summary>Erős jelek: ezek maguk a kezelési/karbantartási kézikönyvek.</summary>
    private static readonly string[] Strong =
    {
        "betriebsanleitung", "bedienungsanleitung", "bedienung", "anleitung", "handbuch", "wartung",
        "manual", "manuale", "operator", "operatore", "operating", "instruction", "maintenance",
        "kezel", "kezikonyv", "kézikönyv", "utmutato", "útmutató", "kezikonyv", "kézikönyv",
        "karbantart", "hasznalati", "használati", "uzemeltet", "üzemeltet",
    };

    /// <summary>Gyengébb, de még mindig hasznos jelek.</summary>
    private static readonly string[] Weak =
    {
        "dokumentation", "documentation", "doku", "guide", "userguide", "katalog", "catalog",
    };

    /// <summary>
    /// Vezérlő- és komponensgyártók. Az ő kézikönyveik a gépmappában vannak, de NEM a gépről
    /// szólnak. Csak akkor vonunk le értük, ha a gép neve maga nem ez a gyártó — egy valódi
    /// Rittal-szekrénynél a Rittal-doksi a helyes minta.
    /// </summary>
    private static readonly string[] ComponentVendors =
    {
        "fanuc", "sinumerik", "siemens", "heidenhain", "ifm", "euchner", "rittal", "brinkmann",
        "hydac", "mayfran", "knoll", "shell", "rexroth", "festo", "pilz", "balluff", "leuze",
        "wago", "lenze", "danfoss", "schunk", "murrelektronik",
    };

    /// <summary>Adminisztratív papírok: a mappában vannak, de a géptípusról semmit nem mondanak.</summary>
    private static readonly string[] Paperwork =
    {
        "checklist", "ellenorzolista", "ellenőrzőlista", "jegyzokonyv", "jegyzőkönyv", "protokoll",
        "protocol", "munkalap", "sicherheitsdatenblatt", "safety_data", "lieferschein", "rechnung",
        "invoice", "arajanlat", "árajánlat", "offer",
    };

    /// <summary>
    /// A gépnév azon szavai, amelyekre egy fájlnévben érdemes egyezést keresni. A rövid és a
    /// tisztán számokból álló darabokat kihagyjuk: a „20" a „VARIO 20"-ból minden második
    /// fájlnévre illeszkedne.
    /// </summary>
    public static IReadOnlyList<string> NameTokens(string machineName) =>
        (machineName ?? "")
            .Split(new[] { ' ', '-', '_', '(', ')', ',', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 4 && !t.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static int Score(string filePath) => Score(filePath, Array.Empty<string>());

    /// <summary>Nagyobb a jobb. A gép saját kézikönyve a legerősebb minta.</summary>
    public static int Score(string filePath, IReadOnlyList<string> machineNameTokens)
    {
        string name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

        int score = 0;
        foreach (var k in Strong)
            if (name.Contains(k, StringComparison.Ordinal)) { score = 2; break; }
        if (score == 0)
            foreach (var k in Weak)
                if (name.Contains(k, StringComparison.Ordinal)) { score = 1; break; }

        // A legerősebb jel: a fájlnév a GÉP nevére hivatkozik. Ez erősebb, mint bármelyik
        // kulcsszó, mert a beszállítói doksik tele vannak kulcsszóval, de a gép nevét nem viselik.
        if (machineNameTokens.Any(t => name.Contains(t, StringComparison.Ordinal)))
            score += 3;

        foreach (var v in ComponentVendors)
            if (name.Contains(v, StringComparison.Ordinal) &&
                !machineNameTokens.Contains(v, StringComparer.Ordinal))
            {
                score -= 2;
                break;
            }

        foreach (var p in Paperwork)
            if (name.Contains(p, StringComparison.Ordinal)) { score -= 1; break; }

        return score;
    }
}
