using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spindle.Core.Engines;

/// <summary>
/// Bir Office uygulamasını (ör. Word) kendi STA iş parçacığında sıcak tutar.
/// Word'ü açmak tek başına ~3,5 sn sürdüğü için art arda gelen dosyalar aynı Word'de çevrilir ve
/// çark açılınca Word önceden başlatılabilir. Kısa bir boşta kalma süresinden sonra Office kapatılır.
/// COM nesneleri oluşturuldukları iş parçacığına bağlı olduğundan tüm işler bu tek iş parçacığında çalışır.
/// </summary>
sealed class OfficeHost
{
    readonly string progId, processName, friendlyName;
    readonly Action<dynamic> configure;
    readonly Func<dynamic, int> openCount;
    readonly Action<dynamic> quit;
    readonly BlockingCollection<Action> jobs = [];
    readonly object threadLock = new();
    Thread? thread;

    // Yalnızca host iş parçacığında kullanılır.
    OfficeSession? session;
    DateTime idleUntil;

    public OfficeHost(string progId, string processName, string friendlyName,
        Action<dynamic> configure, Func<dynamic, int> openCount, Action<dynamic> quit)
    {
        this.progId = progId;
        this.processName = processName;
        this.friendlyName = friendlyName;
        this.configure = configure;
        this.openCount = openCount;
        this.quit = quit;
    }

    /// <summary>Şimdiye kadar başlatılan Office oturumu sayısı (testler yeniden kullanımı doğrulamak için okur).</summary>
    public int SessionsStarted { get; private set; }

    public Task RunAsync(Action<OfficeSession> work, Action<string>? stage, TimeSpan keepAlive) =>
        Enqueue(() =>
        {
            var current = Acquire(stage);
            OfficeEngine.BeginJob(current);
            try
            {
                work(current);
            }
            catch
            {
                if (!current.IsAlive()) Drop(); // Office kapandıysa ya da kapatıldıysa bir sonraki iş yenisini açar
                throw;
            }
            finally
            {
                OfficeEngine.EndJob(current);
            }
            Park(keepAlive);
        });

    /// <summary>Office'i önceden başlatır; bu sürede iş gelmezse kapatır. Hata olursa sessizce vazgeçer.</summary>
    public void Prewarm(TimeSpan keepAlive)
    {
        if (jobs.Count > 0) return;
        _ = Enqueue(() =>
        {
            try { Acquire(stage: null); Park(keepAlive); }
            catch { Drop(); }
        });
    }

    /// <summary>Sıcak tutulan Office'i düzgünce kapatır (uygulama kapanırken).</summary>
    public void Shutdown(TimeSpan wait)
    {
        if (thread is null) return;
        try { Enqueue(QuitSession).Wait(wait); } catch { /* kapanışta hata önemsiz */ }
    }

    Task Enqueue(Action job)
    {
        EnsureThread();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add(() =>
        {
            try { job(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    void EnsureThread()
    {
        lock (threadLock)
        {
            if (thread is not null) return;
            thread = new Thread(Loop) { IsBackground = true, Name = $"Spindle {friendlyName}" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    void Loop()
    {
        while (true)
        {
            Action? job;
            if (session is null)
            {
                job = jobs.Take();
            }
            else
            {
                var wait = idleUntil - DateTime.UtcNow;
                if (wait <= TimeSpan.Zero || !jobs.TryTake(out job, wait))
                {
                    QuitSession();
                    continue;
                }
            }
            job();
        }
    }

    OfficeSession Acquire(Action<string>? stage)
    {
        if (session is not null && session.IsAlive()) return session;
        if (session is not null) Drop();

        stage?.Invoke("Program yükleniyor…");
        var started = OfficeSession.Start(progId, processName, friendlyName);
        // Kullanıcının zaten açık olan Office'ine bağlandıysak onun ayarlarına dokunma
        // (ör. Visible=false kullanıcının penceresini gizlerdi).
        if (started.Ours) configure(started.App);
        session = started;
        SessionsStarted++;
        return started;
    }

    /// <summary>İş bitince: kendi Office'imizse kısa süre sıcak tut, kullanıcınınkiyse hemen bırak.</summary>
    void Park(TimeSpan keepAlive)
    {
        if (session is null) return;
        if (!session.Ours || keepAlive <= TimeSpan.Zero)
        {
            QuitSession();
            return;
        }
        idleUntil = DateTime.UtcNow + keepAlive;
    }

    void QuitSession()
    {
        if (session is null) return;
        var current = session;
        session = null;
        // Kullanıcının Office'ine bağlandıysak ve içinde belgeleri varsa kapatma.
        Try(() => { if (current.Ours || openCount(current.App) == 0) quit(current.App); });
        current.Dispose();
    }

    void Drop()
    {
        session?.Dispose();
        session = null;
    }

    static void Try(Action a) { try { a(); } catch { /* kapanış sırasında hata önemsiz */ } }
}

/// <summary>Başlatılmış (ya da bağlanılmış) tek bir Office uygulaması ve onun diyalog bekçisi.</summary>
sealed class OfficeSession : IDisposable
{
    public dynamic App { get; }
    public int? Pid { get; }
    public bool Ours => Pid is not null;
    public OfficeDialogGuard? Guard { get; }

    OfficeSession(dynamic app, int? pid)
    {
        App = app;
        Pid = pid;
        if (pid is not null) Guard = new OfficeDialogGuard(pid.Value);
    }

    public static OfficeSession Start(string progId, string processName, string friendlyName)
    {
        var type = Type.GetTypeFromProgID(progId)
            ?? throw new ConversionException($"{friendlyName} bulunamadı. Bu dönüşüm için Microsoft Office gerekli.");
        var before = Process.GetProcessesByName(processName).Select(p => p.Id).ToHashSet();
        object app;
        try
        {
            app = Activator.CreateInstance(type)!;
        }
        catch (COMException ex)
        {
            throw new ConversionException($"{friendlyName} başlatılamadı (0x{ex.HResult:X8}). Office'i bir kez açıp oturum açtığınızdan emin olun.", ex);
        }
        var pid = Process.GetProcessesByName(processName).Select(p => (int?)p.Id).FirstOrDefault(id => !before.Contains(id!.Value));
        return new OfficeSession(app, pid);
    }

    public bool IsAlive()
    {
        try
        {
            _ = (string)App.Name;
            if (Pid is { } pid) { using var p = Process.GetProcessById(pid); return !p.HasExited; }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Office kullanıcıdan bir şey sormuşsa hata mesajına onu da ekler.</summary>
    public Exception Explain(COMException ex)
    {
        string? asked = null;
        if (Guard is not null) lock (Guard.Messages) asked = Guard.Messages.LastOrDefault();
        return asked is { Length: > 0 }
            ? new ConversionException($"Office şunu sordu ve işlem iptal edildi: \"{asked}\"", ex)
            : ex;
    }

    public void Dispose()
    {
        Guard?.Dispose();
        if (Marshal.IsComObject((object)App)) Marshal.FinalReleaseComObject((object)App);
    }
}
