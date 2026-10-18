using System.Windows.Media;

namespace Spindle.Wheel;

public enum WheelMode { Convert, Tools }

/// <summary>
/// Çarkın renkleri. Çark bir tuş takımıdır: koyu bir tepsinin üstünde, her biri yan yüzü ve üst yüzü olan tuşlar.
/// </summary>
sealed record WheelPalette(
    Color Tray, Color TrayEdge, Color KeyTop, Color KeySide, Color KeyEdge,
    Color Text, Color SubText, Color Accent, Color Shadow)
{
    public static readonly WheelPalette Light = new(
        Tray: Color.FromRgb(0xDC, 0xDA, 0xD5), TrayEdge: Color.FromRgb(0xCF, 0xCC, 0xC6),
        KeyTop: Colors.White, KeySide: Color.FromRgb(0xC9, 0xC6, 0xBF), KeyEdge: Color.FromRgb(0xE7, 0xE4, 0xDE),
        Text: Color.FromRgb(0x1D, 0x1D, 0x1F), SubText: Color.FromRgb(0x86, 0x86, 0x8B),
        Accent: Color.FromRgb(0x3A, 0x86, 0xFF), Shadow: Colors.Black);

    public static readonly WheelPalette Dark = new(
        Tray: Color.FromRgb(0x0F, 0x0F, 0x12), TrayEdge: Color.FromRgb(0x26, 0x26, 0x2B),
        KeyTop: Color.FromRgb(0x33, 0x33, 0x3A), KeySide: Color.FromRgb(0x1A, 0x1A, 0x1E), KeyEdge: Color.FromRgb(0x42, 0x42, 0x4A),
        Text: Color.FromRgb(0xF5, 0xF5, 0xF7), SubText: Color.FromRgb(0xA1, 0xA1, 0xA6),
        Accent: Color.FromRgb(0x5B, 0x9B, 0xFF), Shadow: Colors.Black);

    public bool IsDark => this == Dark;

    public static WheelPalette Current => AppSettings.IsDarkTheme() ? Dark : Light;
}

/// <summary>
/// Hedef biçimin ailesi: tuşun simgesi, rengi ve ortadaki "→ belge" yazısı buradan gelir.
/// Belgeler mavi (PDF kırmızı), görseller yeşil, video mor, ses turuncu.
/// </summary>
static class KeyStyle
{
    public sealed record Family(string Name, string Glyph);

    static readonly Family Document = new("belge", "");
    static readonly Family Pdf = new("belge", "");
    static readonly Family Sheet = new("tablo", "");
    static readonly Family Slides = new("sunum", "");
    static readonly Family Web = new("web", "");
    static readonly Family Image = new("görsel", "");
    static readonly Family Video = new("video", "");
    static readonly Family Audio = new("ses", "");
    static readonly Family Archive = new("arşiv", "");

    static readonly Dictionary<string, (string Hex, Family Family)> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = ("#E5484D", Pdf),
        ["docx"] = ("#2B6CD6", Document), ["odt"] = ("#3A86FF", Document), ["rtf"] = ("#5B5BD6", Document),
        ["txt"] = ("#6B7280", Document), ["doc"] = ("#1F4FA3", Document),
        ["html"] = ("#EA6C1A", Web),
        ["xlsx"] = ("#1E8E4E", Sheet), ["csv"] = ("#4C9A6A", Sheet), ["ods"] = ("#2F9E6E", Sheet), ["xls"] = ("#17703C", Sheet),
        ["pptx"] = ("#D9532B", Slides), ["odp"] = ("#E0703C", Slides), ["ppt"] = ("#B8441F", Slides),
        ["png"] = ("#22A565", Image), ["jpg"] = ("#2F9E44", Image), ["webp"] = ("#0F9FB0", Image),
        ["avif"] = ("#30A46C", Image), ["tiff"] = ("#6B9E1F", Image), ["bmp"] = ("#5AA02C", Image),
        ["gif"] = ("#D6409F", Image), ["ico"] = ("#16A34A", Image),
        ["mp4"] = ("#7048E8", Video), ["mov"] = ("#5F3DC4", Video), ["mkv"] = ("#845EF7", Video),
        ["webm"] = ("#9C36B5", Video), ["avi"] = ("#6741D9", Video),
        ["mp3"] = ("#EA580C", Audio), ["m4a"] = ("#F76707", Audio), ["wav"] = ("#D9480F", Audio),
        ["flac"] = ("#E67700", Audio), ["ogg"] = ("#D97706", Audio), ["opus"] = ("#C2410C", Audio),
        ["aiff"] = ("#B45309", Audio), ["aac"] = ("#F59F00", Audio),
        ["zip"] = ("#8A6D3B", Archive),
    };

    /// <summary>Dönüştürme tuşu için renk ve aile; bilinmeyen biçimde (ve araçlarda) null.</summary>
    public static (Color Color, Family Family)? ForTarget(string target) =>
        Targets.TryGetValue(target, out var t) ? ((Color)ColorConverter.ConvertFromString(t.Hex), t.Family) : null;
}
