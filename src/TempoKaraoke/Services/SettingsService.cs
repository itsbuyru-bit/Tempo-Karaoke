using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace TempoKaraoke.Services;

/// <summary>Сохраняемые настройки приложения.</summary>
public sealed class SettingsData
{
    public List<string> VideoRoots { get; set; } = new();

    /// <summary>False until settings were saved; keeps the legacy default only for first launch.</summary>
    public bool VideoRootsConfigured { get; set; }

    public List<string> RecentPaths { get; set; } = new();

    public string Theme { get; set; } = "system";

    /// <summary>Язык интерфейса в формате BCP 47: en-US по умолчанию или ru-RU.</summary>
    public string Language { get; set; } = "en-US";

    public string LastTrackPath { get; set; } = string.Empty;

    public long LastTrackPositionMs { get; set; }

    public bool CountdownEnabled { get; set; }

    public int CountdownSeconds { get; set; } = 10;

    public bool RemoteAutoStopEnabled { get; set; } = true;

    public int RemoteAutoStopMinutes { get; set; } = 10;

    public int RequestLimitPerGuest { get; set; } = 2;

    public int MaxPendingRequests { get; set; } = 20;

    public bool LoggingEnabled { get; set; }

    /// <summary>Проверять обновления при запуске (портативная сборка).</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>Эквалайзер включён.</summary>
    public bool EqualizerEnabled { get; set; }

    /// <summary>Индекс пресета LibVLC; -1 — пользовательская настройка.</summary>
    public int EqualizerPreset { get; set; } = -1;

    /// <summary>Усиление по полосам LibVLC в дБ (обычно 10 значений).</summary>
    public List<float> EqualizerBands { get; set; } = new();

    /// <summary>Перемещать соседние ползунки вместе.</summary>
    public bool EqualizerLinkSliders { get; set; } = true;
}

/// <summary>
/// Настройки приложения. Хранятся в JSON-файле рядом с остальными локальными
/// данными, поэтому работают и в упакованном, и в портативном режиме.
/// </summary>
public static class SettingsService
{
    private const string FileName = "settings.json";

    private static readonly object Sync = new();
    private static SettingsData? _data;

    /// <summary>Файл хранилища; миграция переписывает пути прямо в нём.</summary>
    public static string FilePath => AppStorage.PathFor(FileName);

    /// <summary>Сбрасывает кэш: после переписывания файла читаем RecentPaths/LastTrackPath заново.</summary>
    public static void Reload()
    {
        lock (Sync)
        {
            _data = null;
        }
    }

    private static SettingsData Data
    {
        get
        {
            lock (Sync)
            {
                return _data ??= LoadCore();
            }
        }
    }

    public static List<string> VideoRoots => new(Data.VideoRoots);

    public static void SetVideoRoots(IEnumerable<string> roots)
    {
        var normalized = NormalizePaths(roots);
        Update(data =>
        {
            data.VideoRoots = normalized;
            data.VideoRootsConfigured = true;
        });
    }

    public static List<string> RecentPaths
    {
        get => new(Data.RecentPaths);
        set => Update(data => data.RecentPaths = NormalizePaths(value));
    }

    public static string Theme
    {
        get => Data.Theme;
        set => Update(data => data.Theme = string.IsNullOrWhiteSpace(value) ? "system" : value);
    }

    public static string Language
    {
        get => NormalizeLanguage(Data.Language);
        set => Update(data => data.Language = NormalizeLanguage(value));
    }

    private static string NormalizeLanguage(string? value)
    {
        return value?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true ? "ru-RU" : "en-US";
    }

    /// <summary>Файл, который играл при закрытии: его позиция восстанавливается на паузе.</summary>
    public static string LastTrackPath
    {
        get => Data.LastTrackPath;
        set => Update(data => data.LastTrackPath = value ?? string.Empty);
    }

    public static long LastTrackPositionMs
    {
        get => Data.LastTrackPositionMs;
        set => Update(data => data.LastTrackPositionMs = Math.Max(0, value));
    }

    /// <summary>Ждать N секунд перед следующей песней очереди (киоск-режим).</summary>
    public static bool CountdownEnabled
    {
        get => Data.CountdownEnabled;
        set => Update(data => data.CountdownEnabled = value);
    }

    public static int CountdownSeconds
    {
        get => Math.Clamp(Data.CountdownSeconds, 3, 120);
        set => Update(data => data.CountdownSeconds = Math.Clamp(value, 3, 120));
    }

    /// <summary>Выключать пульт, когда телефоны не подключены заданное время.</summary>
    public static bool RemoteAutoStopEnabled
    {
        get => Data.RemoteAutoStopEnabled;
        set => Update(data => data.RemoteAutoStopEnabled = value);
    }

    public static int RemoteAutoStopMinutes
    {
        get => Math.Clamp(Data.RemoteAutoStopMinutes, 1, 240);
        set => Update(data => data.RemoteAutoStopMinutes = Math.Clamp(value, 1, 240));
    }

    public static int RequestLimitPerGuest
    {
        get => Math.Clamp(Data.RequestLimitPerGuest, 1, 20);
        set => Update(data => data.RequestLimitPerGuest = Math.Clamp(value, 1, 20));
    }

    public static int MaxPendingRequests
    {
        get => Math.Clamp(Data.MaxPendingRequests, 1, 200);
        set => Update(data => data.MaxPendingRequests = Math.Clamp(value, 1, 200));
    }

    /// <summary>Писать обычные события в лог (падения пишутся всегда).</summary>
    public static bool LoggingEnabled
    {
        get => Data.LoggingEnabled;
        set => Update(data => data.LoggingEnabled = value);
    }

    public static bool CheckUpdatesOnStartup
    {
        get => Data.CheckUpdatesOnStartup;
        set => Update(data => data.CheckUpdatesOnStartup = value);
    }

    public static bool EqualizerEnabled
    {
        get => Data.EqualizerEnabled;
        set => Update(data => data.EqualizerEnabled = value);
    }

    public static int EqualizerPreset
    {
        get => Data.EqualizerPreset;
        set => Update(data => data.EqualizerPreset = value);
    }

    public static List<float> EqualizerBands
    {
        get => new(Data.EqualizerBands);
        set => Update(data => data.EqualizerBands = new List<float>(value));
    }

    public static bool EqualizerLinkSliders
    {
        get => Data.EqualizerLinkSliders;
        set => Update(data => data.EqualizerLinkSliders = value);
    }

    private static void Update(Action<SettingsData> mutate)
    {
        lock (Sync)
        {
            var data = _data ??= LoadCore();
            mutate(data);
            Save(data);
        }
    }

    private static SettingsData LoadCore()
    {
        var loaded = JsonFile.Read(AppStorage.PathFor(FileName), AppJsonContext.Default.SettingsData);
        if (loaded is not null)
        {
            var normalizedLanguage = NormalizeLanguage(loaded.Language);
            if (!string.Equals(loaded.Language, normalizedLanguage, StringComparison.Ordinal))
            {
                loaded.Language = normalizedLanguage;
                Save(loaded);
            }

            loaded.VideoRootsConfigured = true;
            return loaded;
        }

        // Одноразовая миграция старых настроек из ApplicationData.LocalSettings.
        var migrated = MigrateLegacySettings();
        Save(migrated);
        return migrated;
    }

    private static SettingsData MigrateLegacySettings()
    {
        var data = new SettingsData();
        try
        {
            var container = ApplicationData.Current.LocalSettings;
            if (container.Values.TryGetValue("VideoRoots", out var roots) && roots is string rootText)
            {
                data.VideoRoots = NormalizePaths(rootText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }

            if (container.Values.TryGetValue("RecentPaths", out var recent) && recent is string recentText)
            {
                data.RecentPaths = NormalizePaths(recentText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }

            data.Theme = container.Values.TryGetValue("Theme", out var theme) && theme is string themeText
                ? themeText
                : data.Theme;
            data.LastTrackPath = container.Values.TryGetValue("LastTrackPath", out var lastPath) && lastPath is string lastPathText
                ? lastPathText
                : data.LastTrackPath;
            data.LastTrackPositionMs = container.Values.TryGetValue("LastTrackPositionMs", out var lastPosition) &&
                                       lastPosition is long lastPositionValue
                ? lastPositionValue
                : data.LastTrackPositionMs;
            data.CountdownEnabled = container.Values.TryGetValue("CountdownEnabled", out var countdown) &&
                                    countdown is bool countdownValue
                ? countdownValue
                : data.CountdownEnabled;
            data.CountdownSeconds = container.Values.TryGetValue("CountdownSeconds", out var seconds) &&
                                    seconds is int secondsValue
                ? secondsValue
                : data.CountdownSeconds;
            data.RemoteAutoStopEnabled = container.Values.TryGetValue("RemoteAutoStopEnabled", out var autoStop) &&
                                         autoStop is bool autoStopValue
                ? autoStopValue
                : data.RemoteAutoStopEnabled;
            data.RemoteAutoStopMinutes = container.Values.TryGetValue("RemoteAutoStopMinutes", out var minutes) &&
                                         minutes is int minutesValue
                ? minutesValue
                : data.RemoteAutoStopMinutes;
            data.LoggingEnabled = container.Values.TryGetValue("LoggingEnabled", out var logging) &&
                                  logging is bool loggingValue
                ? loggingValue
                : data.LoggingEnabled;
        }
        catch
        {
            // В портативном режиме LocalSettings недоступны — это нормально.
        }

        data.VideoRootsConfigured = true;
        return data;
    }

    private static void Save(SettingsData data)
    {
        try
        {
            JsonFile.Write(AppStorage.PathFor(FileName), data, AppJsonContext.Default.SettingsData);
        }
        catch
        {
            // Настройки не должны ронять приложение.
        }
    }

    private static List<string> NormalizePaths(IEnumerable<string> paths) =>
        paths
            .Select(path => path.Trim())
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
