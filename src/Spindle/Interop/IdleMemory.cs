using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Spindle.Interop;

/// <summary>
/// Spindle çoğu zaman tepside boş bekler. Ayarlar penceresi ya da bir dönüşüm sırasında büyüyen bellek,
/// iş bitince kendiliğinden geri verilmez. Bu sınıf, 30 sn boyunca hiçbir pencere açık değilken ve dönüşüm
/// yokken belleği sıkıştırıp kullanılmayan kısmı Windows'a iade eder. Kontrol 30 sn'de bir yapılır ve
/// yalnızca araya bir kullanım girdiyse çalışır; boştayken hiçbir iş yapmaz.
/// </summary>
sealed class IdleMemory
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly Func<bool> busy;
    bool usedSinceTrim = true;
    DateTime lastUse = DateTime.UtcNow;

    public IdleMemory(Func<bool> busy)
    {
        this.busy = busy;
        timer.Tick += (_, _) => Check();
        timer.Start();
    }

    /// <summary>Çark, ayarlar ya da dönüşüm kullanıldığında çağrılır.</summary>
    public void MarkUsed()
    {
        usedSinceTrim = true;
        lastUse = DateTime.UtcNow;
    }

    void Check()
    {
        if (!usedSinceTrim || busy() || Application.Current.Windows.Count > 0) return;
        if (DateTime.UtcNow - lastUse < TimeSpan.FromSeconds(25)) return;
        usedSinceTrim = false;
        Trim();
    }

    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        // Çalışma kümesini boşalt: sayfalar gerektiğinde geri gelir, boştayken RAM'de yer kaplamaz.
        SetProcessWorkingSetSizeEx(GetCurrentProcess(), -1, -1, 0);
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSizeEx(IntPtr process, nint min, nint max, uint flags);
}
