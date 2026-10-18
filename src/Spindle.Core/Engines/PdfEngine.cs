using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Spindle.Core.Engines;

/// <summary>PDF sayfalarını Windows'un yerleşik PDF motoruyla (Windows.Data.Pdf) görsele çevirir.</summary>
public static class PdfEngine
{
    public static async Task<IReadOnlyList<string>> RenderPagesAsync(string input, string target, IProgress<double>? progress, CancellationToken ct, double scale = 2.0)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input));
        PdfDocument doc;
        try
        {
            doc = await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex)
        {
            throw new ConversionException("PDF açılamadı (parola korumalı ya da bozuk olabilir).", ex);
        }

        var single = doc.PageCount == 1;
        var folder = single ? null : OutputPaths.FolderFor(input, "sayfalar");
        if (folder is not null) Directory.CreateDirectory(folder);

        var outputs = new List<string>();
        var digits = doc.PageCount.ToString().Length;
        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = doc.GetPage(i);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width * scale),
                DestinationHeight = (uint)Math.Round(page.Size.Height * scale),
                BitmapEncoderId = target == "jpg" ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId,
            };
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options);

            var path = single
                ? OutputPaths.ForTarget(input, target)
                : Path.Combine(folder!, $"{OutputPaths.BaseName(input)}-{(i + 1).ToString().PadLeft(digits, '0')}.{target}");
            stream.Seek(0);
            await using (var source = stream.AsStreamForRead())
            await using (var dest = File.Create(path))
                await source.CopyToAsync(dest, ct);
            outputs.Add(path);
            progress?.Report((i + 1.0) / doc.PageCount);
        }
        return folder is null ? outputs : [folder];
    }
}
