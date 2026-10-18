using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Spindle.Core.Engines;

/// <summary>Ses ve video işlerini ffmpeg ile yapar.</summary>
public sealed partial class MediaEngine(string ffmpegPath)
{
    public Task<string> ConvertAsync(string input, string target, IProgress<double>? progress, CancellationToken ct)
    {
        var output = OutputPaths.ForTarget(input, target);
        return RunAsync(input, output, ArgsForTarget(target), progress, ct);
    }

    public Task<string> ApplyToolAsync(string input, string tool, FileKind kind, IProgress<double>? progress, CancellationToken ct)
    {
        var ext = FormatCatalog.Normalize(FormatCatalog.ExtensionOf(input));
        var (output, args) = (tool, kind) switch
        {
            ("compress", FileKind.Video) => (OutputPaths.WithSuffix(input, "sıkıştırılmış", "mp4"),
                new[] { "-c:v", "libx264", "-crf", "28", "-preset", "slow", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart" }),
            ("compress", FileKind.Audio) => (OutputPaths.WithSuffix(input, "sıkıştırılmış", "mp3"),
                new[] { "-vn", "-c:a", "libmp3lame", "-b:a", "128k" }),
            ("720p", _) => (OutputPaths.WithSuffix(input, "720p", "mp4"),
                new[] { "-vf", "scale=-2:'min(720,ih)'", "-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart" }),
            ("mute", _) => (OutputPaths.WithSuffix(input, "sessiz"), new[] { "-map", "0:v", "-c:v", "copy", "-an" }),
            ("audio", _) => (OutputPaths.ForTarget(input, "mp3"), ArgsForTarget("mp3")),
            ("normalize", _) => (OutputPaths.WithSuffix(input, "dengelenmiş", ext is "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" ? ext : "mp3"),
                new[] { "-vn", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11" }.Concat(AudioCodecFor(ext)).ToArray()),
            _ => throw new NotSupportedException(tool),
        };
        return RunAsync(input, output, args, progress, ct);
    }

    static string[] AudioCodecFor(string ext) => ext switch
    {
        "m4a" => ["-c:a", "aac", "-b:a", "192k"],
        "wav" => ["-c:a", "pcm_s16le"],
        "flac" => ["-c:a", "flac"],
        "ogg" => ["-c:a", "libvorbis", "-q:a", "5"],
        "opus" => ["-c:a", "libopus", "-b:a", "128k"],
        _ => ["-c:a", "libmp3lame", "-q:a", "2"],
    };

    static string[] ArgsForTarget(string target) => target switch
    {
        "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
        "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
        "aac" => ["-vn", "-c:a", "aac", "-b:a", "192k", "-f", "adts"],
        "wav" => ["-vn", "-c:a", "pcm_s16le"],
        "flac" => ["-vn", "-c:a", "flac"],
        "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
        "opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
        "aiff" => ["-vn", "-c:a", "pcm_s16be"],
        "mp4" or "mov" => ["-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"],
        "mkv" => ["-c:v", "libx264", "-crf", "20", "-preset", "medium", "-c:a", "aac", "-b:a", "192k"],
        // cpu-used 4: VP9'un varsayılanından ~3,4 kat hızlı, kalite farkı ölçülemeyecek kadar az
        // (1080p örnekte 67 sn -> 20 sn, SSIM 0,9975 -> 0,9968).
        "webm" => ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-tile-columns", "2", "-c:a", "libopus", "-b:a", "128k"],
        "avi" => ["-c:v", "mpeg4", "-q:v", "3", "-c:a", "libmp3lame", "-q:a", "3"],
        "gif" => ["-vf", "fps=12,scale='min(640,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0"],
        _ => throw new NotSupportedException($"ffmpeg {target} biçimine çeviremiyor."),
    };

    async Task<string> RunAsync(string input, string output, string[] args, IProgress<double>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-n", "-i", input }) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var a in new[] { "-progress", "pipe:1", "-nostats", output }) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new ConversionException("ffmpeg başlatılamadı.");
        double durationSeconds = 0;
        var errorTail = new Queue<string>();

        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                var m = DurationRegex().Match(line);
                if (m.Success && durationSeconds == 0)
                    durationSeconds = TimeSpan.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).TotalSeconds;
                errorTail.Enqueue(line);
                if (errorTail.Count > 8) errorTail.Dequeue();
            }
        });
        var stdout = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(12), out var us) && durationSeconds > 0)
                    progress?.Report(Math.Clamp(us / 1e6 / durationSeconds, 0, 1));
            }
        });

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            TryDelete(output);
            throw;
        }
        await Task.WhenAll(stderr, stdout);

        if (process.ExitCode != 0)
        {
            TryDelete(output);
            var last = errorTail.LastOrDefault(l => l.Trim().Length > 0) ?? "bilinmeyen hata";
            throw new ConversionException($"ffmpeg hata verdi: {last.Trim()}");
        }
        return output;
    }

    static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    [GeneratedRegex(@"Duration:\s*(\d+:\d+:\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();
}

public static class FfmpegLocator
{
    /// <summary>ffmpeg.exe'yi bilinen yerlerde arar; bulamazsa null döner.</summary>
    public static string? Find(string? customPath = null)
    {
        var candidates = new List<string?>
        {
            customPath,
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
        };
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim('"'), "ffmpeg.exe")));

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
        var wingetPackages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPackages))
        {
            foreach (var dir in Directory.EnumerateDirectories(wingetPackages, "*FFmpeg*"))
                candidates.AddRange(SafeEnumerate(dir, "ffmpeg.exe"));
        }
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.Add(Path.Combine(programFiles, "ffmpeg", "bin", "ffmpeg.exe"));
        candidates.Add(Path.Combine(programFiles, "File Converter", "ffmpeg.exe"));

        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));
    }

    static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).ToList(); }
        catch { return []; }
    }
}
