using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TempoKaraoke.Services;

/// <summary>
/// Системные уведомления (Windows App SDK). Если платформа не поддерживает
/// AppNotification (или регистрация не удалась), методы возвращают false —
/// вызывающая сторона остаётся с плашкой в приложении.
/// </summary>
public static class NotificationService
{
    private static bool _initialized;
    private static bool _supported;

    /// <summary>Пользователь нажал кнопку/тело уведомления: install, postpone или open_update_page.</summary>
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
            if (!AppNotificationManager.IsSupported())
            {
                _supported = false;
                return false;
            }

            AppNotificationManager.Default.NotificationInvoked += (_, args) =>
            {
                try
                {
                    var action = UpdateActivationArguments.ParseAction(args.Argument);
                    LogService.Info($"[update] notification activated argument='{args.Argument}' action='{action}'");
                    if (!string.IsNullOrWhiteSpace(action))
                    {
                        UpdateActionInvoked?.Invoke(action);
                    }
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

    /// <summary>Показывает уведомление о доступном обновлении.</summary>
    public static bool TryShowUpdate(UpdateToastContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!TryInitialize())
        {
            return false;
        }

        try
        {
            // Строки уже разрешены до построения XML; ключи ресурсов сюда не попадают.
            var xml = UpdateToastBuilder.BuildXml(content);
            var notification = new AppNotification(xml)
            {
                Tag = content.Tag,
                Group = content.Group,
                // Повторная проверка заменяет прежний тост, а не добавляет новый.
                Expiration = DateTimeOffset.Now.AddHours(24),
            };

            AppNotificationManager.Default.Show(notification);
            LogService.Info(
                $"[update] toast shown tag={content.Tag} group={content.Group} body='{content.Body}'");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Update notification failed: {ex.Message}");
            return false;
        }
    }
}
