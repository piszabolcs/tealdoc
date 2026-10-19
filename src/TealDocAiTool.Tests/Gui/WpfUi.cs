using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TealDocAiTool.Tests.Gui;

/// <summary>
/// WPF a tesztekben, ablak MEGJELENÍTÉSE NÉLKÜL. Egyetlen, a folyamat élettartamára indított
/// STA-szálon fut minden UI-művelet (WPF-objektum csak a saját szálán használható); a
/// <see cref="MainWindow"/> tartalmát leválasztjuk az ablakról, és képernyőn kívül mérjük,
/// rendereljük. Az ablak sosem kap HWND-t, tehát a képernyőn sem jelenhet meg.
/// </summary>
internal static class WpfUi
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(StartUiThread);

    private static Dispatcher StartUiThread()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/TealDocAiTool;component/Themes/Teal.xaml"),
            });
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "TealDoc-teszt-UI",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    /// <summary>A műveletet a UI-szálon futtatja; az await-ek is ott folytatódnak.</summary>
    public static Task RunAsync(Func<Task> action) => UiDispatcher.Value.InvokeAsync(action).Task.Unwrap();

    /// <summary>Új főablak, a háttérszolgáltatások (hardvermonitor, Ollama-ellenőrzés) nélkül.
    /// Visszaadja az ablakról leválasztott, a megadott méretre rendezett tartalmat is.</summary>
    public static (MainWindow Window, FrameworkElement Root) CreateMainWindow(double width, double height)
    {
        var window = new MainWindow(startBackgroundServices: false);
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        Layout(root, width, height);
        return (window, root);
    }

    public static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    public static void SelectTab(MainWindow window, FrameworkElement root, int index)
    {
        ((TabControl)window.FindName("Tabs")).SelectedIndex = index;
        Layout(root, root.ActualWidth, root.ActualHeight);
    }

    /// <summary>Az elem téglalapja a gyökérhez képest (DIP).</summary>
    public static Rect BoundsIn(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    public static T Find<T>(MainWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);

    /// <summary>Várakozik (a UI-szálat nem blokkolva), amíg a feltétel teljesül.</summary>
    public static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("A felület nem jutott el a várt állapotba.");
            await Task.Delay(50);
        }
    }

    /// <summary>PNG-be renderel (2× pixelsűrűséggel, hogy nagy DPI-n is éles legyen).</summary>
    public static void SavePng(FrameworkElement root, string path, double scale = 2.0)
    {
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
