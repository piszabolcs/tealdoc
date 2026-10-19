using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TealDocAiTool;

public partial class App : Application
{
    public App()
    {
        // A cp1250 (Windows-1250) codepage a .NET-en regisztrációt igényel az
        // RtfTextExtractor magyar ékezet-dekódolásához.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Globális hálók: egy váratlan kivétel ne omlassza össze az appot némán. A felhasználó
        // üzenetet kap, a részletek a %LOCALAPPDATA%\TealDoc\hibanaplo.txt fájlba kerülnek.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) => { WriteCrashLog(e.Exception); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string? logPath = WriteCrashLog(e.Exception);
        MessageBox.Show(
            "Váratlan hiba történt, a művelet megszakadt:\n\n" + e.Exception.Message +
            (logPath is null ? "" : "\n\nRészletek: " + logPath),
            "TealDoc – hiba", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <returns>A hibanapló útja, vagy <c>null</c>, ha nem sikerült írni (ez sem dobhat).</returns>
    private static string? WriteCrashLog(Exception? ex)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TealDoc");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "hibanaplo.txt");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            return path;
        }
        catch
        {
            return null;
        }
    }
}
