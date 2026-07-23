using System.Drawing;
using System.Windows.Forms;

namespace FocusTrace;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService(Action showWindow, Action exitApplication)
    {
        ToolStripMenuItem showItem = new("Show FocusTrace");
        showItem.Click += (_, _) => showWindow();

        ToolStripMenuItem exitItem = new("Exit");
        exitItem.Click += (_, _) => exitApplication();

        ContextMenuStrip menu = new();
        menu.Items.Add(showItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = File.Exists(iconPath)
                ? new Icon(iconPath)
                : Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application,
            Text = "FocusTrace — protect your attention"
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public void ShowAlert(string title, string message)
    {
        if (!_notifyIcon.Visible)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }
}
