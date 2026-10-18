using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Spindle.Interop;

namespace Spindle.UI;

/// <summary>
/// Ekranın sağ altındaki küçük kart. Arka planda olan hiçbir şey (Office, ffmpeg) görünmez;
/// kullanıcı yalnızca bu kartı görür: "Program yükleniyor…" → "Tamamlandı".
/// Kart normal bir penceredir (gölge ve yuvarlak köşeler Windows'tan); tek animasyon, logo dilimlerinin
/// saniyede 8 kez adım atmasıdır ve çubuk da bu adımlarla birlikte güncellenir.
/// </summary>
sealed class ToastWindow : Window
{
    const double CardWidth = 340;
    static readonly List<ToastWindow> Open = [];
    public static readonly Color Brand = Color.FromRgb(0x3A, 0x86, 0xFF);

    readonly TextBlock title = new(), status = new();
    readonly Border track = new(), bar = new();
    readonly Grid iconHost = new() { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top };
    readonly RoundKeyLogo logo;
    readonly Border badge = new();
    readonly TextBlock badgeGlyph = new();
    readonly Button action = new();
    readonly DispatcherTimer closeTimer = new();
    readonly DispatcherTimer chase = new() { Interval = TimeSpan.FromMilliseconds(125) };
    readonly bool dark;
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    IReadOnlyList<string> outputs = [];
    int step;

    ToastWindow(string heading, bool? darkTheme = null)
    {
        dark = darkTheme ?? AppSettings.IsDarkTheme();
        // Saydam (katmanlı) pencere her değişiklikte işlemciyle baştan çizildiği için kart normal bir pencere:
        // içerik ekran kartında çizilir, yuvarlak köşeleri ve gölgeyi Windows (DWM) ekler.
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = CardWidth;
        Title = "Spindle";
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0, 0, 0, 1), // DWM gölgesi için
            UseAeroCaptionButtons = false,
        });

        var cardBg = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2D) : Colors.White);
        Background = cardBg;
        var card = new Border
        {
            Padding = new Thickness(14, 12, 12, 14),
            Background = cardBg,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        logo = new RoundKeyLogo(dark);
        iconHost.Children.Add(logo);
        // Sonuç rozeti (onay ya da hata) logonun sağ alt köşesinde
        badge.Width = badge.Height = 16;
        badge.CornerRadius = new CornerRadius(8);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Bottom;
        badge.Margin = new Thickness(0, 0, -3, -2);
        badge.BorderBrush = cardBg;
        badge.BorderThickness = new Thickness(1.5);
        badge.Visibility = Visibility.Collapsed;
        badgeGlyph.FontFamily = IconFont;
        badgeGlyph.FontSize = 8;
        badgeGlyph.Foreground = Brushes.White;
        badgeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        badgeGlyph.VerticalAlignment = VerticalAlignment.Center;
        badge.Child = badgeGlyph;
        iconHost.Children.Add(badge);
        grid.Children.Add(iconHost);

        var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        Grid.SetColumn(texts, 1);
        title.Text = heading;
        title.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        title.FontSize = 13.5;
        title.FontWeight = FontWeights.SemiBold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        status.FontFamily = title.FontFamily;
        status.FontSize = 12;
        status.Margin = new Thickness(0, 2, 0, 0);
        status.TextWrapping = TextWrapping.Wrap;
        status.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x8B));
        texts.Children.Add(title);
        texts.Children.Add(status);

        track.Height = 4;
        track.CornerRadius = new CornerRadius(2);
        track.Margin = new Thickness(0, 9, 0, 0);
        track.ClipToBounds = true;
        track.Background = new SolidColorBrush(dark ? Color.FromRgb(0x3A, 0x3A, 0x3C) : Color.FromRgb(0xEE, 0xEE, 0xF0));
        bar.CornerRadius = new CornerRadius(2);
        bar.Background = new SolidColorBrush(Brand);
        bar.HorizontalAlignment = HorizontalAlignment.Left;
        bar.Width = 0;
        track.Child = bar;
        texts.Children.Add(track);

        action.Visibility = Visibility.Collapsed;
        action.Margin = new Thickness(0, 8, 0, 0);
        action.HorizontalAlignment = HorizontalAlignment.Left;
        action.Cursor = Cursors.Hand;
        action.Template = LinkTemplate();
        action.Foreground = new SolidColorBrush(Brand);
        action.Click += (_, _) => Reveal();
        texts.Children.Add(action);
        grid.Children.Add(texts);

        var close = new Button { Content = "", Template = LinkTemplate(), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top,
            FontFamily = IconFont, FontSize = 10, Foreground = status.Foreground, Padding = new Thickness(4) };
        close.Click += (_, _) => FadeOut();
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        card.Child = grid;
        var root = new Grid();
        root.Children.Add(card);
        Content = root;

        chase.Tick += (_, _) => Tick();
        closeTimer.Tick += (_, _) => { if (!IsMouseOver) FadeOut(); };
        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            Native.RoundCorners(hwnd); // Windows 11: yuvarlak köşe (Windows 10'da köşeler düz kalır)
            Restack();
        };
        Closed += (_, _) => { chase.Stop(); closeTimer.Stop(); Open.Remove(this); Restack(); };
    }

    public static ToastWindow Show(string heading, string message)
    {
        var toast = new ToastWindow(heading);
        toast.SetWorking(message);
        Open.Add(toast);
        toast.Show();
        return toast;
    }

    public static void Info(string heading, string message, string glyph = "")
    {
        var toast = Show(heading, message);
        toast.Finish(success: true, message, autoCloseSeconds: 3);
    }

    public void SetWorking(string message)
    {
        SetStatus(message);
        logo.Light(step);
        chase.Start();
    }

    /// <summary>
    /// Alt satırı değiştirir. Gerçek yüzde bilinmeyen işlerde (ör. Office) çubuk aşamaya göre ilerler:
    /// her aşamada bir sonraki aşamanın sınırına yavaşça yaklaşır, iş bitince %100 olur.
    /// </summary>
    public void SetStatus(string message)
    {
        status.Text = message;
        if (realProgress) return;
        var stageCap = message switch
        {
            _ when message.Contains("Sırada", StringComparison.Ordinal) => 0.06,
            _ when message.Contains("Program yükleniyor", StringComparison.Ordinal) => 0.3,
            _ when message.Contains("Belge açılıyor", StringComparison.Ordinal) => 0.5,
            _ when message.Contains("oluşturuluyor", StringComparison.Ordinal) || message.Contains("kaydediliyor", StringComparison.Ordinal) => 0.94,
            _ => 0.9,
        };
        cap = Math.Max(cap, stageCap);
    }

    /// <summary>Gerçek ilerleme (ffmpeg, PDF sayfaları, çok dosyalı işler): çubuk tam bu orana gelir.</summary>
    public void SetProgress(double fraction, string message)
    {
        status.Text = message;
        realProgress = true;
        shown = Math.Max(shown, Math.Clamp(fraction, 0, 1));
        DrawBar();
    }

    // Çubuk ayrı bir animasyonla değil, logonun saniyede 8 adımıyla birlikte güncellenir.
    double shown, cap = 0.06;
    bool realProgress;

    void Tick()
    {
        logo.Light(++step);
        if (!realProgress && shown < cap)
        {
            shown += (cap - shown) * 0.06; // aşama sınırına yavaşça yaklaş
            DrawBar();
        }
    }

    void DrawBar() => bar.Width = Math.Max(4, shown * Math.Max(0, track.ActualWidth));

    public void Complete(IReadOnlyList<string> results, string message)
    {
        outputs = results;
        if (results.Count > 0)
        {
            action.Content = results.Count == 1 ? "Göster" : "Klasörde göster";
            action.Visibility = Visibility.Visible;
        }
        Finish(success: true, message, autoCloseSeconds: 6);
    }

    public void Fail(string message, IReadOnlyList<string> partialResults)
    {
        outputs = partialResults;
        if (partialResults.Count > 0) { action.Content = "Bitenleri göster"; action.Visibility = Visibility.Visible; }
        Finish(success: false, message, autoCloseSeconds: 12);
    }

    void Finish(bool success, string message, int autoCloseSeconds)
    {
        chase.Stop();
        logo.Light(null);
        badge.Background = new SolidColorBrush(success ? Color.FromRgb(0x22, 0xA5, 0x65) : Color.FromRgb(0xE5, 0x48, 0x4D));
        badgeGlyph.Text = success ? "" : "";
        badge.Visibility = Visibility.Visible;
        status.Text = message;
        track.Visibility = Visibility.Collapsed;
        closeTimer.Interval = TimeSpan.FromSeconds(autoCloseSeconds);
        closeTimer.Start();
        Dispatcher.BeginInvoke(Restack, DispatcherPriority.Loaded);
    }

    void Reveal()
    {
        if (outputs.Count == 0) return;
        var first = outputs[0];
        var args = Directory.Exists(first) && outputs.Count == 1 ? $"\"{first}\"" : $"/select,\"{first}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        FadeOut();
    }

    void FadeOut()
    {
        closeTimer.Stop();
        chase.Stop();
        Close();
    }

    static void Restack()
    {
        var area = SystemParameters.WorkArea;
        var bottom = area.Bottom;
        for (var i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            t.UpdateLayout();
            var h = t.ActualHeight > 0 ? t.ActualHeight : 110;
            t.Left = area.Right - t.Width - 16;
            t.Top = bottom - h - 16;
            bottom -= h + 10;
        }
    }

    /// <summary>README ve görsel kontrol için kartın iki halini PNG olarak çizer (ekrana göstermeden).</summary>
    public static void RenderStates(string folder)
    {
        void Render(ToastWindow t, string name)
        {
            var root = (FrameworkElement)t.Content;
            var frame = new Grid { Width = t.Width };
            t.Content = null;
            frame.Children.Add(root);
            frame.Measure(new Size(t.Width, double.PositiveInfinity));
            var size = new Size(t.Width, frame.DesiredSize.Height + 8);
            frame.Arrange(new Rect(size));
            frame.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(size.Width * 2), (int)(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
            bmp.Render(frame);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(folder, name));
            enc.Save(fs);
            t.Close();
        }

        var working = new ToastWindow("rapor.docx → PDF");
        working.status.Text = "Program yükleniyor…";
        working.logo.Light(1);
        working.bar.Width = 90;
        Render(working, "toast-working.png");

        var done = new ToastWindow("rapor.docx → PDF");
        done.outputs = ["rapor.pdf"];
        done.action.Content = "Göster";
        done.action.Visibility = Visibility.Visible;
        done.logo.Light(null);
        done.badge.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xA5, 0x65));
        done.badgeGlyph.Text = "";
        done.badge.Visibility = Visibility.Visible;
        done.status.Text = "rapor.pdf hazır";
        done.track.Visibility = Visibility.Collapsed;
        Render(done, "toast-done.png");
    }

    // ---------- tanıtım animasyonu için (ekrana gösterilmeden) ----------

    /// <summary>Kartın içeriğini pencereden ayırır; tanıtım sahnesine yerleştirilip her karede güncellenir.</summary>
    internal static (ToastWindow Card, FrameworkElement Content) ForPreview(string heading, bool dark)
    {
        var toast = new ToastWindow(heading, dark);
        var root = (Grid)toast.Content;
        toast.Content = null;
        ((Border)root.Children[0]).CornerRadius = new CornerRadius(8);
        return (toast, root);
    }

    internal void PreviewWorking(string message, double progress, int litSegment)
    {
        status.Text = message;
        badge.Visibility = Visibility.Collapsed;
        action.Visibility = Visibility.Collapsed;
        track.Visibility = Visibility.Visible;
        logo.Light(litSegment);
        track.UpdateLayout();
        shown = progress;
        DrawBar();
    }

    internal void PreviewDone(string message, string actionText)
    {
        logo.Light(null);
        badge.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xA5, 0x65));
        badgeGlyph.Text = "";
        badge.Visibility = Visibility.Visible;
        status.Text = message;
        track.Visibility = Visibility.Collapsed;
        action.Content = actionText;
        action.Visibility = Visibility.Visible;
    }

    static ControlTemplate LinkTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }
}
