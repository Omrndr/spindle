using System.Windows;
using ImageMagick;
using Spindle.Wheel;

namespace Spindle.Tour;

/// <summary>
/// Üç tanıtım GIF'ini üretir (<c>Spindle.exe --render-tour docs</c>): sürükle + Shift, sürükle + Ctrl, sağ tık menüsü.
/// Kareler <see cref="TourStage"/> ile ekrana pencere açılmadan çizilir. Aynı GIF'ler README'de ve ilk açılıştaki
/// tanıtım penceresinde kullanılır.
/// </summary>
static class TourRenderer
{
    const double Step = 0.08; // 12,5 kare/sn (GIF gecikmesi 8/100 sn)

    public static readonly string[] Files = ["tanitim-donustur.gif", "tanitim-araclar.gif", "tanitim-sag-tik.gif"];

    public static void RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        Write(Path.Combine(folder, Files[0]), 8.4, DragConvert());
        Write(Path.Combine(folder, Files[1]), 7.6, DragTools());
        Write(Path.Combine(folder, Files[2]), 8.4, RightClick());
    }

    // ---------- sahneler ----------

    /// <summary>rapor.docx sürüklenir, Shift'e basılır, ODT'nin üstünden geçip PDF'e bırakılır.</summary>
    static Action<TourStage, double> DragConvert()
    {
        Track? path = null;
        bool dragging = false, open = false;
        return (stage, t) =>
        {
            if (path is null)
            {
                stage.ShowKey("⇧ Shift");
                var file = stage.TileCenter("rapor.docx");
                var drop = new Point(380, 222);
                path = new Track(new(0, new(640, 400)), new(0.3, new(640, 400)), new(1.1, file), new(1.3, file), new(2.1, drop));
            }
            var pointer = path.At(t);
            stage.Select(t >= 1.15 && t < 4.6 ? "rapor.docx" : null);

            if (t >= 1.3 && t < 4.6 && !dragging) { stage.StartDrag("rapor.docx", pointer); dragging = true; }
            if (t >= 2.4 && t < 4.6 && !open)
            {
                stage.PressKey(true);
                stage.OpenWheel(WheelMode.Convert, "rapor.docx", pointer);
                var center = pointer;
                path = path.Then(new(2.9, center), new(3.4, stage.KeyCenter(1, 6)), new(3.75, stage.KeyCenter(1, 6)), new(4.2, stage.KeyCenter(0, 6)));
                open = true;
            }
            if (open) { stage.WheelPop((t - 2.4) / 0.17); stage.PointWheel(path.At(t)); }
            if (t >= 4.6 && open)
            {
                stage.CloseWheel();
                stage.EndDrag();
                stage.PressKey(false);
                stage.ShowToast("rapor.docx → PDF");
                open = dragging = false;
            }
            if (t >= 4.6) Toast(stage, t - 4.6, ["Program yükleniyor…", "Belge açılıyor…", "PDF oluşturuluyor…"], 2.0, "rapor.pdf hazır", "Göster");
            if (t >= 6.6) stage.AddOnce("rapor.pdf", 6, Math.Clamp((t - 6.6) / 0.3, 0, 1));
            stage.Cursor = path.At(t);
        };
    }

    /// <summary>foto.jpg sürüklenir, Ctrl'ye basılır, KÜÇÜLT'ün üstünden geçip SIKIŞTIR'a bırakılır.</summary>
    static Action<TourStage, double> DragTools()
    {
        Track? path = null;
        bool dragging = false, open = false;
        return (stage, t) =>
        {
            if (path is null)
            {
                stage.ShowKey("Ctrl");
                var file = stage.TileCenter("foto.jpg");
                path = new Track(new(0, new(660, 380)), new(0.3, new(660, 380)), new(1.0, file), new(1.2, file), new(2.0, new(360, 226)));
            }
            var pointer = path.At(t);
            stage.Select(t >= 1.05 && t < 4.3 ? "foto.jpg" : null);
            if (t >= 1.2 && t < 4.3 && !dragging) { stage.StartDrag("foto.jpg", pointer); dragging = true; }
            if (t >= 2.3 && t < 4.3 && !open)
            {
                stage.PressKey(true);
                stage.OpenWheel(WheelMode.Tools, "foto.jpg", pointer);
                path = path.Then(new(2.8, pointer), new(3.25, stage.KeyCenter(2, 8)), new(3.55, stage.KeyCenter(2, 8)), new(3.95, stage.KeyCenter(0, 8)));
                open = true;
            }
            if (open) { stage.WheelPop((t - 2.3) / 0.17); stage.PointWheel(path.At(t)); }
            if (t >= 4.3 && open)
            {
                stage.CloseWheel();
                stage.EndDrag();
                stage.PressKey(false);
                stage.ShowToast("foto.jpg → SIKIŞTIR");
                open = dragging = false;
            }
            if (t >= 4.3) Toast(stage, t - 4.3, ["Hazırlanıyor…"], 1.1, "foto (sıkıştırılmış).jpg hazır", "Göster");
            if (t >= 5.4) stage.AddOnce("foto (sıkıştırılmış).jpg", 6, Math.Clamp((t - 5.4) / 0.3, 0, 1));
            stage.Cursor = path.At(t);
        };
    }

    /// <summary>klip.mp4'e sağ tıklanır, Spindle ▸ Dönüştür… seçilir, çarkta MP3'e tıklanır.</summary>
    static Action<TourStage, double> RightClick()
    {
        Track? path = null;
        bool menu = false, sub = false, open = false;
        return (stage, t) =>
        {
            if (path is null)
            {
                var file = stage.TileCenter("klip.mp4");
                path = new Track(new(0, new(640, 400)), new(0.3, new(640, 400)), new(1.0, file), new(1.2, file));
            }
            stage.Select(t >= 1.05 && t < 4.7 ? "klip.mp4" : null);
            if (t >= 1.2 && !menu && !open)
            {
                stage.OpenMenu(path.At(t));
                var spindle = stage.MenuItem(2);
                path = path.Then(new(1.8, spindle), new(2.0, spindle));
                menu = true;
            }
            if (menu)
            {
                var p = path.At(t);
                var overSpindle = t >= 1.8;
                if (overSpindle && !sub)
                {
                    stage.OpenSubmenu();
                    var convert = stage.SubmenuItem(0);
                    path = path.Then(new(2.6, convert), new(2.9, convert));
                    sub = true;
                }
                stage.HoverMenu(overSpindle ? 2 : -1, sub && t >= 2.55 ? 0 : -1);
                if (t >= 2.9)
                {
                    stage.CloseMenus();
                    menu = false;
                    var at = path.At(t);
                    // Çark, imlecin olduğu yerde açılır; ekrana sığması için uygulama gibi kenardan uzak tutulur.
                    var center = new Point(Math.Max(at.X, 140), Math.Max(at.Y, 140));
                    stage.OpenWheel(WheelMode.Convert, "klip.mp4", center);
                    path = path.Then(new(3.0, at), new(3.6, stage.KeyCenter(6, 7)), new(3.9, stage.KeyCenter(6, 7)), new(4.3, stage.KeyCenter(5, 7)));
                    open = true;
                }
            }
            if (open) { stage.WheelPop((t - 2.9) / 0.17); stage.PointWheel(path.At(t)); }
            if (t >= 4.7 && open)
            {
                stage.CloseWheel();
                stage.ShowToast("klip.mp4 → MP3");
                open = false;
            }
            if (t >= 4.7)
            {
                var k = t - 4.7;
                if (k < 1.7) stage.ToastWorking($"Dönüştürülüyor · %{Math.Min(100, k / 1.6 * 100):0}", Math.Min(1, k / 1.6), (int)(k / 0.125));
                else stage.ToastDone("klip.mp3 hazır", "Göster");
            }
            if (t >= 6.4) stage.AddOnce("klip.mp3", 6, Math.Clamp((t - 6.4) / 0.3, 0, 1));
            stage.Cursor = path.At(t);
        };
    }

    /// <summary>Office gibi yüzde bildirmeyen işler: aşama adları ve aşamalara göre dolan çubuk, sonra "hazır".</summary>
    static void Toast(TourStage stage, double k, string[] stages, double duration, string done, string action)
    {
        if (k >= duration) { stage.ToastDone(done, action); return; }
        var stageIndex = Math.Min(stages.Length - 1, (int)(k / duration * stages.Length));
        var progress = 1 - Math.Pow(1 - k / duration, 1.6);
        stage.ToastWorking(stages[stageIndex], Math.Min(0.96, progress), (int)(k / 0.125));
    }

    // ---------- kareleri GIF'e yazma ----------

    static void Write(string output, double duration, Action<TourStage, double> scene)
    {
        var stage = new TourStage();
        var frames = new List<(byte[] Pixels, int Delay)>();
        for (var t = 0.0; t < duration; t += Step)
        {
            scene(stage, t);
            var pixels = stage.Render();
            // Değişmeyen kareler ayrı kare olarak yazılmaz; bir önceki karenin süresi uzar.
            if (frames.Count > 0 && frames[^1].Pixels.AsSpan().SequenceEqual(pixels))
                frames[^1] = (frames[^1].Pixels, frames[^1].Delay + 8);
            else
                frames.Add((pixels, 8));
        }
        frames[^1] = (frames[^1].Pixels, frames[^1].Delay + 120); // döngü başlamadan önce sonucu göster
        stage.Dispose();

        using var collection = new MagickImageCollection();
        var settings = new PixelReadSettings(TourStage.Width, TourStage.Height, StorageType.Char, PixelMapping.BGRA);
        foreach (var (pixels, delay) in frames)
        {
            var image = new MagickImage(pixels, settings);
            image.Alpha(AlphaAction.Off);
            image.AnimationDelay = (uint)delay;
            image.GifDisposeMethod = GifDisposeMethod.None;
            collection.Add(image);
        }
        // Tüm karelerde ortak 255 renk (karelerde renk titremesi olmasın), sonra değişmeyen pikseller saydam.
        collection.Quantize(new QuantizeSettings { Colors = 255, DitherMethod = DitherMethod.No });
        collection.OptimizeTransparency();
        collection[0].AnimationIterations = 0;
        // Magick'in kendi dosya açması uzun yollarda başarısız olur; .NET akışıyla yazılır.
        using var file = File.Create(output);
        collection.Write(file, MagickFormat.Gif);
    }

    /// <summary>Zamanla ilerleyen imleç yolu; noktalar arasında yumuşak hızlanıp yavaşlar.</summary>
    sealed class Track(params Track.Key[] keys)
    {
        public readonly record struct Key(double Time, Point At);

        readonly Key[] keys = keys;

        public Track Then(params Key[] more) => new([.. keys.Where(k => k.Time < more[0].Time), .. more]);

        public Point At(double t)
        {
            if (t <= keys[0].Time) return keys[0].At;
            for (var i = 1; i < keys.Length; i++)
            {
                if (t > keys[i].Time) continue;
                var a = keys[i - 1];
                var b = keys[i];
                var u = (t - a.Time) / Math.Max(1e-6, b.Time - a.Time);
                u = u * u * (3 - 2 * u);
                return new Point(a.At.X + (b.At.X - a.At.X) * u, a.At.Y + (b.At.Y - a.At.Y) * u);
            }
            return keys[^1].At;
        }
    }
}
