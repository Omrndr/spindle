using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Spindle.Core;
using Spindle.Interop;
using Spindle.UI;

namespace Spindle;

/// <summary>Çarktan seçilen eylemi çalıştırır ve ilerlemeyi yalnızca köşedeki kartta gösterir.</summary>
sealed class ConversionRunner(ConversionService service)
{
    int active;
    bool queueFailed;

    /// <summary>Şu an süren dönüşüm var mı (boştaki bellek iadesi bunu bekler).</summary>
    public bool IsBusy => active > 0;

    /// <summary>Bir dönüşüm başladığında ya da bittiğinde.</summary>
    public event Action? Used;

    public async void Run(WheelAction action, IReadOnlyList<string> files)
    {
        active++;
        Used?.Invoke();
        var success = false;
        try { success = await RunCore(action, files); }
        finally
        {
            active--;
            queueFailed |= !success;
            // Sıradaki işlerin hepsi bitti ve hiçbiri hata vermediyse kısa bir çan.
            if (active == 0)
            {
                if (!queueFailed && action.Id != "tool:copypath") Sounds.Chime();
                queueFailed = false;
            }
            Used?.Invoke();
        }
    }

    async Task<bool> RunCore(WheelAction action, IReadOnlyList<string> files)
    {
        if (action.Id == "tool:copypath")
        {
            Clipboard.SetText(string.Join(Environment.NewLine, files));
            ToastWindow.Info("Yol kopyalandı", files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} dosya");
            return true;
        }

        var heading = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} dosya";
        var usesOffice = action.IsConversion && files.Any(f => FormatCatalog.KindOf(f) is FileKind.Word or FileKind.Excel or FileKind.PowerPoint);
        var stage = usesOffice ? "Program yükleniyor…" : "Hazırlanıyor…";
        var toast = ToastWindow.Show($"{heading} → {action.Label}", stage);

        // Office yüzde bildirmez; kullanıcı işin ilerlediğini aşama adı ve geçen süreyle görür.
        var prefix = "";
        var hasPercent = false;
        var clock = Stopwatch.StartNew();
        void ShowStage()
        {
            if (hasPercent) return;
            var seconds = clock.Elapsed.TotalSeconds;
            toast.SetStatus(prefix + stage + (seconds >= 3 ? $" · {seconds:0} sn" : ""));
        }
        var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ticker.Tick += (_, _) => ShowStage();
        ticker.Start();
        var status = new Progress<string>(s => { stage = s; ShowStage(); });

        var outputs = new List<string>();
        var errors = new List<string>();
        try
        {
            var parallel = ConversionService.ParallelismFor(action.Id, files);
            if (ConversionService.IsBatchAction(action.Id) && files.Count > 1)
            {
                Collect(await service.RunBatchAsync(action.Id, files), outputs, errors, files.Count > 1);
            }
            else if (parallel > 1)
            {
                // Görsel ve ses/video dosyaları aynı anda birkaç tane çevrilir; kart tamamlanan sayıyı gösterir.
                hasPercent = true;
                var done = 0;
                toast.SetProgress(0, $"0/{files.Count} · aynı anda {parallel} dosya");
                using var slots = new SemaphoreSlim(parallel);
                await Task.WhenAll(files.Select(async file =>
                {
                    await slots.WaitAsync();
                    try
                    {
                        var result = await service.RunAsync(action.Id, file);
                        Collect(result, outputs, errors, prefixName: true);
                        done++;
                        toast.SetProgress((double)done / files.Count, $"{done}/{files.Count} tamamlandı");
                    }
                    finally { slots.Release(); }
                }));
            }
            else
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var index = i;
                    prefix = files.Count > 1 ? $"{i + 1}/{files.Count} · " : "";
                    var progress = new Progress<double>(p =>
                    {
                        hasPercent = true;
                        toast.SetProgress((index + p) / files.Count, files.Count == 1 ? $"Dönüştürülüyor · %{p * 100:0}" : $"{index + 1}/{files.Count} · %{p * 100:0}");
                    });
                    Collect(await service.RunAsync(action.Id, files[i], progress, status: status), outputs, errors, files.Count > 1);
                    if (files.Count > 1) toast.SetProgress((i + 1.0) / files.Count, $"{i + 1}/{files.Count} tamamlandı");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }
        finally
        {
            ticker.Stop();
        }

        if (errors.Count == 0)
        {
            toast.Complete(outputs, outputs.Count == 1 ? Path.GetFileName(outputs[0]) + " hazır" : $"{outputs.Count} dosya hazır");
            return true;
        }
        toast.Fail(string.Join("\n", errors.Take(3)) + (errors.Count > 3 ? $"\n… ve {errors.Count - 3} hata daha" : ""), outputs);
        return false;
    }

    static void Collect(ConversionResult result, List<string> outputs, List<string> errors, bool prefixName)
    {
        if (result.Success) outputs.AddRange(result.Outputs);
        else errors.Add(prefixName ? $"{Path.GetFileName(result.Input)}: {result.Error}" : result.Error ?? "Bilinmeyen hata");
    }
}
