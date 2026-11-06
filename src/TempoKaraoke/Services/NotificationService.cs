using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TempoKaraoke.Services;

/// <summary>
/// Системные уведомления (Windows App SDK). Если платформа не поддерживает
/// AppNotification (или регистрация не удалась), методы возвращают false —
/// вызывающая сторона остаётся с бейджем в приложении.
/// </summary>
public static class NotificationService
{
    private static bool _initialized;
    private static bool _supported;

    /// <summary>Пользователь нажал кнопку в уведомлении: "install" или "later".</summary>
    public static event Action<string>? UpdateActionInvoked;

    public static bool IsSupported
    {
        get
        {
            TryInitialize();
            return _supported;
        }
    }

    public static bool TryInitialize()
    {
        if (_initialized)
        {
            return _supported;
        }

        _initialized = true;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, args) =>
            {
                try
                {
                    UpdateActionInvoked?.Invoke(ParseAction(args.Argument));
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Notification action failed: {ex.Message}");
                }
            };

            AppNotificationManager.Default.Register();
            _supported = true;
        }
        catch (Exception ex)
        {
            _supported = false;
            LogService.Warn($"Notifications unavailable: {ex.Message}");
        }

        return _supported;
    }

    /// <summary>Показывает уведомление о доступном обновлении с кнопками.</summary>
    public static bool TryShowUpdate(
        string title,
        string body,
        string installLabel,
        string laterLabel)
    {
        if (!TryInitialize())
        {
            return false;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .AddButton(new AppNotificationButton(installLabel).AddArgument("action", "install"))
                .AddButton(new AppNotificationButton(laterLabel).AddArgument("action", "later"))
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Update notification failed: {ex.Message}");
            return false;
        }
    }

    private static string ParseAction(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return string.Empty;
        }

        foreach (var pair in argument.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "action", StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return argument;
    }
}
