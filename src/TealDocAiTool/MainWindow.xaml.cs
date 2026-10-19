using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;
using TealDocAiTool.Common;
using TealDocAiTool.Engine;
using TealDocAiTool.Interop;
using TealDocAiTool.Models;
using TealDocAiTool.Services;

namespace TealDocAiTool;

public partial class MainWindow : Window
{
    private const int HistoryLength = 60; // ~60 mp-nyi előzmény 1 mp-es mintavételnél

    private HardwareMonitorService? _hw;
    private readonly CancellationTokenSource _hwCts = new();
    private Task? _hwLoop;
    private readonly ObservableCollection<double> _cpuHistory = new();
    private readonly ObservableCollection<double> _gpuHistory = new();

    private CancellationTokenSource? _runCts;
    private int _heuristicCount;
    private int _llmCount;
    private int _keptCount;
    private int _failedCount;
    private int _shownProcessed;
    private int _shownMachines;
    private int _shownIdxFiles;

    public MainWindow() : this(startBackgroundServices: true) { }

    /// <param name="startBackgroundServices">A tesztek és a képernyőkép-készítés hamisat adnak:
    /// ilyenkor nem indul a hardvermonitor és az Ollama-ellenőrzés (nincs valódi gépadat a képen).</param>
    internal MainWindow(bool startBackgroundServices)
    {
        InitializeComponent();
        MaximizeBounds.Apply(this);
        TrackPageMinHeight(RenameScroll, RenamePage);
        TrackPageMinHeight(IndexScroll, IndexPage);
        InitSignature();
        SetupCharts();
        if (!startBackgroundServices) return;
        _ = StartHardwareMonitoringAsync();
        _ = CheckOllamaStatusAsync();
    }

    /// <summary>
    /// A lap legkisebb természetes magassága (üres napló, minimális naplómagasság mellett). Ennél
    /// alacsonyabb ablakban a lap ezen a magasságon marad, és a görgetősáv jelenik meg — korábban a
    /// gombok és a napló egyszerűen az ablakon kívülre kerültek (C-18, 5.3).
    /// </summary>
    private void TrackPageMinHeight(ScrollViewer scroll, Border page)
    {
        FitPageMinHeight(page, MinWidth);
        // A tördelő sorok miatt a szükséges magasság a szélességtől függ: szélességváltozáskor újramérjük
        // (a függőleges görgetősáv helyét levonva, hogy a becslés inkább bő legyen, mint szűk).
        scroll.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) FitPageMinHeight(page, e.NewSize.Width - SystemParameters.VerticalScrollBarWidth);
        };
    }

    private static void FitPageMinHeight(Border page, double width)
    {
        var content = (FrameworkElement)page.Child;
        content.Measure(new Size(Math.Max(0, width - page.Padding.Left - page.Padding.Right), double.PositiveInfinity));
        page.MinHeight = Math.Ceiling(content.DesiredSize.Height + page.Padding.Top + page.Padding.Bottom);
    }

    /// <summary>Csak a jelzőfényhez: az átnevezés heurisztikája Ollama nélkül is működik.</summary>
    private async Task CheckOllamaStatusAsync()
    {
        bool available = OllamaClient.ValidateHost(TxtOllamaHost.Text, out _) is null &&
            await new OllamaClient(OllamaClient.SharedHttp, TxtOllamaHost.Text, TxtOllamaModel.Text)
                .IsAvailableAsync(CancellationToken.None);
        OllamaDot.Fill = (Brush)FindResource(available ? "GreenBrush" : "RedBrush");
        TxtOllamaStatus.Text = available ? $"elérhető ({TxtOllamaModel.Text})" : "nem elérhető";
    }

    // ===================== Szerzői névjegy (lábléc) =====================

    private void InitSignature()
    {
        // A verzió egyetlen forrása a csproj <Version>-je (AppInfo) — itt sincs beégetve.
        SignatureText.Text = AppInfo.Signature;
        AboutVersion.Text = "v" + AppInfo.Version;

        BtnSignature.MouseEnter += (_, _) => SetSignatureGlow(true);
        BtnSignature.MouseLeave += (_, _) => SetSignatureGlow(BtnSignature.IsKeyboardFocused);
        BtnSignature.GotKeyboardFocus += (_, _) => SetSignatureGlow(true);
        BtnSignature.LostKeyboardFocus += (_, _) => SetSignatureGlow(BtnSignature.IsMouseOver);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !AboutPopup.IsOpen) return;
            AboutPopup.IsOpen = false;
            e.Handled = true;
        };
    }

    /// <summary>A téma akcentszínű ragyogása ~180 ms alatt; ha a Windowsban ki vannak kapcsolva az
    /// animációk, azonnal (a felirat így is használható marad).</summary>
    private void SetSignatureGlow(bool on)
    {
        var duration = SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
        SignatureGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(on ? 0.85 : 0, duration));
        SignatureText.Foreground = (Brush)FindResource(on ? "TealBrush" : "TextSecondaryBrush");
    }

    // Mindig nyit (nem vált): a StaysOpen=False popup a gombra kattintáskor már bezárult volna.
    private void BtnSignature_Click(object sender, RoutedEventArgs e) => AboutPopup.IsOpen = true;

    private void BtnAboutClose_Click(object sender, RoutedEventArgs e) => AboutPopup.IsOpen = false;

    // ===================== Címsor =====================

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool max = WindowState == WindowState.Maximized;
        BtnMaximize.Content = max ? "" : ""; // Segoe MDL2: ChromeRestore / ChromeMaximize
        BtnMaximize.ToolTip = max ? "Előző méret" : "Teljes méret";
    }

    // ===================== Hardver-telemetria =====================

    private void SetupCharts()
    {
        for (int i = 0; i < HistoryLength; i++) { _cpuHistory.Add(0); _gpuHistory.Add(0); }

        var teal = SKColor.Parse("#2DD4BF");
        var tealFill = SKColor.Parse("#2DD4BF").WithAlpha(40);

        CpuChart.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _cpuHistory,
                Fill = new SolidColorPaint(tealFill),
                Stroke = new SolidColorPaint(teal, 2),
                GeometrySize = 0,
                LineSmoothness = 0.3,
            }
        };
        CpuChart.YAxes = new[] { new Axis { MinLimit = 0, MaxLimit = 100, IsVisible = false } };
        CpuChart.XAxes = new[] { new Axis { IsVisible = false } };
        CpuChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
        CpuChart.TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Hidden;

        GpuChart.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _gpuHistory,
                Fill = new SolidColorPaint(tealFill),
                Stroke = new SolidColorPaint(teal, 2),
                GeometrySize = 0,
                LineSmoothness = 0.3,
            }
        };
        GpuChart.YAxes = new[] { new Axis { MinLimit = 0, MaxLimit = 100, IsVisible = false } };
        GpuChart.XAxes = new[] { new Axis { IsVisible = false } };
        GpuChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
        GpuChart.TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Hidden;
    }

    /// <summary>
    /// A LibreHardwareMonitor indítása (~1,2 s) és a mintavétel (~75 ms) háttérszálon fut — korábban
    /// a UI-szálat foglalta. Ha a könyvtár nem indul (driver, jogosultság), a panel jelzi, az app megy tovább.
    /// </summary>
    private async Task StartHardwareMonitoringAsync()
    {
        try
        {
            _hw = await Task.Run(() => new HardwareMonitorService());
        }
        catch (Exception ex)
        {
            TxtElevated.Text = "Hardver-monitorozás nem elérhető: " + ex.Message;
            return;
        }
        _hwLoop = Task.Run(() => PollHardwareLoopAsync(_hwCts.Token));
    }

    private async Task PollHardwareLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                HardwareSnapshot? snap = null;
                try { snap = _hw?.ReadSnapshot(); }
                catch { /* egy sikertelen mintavétel nem állíthatja meg a monitorozást */ }
                // InvokeAsync, nem Invoke: a háttérszál sosem vár a UI-szálra (bezáráskor sem).
                if (snap is not null) _ = Dispatcher.InvokeAsync(() => ShowHardwareSnapshot(snap));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    private void ShowHardwareSnapshot(HardwareSnapshot snap)
    {
        TxtCpuPct.Text = $"{snap.CpuLoadPercent:0} %";
        TxtCpuTemp.Text = snap.CpuTempCelsius is { } ct ? $"{ct:0} °C" : "-- °C";
        // A LibreHardwareMonitor a CPU-hőmérséklethez a PawnIO drivert használja — rendszergazdai
        // jog önmagában kevés, ezért a régi „indítsd rendszergazdaként” tanács félrevezető volt.
        TxtElevated.Text = snap.CpuTempCelsius is null
            ? "A CPU-hőmérséklet nem olvasható: ehhez a PawnIO driver (és rendszergazdai jog) szükséges."
            : "";
        PushHistory(_cpuHistory, snap.CpuLoadPercent);

        if (snap.HasGpu)
        {
            TxtGpuPct.Text = $"{snap.GpuLoadPercent:0} %";
            TxtGpuTemp.Text = snap.GpuTempCelsius is { } gt ? $"{gt:0} °C" : "-- °C";
            TxtGpuName.Text = snap.GpuName;
            TxtVram.Text = snap.GpuMemTotalMb > 0
                ? $"VRAM: {snap.GpuMemUsedMb:0} / {snap.GpuMemTotalMb:0} MB"
                : "";
            PushHistory(_gpuHistory, snap.GpuLoadPercent);
        }
        else
        {
            TxtGpuPct.Text = "--";
            TxtGpuTemp.Text = "";
            TxtGpuName.Text = "Nincs támogatott GPU";
            TxtVram.Text = "";
        }
    }

    private static void PushHistory(ObservableCollection<double> history, double value)
    {
        history.Add(value);
        if (history.Count > HistoryLength) history.RemoveAt(0);
    }

    /// <summary>Futó köteg mellett a bezárás kérdez, és leállítja a futást (C-16).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_runCts is not null || _idxCts is not null)
        {
            var answer = MessageBox.Show(
                "Egy feldolgozás még fut. Leállítod, és kilépsz?\n\nA már kész fájlok a naplóban, illetve a gyorsítótárban maradnak.",
                "Kilépés", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _runCts?.Cancel();
            _idxCts?.Cancel();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hwCts.Cancel();
        try { _hwLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* a leállás nem dobhat */ }
        _hw?.Dispose();
        base.OnClosed(e);
    }

    // ===================== Közös beviteli ellenőrzések =====================

    /// <summary>Számmező tartományba szorítva; ha javítani kellett, a mező a használt értéket mutatja,
    /// és a napló jelzi (C-15).</summary>
    private static int ReadNumber(TextBox box, string label, int min, int max, int fallback, Action<string> log)
    {
        int value = InputParsing.ParseClamped(box.Text, min, max, fallback, out bool adjusted);
        if (adjusted)
        {
            log($"Figyelem: a(z) „{label}” mező értéke ({box.Text}) érvénytelen vagy {min}–{max} tartományon kívüli; {value} lesz használva.");
            box.Text = value.ToString();
        }
        return value;
    }

    /// <summary>Az Ollama-cím ellenőrzése; távoli gépnél a felhasználó dönt (S-6).</summary>
    /// <returns><c>false</c>, ha a futás nem indulhat.</returns>
    private static bool ConfirmOllamaHost(string host, Action<string> log)
    {
        string? error = OllamaClient.ValidateHost(host, out bool isRemote);
        if (error is not null)
        {
            log("Hiba: " + error);
            return false;
        }
        if (!isRemote) return true;

        return MessageBox.Show(
            $"Az Ollama címe nem a helyi gépre mutat:\n{host}\n\nA dokumentumok szövege titkosítatlanul erre a gépre kerül. Folytatod?",
            "Távoli Ollama-cím", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    // ===================== Átnevezés fül =====================

    private void BtnPickTarget_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Célmappa kiválasztása" };
        if (dlg.ShowDialog() == true) SetTargetFolder(dlg.FolderName);
    }

    private void BtnPickLogDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Napló mappa kiválasztása" };
        if (dlg.ShowDialog() == true)
        {
            TxtLogDir.Text = dlg.FolderName;
        }
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        // A gomb AZONNAL tilt (C-7): korábban csak a ~4 s-os Ollama-ellenőrzés UTÁN, és egy második
        // kattintás addig egy párhuzamos második futást indított ugyanarra a mappára.
        if (_runCts is not null) return;
        _runCts = new CancellationTokenSource();
        BtnStart.IsEnabled = false;
        BtnUndo.IsEnabled = false;
        try
        {
            await RunRenameAsync(_runCts.Token);
        }
        finally
        {
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            BtnUndo.IsEnabled = true;
            _runCts.Dispose();
            _runCts = null;
        }
    }

    private async Task RunRenameAsync(CancellationToken runToken)
    {
        if (string.IsNullOrWhiteSpace(TxtTarget.Text) || !Directory.Exists(TxtTarget.Text))
        {
            AppendLog("Hiba: érvénytelen célmappa.");
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtLogDir.Text))
        {
            AppendLog("Hiba: nincs napló mappa megadva.");
            return;
        }
        if (!ConfirmOllamaHost(TxtOllamaHost.Text, AppendLog)) return;
        int workers = ReadNumber(TxtWorkers, "Párhuzamos", 1, 32, 4, AppendLog);
        int timeoutSeconds = ReadNumber(TxtTimeout, "Időkorlát (s)", 5, 600, 60, AppendLog);

        var settings = new RunSettings(
            TxtTarget.Text, TxtLogDir.Text, TxtOllamaHost.Text, TxtOllamaModel.Text,
            workers, ChkRecursive.IsChecked == true, ChkDryRun.IsChecked == true);

        _shownProcessed = 0;
        _heuristicCount = 0;
        _llmCount = 0;
        _keptCount = 0;
        _failedCount = 0;
        LlmStatusText.Text = "Heurisztika: 0 · LLM: 0";
        BtnStop.IsEnabled = true;
        TxtStatus.Text = "Fut...";
        AppendLog($"Indítás: {settings.TargetFolder}" + (settings.DryRun ? " (előnézet, nem nevez át)" : ""));
        _ = CheckOllamaStatusAsync(); // csak a jelzőfény; a futás nem vár rá

        var progress = new Progress<RunProgress>(OnRunProgress);
        var ollama = new OllamaClient(OllamaClient.SharedHttp, settings.OllamaHost, settings.OllamaModel,
            TimeSpan.FromSeconds(timeoutSeconds));
        // A RunCoordinator LLM-callbackje egyparaméteres (csak a kérés) — a futás token-jét
        // lambdával zárjuk rá, így a Leállítás a folyamatban lévő Ollama-hívást is megszakítja.
        var coordinator = new RunCoordinator(request => ollama.GenerateAsync(request, runToken));

        try
        {
            await coordinator.RunAsync(settings, progress, runToken);
            TxtStatus.Text = "Kész.";
            AppendLog("Kész.");
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Megszakítva.";
            AppendLog("Leállítva a felhasználó kérésére.");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Hiba.";
            AppendLog("Hiba: " + ex.Message);
        }
    }

    // ===================== Ráhúzás: mappa vagy egyfájlos gyors mód =====================

    private const int MaxDroppedFiles = 10;

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Mappa ráhúzása: célmappa (az Index fülön gyökérmappa). Fájl(ok) ráhúzása az Átnevezés
    /// fülön: fájlonként javaslat → megerősítés (Enter = igen) → átnevezés ugyanazzal a write-ahead
    /// naplóval, mint a kötegben, tehát a Visszavonás ezt is visszaállítja.
    /// </summary>
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        bool indexTab = Tabs.SelectedIndex == 1;

        if (paths.Length == 1 && Directory.Exists(paths[0]))
        {
            if (indexTab) TxtIdxRoot.Text = paths[0];
            else SetTargetFolder(paths[0]);
            return;
        }
        if (indexTab || _runCts is not null) return;

        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return;
        if (files.Count > MaxDroppedFiles)
        {
            AppendLog($"Egyszerre legfeljebb {MaxDroppedFiles} fájlt húzz ide — sok fájlhoz add meg a mappájukat célmappaként.");
            return;
        }
        if (!ConfirmOllamaHost(TxtOllamaHost.Text, AppendLog)) return;
        if (string.IsNullOrWhiteSpace(TxtLogDir.Text)) TxtLogDir.Text = DefaultLogFolder(Path.GetDirectoryName(files[0])!);
        int timeoutSeconds = ReadNumber(TxtTimeout, "Időkorlát (s)", 5, 600, 60, AppendLog);

        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        BtnStart.IsEnabled = false;
        BtnUndo.IsEnabled = false;
        try
        {
            string logFolder = TxtLogDir.Text;
            var ollama = new OllamaClient(OllamaClient.SharedHttp, TxtOllamaHost.Text, TxtOllamaModel.Text,
                TimeSpan.FromSeconds(timeoutSeconds));
            var coordinator = new RunCoordinator(request => ollama.GenerateAsync(request, token));
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                TxtStatus.Text = $"Javaslat készül: {name}…";
                var proposal = await coordinator.ProposeAsync(file, token);
                if (proposal.NewFileName is null)
                {
                    AppendLog($"{name} -> {proposal.Decision.FailureReason}");
                    continue;
                }

                var answer = MessageBox.Show(
                    $"Javasolt új név:\n\n{proposal.NewFileName}\n\nEredeti: {name}\n\nÁtnevezed? (Enter = Igen)",
                    "Átnevezés", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
                if (answer != MessageBoxResult.Yes)
                {
                    AppendLog($"{name} -> kihagyva (nem hagytad jóvá)");
                    continue;
                }
                await Task.Run(() => RunCoordinator.ApplyProposal(proposal, logFolder), token);
                AppendLog($"{name} -> {proposal.NewFileName} (naplózva, visszavonható)");
            }
            TxtStatus.Text = "Kész.";
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Megszakítva.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Hiba.";
            AppendLog("Hiba: " + ex.Message);
        }
        finally
        {
            BtnStart.IsEnabled = true;
            BtnUndo.IsEnabled = true;
            _runCts.Dispose();
            _runCts = null;
        }
    }

    private void SetTargetFolder(string folder)
    {
        TxtTarget.Text = folder;
        if (string.IsNullOrWhiteSpace(TxtLogDir.Text)) TxtLogDir.Text = DefaultLogFolder(folder);
    }

    /// <summary>A napló alapértelmezett helye a célmappa MELLETT (nem benne), hogy a köteg ne
    /// dolgozza fel a saját naplóját.</summary>
    private static string DefaultLogFolder(string folder)
    {
        string parent = Directory.GetParent(folder.TrimEnd('\\', '/'))?.FullName ?? folder;
        return Path.Combine(parent, "TealDoc_naplo");
    }

    private void OnRunProgress(RunProgress p)
    {
        // A párhuzamos munkások jelzései nem sorrendben érkeznek: a kijelzés csak nőhet.
        _shownProcessed = Math.Max(_shownProcessed, p.Processed);
        Progress.Maximum = Math.Max(p.Total, 1);
        Progress.Value = _shownProcessed;
        TxtCounts.Text = $"{_shownProcessed} / {p.Total}";

        if (p.LastMethod == RenameMethod.Heuristic) _heuristicCount++;
        else if (p.LastMethod == RenameMethod.Llm) _llmCount++;
        else if (p.LastMethod == RenameMethod.Kept) _keptCount++;
        else if (p.LastMethod == RenameMethod.Failed) _failedCount++;
        LlmStatusText.Text = $"Heurisztika: {_heuristicCount} · LLM: {_llmCount} · Megtartva: {_keptCount} · Sikertelen: {_failedCount}";

        AppendLog($"{Path.GetFileName(p.CurrentFile)} -> {p.Message}");
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _runCts?.Cancel();
        AppendLog("Leállítás kérve — a folyamatban lévő fájlok után áll meg.");
    }

    /// <summary>
    /// Aszinkron és védett: a napló olvasása és a fájlok visszamozgatása háttérszálon fut (egy
    /// 15 000 soros naplónál a UI különben percekig állna), és semmilyen hiba nem omlaszthatja
    /// össze az appot — a sérült naplósorok kimaradnak, a hibák a naplódobozba kerülnek.
    /// </summary>
    private async void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_runCts is not null || !BtnUndo.IsEnabled) return;
        if (string.IsNullOrWhiteSpace(TxtLogDir.Text))
        {
            AppendLog("Hiba: nincs napló mappa megadva.");
            return;
        }

        string logPath = RenameLog.PathIn(TxtLogDir.Text);
        BtnUndo.IsEnabled = false;
        BtnStart.IsEnabled = false;
        try
        {
            var content = await Task.Run(() => RenameLog.Read(logPath));
            if (content.SkippedLines > 0)
                AppendLog($"Figyelem: {content.SkippedLines} sérült naplósor kimaradt a feldolgozásból.");

            var active = RenameLog.ActiveRenames(content.Entries);
            if (active.Count == 0)
            {
                AppendLog("Nincs visszavonható bejegyzés a naplóban.");
                return;
            }

            string? runFilter = AskUndoScope(active, RenameLog.LastRunId(content.Entries), out bool cancelled);
            if (cancelled) return;

            TxtStatus.Text = "Visszavonás…";
            var result = await Task.Run(() => new UndoService().Undo(logPath, runFilter));
            TxtStatus.Text = "Visszavonás kész.";
            AppendLog($"Visszavonva: {result.Restored}, kihagyva (hiányzó fájl): {result.SkippedMissing}, hiba: {result.Errors.Count}. " +
                      "A visszavont sorok a naplóban „Undone” jelölést kaptak.");
            foreach (var err in result.Errors) AppendLog("  " + err);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Hiba.";
            AppendLog("Hiba a visszavonás közben: " + ex.Message);
        }
        finally
        {
            BtnUndo.IsEnabled = true;
            BtnStart.IsEnabled = true;
        }
    }

    /// <summary>Ha több futás átnevezései is visszavonhatók, megkérdezi, mire vonatkozzon a
    /// visszavonás. <c>null</c> = minden futás; különben a legutóbbi futás azonosítója.</summary>
    private static string? AskUndoScope(List<RenameLogEntry> active, string? lastRunId, out bool cancelled)
    {
        int lastRunCount = active.Count(x => x.RunId == lastRunId);
        if (lastRunCount == active.Count)
        {
            var confirm = MessageBox.Show(
                $"{active.Count} fájl kerül vissza az eredeti nevére. Folytatod?",
                "Visszavonás megerősítése", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            cancelled = confirm != MessageBoxResult.Yes;
            return null;
        }

        var scope = MessageBox.Show(
            $"Visszavonható átnevezés: {active.Count} fájl, ebből a legutóbbi futásé {lastRunCount}.\n\n" +
            $"Igen – csak a legutóbbi futás visszavonása ({lastRunCount} fájl)\n" +
            $"Nem – minden futás visszavonása ({active.Count} fájl)\n" +
            "Mégse – semmi ne történjen",
            "Visszavonás", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        cancelled = scope == MessageBoxResult.Cancel;
        return scope == MessageBoxResult.Yes ? lastRunId : null;
    }

    private readonly Queue<string> _logLines = new();

    /// <summary>Ugyanaz a korlát, mint az Index fülön: egy 15 000 fájlos átnevezés naplója
    /// korlátlanul nőne a memóriában. Itt nem kell fájlba írni — a teljes, visszavonáshoz is
    /// használt napló amúgy is CSV-be kerül.</summary>
    private void AppendLog(string text)
    {
        _logLines.Enqueue(text);
        if (_logLines.Count > MaxIdxLogLines)
        {
            while (_logLines.Count > MaxIdxLogLines) _logLines.Dequeue();
            TxtLog.Text = string.Join(Environment.NewLine, _logLines) + Environment.NewLine;
        }
        else
        {
            TxtLog.AppendText(text + Environment.NewLine);
        }
        TxtLog.ScrollToEnd();
    }

    // ===================== Index fül =====================
    private CancellationTokenSource? _idxCts;

    private void RbMode_Changed(object sender, RoutedEventArgs e)
    {
        // A mód-választó a betöltés előtt is tüzelhet; védekezünk a null ellen.
        if (PanelFolderOut is null || PanelCsvOut is null) return;
        bool perFile = RbModeCsv.IsChecked != true;   // Word VAGY Markdown — mindkettő mappába ír
        PanelFolderOut.Visibility = perFile ? Visibility.Visible : Visibility.Collapsed;
        PanelCsvOut.Visibility = perFile ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnIdxPickRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Gyökérmappa kiválasztása (almappánként egy egység)" };
        if (dlg.ShowDialog() != true) return;
        TxtIdxRoot.Text = dlg.FolderName;
        var parent = Directory.GetParent(dlg.FolderName.TrimEnd('\\', '/'))?.FullName ?? dlg.FolderName;
        if (string.IsNullOrWhiteSpace(TxtIdxOutFolder.Text))
            TxtIdxOutFolder.Text = Path.Combine(parent, "TealDoc_index");
        if (string.IsNullOrWhiteSpace(TxtIdxCsv.Text))
            TxtIdxCsv.Text = Path.Combine(parent, "tealdoc_index.csv");
        if (string.IsNullOrWhiteSpace(TxtIdxCache.Text))
            TxtIdxCache.Text = Path.Combine(parent, "TealDoc_index_cache");
    }

    private void BtnIdxPickOutFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Kimeneti mappa (egységenkénti fájlok)" };
        if (dlg.ShowDialog() == true) TxtIdxOutFolder.Text = dlg.FolderName;
    }

    private void BtnIdxPickCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "CSV-táblázat mentése",
            Filter = "CSV fájl (*.csv)|*.csv",
            FileName = string.IsNullOrWhiteSpace(TxtIdxCsv.Text) ? "tealdoc_index.csv" : Path.GetFileName(TxtIdxCsv.Text),
        };
        if (dlg.ShowDialog() == true) TxtIdxCsv.Text = dlg.FileName;
    }

    private async void BtnIdxStart_Click(object sender, RoutedEventArgs e)
    {
        // Azonnali tiltás, mint az Átnevezés fülön (C-7).
        if (_idxCts is not null) return;
        _idxCts = new CancellationTokenSource();
        BtnIdxStart.IsEnabled = false;
        try
        {
            await RunIndexAsync(_idxCts.Token);
        }
        finally
        {
            BtnIdxStart.IsEnabled = true;
            BtnIdxStop.IsEnabled = false;
            _idxCts.Dispose();
            _idxCts = null;
        }
    }

    private async Task RunIndexAsync(CancellationToken runToken)
    {
        if (string.IsNullOrWhiteSpace(TxtIdxRoot.Text) || !Directory.Exists(TxtIdxRoot.Text))
        {
            AppendIdxLog("Hiba: érvénytelen gyökérmappa.");
            return;
        }

        var mode =
            RbModeWord.IsChecked == true ? IndexOutputMode.WordFiles :
            RbModeMarkdown.IsChecked == true ? IndexOutputMode.MarkdownFiles :
            IndexOutputMode.Csv;
        bool perFileMode = mode != IndexOutputMode.Csv;

        if (perFileMode && string.IsNullOrWhiteSpace(TxtIdxOutFolder.Text))
        {
            AppendIdxLog("Hiba: nincs kimeneti mappa megadva.");
            return;
        }
        if (!perFileMode && string.IsNullOrWhiteSpace(TxtIdxCsv.Text))
        {
            AppendIdxLog("Hiba: nincs kimeneti CSV megadva.");
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtIdxCache.Text))
        {
            string basis = perFileMode ? TxtIdxOutFolder.Text : Path.GetDirectoryName(TxtIdxCsv.Text)!;
            TxtIdxCache.Text = Path.Combine(basis, "TealDoc_index_cache");
        }
        bool needsLlm = ChkIdxMachineType.IsChecked == true || ChkIdxFileSummaries.IsChecked == true;
        if (needsLlm && !ConfirmOllamaHost(TxtIdxOllamaHost.Text, AppendIdxLog)) return;
        int workers = ReadNumber(TxtIdxWorkers, "Párhuzamos", 1, 16, 4, AppendIdxLog);
        int maxFiles = ReadNumber(TxtIdxMaxFiles, "Max. leírt fájl / gép", 0, 100_000, 0, AppendIdxLog);
        int timeoutSeconds = ReadNumber(TxtIdxTimeout, "Időkorlát (s)", 5, 600, 120, AppendIdxLog);

        // A hivatkozás-alap URL-t a futás ELŐTT ellenőrizzük. Egy rossz cím némán végigmenne, és
        // több ezer kimeneti fájlba írna használhatatlan hivatkozást — az újragenerálás órákba kerülne.
        string spRaw = TxtIdxSharePointUrl.Text?.Trim() ?? "";
        string spBase = SharePointUrlBuilder.NormalizeBase(spRaw);
        if (spRaw.Length > 0 && spBase.Length == 0)
        {
            AppendIdxLog(SharePointUrlBuilder.IsSharingLink(spRaw)
                ? "Hiba: ez egy megosztási (Megosztás gombbal másolt) link. Ebből a fájlok címe nem állítható elő, mert a mappát egy token azonosítja. Nyisd meg a mappát a böngészőben, és a CÍMSORBÓL másold ki a linket."
                : "Hiba: a hivatkozás-alap nem érvényes webcím. Teljes http(s) cím kell.");
            return;
        }

        var settings = new IndexSettings(
            RootFolder: TxtIdxRoot.Text,
            OutputMode: mode,
            OutputCsvPath: TxtIdxCsv.Text ?? "",
            OutputFolder: TxtIdxOutFolder.Text ?? "",
            CacheFolder: TxtIdxCache.Text,
            OllamaHost: TxtIdxOllamaHost.Text,
            OllamaModel: TxtIdxOllamaModel.Text,
            Workers: workers,
            GenerateMachineType: ChkIdxMachineType.IsChecked == true,
            GenerateFileSummaries: ChkIdxFileSummaries.IsChecked == true,
            MaxFilesPerMachineForSummary: maxFiles,
            CategoryHint: TxtIdxCategoryHint.Text?.Trim() ?? "",
            SharePointBaseUrl: spBase,
            LinkEachDocument: ChkIdxLinkDocs.IsChecked == true);

        // A teljes napló fájlba megy, hogy a képernyőn korlátozhassuk a sorok számát.
        try
        {
            Directory.CreateDirectory(settings.CacheFolder);
            _idxLogFile = Path.Combine(settings.CacheFolder, "index_naplo.txt");
        }
        catch (Exception) { _idxLogFile = null; }

        BtnIdxStop.IsEnabled = true;
        _shownMachines = 0;
        _shownIdxFiles = 0;
        TxtIdxStatus.Text = "Fut...";
        AppendIdxLog($"Index indítása: {settings.RootFolder}");
        string modeLabel = mode switch
        {
            IndexOutputMode.WordFiles => "DOCX-kártyák",
            IndexOutputMode.MarkdownFiles => "Markdown",
            _ => "CSV-táblázat",
        };
        AppendIdxLog($"Mód: {modeLabel} · Típus: {(settings.GenerateMachineType ? "igen" : "nem")} · Fájl-leírások: {(settings.GenerateFileSummaries ? "igen" : "nem")}");
        AppendIdxLog(settings.CategoryHint.Length > 0
            ? $"Típus-támpont: „{settings.CategoryHint}”"
            : "Típus-támpont: nincs — a típus csak a dokumentumokból jöhet.");
        AppendIdxLog(settings.SharePointBaseUrl.Length > 0
            ? $"Hivatkozás-alap: {settings.SharePointBaseUrl}"
            : "Hivatkozás-alap: nincs — a kimenetbe csak a gyökérhez képesti mappanevek kerülnek.");

        var ollama = new OllamaClient(OllamaClient.SharedHttp, settings.OllamaHost, settings.OllamaModel,
            TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            // Futás előtti próba (C-5): kiesett Ollama mellett korábban minden gép „Kész.”-szel,
            // üres géptípussal készült el — és a gyorsítótárba is került.
            if (needsLlm)
            {
                TxtIdxStatus.Text = "Az Ollama ellenőrzése…";
                string? problem = await ollama.ProbeAsync(runToken);
                if (problem is not null)
                {
                    TxtIdxStatus.Text = "Hiba: az Ollama nem használható.";
                    AppendIdxLog($"Hiba: az Ollama nem használható ({problem}). Indítsd el az Ollamát és töltsd le a modellt, " +
                                 "vagy kapcsold ki a típus- és a leírás-generálást.");
                    return;
                }
                TxtIdxStatus.Text = "Fut...";
            }

            var progress = new Progress<IndexProgress>(OnIndexProgress);
            var coordinator = new MachineIndexCoordinator(request => ollama.GenerateAsync(request, runToken));
            await coordinator.RunAsync(settings, progress, runToken);
            TxtIdxStatus.Text = "Kész.";
            if (perFileMode)
            {
                string ext = mode == IndexOutputMode.WordFiles ? ".docx" : ".md";
                AppendIdxLog($"Kész. A {ext} fájlok itt vannak: {settings.OutputFolder}");
            }
            else
            {
                AppendIdxLog($"Kész. A CSV elmentve ide: {settings.OutputCsvPath}");
            }
        }
        catch (OperationCanceledException)
        {
            TxtIdxStatus.Text = "Megszakítva.";
            AppendIdxLog("Leállítva. A kész egységek a gyorsítótárban maradtak — újraindításkor onnan folytatja.");
        }
        catch (LlmUnavailableException ex)
        {
            TxtIdxStatus.Text = "Hiba: az Ollama kiesett.";
            AppendIdxLog($"Hiba: {ex.Message} A futás leállt; a kész egységek a gyorsítótárban vannak, a többit a következő futás pótolja.");
        }
        catch (Exception ex)
        {
            TxtIdxStatus.Text = "Hiba.";
            AppendIdxLog("Hiba: " + ex.Message);
        }
    }

    private void OnIndexProgress(IndexProgress p)
    {
        if (p.TotalMachines > 0)
        {
            _shownMachines = Math.Max(_shownMachines, p.ProcessedMachines);
            _shownIdxFiles = Math.Max(_shownIdxFiles, p.ProcessedFilesTotal);
            IdxProgress.Maximum = Math.Max(p.TotalMachines, 1);
            IdxProgress.Value = _shownMachines;
            TxtIdxCounts.Text = $"Egységek: {_shownMachines} / {p.TotalMachines} · Feldolgozott fájl: {_shownIdxFiles}";
        }

        // Fájl-szintű jelzés: NEM naplózzuk. Egy teljes, valós archívumon ez 23 000 sort jelentene a
        // memóriában tartott napló-dobozban — egy nagy modell mellett, ami a RAM kétharmadát elviszi,
        // pont ez a fölösleges teher hiányzik. Helyette csak az állapotsort frissítjük.
        if (p.IsFileLevel)
        {
            if (!string.IsNullOrEmpty(p.CurrentMachine))
                TxtIdxStatus.Text = $"Fut… {p.CurrentMachine} — {p.Message}";
            return;
        }

        // Egység-szintű jelentés: egységenként egy naplósor.
        if (!string.IsNullOrEmpty(p.CurrentMachine))
            AppendIdxLog($"{p.CurrentMachine}: {p.Message}");
    }

    private void BtnIdxStop_Click(object sender, RoutedEventArgs e)
    {
        _idxCts?.Cancel();
        AppendIdxLog("Leállítás kérve — a folyamatban lévő egységek után áll meg.");
    }

    /// <summary>Hány sort tartunk a képernyőn. A teljes napló fájlba megy, tehát semmi nem vész el.</summary>
    private const int MaxIdxLogLines = 300;

    private readonly Queue<string> _idxLogLines = new();
    private string? _idxLogFile;

    /// <summary>
    /// Korlátos napló: a doboz csak az utolsó <see cref="MaxIdxLogLines"/> sort tartja, a teljes
    /// szöveg viszont a gyorsítótár mappába íródik (`index_naplo.txt`). Így egy több ezer gépes
    /// futás naplója sem nő korlátlanul a memóriában, de utólag minden visszakereshető.
    /// </summary>
    private void AppendIdxLog(string text)
    {
        if (_idxLogFile is not null)
        {
            try { File.AppendAllText(_idxLogFile, text + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { /* a napló-írás soha ne állítsa meg a futást */ }
            catch (UnauthorizedAccessException) { }
        }

        _idxLogLines.Enqueue(text);
        if (_idxLogLines.Count > MaxIdxLogLines)
        {
            while (_idxLogLines.Count > MaxIdxLogLines) _idxLogLines.Dequeue();
            TxtIdxLog.Text = string.Join(Environment.NewLine, _idxLogLines) + Environment.NewLine;
        }
        else
        {
            TxtIdxLog.AppendText(text + Environment.NewLine);
        }
        TxtIdxLog.ScrollToEnd();
    }
}
