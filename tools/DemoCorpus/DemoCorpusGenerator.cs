using System.Text;
using NPOI.XSSF.UserModel;
using NPOI.XWPF.UserModel;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace DemoCorpus;

/// <summary>
/// Kitalált dokumentum-archívum a TealDoc kipróbálásához, a képernyőképekhez és a tesztekhez.
/// Determinisztikus (mindig ugyanazt a tartalmat írja), kicsi (néhány tíz KB), és MINDEN adata
/// kitalált: „Mintagyártó Kft.”, <c>.example</c> e-mail-címek, DEMO/MINTA gépnevek.
///
/// Az almappák az archívum „egységei” (gépek, egy gyártósor és egy vegyes mappa) — ez a
/// Mappa-index bemenete. A fájlok a motor fő ágait mutatják be: PDF nagy betűs címmel
/// (heurisztika), szöveg nélküli „szkennelt” PDF, sérült PDF, DOCX elérhetőségi zajjal az elején,
/// XLSX alkatrészlista, UTF-8 és Windows-1250 TXT (LLM-et igényel), RTF, már beszédes nevű fájl
/// (megtartva), két azonos című fájl (ütközés), láthatatlan formázó karaktert tartalmazó cím.
/// </summary>
public static class DemoCorpusGenerator
{
    public const string LatheFolder = "0101 DEMO ESZTERGA CX-200";
    public const string MillFolder = "0102 MINTA MARÓGÉP VM-5";
    public const string PressFolder = "0201 DEMO PRÉS P-80";
    public const string LineFolder = "T05 DEMO SZERELŐSOR";
    public const string MiscFolder = "Egyéb dokumentumok";

    /// <summary>A korpusz létrehozása. A célmappa nem létezhet, vagy üresnek kell lennie — a
    /// generátor soha nem ír felül és nem töröl semmit.</summary>
    /// <returns>A létrehozott fájlok száma.</returns>
    public static int Generate(string root)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"A célmappa nem üres, a generátor nem ír felül semmit: {root}");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        int count = 0;
        void Count(Action write) { write(); count++; }

        string lathe = Folder(root, LatheFolder);
        Count(() => Pdf(Path.Combine(lathe, "scan_0001.pdf"), "Kezelési útmutató – CX-200 eszterga",
            "Mintagyártó Kft. · 2024. kiadás", "A gép üzembe helyezése, kezelése és napi karbantartása."));
        Count(() => Docx(Path.Combine(lathe, "doc_12345.docx"), "Karbantartási terv – CX-200 főorsó",
            "Heti, havi és éves karbantartási feladatok a főorsóhoz és a hűtőkörhöz."));
        Count(() => Xlsx(Path.Combine(lathe, "1234567.xlsx"), "Alkatrészlista – CX-200 főorsó egység",
            ("DM-1001", "Főorsó csapágy", 2), ("DM-1002", "Tömítőgyűrű 45x62x8", 4), ("DM-1003", "Hajtószíj", 1)));
        Count(() => Pdf(Path.Combine(lathe, "Kezelési útmutató CX-200.pdf"), "Kezelési útmutató – CX-200",
            "Már beszédes fájlnév: a TealDoc érintetlenül hagyja."));
        Count(() => Pdf(Path.Combine(lathe, "img_0042.pdf"), title: null)); // „szkennelt”: nincs szövegréteg

        string mill = Folder(root, MillFolder);
        Count(() => Docx(Path.Combine(mill, "P-00011.docx"), "Szerelési útmutató – VM-5 orsófej",
            "A bal oldali orsófej szerelése."));
        Count(() => Docx(Path.Combine(mill, "P-00012.docx"), "Szerelési útmutató – VM-5 orsófej",
            "A jobb oldali orsófej szerelése (azonos cím → az eredeti név különbözteti meg)."));
        Count(() => File.WriteAllText(Path.Combine(mill, "jk_0007.txt"),
            "Műszakátadási jegyzőkönyv\nVM-5 marógép, 2. műszak\nA hűtőfolyadék szintje rendben, a forgácsszállító tisztítva.\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
        Count(() => File.WriteAllText(Path.Combine(mill, "x-0001.rtf"),
            @"{\rtf1\ansi\ansicpg1250{\fonttbl{\f0 Arial;}{\f1 Courier New;}}\f0\fs24 " +
            @"Ellen\'f5rz\'f5 lista \endash  VM-5 tengelyek be\'e1ll\'edt\'e1sa\par " +
            @"A tengelyek h\'e9zag\'e1nak m\'e9r\'e9se \'e9s ut\'e1n\'e1ll\'edt\'e1sa.\par }"));
        Count(() => File.WriteAllBytes(Path.Combine(mill, "scan_0002.pdf"),
            Encoding.ASCII.GetBytes("%PDF-1.4\n% sérült fájl: a fejléc után nincs érvényes PDF-szerkezet\n")));

        string press = Folder(root, PressFolder);
        Count(() => Xlsx(Path.Combine(press, "1500.xlsx"), "Kenési terv – P-80 hidraulikus prés",
            ("KP-01", "Vezetőoszlopok", 8), ("KP-02", "Hidraulika szivattyú", 1)));
        Count(() => File.WriteAllBytes(Path.Combine(press, "dl_p80.txt"), Encoding.GetEncoding(1250).GetBytes(
            "Műszaki adatlap – P-80 hidraulikus prés\r\nPréserő: 80 t, löket: 250 mm, motor: 7,5 kW\r\n")));
        Count(() => Pdf(Path.Combine(press, "doc_0099.pdf"), "Hidraulikus kapcsolási rajz – P-80",
            "Szelepek, hengerek és nyomáshatárolók jelölései."));

        string line = Folder(root, LineFolder);
        Count(() => Pdf(Path.Combine(line, "doc_0500.pdf"), "Napi karbantartási lista – T05 sor",
            "Napi vizsgálatok gyártásindításkor."));
        Count(() => Docx(Path.Combine(line, "T05-01.docx"), "Gyártósor-elrendezés és biztonsági zónák",
            "A szerelősor állomásai és a fénysorompók helye."));

        string misc = Folder(root, MiscFolder);
        Count(() => Docx(Path.Combine(misc, "doc_0001.docx"),
            "Mintagyártó Kft. · info@pelda-gyarto.example · +36 (0)1 234 5678",
            "Megfelelőségi nyilatkozat – CX-200 eszterga", "Kijelentjük, hogy a gép megfelel a vonatkozó előírásoknak."));
        // A U+202E (jobbról-balra) a címben megfordítaná a látszó kiterjesztést — a TealDoc kiszűri.
        Count(() => Docx(Path.Combine(misc, "doc_0002.docx"), "Biztonsági adatlap ‮fdp.exe",
            "Hűtő-kenő folyadék biztonsági adatlapja."));
        Count(() => File.WriteAllText(Path.Combine(misc, "megjegyzes.txt"),
            "Ez a mappa vegyes dokumentumokat tartalmaz.\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));

        return count;
    }

    private static string Folder(string root, string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A PDF-ekbe ágyazott betűtípus. A PdfPig szabványos (Standard 14) betűi nem ismerik
    /// az ékezetes betűket, ezért egy kicsi Windows-betűtípust ágyazunk be (a TealDoc amúgy is
    /// Windows-alkalmazás).</summary>
    private static readonly Lazy<byte[]> EmbeddedFont = new(() =>
    {
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (string name in new[] { "verdana.ttf", "arial.ttf" })
        {
            string path = Path.Combine(fonts, name);
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }
        throw new IOException($"Nem található beágyazható betűtípus (verdana.ttf / arial.ttf) itt: {fonts}");
    });

    /// <summary>Egyoldalas PDF: a cím nagy betűmérettel (ezt találja meg a heurisztika), alatta
    /// normál sorok. <c>title: null</c> = szövegréteg nélküli, „szkennelt” oldal.</summary>
    private static void Pdf(string path, string? title, params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        if (title is not null || lines.Length > 0)
        {
            var font = builder.AddTrueTypeFont(EmbeddedFont.Value);
            if (title is not null) page.AddText(title, 22, new PdfPoint(50, 760), font);
            double y = 720;
            foreach (string line in lines)
            {
                page.AddText(line, 11, new PdfPoint(50, y), font);
                y -= 18;
            }
        }
        File.WriteAllBytes(path, builder.Build());
    }

    private static void Docx(string path, params string[] paragraphs)
    {
        var document = new XWPFDocument();
        foreach (string text in paragraphs)
            document.CreateParagraph().CreateRun().SetText(text);
        using var stream = File.Create(path);
        document.Write(stream);
    }

    private static void Xlsx(string path, string title, params (string Code, string Name, int Quantity)[] items)
    {
        var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Lista");
        sheet.CreateRow(0).CreateCell(0).SetCellValue(title);
        var header = sheet.CreateRow(2);
        header.CreateCell(0).SetCellValue("Cikkszám");
        header.CreateCell(1).SetCellValue("Megnevezés");
        header.CreateCell(2).SetCellValue("Darab");
        for (int i = 0; i < items.Length; i++)
        {
            var row = sheet.CreateRow(3 + i);
            row.CreateCell(0).SetCellValue(items[i].Code);
            row.CreateCell(1).SetCellValue(items[i].Name);
            row.CreateCell(2).SetCellValue(items[i].Quantity);
        }
        using var stream = File.Create(path);
        workbook.Write(stream);
    }
}
