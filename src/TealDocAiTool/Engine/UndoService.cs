using System.IO;
using TealDocAiTool.Models;

namespace TealDocAiTool.Engine;

public sealed class UndoService
{
    public sealed record UndoResult(int Restored, int SkippedMissing, List<string> Errors);

    /// <summary>
    /// Visszaállítja az érvényben lévő átnevezéseket (a legújabbal kezdve), és minden visszavont
    /// fájlról <see cref="RenameMethod.Undone"/> sort ír a naplóba. Ettől (a) egy újabb Visszavonás
    /// már nem ígéri ugyanazokat a fájlokat, (b) a következő futás a visszaállított fájlokat újra
    /// feldolgozza. Fájlt sosem ír felül: ha az eredeti név foglalt, az hiba, nem felülírás.
    /// </summary>
    /// <param name="runId">Csak ennek a futásnak az átnevezései; <c>null</c> = minden futásé.</param>
    public UndoResult Undo(string logFilePath, string? runId = null)
    {
        // Az írót ELŐSZÖR nyitjuk meg: ha a napló nem írható, egyetlen fájlhoz sem nyúlunk.
        using var log = RenameLog.OpenWriter(logFilePath);
        var targets = RenameLog.ActiveRenames(RenameLog.ReadAll(logFilePath), runId);
        targets.Reverse();

        int restored = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var entry in targets)
        {
            // Kézzel szerkesztett vagy sérült napló: az új név nem tartalmazhat útvonalat, különben
            // a visszavonás tetszőleges fájlt mozgathatna.
            if (Path.GetFileName(entry.NewFileName) != entry.NewFileName)
            {
                errors.Add($"Érvénytelen naplóbejegyzés (a fájlnév útvonalat tartalmaz): {entry.NewFileName}");
                continue;
            }

            string directory = Path.GetDirectoryName(entry.OriginalPath) ?? "";
            string currentPath = Path.Combine(directory, entry.NewFileName);

            if (!File.Exists(currentPath))
            {
                skipped++;
                // Nincs mit visszavonni. Ezt is lezárjuk, különben minden későbbi Visszavonás újra
                // felajánlaná. Ha az eredeti név a helyén van, az átnevezés meg sem történt (pl.
                // összeomlás a naplósor és az áthelyezés között) — így a fájl újra sorra kerül.
                log.Append(UndoneEntry(entry, File.Exists(entry.OriginalPath)
                    ? "Nem volt mit visszavonni: a fájl az eredeti nevén van."
                    : "Nem volt mit visszavonni: az átnevezett fájl már nem található."));
                continue;
            }
            if (File.Exists(entry.OriginalPath))
            {
                errors.Add($"Az eredeti út már foglalt: {entry.OriginalPath}");
                continue;
            }

            try
            {
                File.Move(currentPath, entry.OriginalPath);
                log.Append(UndoneEntry(entry, "Visszavonva."));
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{currentPath}: {ex.Message}");
            }
        }

        return new UndoResult(restored, skipped, errors);
    }

    private static RenameLogEntry UndoneEntry(RenameLogEntry entry, string detail) =>
        new(DateTimeOffset.Now, entry.OriginalPath, entry.NewFileName, RenameMethod.Undone, detail, DryRun: false, entry.RunId);
}
