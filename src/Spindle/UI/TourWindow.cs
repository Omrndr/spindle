using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Spindle.Interop;

namespace Spindle.UI;

/// <summary>
/// İlk açılışta gösterilen kısa tanıtım: üç sayfa, her birinde bir GIF (README'dekilerin aynısı).
/// Tepsi menüsünden ve Ayarlar'dan yeniden açılabilir.
/// </summary>
sealed class TourWindow : Window
{
    sealed record Page(string Gif, string Title, string Text);

    readonly Page[] pages;
    readonly GifPlayer player = new() { Width = 720, Height = 450, Clip = new RectangleGeometry(new Rect(0, 0, 720, 450), 11, 11) };
    readonly TextBlock title = new(), text = new(), counter = new();
    readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Button back, next;
    readonly Border menuRow;
    readonly bool dark = AppSettings.IsDarkTheme();
    int index;

    public TourWindow(AppSettings settings)
    {
        var convert = settings.ConvertKey;
        var tools = settings.ToolsKey;
        pages =
        [
            new("tanitim-donustur.gif", $"Sürükle, {Dative(convert)} bas, bırak",
                $"Dosya Gezgini'nde bir dosyayı sürüklerken {Dative(convert)} basın. İmlecin etrafında tuşlar açılır; " +
                "dosyayı istediğiniz biçimin tuşuna bırakın. Yeni dosya orijinalin yanına kaydedilir, orijinale dokunulmaz."),
            new("tanitim-araclar.gif", $"{tools} ile araçlar",
                $"Sürüklerken {Dative(tools)} basarsanız araçlar açılır: sıkıştır, küçült, döndür, metadata sil, sesi çıkar… " +
                "Sonuç yine orijinalin yanına, yeni bir adla kaydedilir."),
            new("tanitim-sag-tik.gif", "Sağ tık menüsünden de olur",
                "Dosyaya sağ tıklayıp Spindle ▸ Dönüştür… seçin, sonra tuşa tıklayın. Windows 11'de bu menü " +
                "\"Diğer seçenekleri göster\" altındadır. Spindle sistem tepsisinde bekler; ayarlar için simgesine tıklayın."),
        ];

        Title = "Spindle'a hoş geldin";
        Width = 800;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        var ink = Brush(dark ? 0xF5F5F7 : 0x1D1D1F);
        var muted = Brush(dark ? 0xA1A1A6 : 0x6E6E73);
        Background = Brush(dark ? 0x1C1C1E : 0xF4F4F5);
        Foreground = ink;

        var root = new StackPanel { Margin = new Thickness(40, 24, 40, 22) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        counter.Foreground = muted;
        counter.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(counter, Dock.Right);
        header.Children.Add(counter);
        header.Children.Add(new RoundKeyLogo(dark) { Width = 30, Height = 30 });
        header.Children.Add(new TextBlock
        {
            Text = "Spindle'a hoş geldin", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0),
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(header);

        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Brush(dark ? 0x3A3A3C : 0xE3E3E6),
            Child = player, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center,
        });

        title.FontSize = 17;
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 18, 0, 4);
        text.Foreground = muted;
        text.TextWrapping = TextWrapping.Wrap;
        text.MinHeight = 40;
        root.Children.Add(title);
        root.Children.Add(text);

        // Son sayfada: sağ tık menüsünü buradan açıp kapatma
        var menuSwitch = new Hyperlink { TextDecorations = null, Foreground = new SolidColorBrush(ToastWindow.Brand) };
        var menuState = new Run();
        void ShowMenuState()
        {
            menuState.Text = ShellIntegration.IsMenuRegistered ? "Sağ tık menüsünde gösteriliyor · kaldır" : "Sağ tık menüsüne ekle";
        }
        menuSwitch.Inlines.Add(menuState);
        menuSwitch.Click += (_, _) =>
        {
            if (ShellIntegration.IsMenuRegistered) ShellIntegration.UnregisterMenu(); else ShellIntegration.RegisterMenu();
            ShowMenuState();
        };
        ShowMenuState();
        menuRow = new Border { Margin = new Thickness(0, 8, 0, 0), Child = new TextBlock(menuSwitch) { FontWeight = FontWeights.SemiBold } };
        root.Children.Add(menuRow);

        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        next = PillButton("İleri", primary: true);
        back = PillButton("Geri", primary: false);
        back.Margin = new Thickness(0, 0, 8, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(back);
        buttons.Children.Add(next);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(dots);
        root.Children.Add(footer);

        next.Click += (_, _) => { if (index == pages.Length - 1) Close(); else Show(index + 1); };
        back.Click += (_, _) => Show(index - 1);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Right && index < pages.Length - 1) Show(index + 1);
            else if (e.Key == Key.Left && index > 0) Show(index - 1);
        };
        Closed += (_, _) => player.Stop();

        Content = root;
        Show(0);
    }

    void Show(int page)
    {
        index = page;
        var p = pages[page];
        player.Play(new Uri($"pack://application:,,,/Tour/{p.Gif}"));
        title.Text = p.Title;
        text.Text = p.Text;
        counter.Text = $"{page + 1} / {pages.Length}";
        back.Visibility = page == 0 ? Visibility.Hidden : Visibility.Visible;
        next.Content = page == pages.Length - 1 ? "Başla" : "İleri";
        menuRow.Visibility = page == pages.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        dots.Children.Clear();
        for (var i = 0; i < pages.Length; i++)
        {
            var dot = new Border
            {
                Width = i == page ? 18 : 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 0, 6, 0),
                Background = i == page ? new SolidColorBrush(ToastWindow.Brand) : Brush(dark ? 0x48484A : 0xD4D4D8),
                Cursor = Cursors.Hand,
            };
            var target = i;
            dot.MouseLeftButtonUp += (_, _) => Show(target);
            dots.Children.Add(dot);
        }
    }

    Button PillButton(string label, bool primary)
    {
        var bg = primary ? new SolidColorBrush(ToastWindow.Brand) : Brush(dark ? 0x333335 : 0xE6E6EA);
        var fg = primary ? Brushes.White : Foreground;
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, bg);
        border.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return new Button { Content = label, Template = template, Foreground = fg, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, MinWidth = 88 };
    }

    static SolidColorBrush Brush(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    /// <summary>"Shift'e", "Ctrl'ye", "Alt'a"</summary>
    static string Dative(string key) => key switch
    {
        "Ctrl" => "Ctrl'ye",
        "Alt" => "Alt'a",
        _ => key + "'e",
    };
}
