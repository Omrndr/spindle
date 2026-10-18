using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfPath = System.Windows.Shapes.Path;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Spindle.UI;

/// <summary>
/// Spindle logosu: üstten görülen yuvarlak bir klavye tuşu, yüzünde biçim ailelerinin renginde altı dilim.
/// Bildirim kartında dilimler sırayla yanarak "çalışıyor" durumunu gösterir.
/// Çizim 64x64 birimlik bir tuval üzerinde yapılır ve Viewbox ile istenen boyuta ölçeklenir.
/// </summary>
sealed class RoundKeyLogo : Viewbox
{
    public static readonly Color[] SegmentColors =
    [
        Color.FromRgb(0x3A, 0x86, 0xFF), // belge mavisi
        Color.FromRgb(0x22, 0xA5, 0x65), // görsel yeşili
        Color.FromRgb(0x70, 0x48, 0xE8), // video moru
        Color.FromRgb(0xEA, 0x58, 0x0C), // ses turuncusu
        Color.FromRgb(0x3A, 0x86, 0xFF),
        Color.FromRgb(0x22, 0xA5, 0x65),
    ];

    readonly WpfPath[] segments = new WpfPath[6];
    readonly double dimOpacity;

    public RoundKeyLogo(bool dark)
    {
        dimOpacity = dark ? 0.3 : 0.25;
        Stretch = Stretch.Uniform;
        var canvas = new Canvas { Width = 64, Height = 64 };

        var side = new Ellipse { Width = 54, Height = 54, Fill = Brush(dark ? Color.FromRgb(0x12, 0x12, 0x15) : Color.FromRgb(0xBD, 0xB8, 0xAE)) };
        Canvas.SetLeft(side, 5); Canvas.SetTop(side, 8);
        var top = new Ellipse
        {
            Width = 54, Height = 54,
            Fill = Brush(dark ? Color.FromRgb(0x3A, 0x3A, 0x41) : Colors.White),
            Stroke = Brush(dark ? Color.FromRgb(0x4A, 0x4A, 0x52) : Color.FromRgb(0xE2, 0xDE, 0xD6)),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(top, 5); Canvas.SetTop(top, 4);
        canvas.Children.Add(side);
        canvas.Children.Add(top);

        for (var i = 0; i < 6; i++)
        {
            segments[i] = new WpfPath { Data = Segment(32, 31, 19, 8, i * 60 - 90 + 6, (i + 1) * 60 - 90 - 6), Fill = Brush(SegmentColors[i]) };
            canvas.Children.Add(segments[i]);
        }
        var hub = new Ellipse { Width = 8, Height = 8, Fill = top.Fill };
        Canvas.SetLeft(hub, 28); Canvas.SetTop(hub, 27);
        canvas.Children.Add(hub);
        Child = canvas;
    }

    /// <summary>Yalnızca verilen dilim parlak, diğerleri soluk; null ise hepsi parlak.</summary>
    public void Light(int? index)
    {
        for (var i = 0; i < 6; i++)
            segments[i].Opacity = index is null || index.Value % 6 == i ? 1 : dimOpacity;
    }

    static Geometry Segment(double cx, double cy, double R, double r, double fromDeg, double toDeg)
    {
        Point P(double rad, double deg) { var a = deg * Math.PI / 180; return new Point(cx + rad * Math.Cos(a), cy + rad * Math.Sin(a)); }
        var figure = new PathFigure { StartPoint = P(R, fromDeg), IsClosed = true };
        figure.Segments.Add(new ArcSegment(P(R, toDeg), new Size(R, R), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(P(r, toDeg), true));
        figure.Segments.Add(new ArcSegment(P(r, fromDeg), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry([figure]);
        g.Freeze();
        return g;
    }

    static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
