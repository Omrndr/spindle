using ImageMagick;

namespace Spindle.Core.Engines;

public static class ImageEngine
{
    public static string Convert(string input, string target, int quality)
    {
        var output = OutputPaths.ForTarget(input, target);
        using var image = Load(input);
        var format = FormatFor(target);

        if (format is MagickFormat.Jpeg or MagickFormat.Bmp && image.HasAlpha)
            Flatten(image);
        if (format == MagickFormat.Ico && (image.Width > 256 || image.Height > 256))
            image.Resize(new MagickGeometry(256, 256));

        image.Quality = (uint)quality;
        image.Write(output, format);
        return output;
    }

    public static string ApplyTool(string input, string tool, int quality)
    {
        var (suffix, op) = tool switch
        {
            "compress" => ("sıkıştırılmış", (Action<IMagickImage<ushort>>)(img => Compress(img, quality))),
            "strip" => ("temiz", img => img.Strip()),
            "half" => ("küçük", img => img.Resize(new Percentage(50))),
            "rotl" => ("döndürülmüş", img => img.Rotate(-90)),
            "rotr" => ("döndürülmüş", img => img.Rotate(90)),
            "gray" => ("siyah-beyaz", img => img.Grayscale()),
            _ => throw new NotSupportedException(tool),
        };
        var output = OutputPaths.WithSuffix(input, suffix);
        using var image = Load(input);
        image.Quality = (uint)quality;
        op(image);
        image.Write(output);
        return output;
    }

    static void Compress(IMagickImage<ushort> image, int quality)
    {
        image.Strip();
        image.Quality = (uint)Math.Min(quality, 72);
        if (image.Format == MagickFormat.Png)
        {
            // PNG kayıpsızdır; asıl kazanç renk sayısını 256'ya indirmekten gelir (pngquant benzeri).
            image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
            image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        }
    }

    static void Flatten(IMagickImage<ushort> image)
    {
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
    }

    static IMagickImage<ushort> Load(string path)
    {
        var ext = FormatCatalog.ExtensionOf(path);
        var settings = new MagickReadSettings();
        if (ext == "svg")
        {
            settings.Density = new Density(192);
            settings.BackgroundColor = MagickColors.Transparent;
        }

        if (ext is "ico" or "gif" or "tif" or "tiff")
        {
            // Çok kareli dosyalarda en büyük kareyi al (ICO'da genelde 256 px'lik olan).
            using var frames = new MagickImageCollection(path, settings);
            var best = frames.OrderByDescending(f => (long)f.Width * f.Height).First();
            return best.Clone();
        }

        var image = new MagickImage(path, settings);
        image.AutoOrient();
        return image;
    }

    static MagickFormat FormatFor(string target) => target switch
    {
        "png" => MagickFormat.Png,
        "jpg" => MagickFormat.Jpeg,
        "webp" => MagickFormat.WebP,
        "pdf" => MagickFormat.Pdf,
        "avif" => MagickFormat.Avif,
        "tiff" => MagickFormat.Tiff,
        "bmp" => MagickFormat.Bmp,
        "gif" => MagickFormat.Gif,
        "ico" => MagickFormat.Ico,
        _ => throw new NotSupportedException($"Görsel {target} biçimine çevrilemez."),
    };
}
