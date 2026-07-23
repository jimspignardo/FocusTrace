using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace FocusTrace;

public static class FocusNotificationService
{
    public static bool Enabled { get; set; } = true;

    public static void Show(string title, string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            AppNotification notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // In-app InfoBars provide a fallback when system notifications are disabled.
        }

        if (Microsoft.UI.Xaml.Application.Current is App app)
        {
            app.MainWindowHost?.ShowTrayAlert(title, message);
        }
    }
}
