using Spindle.Core.Engines;

namespace Spindle.Core;

/// <summary>
/// Bu bilgisayarda hangi dönüştürme motorlarının kurulu olduğu. Çark, eksik motor gerektiren dilimleri
/// soluk gösterir ("Office gerekli") ki kullanıcı dosyayı bırakıp ancak sonra hata görmesin.
/// Sonuç 30 sn önbellekte tutulur; her çark açılışında kayıt defteri ve disk taranmaz.
/// </summary>
public sealed record EngineAvailability(bool Word, bool Excel, bool PowerPoint, bool Ffmpeg)
{
    public static readonly EngineAvailability All = new(true, true, true, true);

    /// <summary>Ayarlarda elle seçilmiş ffmpeg yolu (varsa).</summary>
    public static string? FfmpegCustomPath { get; set; }

    static readonly object Gate = new();
    static EngineAvailability? cached;
    static DateTime cachedAt;

    public static EngineAvailability Current
    {
        get
        {
            lock (Gate)
            {
                if (cached is null || DateTime.UtcNow - cachedAt > TimeSpan.FromSeconds(30))
                {
                    cached = new EngineAvailability(
                        OfficeEngine.IsAvailable("Word.Application"),
                        OfficeEngine.IsAvailable("Excel.Application"),
                        OfficeEngine.IsAvailable("PowerPoint.Application"),
                        FfmpegLocator.Find(FfmpegCustomPath) is not null);
                    cachedAt = DateTime.UtcNow;
                }
                return cached;
            }
        }
    }

    /// <summary>Kullanıcı Office ya da ffmpeg kurduktan sonra (ör. Ayarlar penceresi açılınca) çağrılır.</summary>
    public static void Refresh()
    {
        lock (Gate) cached = null;
    }
}
