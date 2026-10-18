using System.Windows;
using System.Windows.Threading;
using Spindle.Core;
using Spindle.Interop;
using Spindle.UI;
using Spindle.Wheel;

namespace Spindle;

/// <summary>
/// Komut satırı:
///   (yok)                  ayarları açar (zaten çalışıyorsa çalışan kopyada)
///   --background           yalnızca tepsiye yerleşir (Windows ile başlarken)
///   --wheel  dosya...      dönüştürme çarkını imlecin yanında açar (sağ tık menüsü)
///   --tools  dosya...      araçlar çarkını açar
///   --convert id dosya...  arayüzsüz dönüştürür (ör. --convert to:pdf rapor.docx)
///   --snapshot çıktı.png convert|tools [üzerine-gelinen-dilim] dosya...   çarkı PNG'ye çizer
/// </summary>
public partial class App : Application
{
    AppSettings settings = new();
    ConversionService service = null!;
    ConversionRunner runner = null!;
    GlobalDragWatcher? watcher;
    TrayIcon? tray;
    WheelWindow? wheel;
    SettingsWindow? settingsWindow;
    TourWindow? tourWindow;
    Native.POINT wheelShownAt;
    // Yalnızca sürükleme çarkı açıkken çalışır; fare hareketleri başka hiçbir zaman izlenmez.
    readonly DispatcherTimer dragTracker = new() { Interval = TimeSpan.FromMilliseconds(50) };
    IdleMemory? idleMemory;

    // Sağ tık menüsü her dosya için ayrı istek gönderir; kısa bir süre toplayıp tek çark açarız.
    readonly List<string> pendingFiles = [];
    WheelMode pendingMode;
    DispatcherTimer? pendingTimer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        if (args.FirstOrDefault() == "--snapshot") { Snapshot(args); Shutdown(); return; }
        if (args.FirstOrDefault() == "--snapshot-ui") { SnapshotUi(Path.GetFullPath(args[1])); Shutdown(); return; }
        if (args.FirstOrDefault() == "--convert") { Shutdown(await ConvertHeadless(args)); return; }
        if (args.FirstOrDefault() == "--toast-demo") { await ToastDemo(args); Shutdown(); return; }
        if (args.FirstOrDefault() == "--render-icon") { RenderIcon(Path.GetFullPath(args[1])); Shutdown(); return; }
        if (args.FirstOrDefault() == "--render-tour") { Tour.TourRenderer.RenderAll(Path.GetFullPath(args[1])); Shutdown(); return; }
        if (args.FirstOrDefault() == "--export-sounds") { Sounds.Export(Path.GetFullPath(args[1])); Shutdown(); return; }

        if (args.FirstOrDefault() == "--uninstall") { Installer.Uninstall(); Shutdown(); return; }
        // İndirilen Spindle.exe ilk açılışta kendini kurar ve kurulu kopyayı başlatır.
        if (args.Length == 0 && Installer.InstallIfDownloaded()) { Shutdown(); return; }

        if (!SingleInstance.TryBecomePrimary())
        {
            SingleInstance.Forward(args);
            Shutdown();
            return;
        }

        Installer.EnsureRegistered();
        var firstRun = !AppSettings.Exists;
        settings = AppSettings.Load();
        if (firstRun) settings.Save();
        service = new ConversionService(new ConversionOptions { ImageQuality = settings.ImageQuality, FfmpegPath = settings.FfmpegPath });
        EngineAvailability.FfmpegCustomPath = settings.FfmpegPath;
        Sounds.Enabled = settings.SoundsEnabled;
        runner = new ConversionRunner(service);
        idleMemory = new IdleMemory(busy: () => runner.IsBusy);
        runner.Used += idleMemory.MarkUsed;

        tray = new TrayIcon(PickFiles, ShowSettings, ShowTour, Quit);

        watcher = new GlobalDragWatcher(Dispatcher) { Settings = () => settings };
        watcher.WheelRequested += OnDragWheelRequested;
        watcher.EscapePressed += () => wheel?.Close();
        dragTracker.Tick += (_, _) => TrackDrag();
        try { watcher.Start(); }
        catch (Exception ex) { ToastWindow.Info("Kısayol başlatılamadı", ex.Message, ""); }

        SingleInstance.Listen(Dispatcher, HandleArgs);

        // İlk açılışta (arka planda başlatılmadıysa) kısa tanıtım; ayarlar penceresi onun yerine açılmaz.
        if (!settings.TourShown && args.FirstOrDefault() is null or "--settings") ShowTour();
        else if (firstRun) ShowSettings();
        HandleArgs(args, launchedNow: true);
    }

    void HandleArgs(string[] args) => HandleArgs(args, launchedNow: false);

    void HandleArgs(string[] args, bool launchedNow)
    {
        var flag = args.FirstOrDefault();
        switch (flag)
        {
            case "--background":
                break;
            case "--wheel" or "--tools":
                QueueWheel(flag == "--tools" ? WheelMode.Tools : WheelMode.Convert, args.Skip(1));
                break;
            case "--quit":
                Quit();
                break;
            case "--tour":
                ShowTour();
                break;
            case "--settings" when tourWindow is null || !launchedNow:
            case null when !launchedNow:
                ShowSettings();
                break;
            case null:
                if (AppSettings.Exists && settingsWindow is null && tourWindow is null) ShowSettings();
                break;
        }
    }

    // ---------- sağ tık / tepsi: tıklama modlu çark ----------

    void QueueWheel(WheelMode mode, IEnumerable<string> files)
    {
        pendingMode = mode;
        pendingFiles.AddRange(files.Where(f => File.Exists(f) || Directory.Exists(f)));
        pendingTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Normal, (_, _) =>
        {
            pendingTimer!.Stop();
            var batch = pendingFiles.Distinct().ToList();
            pendingFiles.Clear();
            if (batch.Count > 0) ShowClickWheel(pendingMode, batch);
        }, Dispatcher);
        pendingTimer.Stop();
        pendingTimer.Start();
    }

    void ShowClickWheel(WheelMode mode, IReadOnlyList<string> files)
    {
        wheel?.Close();
        wheel = new WheelWindow(mode, files, dragMode: false);
        wheel.ActionChosen += runner.Run;
        wheel.Closed += (_, _) => wheel = null;
        wheel.ShowAt(Native.CursorPosition());
        idleMemory?.MarkUsed();
        if (wheel.Mode == WheelMode.Convert) ConversionService.Prewarm(files);
    }

    void PickFiles()
    {
        ShowSettings();
        settingsWindow?.PickFiles();
    }

    // ---------- sürükle + tuş ----------

    void OnDragWheelRequested(WheelMode mode, Native.POINT pt)
    {
        if (wheel is { DragMode: true })
        {
            wheel.SetMode(mode);
            return;
        }
        wheel?.Close();
        wheel = new WheelWindow(mode, null, dragMode: true);
        wheel.ActionChosen += runner.Run;
        wheel.Closed += (_, _) => wheel = null;
        // Sürüklenen dosya çarkın üstüne gelince türü belli olur; Office belgesiyse Office'i şimdiden başlat.
        wheel.FilesChanged += (w, files) => { if (w.Mode == WheelMode.Convert) ConversionService.Prewarm(files); };
        wheelShownAt = pt;
        wheel.ShowAt(pt);
        idleMemory?.MarkUsed();
        dragTracker.Start();
    }

    /// <summary>Sürükleme çarkı açıkken 50 ms'de bir: fare tuşu ve imleç konumuna bakar.</summary>
    void TrackDrag()
    {
        if (wheel is not { DragMode: true } current)
        {
            dragTracker.Stop();
            return;
        }

        // Çark açıldı ama hiç dosya gelmedi ve imleç uzaklaştı: bu bir dosya sürüklemesi değil (ör. seçim kutusu).
        var pt = Native.CursorPosition();
        if (!current.GotDragEnter && Math.Abs(pt.X - wheelShownAt.X) + Math.Abs(pt.Y - wheelShownAt.Y) > 80)
        {
            current.Close();
            return;
        }

        if (GlobalDragWatcher.PrimaryButtonDown()) return;

        // Fare bırakıldı. Bırakma çarkın dışında olduysa kapat; Drop olayı biraz gecikebilir.
        dragTracker.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (current.IsLoaded && !current.Dropped) current.Close();
        };
        timer.Start();
    }

    // ---------- pencereler ----------

    void ShowSettings()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(settings, files => ShowClickWheel(WheelMode.Convert, files));
            settingsWindow.TourRequested += ShowTour;
            settingsWindow.Closed += (_, _) =>
            {
                settingsWindow = null;
                service.Options.ImageQuality = settings.ImageQuality;
            };
            settingsWindow.Show();
            idleMemory?.MarkUsed();
        }
        if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
        settingsWindow.Activate();
    }

    void ShowTour()
    {
        if (tourWindow is null)
        {
            tourWindow = new TourWindow(settings);
            tourWindow.Closed += (_, _) => tourWindow = null;
            tourWindow.Show();
            idleMemory?.MarkUsed();
            if (!settings.TourShown)
            {
                settings.TourShown = true;
                settings.Save();
            }
        }
        tourWindow.Activate();
    }

    void Quit()
    {
        watcher?.Dispose();
        tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        watcher?.Dispose();
        tray?.Dispose();
        ConversionService.ShutdownOffice();
        base.OnExit(e);
    }

    // ---------- komut satırı yardımcıları ----------

    static async Task<int> ConvertHeadless(string[] args)
    {
        if (args.Length < 3) return 2;
        var s = AppSettings.Load();
        var svc = new ConversionService(new ConversionOptions { ImageQuality = s.ImageQuality, FfmpegPath = s.FfmpegPath });
        var failed = 0;
        var log = new List<string>();
        var files = args.Skip(2).Select(Path.GetFullPath).ToList();
        using var slots = new SemaphoreSlim(ConversionService.ParallelismFor(args[1], files));
        await Task.WhenAll(files.Select(async file =>
        {
            await slots.WaitAsync();
            try
            {
                var result = await svc.RunAsync(args[1], file);
                lock (log)
                {
                    if (!result.Success) failed++;
                    log.Add(result.Success
                        ? $"TAMAM  {file} -> {string.Join(", ", result.Outputs)}"
                        : $"HATA   {file}: {result.Error}");
                }
            }
            finally { slots.Release(); }
        }));
        ConversionService.ShutdownOffice();
        // Arayüzsüz çalışmada sonuçlar %LOCALAPPDATA%\Spindle\son-donusum.log dosyasına yazılır.
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spindle");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "son-donusum.log"), log);
        }
        catch { /* günlük yazılamazsa dönüşüm sonucu yine çıkış koduyla bildirilir */ }
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Bildirim kartını verilen süre boyunca "çalışıyor" durumunda gösterir (--toast-demo [saniye]).
    /// Kartın animasyon maliyetini ölçmek ve ekran görüntüsü almak için.
    /// </summary>
    static async Task ToastDemo(string[] args)
    {
        var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 10;
        var toast = ToastWindow.Show("rapor.docx → PDF", "Program yükleniyor…");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        toast.Close();
    }

    /// <summary>
    /// Uygulama simgesini (Assets/spindle.ico) logonun kendi çiziminden üretir (--render-icon çıktı.ico).
    /// Böylece kart, ayarlar ve görev çubuğu birebir aynı logoyu gösterir.
    /// </summary>
    static void RenderIcon(string output)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = new List<byte[]>();
        foreach (var size in sizes)
        {
            var logo = new RoundKeyLogo(dark: false) { Width = size, Height = size };
            logo.Measure(new Size(size, size));
            logo.Arrange(new Rect(0, 0, size, size));
            logo.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(logo);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            frames.Add(ms.ToArray());
        }
        using var fs = File.Create(output);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(frames[i].Length); w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var f in frames) w.Write(f);
    }

    static void SnapshotUi(string folder)
    {
        Directory.CreateDirectory(folder);
        ToastWindow.RenderStates(folder);

        RenderOffscreen(new SettingsWindow(AppSettings.Load(), _ => { }), Path.Combine(folder, "settings.png"));
        RenderOffscreen(new TourWindow(new AppSettings()), Path.Combine(folder, "tour.png"));
    }

    /// <summary>Pencereyi ekran dışında açar ve tüm içeriğini (ekrandan uzun olsa da) tek görsele çizer.</summary>
    static void RenderOffscreen(Window window, string output)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        var content = window.Content is System.Windows.Controls.ScrollViewer scroller ? (FrameworkElement)scroller.Content : (FrameworkElement)window.Content;
        window.UpdateLayout();
        var margin = content.Margin;
        var area = new Rect(0, 0, content.ActualWidth + margin.Left + margin.Right, content.ActualHeight + margin.Top + margin.Bottom);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((System.Windows.Media.Brush)window.Background, null, area);
            // Fırçanın koordinatları içeriğin kenar boşluğunu da kapsar.
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(content)
            {
                Viewbox = area, ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute,
            }, null, area);
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(area.Width * 2), (int)(area.Height * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(output)) enc.Save(fs);
        window.Close();
    }

    static void Snapshot(string[] args)
    {
        // --snapshot out.png convert|tools [hover] files...
        var output = Path.GetFullPath(args[1]);
        var mode = args[2] == "tools" ? WheelMode.Tools : WheelMode.Convert;
        var rest = args.Skip(3).ToList();
        var hover = -1;
        if (rest.Count > 0 && int.TryParse(rest[0], out var h)) { hover = h; rest.RemoveAt(0); }
        var palette = rest.Remove("--dark") ? WheelPalette.Dark : rest.Remove("--light") ? WheelPalette.Light : null;
        var engines = rest.Remove("--no-office") ? EngineAvailability.All with { Word = false, Excel = false, PowerPoint = false } : null;
        WheelWindow.RenderToPng(mode, rest, hover, output, palette, engines);
    }
}
