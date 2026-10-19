using System.IO;
using System.Runtime.InteropServices;
using NPOI.XWPF.UserModel;
using TealDocAiTool.Common;
using TealDocAiTool.Models;

namespace TealDocAiTool.Extractors;

public sealed class WordTextExtractor : IDocumentExtractor
{
    private const int MaxCandidates = 5;
    private const int MaxParagraphsForFullText = 30;

    // A Word COM interop nem szálbiztos — a régi .doc-ok emiatt egyesével futnak,
    // míg a .docx (NPOI, natív .NET) szabadon párhuzamosítható a többi kinyeréssel.
    private static readonly SemaphoreSlim LegacyDocGate = new(1, 1);

    // Egy .doc megnyitására ennyit várunk. Bőven elég a legnagyobb kézikönyvhöz is; ha ennél
    // tovább tart, akkor nem lassú, hanem beragadt (láthatatlan modális Word-ablak).
    private static readonly TimeSpan LegacyDocTimeout = TimeSpan.FromSeconds(60);

    private volatile bool _wordUnresponsive;

    /// <summary>Igaz, ha egy .doc túllépte az időkorlátot. Onnantól ebben a futásban a további
    /// .doc fájlokat meg sem próbáljuk: a beragadt Word-példányhoz csatlakozva mindegyik ugyanúgy
    /// elakadna, és fájlonként egy percet veszítenénk. PÉLDÁNYSZINTŰ: minden futás új
    /// kinyerő-készletet kap, így egy új futás (pl. a Word-ablak bezárása után) újra próbálkozik —
    /// korábban az app újraindításáig minden .doc kimaradt.</summary>
    public bool WordUnresponsive
    {
        get => _wordUnresponsive;
        set => _wordUnresponsive = value;
    }

    /// <summary>A kihagyás oka a naplóba is kikerül. Enélkül a bejegyzés "Nincs kinyerhető
    /// szöveg" lenne, ami a dokumentumot vádolja, pedig a Word akadt meg egy KORÁBBI fájlon.</summary>
    private const string UnresponsiveReason =
        "A Word nem válaszol (egy korábbi .doc időtúllépése miatt), ezért a további .doc fájlokat kihagyjuk. " +
        "Zárd be a nyitva ragadt Word-ablakot, és futtasd újra — a kész fájlokat a napló alapján átugorja.";

    public bool CanHandle(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".doc" or ".docx" or ".docm";
    }

    public ExtractedDocument? Extract(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".doc") return ExtractDocx(filePath);
        if (_wordUnresponsive) throw new InvalidOperationException(UnresponsiveReason);

        LegacyDocGate.Wait();
        try
        {
            // Amíg a sorra vártunk, egy másik fájl megakaszthatta a Wordöt.
            if (_wordUnresponsive) throw new InvalidOperationException(UnresponsiveReason);

            if (TimeLimited.TryRun(() => ExtractLegacyDoc(filePath), LegacyDocTimeout, out var document))
                return document;

            _wordUnresponsive = true;
            throw new InvalidOperationException(UnresponsiveReason);
        }
        finally { LegacyDocGate.Release(); }
    }

    private static ExtractedDocument? ExtractDocx(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var docx = new XWPFDocument(stream);

        var paragraphs = docx.Paragraphs
            .Select(p => p.Text?.Trim() ?? "")
            .Where(t => t.Length > 0)
            .ToList();

        if (paragraphs.Count == 0) return null;

        string fullText = string.Join("\n", paragraphs.Take(MaxParagraphsForFullText));
        return new ExtractedDocument(fullText, paragraphs.Take(MaxCandidates).ToList());
    }

    /// <summary>Az NPOI 2.8.0 .NET 8 alatt NEM tartalmaz HWPF modult (ellenőrizve: 0 találat
    /// "HWPF"-re az összes szállított DLL-ben) — a régi bináris .doc-hoz nincs jó free
    /// alternatíva, ezért Word COM. KÉSŐI KÖTÉS (late-bound: Type.GetTypeFromProgID + dynamic):
    /// nincs Microsoft.Office.Interop.Word PIA-függőség, így verziófüggetlen. A korábbi hiba az
    /// volt, hogy a 15.0-ás (Office 2013) interop a 'office, Version=15.0.0.0' assembly-t kereste,
    /// ami a ténylegesen telepített Word 16 mellett nincs meg → minden .doc elhasalt. Ha Word
    /// nincs telepítve vagy hibázik, null-t ad vissza — NEM omlik el a köteg emiatt.</summary>
    private static ExtractedDocument? ExtractLegacyDoc(string filePath)
    {
        // A Word COM NEM tűri a '/' elválasztót: "C:/mappa/fájl.doc"-ra azt mondja, hogy nem
        // találja a fájlt — a .NET fájl-API-k viszont simán megnyitják. Egy ilyen útvonalon a Word
        // modális "Sorry, we couldn't find your file" ablakot nyit, ami láthatatlanul megállítja a
        // kötegelt futást. Ez vitte el egy teljes éles futás összes .doc-ját.
        filePath = Path.GetFullPath(filePath);

        Type? wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType is null) return null; // nincs Word telepítve

        dynamic? wordApp = null;
        dynamic? wordDoc = null;
        try
        {
            wordApp = Activator.CreateInstance(wordType);
            if (wordApp is null) return null;
            wordApp.Visible = false;

            // MINDEN modális párbeszéd megállítja a kötegelt futást: a Word megvárja a kattintást,
            // ami sosem jön. Egy teljes éles futásban emiatt akadt meg egy .doc-on, és utána 34
            // további .doc kimaradt. wdAlertsNone = a sérült/konvertálandó fájl figyelmeztetései,
            // ForceDisable = a makró-kérdés. (Az AutomationSecurity régebbi Wordben nincs meg.)
            wordApp.DisplayAlerts = 0;
            try { wordApp.AutomationSecurity = 3; } catch (COMException) { /* régi Word: nincs ilyen */ }

            // Pozicionális args: FileName, ConfirmConversions=false, ReadOnly=true,
            // AddToRecentFiles=false.
            wordDoc = wordApp.Documents.Open(filePath, false, true, false);
            string content = (string)(wordDoc.Content.Text ?? "");

            var paragraphs = content
                .Split('\r', '\n', '\v', '\f')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            if (paragraphs.Count == 0) return null;

            string fullText = string.Join("\n", paragraphs.Take(MaxParagraphsForFullText));
            return new ExtractedDocument(fullText, paragraphs.Take(MaxCandidates).ToList());
        }
        catch (Exception ex) when (ex is COMException or IOException)
        {
            return null; // Word hibázott vagy a fájl zárolt — a köteget nem szakítjuk meg
        }
        finally
        {
            try { if (wordDoc is not null) wordDoc.Close(false); } catch { /* ignoráljuk */ }
            try { if (wordApp is not null) wordApp.Quit(false); } catch { /* ignoráljuk */ }
            if (wordDoc is not null) Marshal.ReleaseComObject((object)wordDoc);
            if (wordApp is not null) Marshal.ReleaseComObject((object)wordApp);
        }
    }
}
