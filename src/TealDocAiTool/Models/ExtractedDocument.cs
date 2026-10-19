namespace TealDocAiTool.Models;

/// <summary>
/// Egy dokumentumból kinyert nyers adat. A <see cref="HeuristicCandidates"/> prioritás
/// szerint rendezett (legjobb jelölt elöl) — PDF-nél betűméret szerint csökkenő sorrendben,
/// Office dokumentumnál dokumentum-sorrendben. Üres lista = ehhez a típushoz nincs
/// heurisztika-lépcső, egyenesen LLM-re esik.
/// </summary>
public sealed record ExtractedDocument(string FullText, IReadOnlyList<string> HeuristicCandidates);
