using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace Spindle.Interop;

/// <summary>
/// İndirilen tek dosyalık Spindle.exe kurulum programı gerektirmez: ilk açılışta kendini
/// %LOCALAPPDATA%\Programs\Spindle klasörüne kopyalar, Başlat menüsüne ekler ve Windows'un "Uygulamalar"
/// listesine (Kaldır düğmesiyle) yazılır. Hepsi geçerli kullanıcı için; yönetici izni gerekmez.
/// </summary>
static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Spindle";
    const string Homepage = "https://korayakdogan.me/spindle/";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Spindle");
    static string InstalledExe => Path.Combine(InstallDir, "Spindle.exe");
    static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Spindle.lnk");
    static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    static bool RunningInstalled =>
        string.Equals(Path.GetDirectoryName(Environment.ProcessPath), InstallDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Yayın sürümü kurulu klasörün dışından (ör. İndirilenler) açıldıysa kendini kurar, kurulu kopyayı başlatır
    /// ve true döner; çağıran kapanmalıdır. Geliştirme derlemesinde hiçbir şey yapmaz.
    /// </summary>
    public static bool InstallIfDownloaded()
    {
#if DEBUG
        return false;
#else
        if (RunningInstalled) return false;
        try
        {
            StopInstalledCopy();
            Directory.CreateDirectory(InstallDir);
            // Önceki sürümün dosyaları (eski çok dosyalı kurulum dahil) temizlenir; klasör yalnızca Spindle'ındır.
            foreach (var file in Directory.GetFiles(InstallDir)) File.Delete(file);
            File.Copy(Environment.ProcessPath!, InstalledExe, overwrite: true);
            CreateShortcut();
            Register();
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Spindle kurulamadı: " + ex.Message, "Spindle", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }
#endif
    }

    /// <summary>Kurulu kopya çalışırken "Uygulamalar" kaydını güncel tutar (sürüm değiştiyse yazar).</summary>
    public static void EnsureRegistered()
    {
        if (!RunningInstalled) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("DisplayVersion") as string == Version) return;
        }
        catch { return; }
        Register();
    }

    static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Spindle");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Koray Akdoğan");
        key.SetValue("DisplayIcon", $"\"{InstalledExe}\",0");
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("URLInfoAbout", Homepage);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        if (File.Exists(InstalledExe))
            key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    static void CreateShortcut()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return;
        dynamic shell = Activator.CreateInstance(type)!;
        var link = shell.CreateShortcut(Shortcut);
        link.TargetPath = InstalledExe;
        link.WorkingDirectory = InstallDir;
        link.Description = "Sürükle, tuşa bas, bırak.";
        link.Save();
    }

    /// <summary>Tepside çalışan kurulu kopyadan kapanmasını ister; birkaç saniyede kapanmazsa sonlandırır.</summary>
    static void StopInstalledCopy()
    {
        SingleInstance.Forward(["--quit"]);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var running = Process.GetProcessesByName("Spindle").Where(p => p.Id != Environment.ProcessId && IsInstalled(p)).ToList();
            if (running.Count == 0) return;
            if (DateTime.UtcNow > deadline)
            {
                foreach (var p in running) { try { p.Kill(); p.WaitForExit(2000); } catch { } }
                return;
            }
            Thread.Sleep(150);
        }

        static bool IsInstalled(Process p)
        {
            try { return string.Equals(p.MainModule?.FileName, InstalledExe, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
    }

    /// <summary>"Uygulamalar > Kaldır" buraya gelir: kayıtlar, kısayol, ayarlar ve program klasörü silinir.</summary>
    public static void Uninstall()
    {
        if (MessageBox.Show("Spindle ve ayarları bu bilgisayardan kaldırılsın mı?", "Spindle'ı kaldır",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        StopInstalledCopy();
        ShellIntegration.UnregisterMenu();
        ShellIntegration.SetAutostart(false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        TryDelete(Shortcut);
        TryDeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spindle"));
        TryDeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spindle"));

        MessageBox.Show("Spindle kaldırıldı.", "Spindle", MessageBoxButton.OK, MessageBoxImage.Information);
        // Çalışan exe kendini silemez: bu işlem kapandıktan iki saniye sonra klasörü cmd siler.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
        });

        static void TryDelete(string file) { try { File.Delete(file); } catch { } }
        static void TryDeleteFolder(string folder) { try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { } }
    }
}
