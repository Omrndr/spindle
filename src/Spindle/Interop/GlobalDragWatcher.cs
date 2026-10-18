using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Spindle.Wheel;
using static Spindle.Interop.Native;

namespace Spindle.Interop;

/// <summary>
/// "Dosya sürüklenirken tuşa basıldı" anını yakalar. Yalnızca klavye kancası kullanır: fare hareketleri
/// hiç izlenmez. Tetik tuşuna basıldığı anda sol fare tuşunun basılı olup olmadığına bir kez bakılır;
/// sürüklemenin devamı ancak çark açıkken (App içinde) izlenir. Hiçbir tuşu yutmaz.
/// </summary>
sealed class GlobalDragWatcher : IDisposable
{
    const uint VkShift = 0x10, VkCtrl = 0x11, VkAlt = 0x12, VkEscape = 0x1B;
    const uint VkLShift = 0xA0, VkRShift = 0xA1, VkLCtrl = 0xA2, VkRCtrl = 0xA3, VkLAlt = 0xA4, VkRAlt = 0xA5;

    readonly Dispatcher dispatcher;
    readonly HookProc keyboardProc; // GC'nin toplamaması için alan olarak tutulur
    IntPtr keyboardHook;
    readonly HashSet<uint> keysDown = [];
    readonly Dictionary<int, string> processNames = [];

    public Func<AppSettings> Settings { get; init; } = () => new AppSettings();

    /// <summary>Sürükleme sırasında tetik tuşuna basıldı.</summary>
    public event Action<WheelMode, POINT>? WheelRequested;
    public event Action? EscapePressed;

    public GlobalDragWatcher(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        keyboardProc = OnKeyboard;
    }

    /// <summary>Ana fare tuşu (sol elini kullananlarda sağ tuş) şu an basılı mı?</summary>
    public static bool PrimaryButtonDown()
    {
        // GetAsyncKeyState fiziksel tuşa bakar; tuşları yer değiştirmiş kullanıcıda ana tuş sağ tuştur.
        var vk = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public void Start()
    {
        if (keyboardHook != IntPtr.Zero) return;
        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, GetModuleHandle(null), 0);
        if (keyboardHook == IntPtr.Zero)
            throw new InvalidOperationException($"Klavye kancası kurulamadı ({Marshal.GetLastWin32Error()}).");
    }

    public void Stop()
    {
        if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
        keyboardHook = IntPtr.Zero;
        keysDown.Clear();
    }

    IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            // Yalnızca tuş kodu okunur (yapının ilk alanı); bellekte nesne oluşturulmaz.
            var vk = (uint)Marshal.ReadInt32(lParam);
            if (IsTrigger(vk))
            {
                var msg = (int)wParam;
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (keysDown.Add(vk)) HandleKeyPress(vk); // basılı tutunca gelen tekrarları yok say
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    keysDown.Remove(vk);
                }
            }
        }
        return CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    static bool IsTrigger(uint vk) => vk is VkEscape or VkShift or VkCtrl or VkAlt
        or VkLShift or VkRShift or VkLCtrl or VkRCtrl or VkLAlt or VkRAlt;

    void HandleKeyPress(uint vk)
    {
        if (vk == VkEscape) { dispatcher.BeginInvoke(() => EscapePressed?.Invoke()); return; }

        var settings = Settings();
        if (!settings.DragGestureEnabled || !PrimaryButtonDown()) return;
        WheelMode? mode = Matches(vk, settings.ConvertKey) ? WheelMode.Convert
            : Matches(vk, settings.ToolsKey) ? WheelMode.Tools
            : null;
        if (mode is null) return;
        if (settings.OnlyInExplorer && !ForegroundIsExplorer()) return;

        var pt = CursorPosition();
        dispatcher.BeginInvoke(() => WheelRequested?.Invoke(mode.Value, pt));
    }

    static bool Matches(uint vk, string key) => key switch
    {
        "Shift" => vk is VkLShift or VkRShift or VkShift,
        "Ctrl" => vk is VkLCtrl or VkRCtrl or VkCtrl,
        "Alt" => vk is VkLAlt or VkRAlt or VkAlt,
        _ => false,
    };

    bool ForegroundIsExplorer()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid == Environment.ProcessId) return true;
        if (!processNames.TryGetValue(pid, out var name))
        {
            try { using var p = Process.GetProcessById(pid); name = p.ProcessName; } catch { name = ""; }
            processNames[pid] = name;
        }
        return name.Equals("explorer", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Stop();
}
