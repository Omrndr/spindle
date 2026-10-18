using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Spindle.Core.Engines;

/// <summary>
/// Görünmez çalışan Office bazen yine de kendi uyarı pencerelerini açar ("devam edilsin mi?", PDF uyarısı vb.).
/// Bekçi, yalnızca Spindle'ın başlattığı Office işleminin ve o oturumda başlayan yardımcı işlemlerin
/// pencerelerini izler; bir diyalog oluşur oluşmaz onu saydam yapıp ekran dışına taşır (kullanıcı görmez),
/// metnini kaydeder ve güvenli düğmeye basar.
/// </summary>
/// <remarks>
/// Word PDF açarken "PDF'inizi düzenlenebilir bir Word belgesine dönüştürecek" uyarısını kendisi değil,
/// ayrı bir işlem olan PDFREFLOW.EXE gösterir; bu işlem Word'ün alt işlemi de değildir. Bekçi yalnızca
/// Word'e baksaydı uyarı yanıtsız kalır ve Word sonsuza dek beklerdi.
/// </remarks>
sealed class OfficeDialogGuard : IDisposable
{
    static readonly string[] DialogClasses = ["NUIDialog", "#32770", "bosa_sdm"];
    static readonly string[] HelperProcesses = ["PDFREFLOW"];
    static readonly string[] Positive = ["Tamam", "OK", "Evet", "Yes", "Devam", "Continue"];
    static readonly string[] Negative = ["Hayır", "No", "İptal", "Cancel", "Kapat", "Close"];

    readonly int pid;
    readonly DateTime startedAt = DateTime.Now;
    readonly Thread thread, hookThread;
    readonly HashSet<IntPtr> handled = [];
    readonly Dictionary<int, bool> watched = [];
    readonly WinEventProc hookProc;
    volatile bool stopping;
    volatile int hookThreadId;

    public List<string> Messages { get; } = [];

    public OfficeDialogGuard(int pid)
    {
        this.pid = pid;
        hookProc = OnWinEvent;
        hookThread = new Thread(HookLoop) { IsBackground = true, Name = "Office bekçisi (kanca)" };
        hookThread.Start();
        thread = new Thread(Loop) { IsBackground = true, Name = "Office bekçisi" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>Bu oturumda başlayan yardımcı işlemler (ör. PDFREFLOW); süre aşımında önce bunlar kapatılır.</summary>
    /// <remarks>Pencere açmasa da bulunur; PDF -> Word'de asıl işlemciyi bunlar kullanır. Hiçbir zaman hata fırlatmaz.</remarks>
    public IReadOnlyList<int> HelperPids
    {
        get
        {
            var pids = new List<int>();
            foreach (var name in HelperProcesses)
            {
                Process[] found;
                try { found = Process.GetProcessesByName(name); } catch { continue; }
                foreach (var p in found)
                {
                    try { if (p.StartTime >= startedAt.AddSeconds(-2)) pids.Add(p.Id); }
                    catch { /* işlem bitmiş ya da erişilemiyor */ }
                    finally { p.Dispose(); }
                }
            }
            return pids;
        }
    }

    bool IsWatched(int owner)
    {
        if (owner == pid) return true;
        lock (watched)
        {
            if (watched.TryGetValue(owner, out var known)) return known;
            var isHelper = false;
            try
            {
                using var p = Process.GetProcessById(owner);
                isHelper = HelperProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    && p.StartTime >= startedAt.AddSeconds(-2);
            }
            catch { /* işlem bitmiş ya da erişilemiyor */ }
            watched[owner] = isHelper;
            return isHelper;
        }
    }

    // Kanca pencere oluşturulduğu anda (henüz görünmeden) haber verir; diyalog tek kare bile görünmesin diye
    // burada hemen saydam yapılır. Tarama döngüsü kancanın kaçırdığı pencereler için yedektir.
    void HookLoop()
    {
        hookThreadId = GetCurrentThreadId();
        var hook = SetWinEventHook(EventObjectCreate, EventObjectShow, IntPtr.Zero, hookProc, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        while (!stopping && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (hook != IntPtr.Zero) UnhookWinEvent(hook);
    }

    void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out var owner);
        if (IsWatched(owner) && IsDialog(hwnd)) Hide(hwnd);
    }

    void Loop()
    {
        while (!stopping)
        {
            foreach (var hwnd in DialogsOf())
            {
                if (!handled.Add(hwnd)) continue;
                Hide(hwnd);
                try { Answer(hwnd); }
                catch { PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero); }
            }
            Thread.Sleep(40);
        }
    }

    static void Hide(IntPtr hwnd)
    {
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExLayered);
        SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha);
        SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    void Answer(IntPtr hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().ToList();
        var text = string.Join(" ", all
            .Where(e => e.Current.ControlType == ControlType.Text && !string.IsNullOrWhiteSpace(e.Current.Name))
            .Select(e => e.Current.Name.Trim())
            .Where(t => t != root.Current.Name));
        lock (Messages) Messages.Add(text);

        // PDF dönüştürme uyarısında "Tamam" doğru cevaptır; diğer her soruda işlemi güvenle iptal et.
        var proceed = text.Contains("PDF", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("dönüştür", StringComparison.OrdinalIgnoreCase) || text.Contains("convert", StringComparison.OrdinalIgnoreCase));
        var order = proceed ? Positive.Concat(Negative) : Negative.Concat(Positive);

        var buttons = all.Where(e => e.Current.ControlType == ControlType.Button).ToList();
        foreach (var name in order)
        {
            var button = buttons.FirstOrDefault(b => string.Equals(b.Current.Name.Replace("&", ""), name, StringComparison.OrdinalIgnoreCase));
            if (button?.TryGetCurrentPattern(InvokePattern.Pattern, out var p) == true)
            {
                ((InvokePattern)p).Invoke();
                return;
            }
        }
        PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    List<IntPtr> DialogsOf()
    {
        var found = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (IsWindowVisible(hwnd) && IsWatched(owner) && IsDialog(hwnd)) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static bool IsDialog(IntPtr hwnd)
    {
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        var name = cls.ToString();
        return DialogClasses.Any(c => name.StartsWith(c, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        stopping = true;
        if (hookThreadId != 0) PostThreadMessage(hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        thread.Join(500);
        hookThread.Join(500);
    }

    const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, WmClose = 0x10, WmQuit = 0x12;
    const uint EventObjectCreate = 0x8000, EventObjectShow = 0x8002, WinEventOutOfContext = 0x0, WinEventSkipOwnProcess = 0x2;
    const int GwlExStyle = -20, WsExLayered = 0x80000;
    const uint LwaAlpha = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }

    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(int tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
}
