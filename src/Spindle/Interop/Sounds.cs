using System.Runtime.InteropServices;

namespace Spindle.Interop;

/// <summary>
/// Tuşlar arasında tok bir tık, sıradaki işler bitince kısa yumuşak bir çan.
/// Ses dosyası yoktur: iki ses ilk kullanımda bellekte birkaç kilobaytlık WAV olarak üretilir ve
/// Windows'un en hafif yolu olan PlaySound ile bellekten, beklemeden çalınır.
/// </summary>
static class Sounds
{
    const int Rate = 22050;
    const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004, SND_NOSTOP = 0x0010;

    [DllImport("winmm.dll", SetLastError = false)]
    static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    public static bool Enabled { get; set; } = true;

    // PlaySound eşzamansız çalarken belleği okumaya devam eder; bu yüzden dizi sabitlenir ve hiç bırakılmaz.
    static readonly Lazy<GCHandle> click = new(() => Pin(Wav(ClickSamples())));
    static readonly Lazy<GCHandle> chime = new(() => Pin(Wav(ChimeSamples())));

    public static void Click() => Play(click.Value, SND_NOSTOP);

    public static void Chime() => Play(chime.Value, 0);

    static void Play(GCHandle sound, uint extra)
    {
        if (!Enabled) return;
        try { PlaySound(sound.AddrOfPinnedObject(), IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT | extra); }
        catch { /* ses aygıtı yoksa sessiz geç */ }
    }

    /// <summary>~30 ms: hızla inen alçak bir ton ve çok kısa bir başlangıç vuruşu (tuşun dibe oturması).</summary>
    static float[] ClickSamples()
    {
        var n = Rate * 30 / 1000;
        var samples = new float[n];
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            var freq = 140 + 160 * Math.Exp(-t / 0.004);
            phase += 2 * Math.PI * freq / Rate;
            var body = Math.Sin(phase) * Math.Exp(-t / 0.007);
            var tap = Math.Sin(2 * Math.PI * 1900 * t) * Math.Exp(-t / 0.0012) * 0.35;
            samples[i] = (float)((body + tap) * 0.32 * Math.Min(1, i / 20.0));
        }
        return samples;
    }

    /// <summary>~0,7 sn: iki yumuşak çan tonu (A5 → E6), yavaşça sönerek.</summary>
    static float[] ChimeSamples()
    {
        var n = Rate * 700 / 1000;
        var samples = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            samples[i] = (float)((Bell(t, 880) + Bell(t - 0.09, 1318.5)) * 0.16);
        }
        return samples;

        static double Bell(double t, double freq)
        {
            if (t < 0) return 0;
            var attack = Math.Min(1, t / 0.006);
            var tone = Math.Sin(2 * Math.PI * freq * t) + 0.18 * Math.Sin(2 * Math.PI * freq * 2.76 * t) * Math.Exp(-t / 0.08);
            return tone * attack * Math.Exp(-t / 0.22);
        }
    }

    static byte[] Wav(float[] samples)
    {
        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return stream.ToArray();
    }

    static GCHandle Pin(byte[] data) => GCHandle.Alloc(data, GCHandleType.Pinned);

    /// <summary>Sesleri dinlemek ve kontrol etmek için WAV dosyası olarak yazar.</summary>
    public static void Export(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "tik.wav"), Wav(ClickSamples()));
        File.WriteAllBytes(Path.Combine(folder, "can.wav"), Wav(ChimeSamples()));
    }
}
