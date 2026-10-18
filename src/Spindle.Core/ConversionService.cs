using System.Diagnostics;
using Spindle.Core.Engines;

namespace Spindle.Core;

public sealed record ConversionResult(string Input, bool Success, IReadOnlyList<string> Outputs, string? Error)
{
    public static ConversionResult Ok(string input, IReadOnlyList<string> outputs) => new(input, true, outputs, null);
    public static ConversionResult Fail(string input, string error) => new(input, false, [], error);
}

public sealed class ConversionOptions
{
    public int ImageQuality { get; set; } = 90;
    public string? FfmpegPath { get; set; }

    /// <summary>
    /// Office bu süre boyunca hiç işlemci kullanmazsa takılmış sayılır (ör. görünmez bir soru penceresinde bekliyor)
    /// ve Spindle'ın başlattığı Office kapatılır. Çalışmaya devam eden büyük belgeler kesilmez.
    /// </summary>
    public TimeSpan OfficeIdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Her durumda üst sınır (ör. kullanıcının zaten açık olan Office'ine bağlanıldığında).</summary>
    public TimeSpan OfficeMaxDuration { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>Bir çark eylemini doğru motora yönlendirir.</summary>
public sealed class ConversionService(ConversionOptions options)
{
    // Aynı anda tek bir Office oturumu: paralel Word örnekleri hem yavaş hem kırılgan.
    static readonly SemaphoreSlim OfficeGate = new(1, 1);

    // Word ~259, Excel ~218 karakterden uzun yolları açamıyor. Bu sınırın üstünde dosya kısa bir
    // geçici klasörde çevrilir ve sonuç orijinalin yanına taşınır; kullanıcı fark etmez.
    const int OfficeSafePathLength = 200;

    public ConversionOptions Options { get; } = options;

    /// <summary>Birden fazla dosyayı tek çıktıda birleştiren eylemler (ör. ZIP).</summary>
    public static bool IsBatchAction(string actionId) => actionId == "tool:zip";

    /// <summary>
    /// Çok dosyalı işlerde kaç dosyanın aynı anda çevrileceği. Office tek örnekle sırayla çalışır;
    /// ffmpeg zaten birden çok çekirdek kullandığından ses/video ikişer; görseller çekirdek sayısına göre.
    /// </summary>
    public static int ParallelismFor(string actionId, IReadOnlyList<string> files)
    {
        if (files.Count < 2 || IsBatchAction(actionId)) return 1;
        var kinds = files.Select(FormatCatalog.KindOf).ToHashSet();
        if (kinds.Overlaps([FileKind.Word, FileKind.Excel, FileKind.PowerPoint]) && actionId.StartsWith("to:")) return 1;
        if (kinds.Contains(FileKind.Pdf) && actionId == "to:docx") return 1;
        if (kinds.Overlaps([FileKind.Audio, FileKind.Video])) return 2;
        return Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
    }

    public async Task<ConversionResult> RunBatchAsync(string actionId, IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        try
        {
            var output = await Task.Run(() => ArchiveEngine.Zip(inputs), ct);
            return ConversionResult.Ok(inputs[0], [output]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ConversionResult.Fail(inputs[0], Describe(ex));
        }
    }

    /// <param name="status">Uzun süren işlerde hangi aşamada olunduğu ("PDF oluşturuluyor…" gibi).</param>
    public async Task<ConversionResult> RunAsync(string actionId, string input, IProgress<double>? progress = null,
        CancellationToken ct = default, IProgress<string>? status = null)
    {
        try
        {
            if (!File.Exists(input) && !Directory.Exists(input))
                throw new ConversionException("Dosya bulunamadı.");
            var outputs = await DispatchAsync(actionId, input, progress, status, ct);
            return ConversionResult.Ok(input, outputs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ConversionResult.Fail(input, Describe(ex));
        }
    }

    async Task<IReadOnlyList<string>> DispatchAsync(string actionId, string input, IProgress<double>? progress,
        IProgress<string>? status, CancellationToken ct)
    {
        var kind = FormatCatalog.KindOf(input);
        var sep = actionId.IndexOf(':');
        var verb = actionId[..sep];
        var arg = actionId[(sep + 1)..];

        if (verb == "to")
        {
            return (kind, arg) switch
            {
                (FileKind.Word, _) => [await OfficeAsync(input, arg, OfficeEngine.ConvertWithWordAsync, status)],
                (FileKind.Excel, _) => [await OfficeAsync(input, arg, OfficeEngine.ConvertWithExcelAsync, status)],
                (FileKind.PowerPoint, "png") => [await OfficeFolderAsync(input, status)],
                (FileKind.PowerPoint, _) => [await OfficeAsync(input, arg, OfficeEngine.ConvertWithPowerPointAsync, status)],
                (FileKind.Pdf, "png" or "jpg") => await PdfEngine.RenderPagesAsync(input, arg, progress, ct),
                (FileKind.Pdf, "docx") => [await OfficeAsync(input, arg, OfficeEngine.ConvertWithWordAsync, status)],
                (FileKind.Image, _) => [await Task.Run(() => ImageEngine.Convert(input, arg, Options.ImageQuality), ct)],
                (FileKind.Audio or FileKind.Video, _) => [await Media().ConvertAsync(input, arg, progress, ct)],
                (FileKind.Archive, "zip") => [await Task.Run(() => ArchiveEngine.ToZip(input), ct)],
                _ => throw new ConversionException($"Bu dosya {arg.ToUpperInvariant()} biçimine çevrilemiyor."),
            };
        }

        return (kind, arg) switch
        {
            (_, "zip") => [await Task.Run(() => ArchiveEngine.Zip([input]), ct)],
            (FileKind.Archive, "extract") => [await Task.Run(() => ArchiveEngine.Extract(input), ct)],
            (FileKind.Image, _) => [await Task.Run(() => ImageEngine.ApplyTool(input, arg, Options.ImageQuality), ct)],
            (FileKind.Audio or FileKind.Video, _) => [await Media().ApplyToolAsync(input, arg, kind, progress, ct)],
            _ => throw new ConversionException("Bu araç bu dosya türünde kullanılamıyor."),
        };
    }

    MediaEngine Media()
    {
        var path = FfmpegLocator.Find(Options.FfmpegPath)
            ?? throw new ConversionException("Ses/video için ffmpeg gerekli ama bulunamadı. Ayarlar'dan kurabilirsiniz.");
        return new MediaEngine(path);
    }

    /// <summary>
    /// Çark açıldığında çağrılır: dosyalar arasında Word ya da Excel belgesi varsa, kullanıcı dilimi seçerken
    /// Office arka planda başlatılır. Böylece dönüşüm Word'ün ~3,5 sn'lik açılışını beklemez.
    /// </summary>
    public static void Prewarm(IEnumerable<string> files)
    {
        foreach (var kind in files.Select(FormatCatalog.KindOf).Distinct())
            OfficeEngine.Prewarm(kind);
    }

    /// <summary>Uygulama kapanırken sıcak tutulan Office'leri düzgünce kapatır.</summary>
    public static void ShutdownOffice() => OfficeEngine.ShutdownAll();

    async Task<string> OfficeAsync(string input, string target, Func<string, string, string, Action<string>?, Task> convert, IProgress<string>? status)
    {
        var output = OutputPaths.ForTarget(input, target);
        await RunOfficeAsync(input, output, (src, dst) => convert(src, dst, target, s => status?.Report(s)), status);
        if (!File.Exists(output)) throw new ConversionException("Office dosyayı kaydetmedi.");
        return output;
    }

    async Task<string> OfficeFolderAsync(string input, IProgress<string>? status)
    {
        var folder = OutputPaths.FolderFor(input, "slaytlar");
        await RunOfficeAsync(input, folder, (src, dst) => OfficeEngine.ConvertWithPowerPointAsync(src, dst, "png", s => status?.Report(s)), status);
        if (!Directory.Exists(folder)) throw new ConversionException("PowerPoint slaytları kaydetmedi.");
        return folder;
    }

    /// <summary>Office işini sırayla, gerekirse kısa yollu geçici bir klasörde ve takılma bekçisiyle çalıştırır.</summary>
    async Task RunOfficeAsync(string input, string output, Func<string, string, Task> convert, IProgress<string>? status)
    {
        input = Path.GetFullPath(input);
        status?.Report("Sırada bekliyor…");
        await OfficeGate.WaitAsync();
        string? staging = null;
        try
        {
            var src = input;
            var dst = output;
            if (input.Length > OfficeSafePathLength || output.Length > OfficeSafePathLength)
            {
                staging = Path.Combine(Path.GetTempPath(), "Spindle", Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(staging);
                src = Path.Combine(staging, "belge" + Path.GetExtension(input));
                dst = Path.Combine(staging, "sonuc" + Path.GetExtension(output));
                File.Copy(input, src);
            }

            await WatchOffice(convert(src, dst));

            if (staging is not null)
            {
                if (Directory.Exists(dst)) Directory.Move(dst, output);
                else File.Move(dst, output);
            }
        }
        finally
        {
            OfficeGate.Release();
            if (staging is not null) try { Directory.Delete(staging, recursive: true); } catch { /* geçici klasör */ }
        }
    }

    /// <summary>
    /// Office çalıştığı sürece (işlemci kullandığı sürece) bekler; uzun süre hiçbir şey yapmazsa takılmış sayar.
    /// Böylece 85 sayfalık bir belgenin 2 dakikalık dönüşümü kesilmez, ama görünmez bir soruda bekleyen Office kapatılır.
    /// </summary>
    async Task WatchOffice(Task work)
    {
        var started = Stopwatch.StartNew();
        var idle = Stopwatch.StartNew();
        TimeSpan? lastCpu = null;
        while (true)
        {
            if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(2))) == work)
            {
                await work;
                return;
            }

            // PDF -> Word'de asıl işi Word değil PDFREFLOW yapar; onun işlemcisi de sayılır.
            var cpu = CpuOf(OfficeEngine.ActivePid, OfficeEngine.ActiveHelperPids());
            if (cpu is null || lastCpu is null || (cpu.Value - lastCpu.Value).TotalMilliseconds > 150)
                idle.Restart(); // bilgi yoksa ya da Office çalışıyorsa bekleme sayacını sıfırla
            lastCpu = cpu;

            var tooIdle = OfficeEngine.ActivePid is not null && idle.Elapsed > Options.OfficeIdleTimeout;
            if (tooIdle || started.Elapsed > Options.OfficeMaxDuration) break;
        }

        // Önce yardımcı işlemleri (ör. PDFREFLOW) kapat; Office çoğu zaman hatayla döner ve kendisi kapanır.
        // Office'i zorla kapatmak açılan dosyayı Office'in "devre dışı öğeler" listesine ekler.
        if (!OfficeEngine.AbortHelpers() || !await Finishes(work, TimeSpan.FromSeconds(15)))
        {
            OfficeEngine.AbortActive();
            await Finishes(work, TimeSpan.FromSeconds(20));
        }
        var seen = OfficeEngine.LastDialogs.Count == 0 ? "" : " Office'in sorduğu: " + string.Join(" / ", OfficeEngine.LastDialogs);
        throw new ConversionException("Office bu dosyada yanıt vermedi; arka planda kapatıldı." + seen);
    }

    static async Task<bool> Finishes(Task work, TimeSpan wait)
    {
        try { await work.WaitAsync(wait); }
        catch (TimeoutException) { return false; }
        catch { /* Office kapatıldı, hata beklenen */ }
        return true;
    }

    static TimeSpan? CpuOf(int? pid, IEnumerable<int> helpers)
    {
        if (pid is null) return null;
        var total = TimeSpan.Zero;
        foreach (var id in helpers.Prepend(pid.Value))
        {
            try { total += Process.GetProcessById(id).TotalProcessorTime; }
            catch { if (id == pid) return null; }
        }
        return total;
    }

    static string Describe(Exception ex) => ex switch
    {
        ConversionException => ex.Message,
        NotSupportedException => ex.Message,
        System.Runtime.InteropServices.COMException com => $"Office hata verdi: {com.Message.Trim()} (0x{com.HResult:X8})",
        UnauthorizedAccessException => "Bu klasöre yazma izni yok.",
        IOException => ex.Message,
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
