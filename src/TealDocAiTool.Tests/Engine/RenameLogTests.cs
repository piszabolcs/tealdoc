using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

public class RenameLogTests
{
    [Fact]
    public void AppendThenReadAll_RoundTripsEntry()
    {
        string logPath = Path.Combine(Path.GetTempPath(), $"tealdoc_test_{Guid.NewGuid():N}.csv");
        try
        {
            var entry = new RenameLogEntry(
                DateTimeOffset.Parse("2026-07-25T10:00:00+00:00"),
                @"C:\docs\123.pdf", "Phoenix Contact relé kézikönyv.pdf",
                RenameMethod.Heuristic, "", false);

            RenameLog.Append(logPath, entry);
            var readBack = RenameLog.ReadAll(logPath);

            Assert.Single(readBack);
            Assert.Equal(entry, readBack[0]);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public void AppendThenReadAll_HandlesCommasAndQuotesInFields()
    {
        string logPath = Path.Combine(Path.GetTempPath(), $"tealdoc_test_{Guid.NewGuid():N}.csv");
        try
        {
            var entry = new RenameLogEntry(
                DateTimeOffset.Parse("2026-07-25T10:00:00+00:00"),
                @"C:\docs\a, b ""weird"".pdf", "Cím, ami \"idézőjeles\".pdf",
                RenameMethod.Llm, "megjegyzés, vesszővel", false);

            RenameLog.Append(logPath, entry);
            var readBack = RenameLog.ReadAll(logPath);

            Assert.Equal(entry, readBack[0]);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-07-25T10:00:00+00:00");

    private static string TempLog() => Path.Combine(Path.GetTempPath(), $"tealdoc_test_{Guid.NewGuid():N}.csv");

    // C-2: egy proxy HTML-hibaoldala (CR/LF-fel) korábban többsoros rekordot adott, és onnantól
    // a teljes napló — vele a Visszavonás — olvashatatlan volt.
    [Fact]
    public void Append_MultiLineDetail_StaysOnOneLineAndReadsBack()
    {
        string logPath = TempLog();
        try
        {
            RenameLog.Append(logPath, new RenameLogEntry(T0, @"C:\docs\a.pdf", "", RenameMethod.Failed,
                "<html>\r\n<body>502 Bad Gateway</body>\r\n</html>", false));
            RenameLog.Append(logPath, new RenameLogEntry(T0, @"C:\docs\b.pdf", "B cím (b).pdf", RenameMethod.Heuristic, "", false));

            Assert.Equal(3, File.ReadAllLines(logPath).Length); // fejléc + 2 rekord, mindegyik 1 sor
            var entries = RenameLog.ReadAll(logPath);
            Assert.Equal(2, entries.Count);
            Assert.DoesNotContain('\n', entries[0].Detail);
            Assert.Contains("502 Bad Gateway", entries[0].Detail);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    // A régi író többsoros, idézőjeles mezőt is írt; egy kézzel elrontott sor pedig nem
    // vihet el minden mást.
    [Fact]
    public void Read_LegacyLogWithMultiLineFieldAndGarbage_KeepsValidRows()
    {
        string logPath = TempLog();
        try
        {
            File.WriteAllText(logPath,
                "Timestamp,OriginalPath,NewFileName,Method,Detail,DryRun\r\n" +
                "2026-07-25T10:00:00.0000000+00:00,C:\\docs\\a.pdf,A cím (a).pdf,Heuristic,,False\r\n" +
                "2026-07-25T10:00:01.0000000+00:00,C:\\docs\\b.pdf,,Failed,\"<html>\r\n502\r\n</html>\",False\r\n" +
                "ez egy sérült sor\r\n" +
                "2026-07-25T10:00:02.0000000+00:00,C:\\docs\\c.pdf,C cím (c).pdf,Llm,,False\r\n",
                System.Text.Encoding.UTF8);

            var content = RenameLog.Read(logPath);

            Assert.Equal(3, content.Entries.Count);
            Assert.Equal(1, content.SkippedLines);
            Assert.Equal("", content.Entries[2].RunId);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    // S-5: a dokumentumból jövő „=…” cím Excelben képletként futna — a fájlban védve, visszaolvasva változatlan.
    [Fact]
    public void Append_FormulaLikeValue_IsGuardedInFileButReadsBackUnchanged()
    {
        string logPath = TempLog();
        try
        {
            var entry = new RenameLogEntry(T0, @"C:\docs\x.pdf", "=HYPERLINK(1) Bracket (x).pdf", RenameMethod.Llm, "", false, "run1");

            RenameLog.Append(logPath, entry);

            Assert.Contains(",'=HYPERLINK(1) Bracket (x).pdf,", File.ReadAllText(logPath));
            Assert.Equal(entry, RenameLog.ReadAll(logPath).Single());
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public void ActiveRenames_IgnoresFailedUndoneAndDryRunAttempts()
    {
        var entries = new List<RenameLogEntry>
        {
            new(T0, @"C:\d\a.pdf", "A (a).pdf", RenameMethod.Heuristic, "", false, "r1"),
            new(T0, @"C:\d\b.pdf", "B (b).pdf", RenameMethod.Llm, "", false, "r1"),
            new(T0, @"C:\d\b.pdf", "B (b).pdf", RenameMethod.Failed, "zárolt", false, "r1"),
            new(T0, @"C:\d\c.pdf", "C (c).pdf", RenameMethod.Llm, "", false, "r2"),
            new(T0, @"C:\d\c.pdf", "C (c).pdf", RenameMethod.Undone, "Visszavonva.", false, "r2"),
            new(T0, @"C:\d\e.pdf", "E (e).pdf", RenameMethod.Llm, "", true, "r2"),
        };

        var active = RenameLog.ActiveRenames(entries);

        Assert.Equal(new[] { @"C:\d\a.pdf" }, active.Select(e => e.OriginalPath));
        Assert.Equal("r1", RenameLog.LastRunId(entries));
    }

    [Fact]
    public void ReadAll_ReturnsEmptyListWhenFileDoesNotExist()
    {
        var result = RenameLog.ReadAll(Path.Combine(Path.GetTempPath(), $"tealdoc_missing_{Guid.NewGuid():N}.csv"));
        Assert.Empty(result);
    }

    [Fact]
    public void LoadCompletedOriginalPaths_ExcludesDryRunAndFailedEntries()
    {
        string logPath = Path.Combine(Path.GetTempPath(), $"tealdoc_test_{Guid.NewGuid():N}.csv");
        try
        {
            RenameLog.Append(logPath, new RenameLogEntry(DateTimeOffset.Now, @"C:\a.pdf", "A.pdf", RenameMethod.Heuristic, "", false));
            RenameLog.Append(logPath, new RenameLogEntry(DateTimeOffset.Now, @"C:\b.pdf", "B.pdf", RenameMethod.Heuristic, "", true)); // dry-run
            RenameLog.Append(logPath, new RenameLogEntry(DateTimeOffset.Now, @"C:\c.pdf", "", RenameMethod.Failed, "hiba", false));

            var completed = RenameLog.LoadCompletedOriginalPaths(logPath);

            Assert.Contains(@"C:\a.pdf", completed);
            Assert.DoesNotContain(@"C:\b.pdf", completed);
            Assert.DoesNotContain(@"C:\c.pdf", completed);
        }
        finally
        {
            File.Delete(logPath);
        }
    }
}
