using Microsoft.UI.Xaml;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FocusTrace;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly TrayIconService _trayIcon;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.Resize(new SizeInt32(1120, 820));
        AppWindow.Closing += AppWindow_Closing;

        _trayIcon = new TrayIconService(ShowFromTray, ExitApplication);
        UpdateTraySettings();

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    public void ApplyTheme(ElementTheme theme)
    {
        RootShell.RequestedTheme = theme;
    }

    public void UpdateTraySettings()
    {
        if (Application.Current is App app)
        {
            _trayIcon.Visible = app.Profile.TrayModeEnabled;
        }
    }

    public void ShowTrayAlert(string title, string message) => _trayIcon.ShowAlert(title, message);

    public void HideToTray() => AppWindow.Hide();

    private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowExit || Application.Current is not App app || !app.Profile.TrayModeEnabled)
        {
            _trayIcon.Dispose();
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
        _trayIcon.ShowAlert("FocusTrace is still tracking", "Open FocusTrace from its notification-area icon whenever you need it.");
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _trayIcon.Dispose();
        Close();
    }
}
