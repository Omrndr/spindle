namespace Spindle.Core;

public enum FileKind { Word, Excel, PowerPoint, Pdf, Image, Audio, Video, Archive, Folder, Other }

/// <summary>
/// Çarktaki tek bir dilim. Id "to:pdf" (dönüştür) ya da "tool:compress" (araç) biçimindedir.
/// <see cref="Missing"/> doluysa gereken motor bu bilgisayarda yok ("Office gerekli"); dilim soluk gösterilir.
/// </summary>
public sealed record WheelAction(string Id, string Label, string Caption, string? Glyph = null, string? Missing = null)
{
    public bool IsConversion => Id.StartsWith("to:", StringComparison.Ordinal);
    public string Target => Id[(Id.IndexOf(':') + 1)..];
    public bool IsAvailable => Missing is null;
}

public static class FormatCatalog
{
    /// <summary>
    /// Fotoğraf makinesi RAW dosyaları: Fujifilm .raf, Panasonic/Leica .raw, Adobe .dng, Canon .cr2/.cr3, Nikon .nef,
    /// Sony .arw, Olympus .orf, Panasonic .rw2, Pentax .pef, Samsung .srw. Hepsi Magick.NET'in içindeki LibRaw ile
    /// okunur; hiçbiri yazılamaz.
    /// </summary>
    public const string CameraRaw = "raf raw dng cr2 cr3 nef arw orf rw2 pef srw";

    public static bool IsCameraRaw(string path) => CameraRaw.Split(' ').Contains(ExtensionOf(path));

    static readonly Dictionary<string, FileKind> Kinds = Build(
        (FileKind.Word, "doc docx docm dot dotx rtf odt txt"),
        (FileKind.Excel, "xls xlsx xlsm ods csv"),
        (FileKind.PowerPoint, "ppt pptx pps ppsx odp"),
        (FileKind.Pdf, "pdf"),
        (FileKind.Image, "jpg jpeg jfif png webp heic heif avif bmp tif tiff gif ico svg psd tga " + CameraRaw),
        (FileKind.Audio, "mp3 m4a wav flac ogg oga opus aiff aif wma aac"),
        (FileKind.Video, "mp4 m4v mov mkv webm avi wmv flv mpg mpeg 3gp ts"),
        (FileKind.Archive, "zip tar tgz gz"));

    // Çarktaki sıra: ilk eleman en üstteki dilim.
    static readonly Dictionary<FileKind, string[]> Targets = new()
    {
        [FileKind.Word] = ["pdf", "docx", "odt", "rtf", "txt", "html", "doc"],
        [FileKind.Excel] = ["pdf", "xlsx", "csv", "ods", "xls"],
        [FileKind.PowerPoint] = ["pdf", "pptx", "png", "odp", "ppt"],
        // PDF -> Word, Word'ün kendi PDF dönüştürücüsüyle (PDFREFLOW) yapılır; Office gerekir.
        [FileKind.Pdf] = ["docx", "png", "jpg"],
        // HEIC okunabilir ama yazılamaz (Magick.NET'te HEIC kodlayıcı yok).
        [FileKind.Image] = ["png", "jpg", "webp", "pdf", "avif", "tiff", "bmp", "gif", "ico"],
        [FileKind.Audio] = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "aac"],
        [FileKind.Video] = ["mp4", "mov", "mkv", "webm", "gif", "avi", "mp3", "m4a"],
        [FileKind.Archive] = ["zip"],
    };

    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "jpg", ["jfif"] = "jpg", ["tif"] = "tiff", ["heif"] = "heic", ["aif"] = "aiff",
        ["oga"] = "ogg", ["htm"] = "html", ["m4v"] = "mp4", ["tgz"] = "tar.gz",
    };

    static readonly Dictionary<string, WheelAction> Tools = new[]
    {
        new WheelAction("tool:compress", "SIKIŞTIR", "dosyayı küçült", ""),
        new WheelAction("tool:strip", "METADATA", "bilgileri sil", ""),
        new WheelAction("tool:half", "KÜÇÜLT", "%50 boyut", ""),
        new WheelAction("tool:rotl", "SOLA", "90° döndür", ""),
        new WheelAction("tool:rotr", "SAĞA", "90° döndür", ""),
        new WheelAction("tool:gray", "S/B", "siyah-beyaz", ""),
        new WheelAction("tool:720p", "720P", "çözünürlüğü düşür", ""),
        new WheelAction("tool:mute", "SESSİZ", "sesi kaldır", ""),
        new WheelAction("tool:audio", "SESİ AL", "MP3 olarak çıkar", ""),
        new WheelAction("tool:normalize", "DENGELE", "ses seviyesi", ""),
        new WheelAction("tool:extract", "AÇ", "klasöre çıkar", ""),
        new WheelAction("tool:zip", "ZIP", "arşivle", ""),
        new WheelAction("tool:copypath", "YOL", "yolu kopyala", ""),
    }.ToDictionary(a => a.Id);

    static readonly Dictionary<FileKind, string[]> ToolsByKind = new()
    {
        [FileKind.Image] = ["compress", "strip", "half", "rotl", "rotr", "gray", "zip", "copypath"],
        [FileKind.Video] = ["compress", "720p", "mute", "audio", "zip", "copypath"],
        [FileKind.Audio] = ["compress", "normalize", "zip", "copypath"],
        [FileKind.Archive] = ["extract", "copypath"],
    };

    public static FileKind KindOf(string path)
    {
        if (Directory.Exists(path)) return FileKind.Folder;
        return Kinds.TryGetValue(ExtensionOf(path), out var kind) ? kind : FileKind.Other;
    }

    /// <summary>Uzantıyı küçük harfle ve noktasız döndürür; ".tar.gz" tek uzantı sayılır.</summary>
    public static string ExtensionOf(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return "tgz";
        return Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
    }

    public static string Normalize(string ext) => Aliases.TryGetValue(ext, out var a) ? a : ext.ToLowerInvariant();

    /// <param name="engines">Kurulu motorlar; verilmezse bu bilgisayarınkiler kullanılır.</param>
    public static IReadOnlyList<WheelAction> ConvertActionsFor(IReadOnlyList<string> paths, EngineAvailability? engines = null) =>
        Intersect(paths, ConvertActionsForOne, engines ?? EngineAvailability.Current);

    public static IReadOnlyList<WheelAction> ToolActionsFor(IReadOnlyList<string> paths, EngineAvailability? engines = null) =>
        Intersect(paths, ToolActionsForOne, engines ?? EngineAvailability.Current);

    /// <summary>Bu eylem için gereken motor eksikse kullanıcıya gösterilecek kısa açıklama.</summary>
    static string? MissingEngine(FileKind kind, string actionId, EngineAvailability e)
    {
        var converts = actionId.StartsWith("to:", StringComparison.Ordinal);
        return kind switch
        {
            FileKind.Word when converts && !e.Word => "Office gerekli",
            FileKind.Excel when converts && !e.Excel => "Office gerekli",
            FileKind.PowerPoint when converts && !e.PowerPoint => "Office gerekli",
            FileKind.Pdf when actionId == "to:docx" && !e.Word => "Office gerekli",
            FileKind.Audio or FileKind.Video when actionId is not ("tool:zip" or "tool:copypath") && !e.Ffmpeg => "ffmpeg gerekli",
            _ => null,
        };
    }

    public static WheelAction? Find(string id)
    {
        if (Tools.TryGetValue(id, out var tool)) return tool;
        return id.StartsWith("to:", StringComparison.Ordinal) ? MakeConvert(FileKind.Other, id[3..]) : null;
    }

    static List<WheelAction> ConvertActionsForOne(string path)
    {
        var kind = KindOf(path);
        if (!Targets.TryGetValue(kind, out var targets)) return [];
        var own = Normalize(ExtensionOf(path));
        // .zip zaten zip; .tar/.tgz/.gz ise zip'e çevrilebilir.
        return targets.Where(t => t != own).Select(t => MakeConvert(kind, t)).ToList();
    }

    static List<WheelAction> ToolActionsForOne(string path)
    {
        var kind = KindOf(path);
        var ids = ToolsByKind.TryGetValue(kind, out var list) ? list : ["zip", "copypath"];
        return ids.Select(id => Tools["tool:" + id]).ToList();
    }

    static WheelAction MakeConvert(FileKind kind, string target)
    {
        var caption = (kind, target) switch
        {
            (FileKind.Video, "mp3" or "m4a") => "sesi çıkar",
            (FileKind.Pdf, "png" or "jpg") => "sayfaları kaydet",
            (FileKind.PowerPoint, "png") => "slaytları kaydet",
            _ => "dönüştür",
        };
        return new WheelAction("to:" + target, target.ToUpperInvariant(), caption);
    }

    static IReadOnlyList<WheelAction> Intersect(IReadOnlyList<string> paths, Func<string, List<WheelAction>> f, EngineAvailability engines)
    {
        if (paths.Count == 0) return [];
        var result = f(paths[0]);
        foreach (var p in paths.Skip(1))
        {
            var ids = f(p).Select(a => a.Id).ToHashSet();
            result = result.Where(a => ids.Contains(a.Id)).ToList();
        }
        // Seçimdeki herhangi bir dosya için motor eksikse dilim soluk gösterilir.
        return result.Select(a =>
        {
            var missing = paths.Select(p => MissingEngine(KindOf(p), a.Id, engines)).FirstOrDefault(m => m is not null);
            return missing is null ? a : a with { Missing = missing };
        }).ToList();
    }

    static Dictionary<string, FileKind> Build(params (FileKind kind, string exts)[] groups)
    {
        var d = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, exts) in groups)
            foreach (var e in exts.Split(' ')) d[e] = kind;
        return d;
    }
}
