using System.IO;

namespace TealDocAiTool.Engine;

/// <summary>
/// A helyi mappaszerkezetet webes (pl. SharePoint-) hivatkozásokká képezi le — ez az opcionális
/// „hivatkozás-alap URL” mögötti logika.
///
/// Miért kell: a dokumentumok gyakran egy webes dokumentumtárban élnek, a generálás viszont egy
/// leszinkronizált HELYI másolaton fut. A helyi út egy megosztott tudásbázisban használhatatlan.
/// A megoldás: a felhasználó megadja, melyik webes mappának felel meg a helyi gyökér, és minden
/// alatta lévő elem útját ehhez fűzzük. A SharePoint két címsor-alakját (kanonikus út és
/// <c>AllItems.aspx?id=</c>) is felismeri; a tokenes megosztási linket elutasítja.
/// </summary>
public static class SharePointUrlBuilder
{
    /// <summary>
    /// Megosztási („Share” gombbal másolt) link-e. Ezek `/:f:/`, `/:w:/`, `/:b:/` … alakúak, és
    /// a mappát egy ÁTLÁTSZATLAN tokennel azonosítják, nem az útvonalával. Emiatt gyerek-elem
    /// URL-jét NEM lehet belőlük képezni — a fájl neve nem fűzhető a token után. A felhasználónak
    /// a böngésző címsorából kell kimásolnia a valódi URL-t.
    /// </summary>
    public static bool IsSharingLink(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)) return false;
        string path = uri.AbsolutePath;
        // Alak: "/:f:/s/Site/Ig..." — az első szegmens egyetlen betű két kettőspont között.
        return path.Length >= 4 && path[0] == '/' && path[1] == ':' && path[3] == ':';
    }

    /// <summary>
    /// A felhasználó által beírt URL-t kanonikus mappacímmé alakítja, amihez gyerek-utak fűzhetők.
    ///
    /// Kétféle bemenetet fogad el, mert a böngészőből mindkettő könnyen kimásolható:
    ///   1. Kanonikus cím: `https://…/sites/Site/Shared%20Documents/Archivum/CNC%20Esztergak`
    ///   2. Dokumentumtár-nézet: `…/Forms/AllItems.aspx?id=%2Fsites%2F…%2FCNC%20Esztergak&viewid=…`
    ///      — ilyet mutat a címsor, amikor a mappában állsz. Ebből az `id` a szerver-relatív út.
    ///
    /// Üres sztringet ad vissza, ha nem használható (üres, nem abszolút URL, vagy megosztási link).
    /// </summary>
    public static string NormalizeBase(string? input)
    {
        string raw = input?.Trim() ?? "";
        if (raw.Length == 0) return "";
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return "";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "";
        if (IsSharingLink(raw)) return "";

        string origin = uri.GetLeftPart(UriPartial.Authority);

        // A nézet-URL a valódi utat az `id` paraméterben hordozza; az AbsolutePath ilyenkor csak
        // az AllItems.aspx-re mutat, ami nem mappa.
        string? serverRelative = ExtractIdParameter(uri);
        if (serverRelative is not null)
            return origin + "/" + EncodePath(serverRelative);

        // Kanonikus cím: az AbsolutePath már kódolt, de a query/fragment (pl. `?e=AbCd12`)
        // eldobandó — ha bennmaradna, a hozzáfűzött fájlnév a query STRINGBE kerülne.
        return (origin + uri.AbsolutePath).TrimEnd('/');
    }

    /// <summary>
    /// Gyerek-elem teljes URL-je. A relatív út lehet Windows-os (`alkonyvtar\fajl.pdf`); a
    /// szegmenseket külön kódoljuk, hogy az elválasztó `/` ne váljon `%2F`-fé, az ékezetek és a
    /// szóközök viszont igen (`Szerszámtartók` → `Szersz%C3%A1mtart%C3%B3k`).
    /// </summary>
    public static string Combine(string? baseUrl, string? relativePath)
    {
        string root = (baseUrl ?? "").TrimEnd('/');
        if (root.Length == 0) return "";
        string encoded = EncodePath(relativePath ?? "");
        return encoded.Length == 0 ? root : root + "/" + encoded;
    }

    /// <summary>
    /// Egy gépmappa SharePoint-címe. A gyökérhez képesti relatív utat fűzi a gyökér címéhez — NEM
    /// a mappanevet, mert a gépmappa nem feltétlenül közvetlen gyereke a gyökérnek.
    /// Ugyanezt hívja a generálás és a gyorsítótárból való visszatöltés is, hogy a kettő ne
    /// csússzon szét.
    /// </summary>
    public static string ForMachineFolder(string? baseUrl, string? rootFolder, string machineFolder)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return "";
        string trimmed = machineFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(rootFolder)) return Combine(baseUrl, Path.GetFileName(trimmed));
        if (!IsUnderRoot(rootFolder, trimmed)) return "";
        return Combine(baseUrl, Path.GetRelativePath(rootFolder, trimmed));
    }

    /// <summary>
    /// A gyökér alatt van-e ez a mappa. Azért kell, mert EGY gyorsítótárba több gyökér is futhat
    /// (egy archívum több fája), és a másik gyökér gépére számolt relatív út `..` szegmenseket
    /// tartalmazna — abból csak halott hivatkozás lenne.
    /// </summary>
    public static bool IsUnderRoot(string? rootFolder, string path)
    {
        if (string.IsNullOrWhiteSpace(rootFolder)) return true;
        string rel;
        try { rel = Path.GetRelativePath(rootFolder, path); }
        catch (ArgumentException) { return false; }
        // Más meghajtón: a GetRelativePath ilyenkor magát az abszolút utat adja vissza.
        if (Path.IsPathRooted(rel)) return false;
        return rel != ".." && !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string EncodePath(string path)
    {
        var segments = path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != ".")
            .Select(Uri.EscapeDataString);
        return string.Join('/', segments);
    }

    private static string? ExtractIdParameter(Uri uri)
    {
        string query = uri.Query.TrimStart('?');
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (!string.Equals(pair[..eq], "id", StringComparison.OrdinalIgnoreCase)) continue;
            // NEM cserélünk `+`-t szóközre: a SharePoint az `id`-t %-kódolja, tehát a `+` valódi
            // plusz jel egy mappanévben (pl. „Fűtés + szellőzés”).
            string value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            return value.Length > 0 ? value : null;
        }
        return null;
    }
}
