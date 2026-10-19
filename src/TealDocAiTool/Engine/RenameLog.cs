using System.Globalization;
using System.IO;
using System.Text;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

/// <summary>
/// Kumulatív, append-only CSV napló minden átnevezésről (vagy kihagyásról/hibáról/visszavonásról).
/// Ez a Visszavonás és a futtatás-folytathatóság egyetlen adatforrása, ezért három szabály védi:
/// <list type="number">
/// <item>Íráskor minden rekord EGYETLEN fizikai sor: a vezérlőkaraktereket (CR/LF/TAB…) szóközre
/// cseréljük. Egy többsoros hibaüzenet (pl. egy proxy HTML-oldala) korábban az egész naplót
/// olvashatatlanná tette — és vele a Visszavonást is.</item>
/// <item>Olvasáskor valódi RFC 4180 értelmezés (idézőjeles, akár többsoros mezővel, a régi
/// naplók miatt), és a hibás sor nem dönti be az egészet: kimarad és megszámoljuk.</item>
/// <item>Az <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c> kezdetű szövegmező elé <c>'</c> kerül, hogy az
/// Excel ne képletként értékelje; olvasáskor levesszük, tehát az érték változatlan marad.</item>
/// </list>
/// </summary>
public static class RenameLog
{
    public const string FileName = "tealdoc_naplo.csv";

    private static readonly string[] Header = { "Timestamp", "OriginalPath", "NewFileName", "Method", "Detail", "DryRun", "RunId" };

    public static string PathIn(string logFolder) => Path.Combine(logFolder, FileName);

    /// <summary>
    /// A futás teljes idejére megnyitja a naplót írásra. Más program ezalatt csak olvashatja.
    /// Ha nem nyitható meg (pl. az Excel tartja nyitva), <see cref="IOException"/>-t dob — a hívó
    /// ilyenkor EL SEM INDÍTJA a futást, így nem keletkezhet napló nélküli átnevezés.
    /// </summary>
    public static RenameLogWriter OpenWriter(string logFilePath) => new(logFilePath);

    /// <summary>Egyetlen sor hozzáfűzése (megnyit, ír, lezár). Futás közben a
    /// <see cref="OpenWriter"/> nyitva tartott írója használandó.</summary>
    public static void Append(string logFilePath, RenameLogEntry entry)
    {
        using var writer = OpenWriter(logFilePath);
        writer.Append(entry);
    }

    public static List<RenameLogEntry> ReadAll(string logFilePath) => Read(logFilePath).Entries;

    /// <summary>A napló beolvasása. A sérült sorok kimaradnak, a számuk a
    /// <see cref="RenameLogContent.SkippedLines"/>-ban — a hívó figyelmeztethet, de nem akad el.</summary>
    public static RenameLogContent Read(string logFilePath)
    {
        var entries = new List<RenameLogEntry>();
        if (!File.Exists(logFilePath)) return new RenameLogContent(entries, 0);

        string text;
        // ReadWrite megosztás: olvasni akkor is tudjunk, ha épp egy futás írója tartja nyitva.
        using (var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();

        int skipped = 0;
        foreach (var fields in ParseRecords(text))
        {
            if (fields.Count == 1 && fields[0].Length == 0) continue;           // üres sor
            if (fields[0] == Header[0]) continue;                              // fejléc (régi vagy új)
            if (TryCreateEntry(fields, out var entry)) entries.Add(entry);
            else skipped++;
        }
        return new RenameLogContent(entries, skipped);
    }

    /// <summary>
    /// A ténylegesen érvényben lévő átnevezések (nem dry-run, Heuristic/Llm), fájlbeli sorrendben.
    /// Egy átnevezést ugyanarra az (eredeti út, új név) párra írt KÉSŐBBI sor érvénytelenít:
    /// a <see cref="RenameMethod.Failed"/> (az áthelyezés nem sikerült) vagy az
    /// <see cref="RenameMethod.Undone"/> (visszavonva). Ez a Visszavonás és a folytatás alapja.
    /// </summary>
    /// <param name="runId">Csak ennek a futásnak a sorai; <c>null</c> = minden futás.</param>
    public static List<RenameLogEntry> ActiveRenames(IReadOnlyList<RenameLogEntry> entries, string? runId = null)
    {
        var latest = new Dictionary<(string, string), int>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.DryRun || e.NewFileName.Length == 0) continue;
            if (e.Method is RenameMethod.Heuristic or RenameMethod.Llm or RenameMethod.Failed or RenameMethod.Undone)
                latest[Key(e)] = i;
        }

        return latest.Values
            .OrderBy(i => i)
            .Select(i => entries[i])
            .Where(e => e.Method is RenameMethod.Heuristic or RenameMethod.Llm)
            .Where(e => runId is null || e.RunId == runId)
            .ToList();
    }

    /// <summary>A legutóbbi futás azonosítója, amelynek még van érvényes átnevezése (a régi,
    /// azonosító nélküli naplóknál üres sztring). <c>null</c>, ha nincs visszavonható sor.</summary>
    public static string? LastRunId(IReadOnlyList<RenameLogEntry> entries) =>
        ActiveRenames(entries).LastOrDefault()?.RunId;

    /// <summary>Azok az eredeti útvonalak, amelyeket egy korábbi (nem dry-run) futás lezárt: a
    /// fájl szándékosan megtartott nevét (Kept), vagy egy még érvényes átnevezést.</summary>
    public static HashSet<string> LoadCompletedOriginalPaths(string logFilePath)
    {
        var entries = ReadAll(logFilePath);
        var result = new HashSet<string>(
            entries.Where(e => !e.DryRun && e.Method == RenameMethod.Kept).Select(e => e.OriginalPath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var e in ActiveRenames(entries)) result.Add(e.OriginalPath);
        return result;
    }

    internal static string FormatLine(RenameLogEntry entry) => string.Join(',', new[]
    {
        entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        CsvField(entry.OriginalPath),
        CsvField(entry.NewFileName),
        entry.Method.ToString(),
        CsvField(entry.Detail),
        entry.DryRun.ToString(),
        CsvField(entry.RunId),
    });

    internal static string HeaderLine => string.Join(',', Header);

    private static (string, string) Key(RenameLogEntry e) =>
        (e.OriginalPath.ToUpperInvariant(), e.NewFileName.ToUpperInvariant());

    private static bool TryCreateEntry(List<string> f, out RenameLogEntry entry)
    {
        entry = null!;
        if (f.Count < 6) return false;
        if (!DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)) return false;
        if (!Enum.TryParse<RenameMethod>(f[3], ignoreCase: false, out var method) || !Enum.IsDefined(method)) return false;
        if (!bool.TryParse(f[5], out bool dryRun)) return false;

        entry = new RenameLogEntry(timestamp, Unguard(f[1]), Unguard(f[2]), method, Unguard(f[4]), dryRun,
            f.Count > 6 ? Unguard(f[6]) : "");
        return true;
    }

    /// <summary>Egysoros, Excel-biztos CSV-mező.</summary>
    private static string CsvField(string? value)
    {
        value ??= "";
        var sb = new StringBuilder(value.Length + 2);
        foreach (char c in value) sb.Append(char.IsControl(c) ? ' ' : c);
        string s = sb.ToString();
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\'') s = "'" + s;
        return s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    private static string Unguard(string value) => value.Length > 0 && value[0] == '\'' ? value[1..] : value;

    /// <summary>RFC 4180: az idézőjeles mezőben vessző és sortörés is lehet (a régi író így írta a
    /// többsoros hibaüzeneteket). A rekordot a NEM idézett LF zárja; az előtte álló CR elhagyandó.</summary>
    private static IEnumerable<List<string>> ParseRecords(string text)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields;
                    fields = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}

public sealed record RenameLogContent(List<RenameLogEntry> Entries, int SkippedLines);

/// <summary>
/// A napló nyitva tartott írója. Szálbiztos; minden sor után az operációs rendszernek adja át a
/// puffert, így a folyamat kilövése után is minden addig megírt sor a fájlban van.
/// </summary>
public sealed class RenameLogWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    internal RenameLogWriter(string logFilePath)
    {
        string? folder = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        FileStream stream;
        bool unterminatedLastLine;
        try
        {
            unterminatedLastLine = EndsWithoutNewline(logFilePath);
            stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        }
        catch (IOException ex)
        {
            throw new IOException(
                $"A napló nem nyitható meg írásra (lehet, hogy egy másik program, pl. az Excel tartja nyitva): {logFilePath}", ex);
        }

        // Encoding.UTF8: új fájl elejére BOM kerül (az Excel így ismeri fel az ékezeteket);
        // meglévő fájl végére hozzáfűzve a StreamWriter nem ír BOM-ot.
        _writer = new StreamWriter(stream, Encoding.UTF8);
        if (stream.Length == 0) WriteAndFlush(RenameLog.HeaderLine);
        // Egy félbeszakadt utolsó sorhoz (összeomlás írás közben) nem ragaszthatjuk hozzá az új
        // rekordot — az is elveszne a sérült sorral együtt.
        else if (unterminatedLastLine) WriteAndFlush("");
    }

    public void Append(RenameLogEntry entry)
    {
        lock (_lock) WriteAndFlush(RenameLog.FormatLine(entry));
    }

    private static bool EndsWithoutNewline(string path)
    {
        if (!File.Exists(path)) return false;
        using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (probe.Length == 0) return false;
        probe.Seek(-1, SeekOrigin.End);
        return probe.ReadByte() != '\n';
    }

    private void WriteAndFlush(string line)
    {
        _writer.WriteLine(line);
        _writer.Flush();
    }

    public void Dispose()
    {
        lock (_lock) _writer.Dispose();
    }
}
