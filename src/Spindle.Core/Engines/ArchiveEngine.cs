using System.Formats.Tar;
using System.IO.Compression;

namespace Spindle.Core.Engines;

public static class ArchiveEngine
{
    public static string Zip(IReadOnlyList<string> inputs)
    {
        var first = inputs[0];
        var name = inputs.Count == 1 ? OutputPaths.BaseName(first) : "Arşiv";
        var output = OutputPaths.Unique(Path.Combine(OutputPaths.DirectoryOf(first), name + ".zip"));

        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var input in inputs)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            if (Directory.Exists(full))
            {
                var root = Path.GetDirectoryName(full)!;
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            else
            {
                zip.CreateEntryFromFile(full, Path.GetFileName(full), CompressionLevel.Optimal);
            }
        }
        return output;
    }

    /// <summary>Arşivi yanındaki yeni bir klasöre açar. Arşiv dışına yazmaya çalışan girdiler reddedilir.</summary>
    public static string Extract(string input)
    {
        var ext = FormatCatalog.ExtensionOf(input);
        var folder = OutputPaths.FolderFor(input);
        Directory.CreateDirectory(folder);
        try
        {
            switch (ext)
            {
                case "zip":
                    ZipFile.ExtractToDirectory(input, folder);
                    break;
                case "tar":
                    TarFile.ExtractToDirectory(input, folder, overwriteFiles: false);
                    break;
                case "tgz":
                    using (var gz = new GZipStream(File.OpenRead(input), CompressionMode.Decompress))
                        TarFile.ExtractToDirectory(gz, folder, overwriteFiles: false);
                    break;
                case "gz":
                    using (var gz = new GZipStream(File.OpenRead(input), CompressionMode.Decompress))
                    using (var dest = File.Create(Path.Combine(folder, OutputPaths.BaseName(input))))
                        gz.CopyTo(dest);
                    break;
                default:
                    throw new ConversionException($".{ext} arşivleri açılamıyor.");
            }
        }
        catch
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
            throw;
        }
        return folder;
    }

    /// <summary>.tar / .tar.gz / .gz arşivini .zip'e çevirir.</summary>
    public static string ToZip(string input)
    {
        var temp = Path.Combine(Path.GetTempPath(), "Spindle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var copy = Path.Combine(temp, Path.GetFileName(input));
            File.Copy(input, copy);
            var extracted = Extract(copy);
            var output = OutputPaths.ForTarget(input, "zip");
            ZipFile.CreateFromDirectory(extracted, output, CompressionLevel.Optimal, includeBaseDirectory: false);
            return output;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}
