using Spindle.Core;

namespace Spindle.Tests;

public class CatalogTests
{
    [Theory]
    [InlineData("rapor.docx", FileKind.Word)]
    [InlineData("TABLO.XLSX", FileKind.Excel)]
    [InlineData("sunum.pptx", FileKind.PowerPoint)]
    [InlineData("foto.HEIC", FileKind.Image)]
    [InlineData("DSCF0001.RAF", FileKind.Image)]
    [InlineData("P1000001.raw", FileKind.Image)]
    [InlineData("IMG_0001.CR3", FileKind.Image)]
    [InlineData("klip.mov", FileKind.Video)]
    [InlineData("ses.opus", FileKind.Audio)]
    [InlineData("yedek.tar.gz", FileKind.Archive)]
    [InlineData("belge.pdf", FileKind.Pdf)]
    [InlineData("bilinmeyen.xyz", FileKind.Other)]
    public void Detects_file_kind(string path, FileKind expected) =>
        Assert.Equal(expected, FormatCatalog.KindOf(path));

    [Fact]
    public void Word_document_offers_pdf_first_and_not_its_own_format()
    {
        var ids = FormatCatalog.ConvertActionsFor(["a.docx"]).Select(a => a.Id).ToList();
        Assert.Equal("to:pdf", ids[0]);
        Assert.DoesNotContain("to:docx", ids);
    }

    [Fact]
    public void Pdf_offers_word_and_page_images()
    {
        var ids = FormatCatalog.ConvertActionsFor(["a.pdf"]).Select(a => a.Id).ToList();
        Assert.Equal(["to:docx", "to:png", "to:jpg"], ids);
    }

    [Fact]
    public void Without_office_word_slices_are_dimmed_but_images_still_work()
    {
        var noOffice = new EngineAvailability(Word: false, Excel: false, PowerPoint: false, Ffmpeg: true);

        var word = FormatCatalog.ConvertActionsFor(["a.docx"], noOffice);
        Assert.All(word, a => Assert.Equal("Office gerekli", a.Missing));
        Assert.All(FormatCatalog.ToolActionsFor(["a.docx"], noOffice), a => Assert.True(a.IsAvailable)); // ZIP, yolu kopyala

        var pdf = FormatCatalog.ConvertActionsFor(["a.pdf"], noOffice).ToDictionary(a => a.Id);
        Assert.False(pdf["to:docx"].IsAvailable);
        Assert.True(pdf["to:png"].IsAvailable);

        Assert.All(FormatCatalog.ConvertActionsFor(["a.png"], noOffice), a => Assert.True(a.IsAvailable));
    }

    [Fact]
    public void Without_ffmpeg_media_slices_are_dimmed()
    {
        var noFfmpeg = new EngineAvailability(Word: true, Excel: true, PowerPoint: true, Ffmpeg: false);
        Assert.All(FormatCatalog.ConvertActionsFor(["a.mp4"], noFfmpeg), a => Assert.Equal("ffmpeg gerekli", a.Missing));
        var tools = FormatCatalog.ToolActionsFor(["a.mp4"], noFfmpeg).ToDictionary(a => a.Id);
        Assert.False(tools["tool:compress"].IsAvailable);
        Assert.True(tools["tool:zip"].IsAvailable);
        // Karışık seçimde bir dosya için bile motor eksikse ortak dilim soluk olur.
        Assert.False(FormatCatalog.ToolActionsFor(["a.png", "b.mp3"], noFfmpeg).Single(a => a.Id == "tool:compress").IsAvailable);
    }

    [Fact]
    public void Office_stays_sequential_while_images_run_in_parallel()
    {
        Assert.Equal(1, ConversionService.ParallelismFor("to:pdf", ["a.docx", "b.docx"]));
        Assert.Equal(1, ConversionService.ParallelismFor("to:docx", ["a.pdf", "b.pdf"]));
        Assert.Equal(2, ConversionService.ParallelismFor("to:mp3", ["a.wav", "b.wav"]));
        Assert.True(ConversionService.ParallelismFor("to:png", ["a.jpg", "b.jpg", "c.jpg"]) >= 2);
        Assert.Equal(1, ConversionService.ParallelismFor("tool:zip", ["a.jpg", "b.jpg"]));
        Assert.Equal(1, ConversionService.ParallelismFor("to:png", ["a.jpg"]));
    }

    [Fact]
    public void Camera_raw_converts_to_every_image_format_but_never_to_itself()
    {
        var ids = FormatCatalog.ConvertActionsFor(["DSCF0001.raf"]).Select(a => a.Id).ToList();
        Assert.Equal(["to:png", "to:jpg", "to:webp", "to:pdf", "to:avif", "to:tiff", "to:bmp", "to:gif", "to:ico"], ids);
        Assert.Contains("tool:compress", FormatCatalog.ToolActionsFor(["DSCF0001.raf"]).Select(a => a.Id));
    }

    [Fact]
    public void Raw_photos_convert_two_at_a_time()
    {
        var raws = Enumerable.Range(1, 8).Select(i => $"DSCF{i:0000}.raf").ToList();
        Assert.Equal(2, ConversionService.ParallelismFor("to:jpg", raws));
    }

    [Theory]
    [InlineData("DSCF0001.raf", true)]
    [InlineData("P1000001.RAW", true)]
    [InlineData("IMG_0001.heic", true)]
    [InlineData("foto.jpg", false)]
    public void Raw_and_heic_tools_save_as_jpg(string path, bool readOnly) =>
        Assert.Equal(readOnly, Spindle.Core.Engines.ImageEngine.ReadOnly(path));

    [Fact]
    public void Jpeg_alias_hides_jpg_target()
    {
        var ids = FormatCatalog.ConvertActionsFor(["a.jpeg"]).Select(a => a.Id);
        Assert.DoesNotContain("to:jpg", ids);
        Assert.Contains("to:png", ids);
    }

    [Fact]
    public void Mixed_selection_only_offers_common_actions()
    {
        var convert = FormatCatalog.ConvertActionsFor(["a.png", "b.mp3"]);
        Assert.Empty(convert);
        var tools = FormatCatalog.ToolActionsFor(["a.png", "b.mp3"]).Select(a => a.Id).ToList();
        Assert.Contains("tool:compress", tools);
        Assert.Contains("tool:zip", tools);
        Assert.DoesNotContain("tool:rotl", tools);
    }

    [Fact]
    public void Unique_output_never_overwrites()
    {
        var dir = TestFiles.NewDir();
        var input = Path.Combine(dir, "x.docx");
        File.WriteAllText(input, "");
        File.WriteAllText(Path.Combine(dir, "x.pdf"), "");
        Assert.Equal(Path.Combine(dir, "x 2.pdf"), OutputPaths.ForTarget(input, "pdf"));
    }
}
