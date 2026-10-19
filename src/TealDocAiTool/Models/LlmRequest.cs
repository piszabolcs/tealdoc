namespace TealDocAiTool.Models;

/// <summary>Melyik kérdést tesszük fel az LLM-nek. A kaszkád lépcsői ebben különböznek.</summary>
public enum LlmPromptKind
{
    /// <summary>„Mi a dokumentum címe?" — a szöveg elejéből.</summary>
    Title,

    /// <summary>„Milyen TÍPUSÚ dokumentum ez?" — zárt listából, ha címet nem sikerült találni.
    /// Műszaki rajzoknál rendszerint ez az egyetlen értelmes válasz.</summary>
    Classify,

    /// <summary>„Milyen TÍPUSÚ GÉP ez?" — a gépmappa nevéből + a dokumentumok mintájából egy rövid,
    /// természetes nyelvű géptípus-megnevezés (pl. „CNC esztergagép"). Az Index módhoz.</summary>
    MachineType,

    /// <summary>„Miről szól ez az EGY fájl?" — egy tömör, egymondatos leírás a fájl tartalmáról.</summary>
    FileSummary,

    /// <summary>„MIT gyártanak/szerelnek ezen a GYÁRTÓSORON?" — egy vegyes gyökér alatt a `T05`,
    /// `C28` jelű egységek sorok, nem gépek; rájuk a „milyen típusú gép ez?" kérdés értelmetlen.</summary>
    ProductionLineType,
}

/// <param name="DocumentText">A kinyert szöveg. A kaszkád első körében rövidebb, a másodikban több.</param>
/// <param name="FolderName">A tartalmazó mappa neve — ez rendszerint a gép vagy berendezés neve
/// (pl. „Demo HD10 teszter R400000001"), és sokat segít a modellnek.</param>
/// <param name="CategoryName">A SZÜLŐ mappa neve = a gépsor (pl. „CNC Esztergak"). Emberi kézzel
/// karbantartott, megbízható kategória, és a MachineType promptnak ez a legerősebb támpontja —
/// a modell világtudása ugyanis nem ismeri a konkrét típusneveket. Üres, ha nincs értelmezhető szülő.</param>
public sealed record LlmRequest(string DocumentText, string FolderName, LlmPromptKind Kind, string CategoryName = "");

