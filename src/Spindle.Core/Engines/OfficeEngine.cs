using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spindle.Core.Engines;

/// <summary>
/// Word, Excel ve PowerPoint'i geç bağlama (late binding) ile kullanır.
/// Eski File Converter'ın çökmesinin sebebi Office'e NetOffice adlı ek DLL'lerle bağlanması ve bu DLL'lerin
/// kurulumda eksik olmasıydı. Burada hiçbir interop DLL'i yok: Office'e doğrudan ProgID üzerinden
/// (Word.Application vb.) bağlanılıyor, bu yüzden Office kuruluysa çalışır.
/// Her uygulama kendi <see cref="OfficeHost"/> iş parçacığında sıcak tutulur.
/// </summary>
public static class OfficeEngine
{
    // Parola korumalı dosyalarda görünmez bir parola penceresi açılıp takılmasın diye sahte parola verilir;
    // korumasız dosyalarda yok sayılır, korumalılarda hata fırlatılır.
    const string NoPassword = "spindle-parola-yok";

    static readonly OfficeHost WordHost = new("Word.Application", "WINWORD", "Microsoft Word",
        configure: app =>
        {
            app.Visible = false;
            app.DisplayAlerts = 0; // wdAlertsNone
            Try(() => app.AutomationSecurity = 3); // makroları kapat
        },
        openCount: app => (int)app.Documents.Count,
        quit: app => app.Quit(0));

    static readonly OfficeHost ExcelHost = new("Excel.Application", "EXCEL", "Microsoft Excel",
        configure: app =>
        {
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;
            Try(() => app.AutomationSecurity = 3);
        },
        openCount: app => (int)app.Workbooks.Count,
        quit: app => app.Quit());

    static readonly OfficeHost PowerPointHost = new("PowerPoint.Application", "POWERPNT", "Microsoft PowerPoint",
        configure: app =>
        {
            Try(() => app.DisplayAlerts = 1); // ppAlertsNone
            Try(() => app.AutomationSecurity = 3);
        },
        openCount: app => (int)app.Presentations.Count,
        quit: app => app.Quit());

    /// <summary>Bir işten sonra Office'in sıcak tutulduğu süre: art arda gelen dosyalar aynı Office'te çevrilir.</summary>
    public static TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Çark açılınca önceden başlatılan Office'in iş gelmezse kapatılacağı süre.</summary>
    public static TimeSpan PrewarmKeepAlive { get; set; } = TimeSpan.FromSeconds(25);

    // PowerPoint tek örnek çalışır; kullanıcı sunum açarsa bizimkine düşmesin diye kısa tutulur.
    static readonly TimeSpan PowerPointKeepAlive = TimeSpan.FromSeconds(3);

    /// <summary>Spindle'ın o an iş yaptırdığı Office işlemi; süre aşımında yalnızca bu kapatılır.</summary>
    public static int? ActivePid { get; private set; }

    static OfficeDialogGuard? activeGuard;

    /// <summary>Son Office işinde bekçinin yakaladığı diyalog metinleri (tanı için).</summary>
    public static IReadOnlyList<string> LastDialogs { get; private set; } = [];

    /// <summary>Başlatılan Word oturumu sayısı (yeniden kullanımı doğrulamak için).</summary>
    public static int WordSessionsStarted => WordHost.SessionsStarted;

    public static bool IsAvailable(string progId) => Type.GetTypeFromProgID(progId) is not null;

    /// <summary>Çark bir Office dosyası için açıldığında, kullanıcı dilimi seçerken Office'i arka planda başlatır.</summary>
    public static void Prewarm(FileKind kind)
    {
        var host = kind switch
        {
            FileKind.Word => WordHost,
            FileKind.Excel => ExcelHost,
            _ => null, // PowerPoint tek örnek çalıştığı için önceden açılmaz
        };
        if (host is not null && IsAvailable(kind == FileKind.Word ? "Word.Application" : "Excel.Application"))
            host.Prewarm(PrewarmKeepAlive);
    }

    /// <summary>Uygulama kapanırken sıcak tutulan Office'leri düzgünce kapatır.</summary>
    public static void ShutdownAll()
    {
        foreach (var host in new[] { WordHost, ExcelHost, PowerPointHost })
            host.Shutdown(TimeSpan.FromSeconds(15));
    }

    public static Task ConvertWithWordAsync(string input, string output, string target, Action<string>? stage = null)
    {
        int format = target switch
        {
            "pdf" => 17, "docx" => 16, "doc" => 0, "rtf" => 6, "txt" => 7, "html" => 10, "odt" => 23,
            _ => throw new NotSupportedException($"Word {target} biçimine kaydedemez."),
        };
        return WordHost.RunAsync(session =>
        {
            dynamic app = session.App;
            dynamic? doc = null;
            try
            {
                stage?.Invoke("Belge açılıyor…");
                doc = FormatCatalog.KindOf(input) == FileKind.Pdf
                    ? app.Documents.Open(input, false, true, false)
                    : app.Documents.Open(input, false, true, false, NoPassword, NoPassword);
                stage?.Invoke(SavingMessage(target));
                // PDF için de SaveAs2 kullanılır: ExportAsFixedFormat güncel Microsoft 365 sürümlerinde
                // DISP_E_TYPEMISMATCH verebiliyor (bkz. Tichau/FileConverter#779).
                if (format == 7)
                    doc.SaveAs2(output, 7, Type.Missing, Type.Missing, false, Type.Missing, Type.Missing,
                        Type.Missing, Type.Missing, Type.Missing, Type.Missing, 65001);
                else
                    doc.SaveAs2(output, format, Type.Missing, Type.Missing, false);
            }
            catch (COMException ex)
            {
                throw session.Explain(ex);
            }
            finally
            {
                if (doc is not null) { Try(() => doc.Close(0)); Release(doc); }
            }
        }, stage, KeepAlive);
    }

    public static Task ConvertWithExcelAsync(string input, string output, string target, Action<string>? stage = null)
    {
        int? format = target switch
        {
            "pdf" => null, "xlsx" => 51, "xls" => 56, "ods" => 60, "csv" => 62,
            _ => throw new NotSupportedException($"Excel {target} biçimine kaydedemez."),
        };
        return ExcelHost.RunAsync(session =>
        {
            dynamic app = session.App;
            dynamic? book = null;
            try
            {
                stage?.Invoke("Belge açılıyor…");
                // CSV gibi metin dosyalarına parola parametresi verilirse Excel açmayı reddediyor (0x800A03EC).
                book = FormatCatalog.ExtensionOf(input) == "csv"
                    ? app.Workbooks.Open(input, 0, true)
                    : app.Workbooks.Open(input, 0, true, Type.Missing, NoPassword, NoPassword);
                stage?.Invoke(SavingMessage(target));
                if (format is null)
                    book.ExportAsFixedFormat(0, output); // xlTypePDF
                else
                    book.SaveAs(output, format.Value);
            }
            catch (COMException ex)
            {
                throw session.Explain(ex);
            }
            finally
            {
                if (book is not null) { Try(() => book.Close(false)); Release(book); }
            }
        }, stage, KeepAlive);
    }

    /// <summary>"png" hedefinde <paramref name="output"/> bir klasördür; slaytlar içine yazılır.</summary>
    public static Task ConvertWithPowerPointAsync(string input, string output, string target, Action<string>? stage = null)
    {
        int format = target switch
        {
            "pdf" => 32, "pptx" => 24, "ppt" => 1, "odp" => 35, "png" => 18,
            _ => throw new NotSupportedException($"PowerPoint {target} biçimine kaydedemez."),
        };
        return PowerPointHost.RunAsync(session =>
        {
            dynamic app = session.App;
            dynamic? pres = null;
            try
            {
                stage?.Invoke("Belge açılıyor…");
                pres = app.Presentations.Open(input, -1, 0, 0); // ReadOnly, Untitled=false, WithWindow=false
                stage?.Invoke(SavingMessage(target));
                pres.SaveAs(output, format);
            }
            catch (COMException ex)
            {
                throw session.Explain(ex);
            }
            finally
            {
                if (pres is not null) { Try(() => pres.Close()); Release(pres); }
            }
        }, stage, PowerPointKeepAlive);
    }

    static string SavingMessage(string target) => target switch
    {
        "pdf" => "PDF oluşturuluyor…",
        "png" => "Slaytlar kaydediliyor…",
        _ => $"{target.ToUpperInvariant()} olarak kaydediliyor…",
    };

    internal static void BeginJob(OfficeSession session)
    {
        if (session.Guard is { } guard) lock (guard.Messages) guard.Messages.Clear();
        activeGuard = session.Guard;
        ActivePid = session.Pid;
    }

    internal static void EndJob(OfficeSession session)
    {
        if (session.Guard is { } guard) lock (guard.Messages) LastDialogs = guard.Messages.ToList();
        else LastDialogs = [];
        activeGuard = null;
        ActivePid = null;
    }

    /// <summary>Office'in bu işte başlattığı yardımcı işlemler (ör. PDFREFLOW).</summary>
    public static IReadOnlyList<int> ActiveHelperPids() => activeGuard?.HelperPids ?? [];

    /// <summary>
    /// Süre aşımında önce bu çağrılır: Office'in o işte başlattığı yardımcı işlemleri (ör. PDFREFLOW) kapatır.
    /// Office bu durumda hatayla kendisi döner ve normal kapanır; Office'i zorla kapatmak ise açılan dosyayı
    /// Office'in "devre dışı öğeler" listesine ekler. Kapatılacak yardımcı yoksa false döner.
    /// </summary>
    public static bool AbortHelpers()
    {
        var helpers = ActiveHelperPids();
        foreach (var helper in helpers)
        {
            try { Process.GetProcessById(helper).Kill(); } catch { }
        }
        return helpers.Count > 0;
    }

    /// <summary>Son çare: yalnızca Spindle'ın başlattığı Office işlemini kapatır.</summary>
    public static void AbortActive()
    {
        if (ActivePid is { } pid)
        {
            try { Process.GetProcessById(pid).Kill(); } catch { }
        }
    }

    static void Release(object? o)
    {
        if (o is not null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o);
    }

    static void Try(Action a) { try { a(); } catch { /* kapanış sırasında hata önemsiz */ } }
}
