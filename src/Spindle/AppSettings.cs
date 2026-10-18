using System.Text.Json;
using Microsoft.Win32;

namespace Spindle;

public sealed class AppSettings
{
    public string ConvertKey { get; set; } = "Shift";
    public string ToolsKey { get; set; } = "Ctrl";
    public bool DragGestureEnabled { get; set; } = true;
    public bool OnlyInExplorer { get; set; } = true;
    public int ImageQuality { get; set; } = 90;
    public bool SoundsEnabled { get; set; } = true;
    /// <summary>İlk açılıştaki tanıtım gösterildi mi.</summary>
    public bool TourShown { get; set; }
    public string? FfmpegPath { get; set; }

    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spindle");
    static string FilePath => Path.Combine(Folder, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static bool Exists => File.Exists(FilePath);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* bozuk ayar dosyası: varsayılanlarla devam */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public static bool IsDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }
}
