using Forms = System.Windows.Forms;

namespace Spindle.UI;

/// <summary>Sistem tepsisindeki spindle simgesi.</summary>
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;

    public TrayIcon(Action pickFiles, Action openSettings, Action showTour, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Dosya seç ve dönüştür…", null, (_, _) => pickFiles());
        menu.Items.Add("Ayarlar", null, (_, _) => openSettings());
        menu.Items.Add("Nasıl kullanılır?", null, (_, _) => showTour());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Spindle'ı kapat", null, (_, _) => quit());

        icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Spindle — sürükle, Shift'e bas, bırak",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) openSettings(); };
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
