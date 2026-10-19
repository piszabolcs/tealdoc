using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TealDocAiTool.Interop;

namespace TealDocAiTool.Tests.Gui;

/// <summary>Füst-tesztek a valódi főablakon — képernyőn kívül, megjelenítés nélkül (lásd <see cref="WpfUi"/>).</summary>
public class MainWindowGuiTests
{
    private static readonly (int Tab, string Scroll, string Start, string Log)[] Pages =
    {
        (0, "RenameScroll", "BtnStart", "TxtLog"),
        (1, "IndexScroll", "BtnIdxStart", "TxtIdxLog"),
    };

    // T-10: a legkisebb megengedett méreten (980×640) az Index fül Start gombja, állapotsora és
    // naplója korábban az ablakon kívülre került, az Átnevezés naplója pedig 0 magasra zsugorodott.
    [Fact]
    public Task MinimumSize_StartButtonsAndLogsAreReachableByScrolling() => WpfUi.RunAsync(() =>
    {
        var (window, root) = WpfUi.CreateMainWindow(980, 640);
        try
        {
            foreach (var page in Pages)
            {
                WpfUi.SelectTab(window, root, page.Tab);
                WpfUi.Find<ScrollViewer>(window, page.Scroll).ScrollToBottom();
                WpfUi.Layout(root, 980, 640);

                foreach (string name in new[] { page.Start, page.Log })
                {
                    var bounds = WpfUi.BoundsIn(WpfUi.Find<FrameworkElement>(window, name), root);
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= 640, $"{name} az ablakon kívül: {bounds}");
                }
                Assert.True(WpfUi.Find<FrameworkElement>(window, page.Log).ActualHeight >= 80);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // Alapméreten (1180×940) mindkét fül görgetés nélkül elfér — korábban az Index naplója
    // 65 DIP-pel az ablak alja alá lógott.
    [Fact]
    public Task DefaultSize_BothTabsFitWithoutScrolling() => WpfUi.RunAsync(() =>
    {
        var (window, root) = WpfUi.CreateMainWindow(1180, 940);
        try
        {
            foreach (var page in Pages)
            {
                WpfUi.SelectTab(window, root, page.Tab);
                Assert.Equal(0, WpfUi.Find<ScrollViewer>(window, page.Scroll).ScrollableHeight);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // C-7: két gyors kattintás korábban két párhuzamos futást indított ugyanarra a mappára.
    [Fact]
    public Task StartClickedTwice_StartsOnlyOneRun() => WpfUi.RunAsync(async () =>
    {
        string dir = Path.Combine(Path.GetTempPath(), "tealdoc_gui_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Kezelési útmutató.txt"), "beszédes nevű fájl");
        var (window, _) = WpfUi.CreateMainWindow(1180, 940);
        try
        {
            WpfUi.Find<TextBox>(window, "TxtTarget").Text = dir;
            WpfUi.Find<TextBox>(window, "TxtLogDir").Text = Path.Combine(dir, "napló");
            var start = WpfUi.Find<Button>(window, "BtnStart");

            start.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            start.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var status = WpfUi.Find<TextBlock>(window, "TxtStatus");
            await WpfUi.WaitUntil(() => status.Text == "Kész.", TimeSpan.FromSeconds(30));

            string log = WpfUi.Find<TextBox>(window, "TxtLog").Text;
            Assert.Single(Regex.Matches(log, "Indítás:"));
        }
        finally
        {
            window.Close();
            Directory.Delete(dir, recursive: true);
        }
    });

    // T-18: a lábléc névjegye és a névjegy-ablak verziója egyetlen forrásból (csproj <Version>)
    // jön; a címsor nem ismétli a verziót.
    [Fact]
    public Task Footer_ShowsTheSignatureFromTheSingleVersionSource() => WpfUi.RunAsync(() =>
    {
        var (window, _) = WpfUi.CreateMainWindow(1180, 940);
        try
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
            Assert.Equal($"psz // v{AppInfo.Version}", WpfUi.Find<TextBlock>(window, "SignatureText").Text);
            Assert.Equal($"v{AppInfo.Version}", WpfUi.Find<TextBlock>(window, "AboutVersion").Text);
            Assert.Equal("TealDoc", window.Title);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // Lágy görgetési szél: a napló a lekerekített kártyában csak arra halványul, amerre még van rejtett sor.
    [Fact]
    public Task Log_FadesOnlyTowardsHiddenLines() => WpfUi.RunAsync(() =>
    {
        var (window, root) = WpfUi.CreateMainWindow(1180, 940);
        try
        {
            var log = WpfUi.Find<TextBox>(window, "TxtLog");
            log.Text = string.Join(Environment.NewLine, Enumerable.Range(1, 200).Select(i => $"{i}. sor"));
            log.ScrollToEnd();
            WpfUi.Layout(root, 1180, 940);
            WpfUi.Layout(root, 1180, 940);

            var host = (ScrollViewer)log.Template.FindName("PART_ContentHost", log);
            var presenter = (ScrollContentPresenter)host.Template.FindName("PART_ScrollContentPresenter", host);
            var mask = Assert.IsType<LinearGradientBrush>(presenter.OpacityMask);
            Assert.Equal(0, mask.GradientStops[0].Color.A);   // fent rejtett sorok vannak
            Assert.Equal(255, mask.GradientStops[3].Color.A); // alul nincs több
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // C-18: a saját WM_GETMINMAXINFO-kezelő miatt a WPF a MinWidth/MinHeight-ot nem érvényesítette.
    [Fact]
    public void MinTrackSize_ConvertsDipToPhysicalPixels() =>
        Assert.Equal((1960, 1280), MaximizeBounds.MinTrackSize(980, 640, 2.0, 2.0));
}
