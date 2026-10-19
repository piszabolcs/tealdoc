using System.Globalization;

namespace TealDocAiTool.Common;

/// <summary>Számmezők beolvasása. Korábban a nem szám vagy a 0 csendben alapértékre esett, felső
/// korlát pedig nem volt (C-15); most tartományba szorítunk, és a hívó jelezheti a javítást.</summary>
public static class InputParsing
{
    /// <returns>A ténylegesen használandó érték; <paramref name="adjusted"/> igaz, ha ez eltér a beírttól.</returns>
    public static int ParseClamped(string? text, int min, int max, int fallback, out bool adjusted)
    {
        string trimmed = text?.Trim() ?? "";
        int value = int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
        adjusted = trimmed != value.ToString(CultureInfo.InvariantCulture);
        return value;
    }
}
