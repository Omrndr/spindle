using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Spindle.UI;
using Spindle.Wheel;
using WpfPath = System.Windows.Shapes.Path;

namespace Spindle.Tour;

/// <summary>
/// Tanıtım GIF'lerinin sahnesi: sade bir masaüstü, bir Dosya Gezgini penceresi, imleç, sürüklenen dosya,
/// basılan tuş, çark ve köşedeki kart. Çark ve kart uygulamanın gerçek bileşenleridir; ekrana hiç gösterilmez.
/// </summary>
sealed class TourStage
{
    public const int Width = 720, Height = 450;
    const double WheelScale = 0.8;

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));
    static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x73));

    public readonly Canvas Root = new() { Width = Width, Height = Height, ClipToBounds = true };
    readonly Canvas files = new();
    readonly Dictionary<string, Border> tiles = [];
    readonly Canvas overlay = new();
    readonly WpfPath cursor;
    readonly TranslateTransform cursorAt = new();
    Border? ghost;
    Point ghostOffset;

    public TourStage()
    {
        Root.Background = new LinearGradientBrush(Color.FromRgb(0xEE, 0xF1, 0xF6), Color.FromRgb(0xD9, 0xE0, 0xEA), 90);
        Root.Children.Add(Explorer());
        Root.Children.Add(overlay);

        cursor = new WpfPath
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.2,13.2 L7,19.6 L9.8,18.4 L7,12.2 L12.6,12.2 Z"),
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = cursorAt,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.3 },
        };
        Panel.SetZIndex(cursor, 100);
        Root.Children.Add(cursor);
    }

    // ---------- Dosya Gezgini ----------

    UIElement Explorer()
    {
        var window = new Border
        {
            Width = 540, Height = 336, CornerRadius = new CornerRadius(8),
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xD9, 0xDE)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.16 },
        };
        Canvas.SetLeft(window, 22);
        Canvas.SetTop(window, 18);

        var stack = new DockPanel { LastChildFill = true };
        var titleBar = new DockPanel { Height = 38, Margin = new Thickness(14, 0, 6, 0) };
        DockPanel.SetDock(titleBar, Dock.Top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        foreach (var g in new[] { "", "", "" })
            buttons.Children.Add(new TextBlock { Text = g, FontFamily = IconFont, FontSize = 10, Foreground = Muted, Width = 38, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        titleBar.Children.Add(buttons);
        titleBar.Children.Add(new TextBlock
        {
            Text = "", FontFamily = IconFont, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB3, 0x3B)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        titleBar.Children.Add(new TextBlock { Text = "Belgeler", FontFamily = TextFont, FontSize = 12, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(titleBar);

        var address = new Border
        {
            Height = 28, Margin = new Thickness(12, 0, 12, 8), CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
            Child = new TextBlock
            {
                Text = "Bu bilgisayar   ›   Belgeler", FontFamily = TextFont, FontSize = 11.5, Foreground = Muted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            },
        };
        DockPanel.SetDock(address, Dock.Top);
        stack.Children.Add(address);
        stack.Children.Add(files);
        window.Child = stack;

        string[] names = ["rapor.docx", "sunum.pptx", "tablo.xlsx", "foto.jpg", "klip.mp4", "müzik.mp3"];
        for (var i = 0; i < names.Length; i++) AddTile(names[i], i);
        return window;
    }

    /// <summary>Dosya ızgarasında <paramref name="slot"/>. sıraya bir dosya ekler (5 sütun).</summary>
    public void AddTile(string name, int slot, double opacity = 1)
    {
        var tile = Tile(name);
        tile.Opacity = opacity;
        Canvas.SetLeft(tile, 16 + slot % 5 * 102);
        Canvas.SetTop(tile, 8 + slot / 5 * 112);
        files.Children.Add(tile);
        tiles[name] = tile;
    }

    static Border Tile(string name)
    {
        var ext = System.IO.Path.GetExtension(name).TrimStart('.');
        var style = KeyStyle.ForTarget(ext);
        var panel = new StackPanel { Margin = new Thickness(4, 8, 4, 6) };
        panel.Children.Add(new TextBlock
        {
            Text = style?.Family.Glyph ?? "", FontFamily = IconFont, FontSize = 38,
            Foreground = new SolidColorBrush(style?.Color ?? Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = name, FontFamily = TextFont, FontSize = 11.5, Foreground = Ink, Margin = new Thickness(0, 7, 0, 0),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 32,
        });
        return new Border { Width = 94, Height = 104, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = panel };
    }

    /// <summary>Yeni oluşan çıktı dosyası: ilk çağrıda eklenir, sonra yalnızca görünürlüğü değişir.</summary>
    public void AddOnce(string name, int slot, double opacity)
    {
        if (tiles.TryGetValue(name, out var tile)) tile.Opacity = opacity;
        else AddTile(name, slot, opacity);
    }

    public void Select(string? name)
    {
        foreach (var (key, tile) in tiles)
        {
            var on = key == name;
            tile.Background = on ? new SolidColorBrush(Color.FromRgb(0xCC, 0xE8, 0xFF)) : null;
            tile.BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0x99, 0xD1, 0xFF)) : null;
        }
    }

    /// <summary>Dosyanın simgesinin ortası (sahne koordinatı); imleç oraya gider.</summary>
    public Point TileCenter(string name)
    {
        var tile = tiles[name];
        return new Point(22 + 1 + Canvas.GetLeft(tile) + tile.Width / 2, 18 + 1 + 38 + 36 + Canvas.GetTop(tile) + 30);
    }

    // ---------- imleç ve sürüklenen dosya ----------

    public Point Cursor
    {
        set
        {
            cursorAt.X = value.X;
            cursorAt.Y = value.Y;
            if (ghost is not null)
            {
                Canvas.SetLeft(ghost, value.X - ghostOffset.X);
                Canvas.SetTop(ghost, value.Y - ghostOffset.Y);
            }
        }
    }

    public void StartDrag(string name, Point grab)
    {
        // Sürüklenen dosyanın soluk kopyası imlecin sağ altında durur; altındaki tuşun yazısını kapatmaz.
        ghost = Tile(name);
        ghost.Opacity = 0.55;
        ghost.RenderTransform = new ScaleTransform(0.75, 0.75);
        ghostOffset = new Point(-8, -10);
        Panel.SetZIndex(ghost, 90);
        Root.Children.Add(ghost);
        Cursor = grab;
    }

    public void EndDrag()
    {
        if (ghost is not null) Root.Children.Remove(ghost);
        ghost = null;
    }

    // ---------- klavye tuşu ----------

    Border? keyTop, keySide;
    TextBlock? keyText;
    TranslateTransform? keyPress;

    public void ShowKey(string label)
    {
        keySide = new Border { Width = 96, Height = 44, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromRgb(0xBD, 0xB8, 0xAE)) };
        Canvas.SetLeft(keySide, 24);
        Canvas.SetTop(keySide, Height - 66 + 5);
        keyPress = new TranslateTransform();
        keyText = new TextBlock
        {
            Text = label, FontFamily = TextFont, FontSize = 15, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        keyTop = new Border
        {
            Width = 96, Height = 44, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
            RenderTransform = keyPress, Child = keyText,
        };
        Canvas.SetLeft(keyTop, 24);
        Canvas.SetTop(keyTop, Height - 66);
        overlay.Children.Add(keySide);
        overlay.Children.Add(keyTop);
        PressKey(false);
    }

    public void PressKey(bool down)
    {
        if (keyTop is null) return;
        keyTop.Background = new SolidColorBrush(down ? ToastWindow.Brand : Colors.White);
        keyTop.BorderBrush = new SolidColorBrush(down ? ToastWindow.Brand : Color.FromRgb(0xE2, 0xDE, 0xD6));
        keySide!.Background = new SolidColorBrush(down ? Color.FromRgb(0x2A, 0x63, 0xC0) : Color.FromRgb(0xBD, 0xB8, 0xAE));
        keyText!.Foreground = down ? Brushes.White : Ink;
        keyPress!.Y = down ? 4 : 0;
    }

    // ---------- çark ----------

    WheelWindow? wheel;
    FrameworkElement? wheelRoot;
    readonly ScaleTransform wheelZoom = new();
    Point wheelCenter;

    public void OpenWheel(WheelMode mode, string file, Point center)
    {
        CloseWheel();
        wheel = new WheelWindow(mode, [file], dragMode: false, WheelPalette.Light, Core.EngineAvailability.All);
        wheelRoot = wheel.DetachForPreview();
        wheelCenter = center;
        var half = WheelWindow.PreviewSide / 2;
        wheelZoom.CenterX = wheelZoom.CenterY = half;
        wheelRoot.RenderTransform = wheelZoom;
        Canvas.SetLeft(wheelRoot, center.X - half);
        Canvas.SetTop(wheelRoot, center.Y - half);
        Panel.SetZIndex(wheelRoot, 50);
        Root.Children.Add(wheelRoot);
        WheelPop(1);
    }

    /// <summary>Açılış animasyonu: 0 → 1 (uygulamadaki gibi %72'den büyüyerek, hafif taşarak).</summary>
    public void WheelPop(double progress)
    {
        if (wheelRoot is null) return;
        var p = Math.Clamp(progress, 0, 1);
        var q = 1 - p;
        var back = 1 - (q * q * q - 0.28 * q * Math.Sin(Math.PI * q)); // BackEase(0.28), EaseOut
        var s = WheelScale * (0.72 + 0.28 * back);
        wheelZoom.ScaleX = wheelZoom.ScaleY = s;
        wheelRoot.Opacity = Math.Clamp(p / 0.65, 0, 1);
    }

    /// <summary>İmleç çarkın üstündeyse altındaki tuşu basılı gösterir.</summary>
    public void PointWheel(Point pointer)
    {
        if (wheel is null) return;
        var half = WheelWindow.PreviewSide / 2;
        wheel.PreviewPointer(new Point((pointer.X - wheelCenter.X) / WheelScale + half, (pointer.Y - wheelCenter.Y) / WheelScale + half));
    }

    /// <summary>Çarktaki bir tuşun ortası (sahne koordinatı). Tuşlar saat yönünde, ilki en üstte.</summary>
    public Point KeyCenter(int index, int count)
    {
        var angle = index * 360.0 / count * Math.PI / 180;
        var r = (64 + 150) / 2.0 * WheelScale;
        return new Point(wheelCenter.X + r * Math.Sin(angle), wheelCenter.Y - r * Math.Cos(angle));
    }

    public void CloseWheel()
    {
        if (wheelRoot is not null) Root.Children.Remove(wheelRoot);
        wheel?.Close();
        wheel = null;
        wheelRoot = null;
    }

    // ---------- bildirim kartı ----------

    ToastWindow? toast;
    Border? toastHost;

    public void ShowToast(string heading)
    {
        HideToast();
        var (card, content) = ToastWindow.ForPreview(heading, dark: false);
        toast = card;
        toastHost = new Border
        {
            Width = 340, CornerRadius = new CornerRadius(8), Child = content,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE6)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.18 },
        };
        Canvas.SetRight(toastHost, 16);
        Canvas.SetBottom(toastHost, 16);
        Panel.SetZIndex(toastHost, 60);
        Root.Children.Add(toastHost);
    }

    public void ToastWorking(string message, double progress, int lit)
    {
        Layout();
        toast?.PreviewWorking(message, progress, lit);
    }

    public void ToastDone(string message, string action) => toast?.PreviewDone(message, action);

    public void HideToast()
    {
        if (toastHost is not null) Root.Children.Remove(toastHost);
        toast?.Close();
        toast = null;
        toastHost = null;
    }

    // ---------- sağ tık menüsü ----------

    Border? menu, submenu;
    readonly List<Border> menuItems = [], submenuItems = [];

    public void OpenMenu(Point at)
    {
        menuItems.Clear();
        menu = Menu([("", "Aç"), ("", "Birlikte aç"), (null, null), ("logo", "Spindle"), (null, null),
            ("", "Kopyala"), ("", "Yeniden adlandır"), ("", "Sil"), (null, null), ("", "Özellikler")], menuItems);
        Canvas.SetLeft(menu, at.X + 2);
        Canvas.SetTop(menu, at.Y + 2);
        Panel.SetZIndex(menu, 70);
        Root.Children.Add(menu);
    }

    public void OpenSubmenu()
    {
        submenuItems.Clear();
        submenu = Menu([("", "Dönüştür…"), ("", "Araçlar…")], submenuItems);
        var anchor = MenuItemCenter(menuItems, 2);
        // Sağda yer yoksa alt menü Windows'taki gibi sola açılır.
        var left = Canvas.GetLeft(menu!);
        Canvas.SetLeft(submenu, left + 196 + 196 <= Width ? left + 192 : left - 192);
        Canvas.SetTop(submenu, anchor.Y - 20);
        Panel.SetZIndex(submenu, 71);
        Root.Children.Add(submenu);
    }

    public Point MenuItem(int index) => MenuItemCenter(menuItems, index);
    public Point SubmenuItem(int index) => MenuItemCenter(submenuItems, index);

    public void HoverMenu(int index, int subIndex)
    {
        for (var i = 0; i < menuItems.Count; i++) menuItems[i].Background = i == index ? new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEE)) : null;
        for (var i = 0; i < submenuItems.Count; i++) submenuItems[i].Background = i == subIndex ? new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEE)) : null;
    }

    public void CloseMenus()
    {
        if (menu is not null) Root.Children.Remove(menu);
        if (submenu is not null) Root.Children.Remove(submenu);
        menu = submenu = null;
    }

    Point MenuItemCenter(List<Border> items, int index)
    {
        Layout();
        var item = items[index];
        var p = item.TranslatePoint(new Point(40, item.ActualHeight / 2), Root);
        return p;
    }

    static Border Menu((string? Glyph, string? Text)[] entries, List<Border> items)
    {
        var stack = new StackPanel();
        foreach (var (glyph, text) in entries)
        {
            if (text is null)
            {
                stack.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE8)) });
                continue;
            }
            var row = new DockPanel { Height = 28 };
            UIElement icon = glyph == "logo"
                ? new RoundKeyLogo(dark: false) { Width = 16, Height = 16 }
                : new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 13, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
            var iconBox = new Border { Width = 34, Child = icon, VerticalAlignment = VerticalAlignment.Center };
            if (icon is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
            row.Children.Add(iconBox);
            if (text == "Spindle")
            {
                var chevron = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 10, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                DockPanel.SetDock(chevron, Dock.Right);
                row.Children.Add(chevron);
            }
            row.Children.Add(new TextBlock { Text = text, FontFamily = TextFont, FontSize = 12, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
            var item = new Border { CornerRadius = new CornerRadius(4), Child = row };
            items.Add(item);
            stack.Children.Add(item);
        }
        return new Border
        {
            Width = 196, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xE1)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.18 },
            Child = stack,
        };
    }

    // ---------- çizim ----------

    public void Layout()
    {
        Root.Measure(new Size(Width, Height));
        Root.Arrange(new Rect(0, 0, Width, Height));
        Root.UpdateLayout();
    }

    /// <summary>Sahneyi BGRA piksel dizisi olarak çizer.</summary>
    public byte[] Render()
    {
        Layout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(Root);
        var pixels = new byte[Width * Height * 4];
        bitmap.CopyPixels(pixels, Width * 4, 0);
        return pixels;
    }

    public void Dispose()
    {
        CloseWheel();
        HideToast();
    }
}
