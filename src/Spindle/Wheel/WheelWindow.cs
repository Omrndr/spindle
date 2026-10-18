using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Spindle.Core;
using Spindle.Interop;
using WpfPath = System.Windows.Shapes.Path;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Spindle.Wheel;

/// <summary>
/// İmlecin etrafında açılan dilimli çark. İki şekilde çalışır:
/// sürükleme modunda dosya bir dilimin üstüne bırakılır; tıklama modunda (sağ tık menüsü) dilime tıklanır.
/// </summary>
sealed class WheelWindow : Window
{
    const double Side = 380, C = Side / 2, OuterR = 150, InnerR = 64, CenterR = 54, Gap = 7, Round = 6;
    const double KeyDepth = 6, PressDepth = 4;
    const int MinSlots = 4, MaxSlots = 8;

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly FontFamily TextFont = new("Segoe UI Variable Display, Segoe UI");

    readonly bool dragMode;
    readonly WheelPalette palette;
    readonly Canvas canvas = new() { Width = Side, Height = Side, Background = Brushes.Transparent };
    readonly ScaleTransform zoom = new(0.72, 0.72, C, C);
    readonly TextBlock centerTitle = new(), centerSub = new(), centerHint = new();
    readonly List<Slot> slots = [];
    readonly DispatcherTimer dwell = new() { Interval = TimeSpan.FromMilliseconds(450) };

    WheelMode mode;
    IReadOnlyList<string> files;
    List<List<WheelAction>> pages = [[]];
    int pageIndex, hover = -1;
    Native.POINT anchor;

    public bool DragMode => dragMode;
    public WheelMode Mode => mode;
    public bool GotDragEnter { get; private set; }
    public bool Dropped { get; private set; }
    public event Action<WheelAction, IReadOnlyList<string>>? ActionChosen;
    /// <summary>Sürüklenen dosyalar çarkın üstüne gelip türleri belli olduğunda.</summary>
    public event Action<WheelWindow, IReadOnlyList<string>>? FilesChanged;

    /// <summary>Bir tuş: yan yüz (sabit, biraz aşağıda) ve üst yüz; seçilince üst yüz <see cref="Press"/> kadar iner.</summary>
    sealed record Slot(WpfPath Top, WpfPath Side, TranslateTransform Press, TextBlock Label, TextBlock? Icon, WheelAction? Action, Color Color);

    readonly EngineAvailability? engines;

    public WheelWindow(WheelMode mode, IReadOnlyList<string>? files, bool dragMode, WheelPalette? palette = null, EngineAvailability? engines = null)
    {
        this.mode = mode;
        this.files = files ?? [];
        this.dragMode = dragMode;
        this.palette = palette ?? WheelPalette.Current;
        this.engines = engines;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = !dragMode;
        Width = Height = Side;
        AllowDrop = true;
        Title = "Spindle";

        var root = new Grid { RenderTransform = zoom, Opacity = 0 };
        root.Children.Add(canvas);
        Content = root;

        BuildStatic();
        Rebuild();

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetHover(-1);
        Drop += OnDrop;
        MouseMove += (_, e) => { if (!dragMode) SetHover(HitTest(e.GetPosition(canvas))); };
        MouseLeftButtonUp += (_, e) => { if (!dragMode) Choose(HitTest(e.GetPosition(canvas)), this.files); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) => { if (!dragMode && IsLoaded) Close(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => PlaceAt(anchor));
        dwell.Tick += (_, _) => { dwell.Stop(); if (hover >= 0) Navigate(slots[hover].Action); };
    }

    // ---------- gösterme ve konumlama ----------

    public void ShowAt(Native.POINT screenPoint)
    {
        anchor = screenPoint;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | (dragMode ? Native.WS_EX_NOACTIVATE : 0));
        Show();
        PlaceAt(screenPoint);
        if (!dragMode) { Activate(); Native.SetForegroundWindow(hwnd); }
        AnimateIn();
    }

    void PlaceAt(Native.POINT pt)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var size = (int)Math.Round(Side * Native.GetDpiForWindow(hwnd) / 96.0);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, pt.X - size / 2, pt.Y - size / 2, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    void AnimateIn()
    {
        var root = (UIElement)Content;
        var ease = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(170);
        zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.72, 1, duration) { EasingFunction = ease });
        zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.72, 1, duration) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
    }

    // ---------- içerik ----------

    public void SetMode(WheelMode newMode)
    {
        if (newMode == mode) return;
        mode = newMode;
        Rebuild();
    }

    void SetFiles(IReadOnlyList<string> newFiles)
    {
        files = newFiles;
        Rebuild();
        FilesChanged?.Invoke(this, newFiles);
    }

    void Rebuild()
    {
        var actions = files.Count == 0 ? []
            : mode == WheelMode.Convert ? FormatCatalog.ConvertActionsFor(files, engines)
            : FormatCatalog.ToolActionsFor(files, engines);
        // Dönüştürülemeyen dosyalarda (ör. bilinmeyen tür) boş çark yerine araçları göster.
        if (files.Count > 0 && actions.Count == 0 && mode == WheelMode.Convert)
        {
            mode = WheelMode.Tools;
            actions = FormatCatalog.ToolActionsFor(files, engines);
        }
        pages = Paginate(actions.ToList());
        pageIndex = 0;
        BuildSlots();
    }

    static List<List<WheelAction>> Paginate(List<WheelAction> actions)
    {
        if (actions.Count <= MaxSlots) return [actions];
        var per = MaxSlots - 1;
        var chunks = actions.Chunk(per).Select(c => c.ToList()).ToList();
        for (var i = 0; i < chunks.Count; i++)
            chunks[i].Add(i < chunks.Count - 1
                ? new WheelAction("nav:more", "•••", "diğerleri", "")
                : new WheelAction("nav:back", "GERİ", "ilk sayfa", ""));
        return chunks;
    }

    void BuildStatic()
    {
        var tray = new Ellipse
        {
            Width = (OuterR + 16) * 2, Height = (OuterR + 16) * 2,
            Fill = new SolidColorBrush(palette.Tray),
            Stroke = new SolidColorBrush(palette.TrayEdge), StrokeThickness = 1,
            Effect = new DropShadowEffect { Color = palette.Shadow, BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = palette.IsDark ? 0.45 : 0.22 },
        };
        Canvas.SetLeft(tray, C - tray.Width / 2);
        Canvas.SetTop(tray, C - tray.Height / 2 + 2);
        canvas.Children.Add(tray);
    }

    void BuildSlots()
    {
        foreach (var s in slots)
        {
            canvas.Children.Remove(s.Side);
            canvas.Children.Remove(s.Top);
            canvas.Children.Remove((UIElement)s.Label.Parent);
        }
        slots.Clear();
        hover = -1;
        var center = canvas.Children.OfType<FrameworkElement>().Where(e => Equals(e.Tag, "center")).ToList();
        foreach (var e in center) canvas.Children.Remove(e);

        var page = pages[pageIndex];
        var count = Math.Max(MinSlots, page.Count);
        var step = 360.0 / count;
        for (var i = 0; i < count; i++)
        {
            var action = i < page.Count ? page[i] : null;
            var angle = i * step;
            var geometry = SegmentGeometry(angle - step / 2, angle + step / 2);
            var color = KeyColor(action);
            var press = new TranslateTransform();
            var opacity = action is null ? 0.5 : action.IsAvailable ? 1 : 0.45;

            var side = KeyFace(geometry, palette.KeySide);
            side.RenderTransform = new TranslateTransform(0, KeyDepth);
            side.Opacity = opacity;
            var top = KeyFace(geometry, palette.KeyTop);
            top.RenderTransform = press;
            top.Opacity = opacity;
            canvas.Children.Insert(1, side); // tüm yan yüzler tüm üst yüzlerin altında kalsın
            canvas.Children.Add(top);

            var (label, icon, holder) = SlotContent(action, color);
            holder.RenderTransform = press;
            if (action is { IsAvailable: false }) holder.Opacity = 0.5; // gereken motor yok (ör. Office)
            var mid = Polar((InnerR + OuterR) / 2, angle);
            holder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(holder, mid.X - holder.DesiredSize.Width / 2);
            Canvas.SetTop(holder, mid.Y - holder.DesiredSize.Height / 2);
            canvas.Children.Add(holder);

            slots.Add(new Slot(top, side, press, label, icon, action, color));
        }
        BuildCenter();
        UpdateCenter();
    }

    /// <summary>Dönüştürmede hedef biçimin rengi; araçlarda ve gezinmede tek vurgu rengi.</summary>
    Color KeyColor(WheelAction? action) =>
        action is { IsConversion: true } && KeyStyle.ForTarget(action.Target) is { } style ? style.Color : palette.Accent;

    static string? KeyGlyph(WheelAction? action) => action switch
    {
        null => null,
        { Id: "nav:more" } => "",
        { Id: "nav:back" } => "",
        { IsConversion: true } => KeyStyle.ForTarget(action.Target)?.Family.Glyph ?? "",
        _ => string.IsNullOrEmpty(action.Glyph) ? null : action.Glyph,
    };

    static WpfPath KeyFace(Geometry geometry, Color color)
    {
        // Köşeleri yuvarlatmak için şekil "Round" kadar içeri çekilmiştir; aynı renkte kalın kenarla çizilir.
        var brush = new SolidColorBrush(color);
        return new WpfPath
        {
            Data = geometry, Fill = brush, Stroke = brush,
            StrokeThickness = Round * 2, StrokeLineJoin = PenLineJoin.Round,
        };
    }

    (TextBlock label, TextBlock? icon, StackPanel holder) SlotContent(WheelAction? action, Color color)
    {
        var holder = new StackPanel { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        TextBlock? icon = null;
        if (KeyGlyph(action) is { } glyph)
        {
            icon = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = 18,
                Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            holder.Children.Add(icon);
        }
        var label = new TextBlock
        {
            Text = action?.Label ?? "",
            FontFamily = TextFont,
            FontSize = action is { IsConversion: true } ? 12 : 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(palette.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        holder.Children.Add(label);
        return (label, icon, holder);
    }

    void BuildCenter()
    {
        // Ortadaki yuvarlak tuş: tepsiye oturan yan yüz ve üst yüz.
        var side = new Ellipse { Tag = "center", Width = CenterR * 2, Height = CenterR * 2, Fill = new SolidColorBrush(palette.KeySide) };
        Canvas.SetLeft(side, C - CenterR);
        Canvas.SetTop(side, C - CenterR + KeyDepth - 1);
        canvas.Children.Add(side);
        var top = new Ellipse
        {
            Tag = "center", Width = CenterR * 2, Height = CenterR * 2,
            Fill = new SolidColorBrush(palette.KeyTop),
            Stroke = new SolidColorBrush(palette.KeyEdge), StrokeThickness = 1,
        };
        Canvas.SetLeft(top, C - CenterR);
        Canvas.SetTop(top, C - CenterR);
        canvas.Children.Add(top);

        var stack = new StackPanel { Tag = "center", Width = CenterR * 2 - 16, IsHitTestVisible = false };
        Style(centerTitle, 15, FontWeights.SemiBold, palette.Text);
        Style(centerSub, 10, FontWeights.Normal, palette.SubText);
        Style(centerHint, 10, FontWeights.SemiBold, palette.SubText);
        foreach (var t in new[] { centerTitle, centerSub, centerHint })
        {
            if (t.Parent is Panel p) p.Children.Remove(t);
            stack.Children.Add(t);
        }
        stack.Measure(new Size(stack.Width, double.PositiveInfinity));
        Canvas.SetLeft(stack, C - stack.Width / 2);
        Canvas.SetTop(stack, C - 24);
        canvas.Children.Add(stack);

        static void Style(TextBlock t, double size, FontWeight weight, Color color)
        {
            t.FontFamily = TextFont;
            t.FontSize = size;
            t.FontWeight = weight;
            t.Foreground = new SolidColorBrush(color);
            t.TextAlignment = TextAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }

    void UpdateCenter()
    {
        var action = hover >= 0 ? slots[hover].Action : null;
        var fileText = files.Count == 0 ? "dosyayı getirin"
            : files.Count == 1 ? System.IO.Path.GetFileName(files[0]) : $"{files.Count} dosya";
        centerHint.Foreground = new SolidColorBrush(palette.SubText);
        if (action is null)
        {
            centerTitle.Text = mode == WheelMode.Convert ? "Dönüştür" : "Araçlar";
            centerSub.Text = fileText;
            centerHint.Text = "";
        }
        else if (!action.IsAvailable)
        {
            centerTitle.Text = action.Label;
            centerSub.Text = fileText;
            centerHint.Text = action.Missing;
        }
        else if (action.IsConversion)
        {
            centerTitle.Text = action.Label;
            centerSub.Text = fileText;
            centerHint.Text = "→ " + (KeyStyle.ForTarget(action.Target)?.Family.Name ?? "dosya");
            centerHint.Foreground = new SolidColorBrush(slots[hover].Color);
        }
        else
        {
            centerTitle.Text = action.Label;
            centerSub.Text = action.Caption;
            centerHint.Text = "";
        }
    }

    // ---------- etkileşim ----------

    void SetHover(int index)
    {
        if (index == hover) return;
        if (hover >= 0) Highlight(slots[hover], false);
        hover = index;
        if (hover >= 0)
        {
            Highlight(slots[hover], true);
            if (slots[hover].Action is { IsAvailable: true }) Sounds.Click();
        }
        UpdateCenter();

        dwell.Stop();
        if (dragMode && hover >= 0 && slots[hover].Action?.Id.StartsWith("nav:") == true) dwell.Start();
    }

    /// <summary>Seçili tuş anında basılır ve ailesinin rengine boyanır; animasyon yok, imleçle aynı hızda.</summary>
    void Highlight(Slot slot, bool on)
    {
        if (slot.Action is not { IsAvailable: true }) return; // eksik motorlu tuş basılmaz; ortada nedeni yazar
        slot.Top.Fill = slot.Top.Stroke = new SolidColorBrush(on ? slot.Color : palette.KeyTop);
        slot.Side.Fill = slot.Side.Stroke = new SolidColorBrush(on ? Darken(slot.Color, 0.7) : palette.KeySide);
        slot.Press.Y = on ? PressDepth : 0;
        slot.Label.Foreground = new SolidColorBrush(on ? Colors.White : palette.Text);
        if (slot.Icon is not null) slot.Icon.Foreground = new SolidColorBrush(on ? Colors.White : slot.Color);
    }

    static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    int HitTest(Point p)
    {
        var dx = p.X - C;
        var dy = p.Y - C;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < InnerR - 4 || r > OuterR + 18) return -1;
        var angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var step = 360.0 / slots.Count;
        var index = (int)(((angle + step / 2) % 360) / step);
        return index < slots.Count && slots[index].Action is not null ? index : -1;
    }

    void Navigate(WheelAction? action)
    {
        if (action is null) return;
        pageIndex = action.Id == "nav:more" ? Math.Min(pageIndex + 1, pages.Count - 1) : 0;
        BuildSlots();
    }

    void Choose(int index, IReadOnlyList<string> chosenFiles)
    {
        if (index < 0 || chosenFiles.Count == 0) return;
        var action = slots[index].Action;
        if (action is not { IsAvailable: true }) return;
        if (action.Id.StartsWith("nav:")) { Navigate(action); return; }
        Close();
        ActionChosen?.Invoke(action, chosenFiles);
    }

    void OnDragEnter(object sender, DragEventArgs e)
    {
        GotDragEnter = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0)
        {
            if (!files.SequenceEqual(dropped)) SetFiles(dropped);
        }
        else
        {
            centerTitle.Text = "Desteklenmiyor";
            centerSub.Text = "yalnızca dosyalar";
        }
        OnDragOver(sender, e);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        var index = HitTest(e.GetPosition(canvas));
        SetHover(index);
        e.Effects = index >= 0 && files.Count > 0 && slots[index].Action is { IsAvailable: true } a && !a.Id.StartsWith("nav:")
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var index = HitTest(e.GetPosition(canvas));
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped || index < 0) return;
        // Gezinme ya da motoru eksik dilime bırakma: iş yapılmaz, çark fare bırakılınca kendiliğinden kapanır.
        if (slots[index].Action is not { IsAvailable: true } chosen || chosen.Id.StartsWith("nav:")) return;
        Dropped = true;
        e.Effects = DragDropEffects.Copy;
        // Kaynak uygulamanın (Dosya Gezgini) sürükleme döngüsünü bekletmemek için işi sonraya bırak.
        Dispatcher.BeginInvoke(() => Choose(index, dropped));
    }

    // ---------- çizim yardımcıları ----------

    Geometry SegmentGeometry(double fromDeg, double toDeg)
    {
        // Köşeleri yuvarlatmak için şekil "Round" kadar içeri çekilir, sonra aynı renkte kalın kenarla çizilir.
        var outer = OuterR - Round;
        var inner = InnerR + Round;
        var gapOuter = (Gap / 2 + Round) / outer * 180 / Math.PI;
        var gapInner = (Gap / 2 + Round) / inner * 180 / Math.PI;
        var a0 = fromDeg + gapOuter; var a1 = toDeg - gapOuter;
        var b0 = fromDeg + gapInner; var b1 = toDeg - gapInner;
        var large = (a1 - a0) > 180;

        var figure = new PathFigure { StartPoint = Polar(outer, a0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(outer, a1), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(inner, b1), true));
        figure.Segments.Add(new ArcSegment(Polar(inner, b0), new Size(inner, inner), 0, (b1 - b0) > 180, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    static Point Polar(double r, double deg)
    {
        var rad = deg * Math.PI / 180;
        return new Point(C + r * Math.Sin(rad), C - r * Math.Cos(rad));
    }

    // ---------- tanıtım animasyonu için (ekrana gösterilmeden) ----------

    /// <summary>Çarkın içeriğini pencereden ayırır (380×380, açılış animasyonu olmadan).</summary>
    internal FrameworkElement DetachForPreview()
    {
        var root = (FrameworkElement)Content;
        Content = null;
        root.Opacity = 1;
        zoom.ScaleX = zoom.ScaleY = 1;
        return root;
    }

    /// <summary>Çark koordinatlarındaki bir noktanın üstündeki tuşu basılı gösterir (sessiz).</summary>
    internal void PreviewPointer(Point local)
    {
        var index = HitTest(local);
        if (index == hover) return;
        if (hover >= 0) Highlight(slots[hover], false);
        hover = index;
        if (hover >= 0) Highlight(slots[hover], true);
        UpdateCenter();
    }

    internal const double PreviewSide = Side;

    // ---------- belgeler ve testler için ekran görüntüsü ----------

    /// <summary>Çarkı ekrana göstermeden PNG olarak çizer (README görselleri ve görsel kontrol için).</summary>
    public static void RenderToPng(WheelMode mode, IReadOnlyList<string> files, int hoverIndex, string output, WheelPalette? palette = null, EngineAvailability? engines = null)
    {
        var wheel = new WheelWindow(mode, files, dragMode: false, palette, engines);
        var root = (FrameworkElement)wheel.Content;
        root.Opacity = 1;
        wheel.zoom.ScaleX = wheel.zoom.ScaleY = 1;
        if (hoverIndex >= 0 && hoverIndex < wheel.slots.Count)
        {
            wheel.hover = hoverIndex;
            wheel.Highlight(wheel.slots[hoverIndex], true);
            wheel.UpdateCenter();
        }
        root.Measure(new Size(Side, Side));
        root.Arrange(new Rect(0, 0, Side, Side));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(Side * 2), (int)(Side * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output);
        encoder.Save(stream);
        wheel.Close();
    }
}
