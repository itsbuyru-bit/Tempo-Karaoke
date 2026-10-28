using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace TempoKaraoke.Services;

/// <summary>
/// Локальные настройки приложения. В упакованном приложении используется
/// ApplicationData.Current.LocalSettings; при недоступности — безопасные дефолты.
/// </summary>
public static class SettingsService
{
    private const string VideoRootsKey = "VideoRoots";
    private const string QueuePathsKey = "QueuePaths";
    private const string RecentPathsKey = "RecentPaths";
    private const string ThemeKey = "Theme";
    private const string AccentKey = "Accent";
    private const string FetchArtworkKey = "FetchArtwork";

    public const string DefaultVideoRoot = @"G:\Karaoke";

    private static ApplicationDataContainer? Container
    {
        get
        {
            try
            {
                return ApplicationData.Current.LocalSettings;
            }
            catch
            {
                return null;
            }
        }
    }

    public static List<string> VideoRoots
    {
        get
        {
            var raw = Container?.Values[VideoRootsKey] as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string> { DefaultVideoRoot };
            }

            return raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static void SetVideoRoots(IEnumerable<string> roots)
    {
        var normalized = roots
            .Select(path => path.Trim())
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Container is not null)
        {
            Container.Values[VideoRootsKey] = string.Join('\n', normalized);
        }
    }

    public static List<string> QueuePaths
    {
        get
        {
            var raw = Container?.Values[QueuePathsKey] as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Length > 0)
                .ToList();
        }
        set
        {
            if (Container is not null)
            {
                Container.Values[QueuePathsKey] = string.Join('\n', value);
            }
        }
    }

    public static List<string> RecentPaths
    {
        get
        {
            var raw = Container?.Values[RecentPathsKey] as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Length > 0)
                .ToList();
        }
        set
        {
            if (Container is not null)
            {
                Container.Values[RecentPathsKey] = string.Join('\n', value);
            }
        }
    }

    public static string Theme
    {
        get => Container?.Values[ThemeKey] as string ?? "system";
        set
        {
            if (Container is not null)
            {
                Container.Values[ThemeKey] = value;
            }
        }
    }

    public static string Accent
    {
        get => Container?.Values[AccentKey] as string ?? "system";
        set
        {
            if (Container is not null)
            {
                Container.Values[AccentKey] = value;
            }
        }
    }

    public static bool FetchArtwork
    {
        get => Container?.Values[FetchArtworkKey] as bool? ?? true;
        set
        {
            if (Container is not null)
            {
                Container.Values[FetchArtworkKey] = value;
            }
        }
    }
}
