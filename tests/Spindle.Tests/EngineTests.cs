using ImageMagick;
using Spindle.Core;
using Spindle.Core.Engines;
using Xunit.Abstractions;

namespace Spindle.Tests;

public class EngineTests(ITestOutputHelper log) : IDisposable
{
    readonly ConversionService service = new(new ConversionOptions { OfficeIdleTimeout = TimeSpan.FromSeconds(45) });

    // Sıcak tutulan Office'ler her testten sonra düzgünce kapatılır; test süreci arkasında Word bırakmaz.
    public void Dispose() => ConversionService.ShutdownOffice();

    async Task<string> Run(string actionId, string input)
    {
        var result = await service.RunAsync(actionId, input);
        Assert.True(result.Success, $"{actionId} başarısız: {result.Error}");
        var output = Assert.Single(result.Outputs);
        Assert.True(File.Exists(output) || Directory.Exists(output), $"{output} oluşmadı");
        log.WriteLine($"{actionId}: {Path.GetFileName(input)} -> {Path.GetFileName(output)}");
        return output;
    }

    [Fact]
    public async Task Images_convert_to_every_offered_format()
    {
        var dir = TestFiles.NewDir();
        var png = Path.Combine(dir, "logo.png");
        using (var img = new MagickImage("gradient:orange-transparent", 320, 200))
            img.Write(png);

        foreach (var action in FormatCatalog.ConvertActionsFor([png]))
        {
            var output = await Run(action.Id, png);
            if (action.Target == "pdf")
            {
                // PDF'i ImageMagick ile okumak Ghostscript ister; başlığı kontrol etmek yeterli.
                Assert.StartsWith("%PDF", File.ReadAllText(output)[..4]);
                continue;
            }
            using var check = new MagickImageCollection(output);
            Assert.NotEmpty(check);
        }
    }

    [Theory]
    [InlineData("tool:compress")]
    [InlineData("tool:strip")]
    [InlineData("tool:half")]
    [InlineData("tool:rotl")]
    [InlineData("tool:gray")]
    public async Task Image_tools_write_a_new_file(string tool)
    {
        var dir = TestFiles.NewDir();
        var jpg = Path.Combine(dir, "foto.jpg");
        using (var img = new MagickImage("plasma:", 400, 300)) img.Write(jpg);
        var output = await Run(tool, jpg);
        Assert.NotEqual(jpg, output);
        if (tool == "tool:rotl")
        {
            using var rotated = new MagickImage(output);
            Assert.Equal(300u, rotated.Width);
        }
    }

    [Fact]
    public async Task Zip_and_extract_round_trip()
    {
        var dir = TestFiles.NewDir();
        var folder = Path.Combine(dir, "proje");
        Directory.CreateDirectory(Path.Combine(folder, "alt"));
        File.WriteAllText(Path.Combine(folder, "alt", "not.txt"), "merhaba");

        var zip = await Run("tool:zip", folder);
        var extracted = await Run("tool:extract", zip);
        Assert.Equal("merhaba", File.ReadAllText(Path.Combine(extracted, "proje", "alt", "not.txt")));
    }

    [Fact]
    public async Task Audio_and_video_convert_with_ffmpeg()
    {
        var ffmpeg = FfmpegLocator.Find();
        if (ffmpeg is null) { log.WriteLine("ffmpeg yok, atlandı"); return; }
        var dir = TestFiles.NewDir();
        var wav = Path.Combine(dir, "ses.wav");
        var mp4 = Path.Combine(dir, "klip.mp4");
        TestFiles.Ffmpeg(ffmpeg, "-f", "lavfi", "-i", "sine=frequency=440:duration=2", wav);
        TestFiles.Ffmpeg(ffmpeg, "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25:duration=2",
            "-f", "lavfi", "-i", "sine=duration=2", "-shortest", "-pix_fmt", "yuv420p", mp4);

        foreach (var action in FormatCatalog.ConvertActionsFor([wav])) await Run(action.Id, wav);
        foreach (var id in new[] { "to:webm", "to:gif", "to:mp3", "tool:compress", "tool:mute", "tool:720p" })
            await Run(id, mp4);
    }

    [Fact]
    public async Task Word_document_to_pdf_and_back()
    {
        if (!OfficeEngine.IsAvailable("Word.Application")) { log.WriteLine("Word yok, atlandı"); return; }
        var dir = TestFiles.NewDir();
        var docx = TestFiles.MinimalDocx(dir);

        var pdf = await Run("to:pdf", docx);
        Assert.StartsWith("%PDF", File.ReadAllText(pdf)[..4]);
        await Run("to:odt", docx);
        var txt = await Run("to:txt", docx);
        Assert.Contains("çğıöşü", File.ReadAllText(txt));

        // PDF -> sayfa görselleri (2 sayfa => klasör)
        var pages = await Run("to:png", pdf);
        Assert.Equal(2, Directory.GetFiles(pages, "*.png").Length);
    }

    [Fact]
    public async Task Consecutive_word_files_share_one_word()
    {
        if (!OfficeEngine.IsAvailable("Word.Application")) { log.WriteLine("Word yok, atlandı"); return; }
        var dir = TestFiles.NewDir();
        var docs = Enumerable.Range(1, 3).Select(i => TestFiles.MinimalDocx(dir, $"belge{i}.docx")).ToList();

        var before = OfficeEngine.WordSessionsStarted;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var doc in docs) await Run("to:pdf", doc);
        log.WriteLine($"3 belge art arda: {clock.Elapsed.TotalSeconds:0.0} sn");

        // Word her dosya için yeniden açılmamalı (~3,5 sn'lik açılış bir kez ödenir).
        Assert.Equal(before + 1, OfficeEngine.WordSessionsStarted);
    }

    [Fact]
    public async Task Prewarm_opens_word_while_the_wheel_is_open()
    {
        if (!OfficeEngine.IsAvailable("Word.Application")) { log.WriteLine("Word yok, atlandı"); return; }
        var docx = TestFiles.MinimalDocx(TestFiles.NewDir());

        var before = OfficeEngine.WordSessionsStarted;
        ConversionService.Prewarm([docx]);
        await Task.Delay(TimeSpan.FromSeconds(6)); // kullanıcının dilimi seçmesi kadar

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Run("to:pdf", docx);
        log.WriteLine($"Önceden ısıtılmış Word ile dönüşüm: {clock.Elapsed.TotalSeconds:0.0} sn");

        Assert.Equal(before + 1, OfficeEngine.WordSessionsStarted);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Önceden ısıtılmış Word ile {clock.Elapsed.TotalSeconds:0.0} sn sürdü");
    }

    [Fact]
    public async Task Pdf_to_word_runs_hidden()
    {
        if (!OfficeEngine.IsAvailable("Word.Application")) { log.WriteLine("Word yok, atlandı"); return; }
        var dir = TestFiles.NewDir();
        var pdf = await Run("to:pdf", TestFiles.MinimalDocx(dir, "kaynak.docx"));

        var docx = await Run("to:docx", pdf);
        Assert.NotEqual(Path.Combine(dir, "kaynak.docx"), docx);
        // Word'ün PDF uyarısı ayrı bir işlemde (PDFREFLOW) açılır; bekçi onu yanıtlamazsa Word takılır.
        foreach (var asked in OfficeEngine.LastDialogs) log.WriteLine($"Office sordu: {asked}");
        using var zip = System.IO.Compression.ZipFile.OpenRead(docx);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var text = System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "<[^>]+>", "");
        Assert.Contains("Merhaba Spindle", text);
        Assert.Contains("İkinci sayfa", text);
    }

    [Fact]
    public async Task Word_works_in_folders_with_very_long_paths()
    {
        if (!OfficeEngine.IsAvailable("Word.Application")) { log.WriteLine("Word yok, atlandı"); return; }
        // Word 259 karakterden uzun yolları açamıyor; Spindle kullanıcıya hissettirmeden kısa bir klasörde çevirmeli.
        var dir = TestFiles.NewDir();
        while (dir.Length < 280) dir = Path.Combine(dir, "Çok Uzun Klasör Adı Burada");
        Directory.CreateDirectory(dir);
        var docx = TestFiles.MinimalDocx(dir, "uzun yoldaki belge.docx");
        Assert.True(docx.Length > 260);

        var stages = new List<string>();
        var result = await service.RunAsync("to:pdf", docx, status: new SyncProgress<string>(stages.Add));
        Assert.True(result.Success, result.Error);
        Assert.Equal(Path.Combine(dir, "uzun yoldaki belge.pdf"), Assert.Single(result.Outputs));
        Assert.StartsWith("%PDF", File.ReadAllText(result.Outputs[0])[..4]);
        Assert.Contains("PDF oluşturuluyor…", stages);
        log.WriteLine($"{docx.Length} karakterlik yol, aşamalar: {string.Join(" > ", stages)}");
    }

    /// <summary>Progress&lt;T&gt; geri çağrıları sonradan çalıştırır; testte anında toplamak için.</summary>
    sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) { lock (this) report(value); }
    }

    [Fact]
    public async Task Excel_and_powerpoint_to_pdf()
    {
        var dir = TestFiles.NewDir();
        if (OfficeEngine.IsAvailable("Excel.Application"))
        {
            var csv = Path.Combine(dir, "tablo.csv");
            File.WriteAllText(csv, "Ürün,Adet\nSpindle,12\nPortakal,7\n");
            await Run("to:xlsx", csv);
            await Run("to:pdf", csv);
        }
        if (OfficeEngine.IsAvailable("PowerPoint.Application"))
        {
            var pptx = Path.Combine(dir, "sunum.pptx");
            await StaRunner.RunAsync(() =>
            {
                dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                dynamic pres = app.Presentations.Add(0);
                dynamic slide = pres.Slides.Add(1, 1);
                slide.Shapes.Item(1).TextFrame.TextRange.Text = "Spindle sunumu";
                pres.SaveAs(pptx, 24);
                pres.Close();
                if ((int)app.Presentations.Count == 0) app.Quit();
            });
            await Run("to:pdf", pptx);
            var slides = await Run("to:png", pptx);
            Assert.NotEmpty(Directory.GetFiles(slides, "*.png", SearchOption.AllDirectories));
        }
    }
}
