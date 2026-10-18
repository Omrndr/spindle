using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spindle.Core.Engines;
using Spindle.Interop;
using Microsoft.Win32;

namespace Spindle.UI;

public partial class SettingsWindow : Window
{
    readonly AppSettings settings;
    readonly Action<IReadOnlyList<string>> openWheel;
    bool loading = true;

    /// <summary>"Nasıl kullanılır?" bağlantısı: tanıtımı yeniden açar.</summary>
    public event Action? TourRequested;

    public SettingsWindow(AppSettings settings, Action<IReadOnlyList<string>> openWheel)
    {
        this.settings = settings;
        this.openWheel = openWheel;
        InitializeComponent();
        if (AppSettings.IsDarkTheme()) ApplyDark();

        // ICO içindeki en büyük kareyi seç; varsayılan küçük kare büyütülünce bulanık görünür.
        var icon = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/spindle.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Logo.Source = icon.Frames.OrderByDescending(f => f.PixelWidth).First();
        Load();
        Wire();
        TourLink.Click += (_, _) => TourRequested?.Invoke();
        loading = false;
    }

    void Load()
    {
        (settings.ConvertKey switch { "Ctrl" => ConvertCtrl, "Alt" => ConvertAlt, _ => ConvertShift }).IsChecked = true;
        (settings.ToolsKey switch { "Shift" => ToolsShift, "Alt" => ToolsAlt, _ => ToolsCtrl }).IsChecked = true;
        Gesture.IsChecked = settings.DragGestureEnabled;
        OnlyExplorer.IsChecked = settings.OnlyInExplorer;
        MenuSwitch.IsChecked = ShellIntegration.IsMenuRegistered;
        Autostart.IsChecked = ShellIntegration.IsAutostartEnabled;
        SoundSwitch.IsChecked = settings.SoundsEnabled;
        Quality.Value = settings.ImageQuality;
        QualityLabel.Text = settings.ImageQuality.ToString();
        UpdateHowTo();
        RefreshEngines();
    }

    void Wire()
    {
        foreach (var rb in new[] { ConvertShift, ConvertCtrl, ConvertAlt, ToolsShift, ToolsCtrl, ToolsAlt })
            rb.Checked += (_, _) => SaveKeys();
        Gesture.Click += (_, _) => { settings.DragGestureEnabled = Gesture.IsChecked == true; Save(); };
        OnlyExplorer.Click += (_, _) => { settings.OnlyInExplorer = OnlyExplorer.IsChecked == true; Save(); };
        MenuSwitch.Click += (_, _) =>
        {
            if (MenuSwitch.IsChecked == true) ShellIntegration.RegisterMenu(); else ShellIntegration.UnregisterMenu();
        };
        Autostart.Click += (_, _) => ShellIntegration.SetAutostart(Autostart.IsChecked == true);
        SoundSwitch.Click += (_, _) =>
        {
            settings.SoundsEnabled = Sounds.Enabled = SoundSwitch.IsChecked == true;
            Save();
            Sounds.Click();
        };
        Quality.ValueChanged += (_, e) =>
        {
            settings.ImageQuality = (int)e.NewValue;
            QualityLabel.Text = settings.ImageQuality.ToString();
            Save();
        };
        InstallFfmpeg.Click += (_, _) => InstallFfmpegWithWinget();
        Activated += (_, _) => RefreshEngines();

        DropZone.MouseLeftButtonUp += (_, _) => PickFiles();
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                Dispatcher.BeginInvoke(() => openWheel(files));
        };
    }

    public void PickFiles()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Dönüştürülecek dosyaları seçin" };
        if (dialog.ShowDialog(this) == true) openWheel(dialog.FileNames);
    }

    void SaveKeys()
    {
        if (loading) return;
        var convert = ConvertCtrl.IsChecked == true ? "Ctrl" : ConvertAlt.IsChecked == true ? "Alt" : "Shift";
        var tools = ToolsShift.IsChecked == true ? "Shift" : ToolsAlt.IsChecked == true ? "Alt" : "Ctrl";
        var clash = convert == tools;
        KeyWarning.Visibility = clash ? Visibility.Visible : Visibility.Collapsed;
        if (clash) return;
        settings.ConvertKey = convert;
        settings.ToolsKey = tools;
        Save();
        UpdateHowTo();
    }

    void Save()
    {
        if (!loading) settings.Save();
    }

    void UpdateHowTo() =>
        HowTo.Text = $"İpucu: Dosya Gezgini'nde bir dosyayı sürüklerken {settings.ConvertKey} tuşuna basın; " +
                     $"çark açılır. Araçlar için {settings.ToolsKey}.";

    void RefreshEngines()
    {
        // Kullanıcı bu arada Office ya da ffmpeg kurmuş olabilir; çark da güncel durumu görsün.
        Spindle.Core.EngineAvailability.Refresh();
        var office = OfficeEngine.IsAvailable("Word.Application");
        OfficeState.Text = office ? "✓ Bulundu" : "Yok";
        OfficeState.Foreground = office ? new SolidColorBrush(Color.FromRgb(0x30, 0xA4, 0x6C)) : (Brush)FindResource("Muted");

        var ffmpeg = FfmpegLocator.Find(settings.FfmpegPath);
        FfmpegState.Text = ffmpeg is not null ? "✓ Bulundu" : "Yok";
        FfmpegState.Foreground = ffmpeg is not null ? new SolidColorBrush(Color.FromRgb(0x30, 0xA4, 0x6C)) : (Brush)FindResource("Muted");
        FfmpegPathText.Text = ffmpeg ?? "Ses ve video dönüştürmek için gerekli.";
        InstallFfmpeg.Visibility = ffmpeg is null ? Visibility.Visible : Visibility.Collapsed;
    }

    void InstallFfmpegWithWinget()
    {
        // Kurulum Windows'un kendi paket yöneticisiyle, kullanıcının göreceği bir pencerede yapılır;
        // lisans onayını kullanıcı verir.
        try
        {
            Process.Start(new ProcessStartInfo("winget", "install --id Gyan.FFmpeg -e") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "winget başlatılamadı: " + ex.Message, "Spindle");
        }
    }

    void ApplyDark()
    {
        void Set(string key, byte r, byte g, byte b) => Resources[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
        Set("WindowBg", 0x1C, 0x1C, 0x1E);
        Set("CardBg", 0x2B, 0x2B, 0x2D);
        Set("CardEdge", 0x3A, 0x3A, 0x3C);
        Set("Ink", 0xF5, 0xF5, 0xF7);
        Set("Muted", 0xA1, 0xA1, 0xA6);
        Set("Track", 0x48, 0x48, 0x4A);
        Set("KeyBg", 0x33, 0x33, 0x35);
    }
}
