using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
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
    private bool _restoreMaximized;
    private SizeInt32 _fullModeSize = new(1120, 820);

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

    public void SetMiniMode(bool enabled)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            AppWindow.Resize(enabled ? new SizeInt32(500, 340) : _fullModeSize);
            return;
        }

        if (enabled)
        {
            _restoreMaximized = presenter.State == OverlappedPresenterState.Maximized;
            if (_restoreMaximized)
            {
                presenter.Restore();
            }
            else if (AppWindow.Size.Width >= 700 && AppWindow.Size.Height >= 500)
            {
                _fullModeSize = AppWindow.Size;
            }

            AppWindow.Resize(new SizeInt32(500, 340));
            return;
        }

        AppWindow.Resize(_fullModeSize);
        if (_restoreMaximized)
        {
            presenter.Maximize();
            _restoreMaximized = false;
        }
    }

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
