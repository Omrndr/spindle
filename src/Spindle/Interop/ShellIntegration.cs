using Microsoft.Win32;

namespace Spindle.Interop;

/// <summary>
/// Sağ tık menüsü ("Spindle ▸ Dönüştür… / Araçlar…") ve Windows ile başlatma. Hepsi yalnızca
/// geçerli kullanıcı için (HKCU) yazılır; yönetici izni gerekmez ve Ayarlar'dan geri alınabilir.
/// Windows 11'de klasik menü öğeleri "Diğer seçenekleri göster" altında görünür.
/// </summary>
static class ShellIntegration
{
    const string MenuKey = @"Software\Classes\AllFilesystemObjects\shell\Spindle";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static string Exe => Environment.ProcessPath!;

    public static bool IsMenuRegistered
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(MenuKey); return key is not null; }
    }

    public static void RegisterMenu()
    {
        using var root = Registry.CurrentUser.CreateSubKey(MenuKey);
        root.SetValue("MUIVerb", "Spindle");
        root.SetValue("Icon", $"\"{Exe}\",0");
        root.SetValue("SubCommands", "");
        AddVerb(root, "1convert", "Dönüştür…", "--wheel");
        AddVerb(root, "2tools", "Araçlar…", "--tools");
    }

    static void AddVerb(RegistryKey root, string name, string title, string flag)
    {
        using var verb = root.CreateSubKey($@"shell\{name}");
        verb.SetValue("MUIVerb", title);
        verb.SetValue("MultiSelectModel", "Player");
        using var command = verb.CreateSubKey("command");
        command.SetValue("", $"\"{Exe}\" {flag} \"%1\"");
    }

    public static void UnregisterMenu() => Registry.CurrentUser.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);

    public static bool IsAutostartEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("Spindle") is not null; }
    }

    public static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("Spindle", $"\"{Exe}\" --background");
        else key.DeleteValue("Spindle", throwOnMissingValue: false);
    }
}
