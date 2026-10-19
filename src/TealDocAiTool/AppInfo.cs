using System.Reflection;

namespace TealDocAiTool;

/// <summary>
/// Az alkalmazás azonosító adatai. A verzió EGYETLEN forrása a csproj <c>&lt;Version&gt;</c>
/// tulajdonsága; futásidőben az assembly-attribútumból olvassuk, sehol nincs kétszer beégetve.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    /// <summary>A szerzői névjegy felirata a láblécben.</summary>
    public static string Signature => $"psz // v{Version}";

    private static string ReadVersion()
    {
        string version = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        // Az SDK a git-commit azonosítóját „+…” utótagként hozzáfűzheti — a felületen nem kell.
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
