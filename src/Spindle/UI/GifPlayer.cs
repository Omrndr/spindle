using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Spindle.UI;

/// <summary>
/// Tanıtım GIF'lerini oynatır (WPF GIF'i hareketli göstermez). İlk turda her kare bir kez çözülür ve yalnızca
/// değişen dikdörtgeni saklanır; sonraki turlarda kare çözülmez, o küçük parça bitmap'e yazılır.
/// Gizlenince oynatma durur.
/// </summary>
sealed class GifPlayer : Image
{
    sealed record Patch(Int32Rect Rect, int[] Pixels, int DelayMs);

    readonly DispatcherTimer timer = new();
    readonly List<Patch> patches = [];
    GifBitmapDecoder? decoder;
    Stream? stream;
    WriteableBitmap? canvas;
    int[] composite = [], frame = [];
    int width, height, frameCount, index = -1;

    public GifPlayer()
    {
        Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => { if (IsVisible && canvas is not null) timer.Start(); else timer.Stop(); };
        Unloaded += (_, _) => Stop();
    }

    public void Play(Uri resource)
    {
        Stop();
        patches.Clear();
        stream = Application.GetResourceStream(resource).Stream;
        // Önbelleksiz çözücü: kareler WIC tarafında birikmez; gerekeni Patch olarak biz saklarız.
        decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
        frameCount = decoder.Frames.Count;
        width = decoder.Frames[0].PixelWidth;
        height = decoder.Frames[0].PixelHeight;
        composite = new int[width * height];
        frame = new int[width * height];
        canvas = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        Source = canvas;
        index = -1;
        Advance();
    }

    public void Stop()
    {
        timer.Stop();
        ReleaseDecoder();
        canvas = null;
    }

    void ReleaseDecoder()
    {
        decoder = null;
        stream?.Dispose();
        stream = null;
        composite = frame = [];
    }

    void Advance()
    {
        if (canvas is null) return;
        index = (index + 1) % frameCount;
        if (index >= patches.Count) patches.Add(Decode(index));
        // Bütün kareler bir kez çözüldü: çözücü ve tam boy ara bellekler artık gereksiz.
        if (patches.Count == frameCount && decoder is not null) ReleaseDecoder();

        var patch = patches[index];
        if (patch.Pixels.Length > 0) canvas.WritePixels(patch.Rect, patch.Pixels, patch.Rect.Width * 4, 0);
        timer.Interval = TimeSpan.FromMilliseconds(patch.DelayMs);
        if (IsVisible) timer.Start();
    }

    /// <summary>Kareyi önceki karelerin üstüne yerleştirir ve değişen dikdörtgenin son halini döndürür.</summary>
    Patch Decode(int i)
    {
        var source = decoder!.Frames[i];
        var meta = source.Metadata as BitmapMetadata;
        var left = Query(meta, "/imgdesc/Left");
        var top = Query(meta, "/imgdesc/Top");
        var delay = Math.Max(2, Query(meta, "/grctlext/Delay", 8)) * 10;
        if (i == 0) Array.Clear(composite);

        // Saydam pikseller bir önceki kareyi korur (GIF "değiştirme yok" kipi).
        var w = Math.Min(source.PixelWidth, width - left);
        var h = Math.Min(source.PixelHeight, height - top);
        new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(frame, source.PixelWidth * 4, 0);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < h; y++)
        {
            var row = y * source.PixelWidth;
            var target = (y + top) * width + left;
            for (var x = 0; x < w; x++)
            {
                var pixel = frame[row + x];
                if ((pixel >>> 24) == 0 || composite[target + x] == pixel) continue;
                composite[target + x] = pixel;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        if (maxX < 0) return new Patch(Int32Rect.Empty, [], delay);

        var rect = new Int32Rect(left + minX, top + minY, maxX - minX + 1, maxY - minY + 1);
        var pixels = new int[rect.Width * rect.Height];
        for (var y = 0; y < rect.Height; y++)
            Array.Copy(composite, (rect.Y + y) * width + rect.X, pixels, y * rect.Width, rect.Width);
        return new Patch(rect, pixels, delay);
    }

    static int Query(BitmapMetadata? meta, string query, int fallback = 0)
    {
        try { return meta?.GetQuery(query) is ushort v ? v : fallback; }
        catch { return fallback; }
    }
}
