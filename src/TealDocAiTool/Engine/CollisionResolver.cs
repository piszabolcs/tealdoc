using System.Collections.Concurrent;

namespace TealDocAiTool.Engine;

/// <summary>
/// Szálbiztos ütközés-feloldás: <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> atomi
/// check-and-add, mert a hívó (RunCoordinator) több fájlt párhuzamosan dolgoz fel, és két
/// egyidejű fájl könnyen ugyanarra a címre futhatna ki.
/// </summary>
public static class CollisionResolver
{
    public static string ResolveUniqueName(string desiredNameWithoutExt, string extension, ConcurrentDictionary<string, byte> takenNamesLower)
    {
        string candidate = desiredNameWithoutExt + extension;
        if (takenNamesLower.TryAdd(candidate.ToLowerInvariant(), 0)) return candidate;

        int n = 2;
        while (true)
        {
            candidate = $"{desiredNameWithoutExt} ({n}){extension}";
            if (takenNamesLower.TryAdd(candidate.ToLowerInvariant(), 0)) return candidate;
            n++;
        }
    }
}
