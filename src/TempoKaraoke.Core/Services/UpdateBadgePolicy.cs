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

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return true;
        }

        // Older releases stored three-component versions (1.10.3), while the
        // installed assembly exposes four (1.10.3.3). Compare semantically so an
        // already-installed release never leaves an eternal "Update" badge.
        if (Version.TryParse(pendingVersion, out var pending) &&
            Version.TryParse(installedVersion, out var installed))
        {
            return pending > installed;
        }

        return !string.Equals(pendingVersion, installedVersion, StringComparison.OrdinalIgnoreCase);
    }
}
