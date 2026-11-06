using System;

namespace TempoKaraoke.Core.Services;

/// <summary>
/// Политика бейджа «доступно обновление»: показывать, пока пользователь
/// не установил обновление, не отложил именно эту версию и она отличается
/// от уже установленной.
/// </summary>
public static class UpdateBadgePolicy
{
    public static bool ShouldShow(string? pendingVersion, string? dismissedVersion, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(pendingVersion))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(dismissedVersion) &&
            string.Equals(pendingVersion, dismissedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(installedVersion) ||
               !string.Equals(pendingVersion, installedVersion, StringComparison.OrdinalIgnoreCase);
    }
}
