using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TempoKaraoke.Services;

/// <summary>
/// Всплывающие уведомления Windows (Action Center). Работают и в MSIX, и в
/// портативной сборке; если окружение их не поддерживает, приложение просто
/// продолжает работать без уведомлений.
/// </summary>
public static class NotificationService
{
    private static readonly object Gate = new();
    private static bool _registered;
    private static bool _unavailable;

    public static void Show(string title, string message)
    {
        if (_unavailable || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                if (!_registered)
                {
                    if (!AppNotificationManager.IsSupported())
                    {
                        _unavailable = true;
                        return;
                    }

                    AppNotificationManager.Default.Register();
                    _registered = true;
                }
            }

            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            // Уведомления — вспомогательный канал: сбой не должен ломать приложение.
            _unavailable = true;
            LogService.Warn($"App notifications unavailable: {ex.Message}");
        }
    }
}
