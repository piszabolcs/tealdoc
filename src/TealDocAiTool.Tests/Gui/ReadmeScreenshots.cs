using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DemoCorpus;

namespace TealDocAiTool.Tests.Gui;

/// <summary>
/// A README képernyőképei — CSAK kérésre fut (különben azonnal, sikeresen visszatér):
/// <code>$env:TEALDOC_SCREENSHOTS = "docs/screenshots"; dotnet test --filter ReadmeScreenshots</code>
/// A valódi főablakot rendereli képernyőn kívül, a kitalált demó-archívumon, egy helyi, hamis
/// Ollama-végponttal. A képeken a valódi (felhasználónevet tartalmazó) ideiglenes út helyett
/// <c>D:\Demo</c> látszik.
/// </summary>
public class ReadmeScreenshots
{
    private const string Alias = @"D:\Demo";
    private const double Scale = 1.5;

    [Fact]
    public Task Generate() => WpfUi.RunAsync(async () =>
    {
        string? outDir = Environment.GetEnvironmentVariable("TEALDOC_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        outDir = Path.GetFullPath(outDir, RepositoryRoot()); // relatív út a repó gyökeréhez képest

        string work = Path.Combine(Path.GetTempPath(), "tealdoc_shots_" + Guid.NewGuid().ToString("N"));
        string corpus = Path.Combine(work, "Archivum");
        using var ollama = new FakeOllamaServer();
        try
        {
            DemoCorpusGenerator.Generate(corpus);

            // 1) Átnevezés: előnézet (dry-run) — semmi nem nevez át, a napló mutatja a javaslatokat.
            var (window, root) = WpfUi.CreateMainWindow(1180, 940);
            Set(window, "TxtTarget", corpus);
            Set(window, "TxtLogDir", Path.Combine(work, "TealDoc_naplo"));
            Set(window, "TxtOllamaHost", ollama.BaseUrl);
            await RunAndWait(window, "BtnStart", "TxtStatus");
            Save(window, root, work, ollama, Path.Combine(outDir, "01_atnevezes_elonezet.png"));

            // 2) Átnevezés: éles futás ugyanazon az archívumon.
            WpfUi.Find<CheckBox>(window, "ChkDryRun").IsChecked = false;
            await RunAndWait(window, "BtnStart", "TxtStatus");
            Save(window, root, work, ollama, Path.Combine(outDir, "02_atnevezes_eles.png"));
            window.Close();

            // 3) Mappa-index: egységenkénti DOCX-kártyák (típus + fájlonkénti leírás).
            (window, root) = WpfUi.CreateMainWindow(1180, 940);
            WpfUi.SelectTab(window, root, 1);
            Set(window, "TxtIdxRoot", corpus);
            Set(window, "TxtIdxOutFolder", Path.Combine(work, "TealDoc_index"));
            Set(window, "TxtIdxOllamaHost", ollama.BaseUrl);
            await RunAndWait(window, "BtnIdxStart", "TxtIdxStatus");
            Save(window, root, work, ollama, Path.Combine(outDir, "03_mappa_index.png"));
            window.Close();

            // 4) Névjegy (a lábléc „psz // v…” feliratára kattintva) — a kártya önmagában renderelve.
            (window, _) = WpfUi.CreateMainWindow(1180, 940);
            var popup = WpfUi.Find<Popup>(window, "AboutPopup");
            var card = (FrameworkElement)popup.Child;
            popup.Child = null;
            card.Measure(new Size(card.Width, double.PositiveInfinity));
            WpfUi.Layout(card, card.Width, card.DesiredSize.Height);
            WpfUi.SavePng(card, Path.Combine(outDir, "04_nevjegy.png"), Scale);
            window.Close();
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    });

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TealDocAiTool.slnx"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    private static void Set(MainWindow window, string name, string text) => WpfUi.Find<TextBox>(window, name).Text = text;

    private static async Task RunAndWait(MainWindow window, string button, string status)
    {
        var statusText = WpfUi.Find<TextBlock>(window, status);
        statusText.Text = "";
        WpfUi.Find<Button>(window, button).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WpfUi.WaitUntil(() => statusText.Text is "Kész." or "Hiba." || statusText.Text.StartsWith("Hiba:"), TimeSpan.FromSeconds(120));
        Assert.Equal("Kész.", statusText.Text);
        await Task.Delay(500); // az Ollama-jelzőfény frissülése
    }

    /// <summary>A képen nem látszhat valódi út (benne a Windows-felhasználónévvel) és véletlen port:
    /// csak a render idejére cseréljük őket, utána visszaállítjuk (a következő futás a valódi utat kapja).</summary>
    private static void Save(MainWindow window, FrameworkElement root, string work, FakeOllamaServer ollama, string path)
    {
        var originals = new Dictionary<TextBox, string>();
        foreach (string name in new[] { "TxtTarget", "TxtLogDir", "TxtLog", "TxtIdxRoot", "TxtIdxOutFolder", "TxtIdxCache",
                     "TxtIdxCsv", "TxtIdxLog", "TxtOllamaHost", "TxtIdxOllamaHost" })
        {
            var box = WpfUi.Find<TextBox>(window, name);
            originals[box] = box.Text;
            box.Text = box.Text.Replace(work, Alias).Replace(ollama.BaseUrl, "http://localhost:11434");
        }
        WpfUi.Layout(root, root.ActualWidth, root.ActualHeight);
        WpfUi.SavePng(root, path, Scale);
        foreach (var (box, text) in originals) box.Text = text;
        WpfUi.Layout(root, root.ActualWidth, root.ActualHeight);
    }
}
