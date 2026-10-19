using System.Diagnostics;
using TealDocAiTool.Common;

namespace TealDocAiTool.Tests.Common;

public class TimeLimitedTests
{
    [Fact]
    public void TryRun_ReturnsResultWhenWorkFinishesInTime()
    {
        Assert.True(TimeLimited.TryRun(() => "kész", TimeSpan.FromSeconds(5), out string? result));
        Assert.Equal("kész", result);
    }

    [Fact]
    public void TryRun_ReturnsFalseWhenWorkHangs()
    {
        // Ez a beragadt Word COM-hívás modellje: a munka SOHA nem fejeződik be magától.
        using var release = new ManualResetEventSlim(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            bool finished = TimeLimited.TryRun<string?>(
                () => { release.Wait(); return "túl késő"; },
                TimeSpan.FromMilliseconds(200),
                out string? result);
            stopwatch.Stop();

            Assert.False(finished);
            Assert.Null(result);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"nem az időkorlátnál tért vissza: {stopwatch.Elapsed}");
        }
        finally
        {
            release.Set(); // a beragadt háttérszál elengedése
        }
    }

    [Fact]
    public void TryRun_TreatsThrownExceptionAsFinishedWithoutResult()
    {
        // A hibázó kinyerés NEM időtúllépés: nem szabad emiatt "nem válaszol"-nak minősíteni a Wordöt.
        Assert.True(TimeLimited.TryRun<string?>(
            () => throw new InvalidOperationException("kinyerési hiba"),
            TimeSpan.FromSeconds(5),
            out string? result));
        Assert.Null(result);
    }
}
