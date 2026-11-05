using System;
using System.Collections.Generic;
using System.Linq;

namespace TempoKaraoke.Services;

public sealed class LibraryActivityData
{
    public List<string> Favorites { get; set; } = new();

    public List<LibraryHistoryEntry> History { get; set; } = new();
}

public sealed record LibraryHistoryEntry(string Path, long PlayedUtcTicks);

/// <summary>Favorites and play history shared by the desktop and remote UI.</summary>
public static class LibraryActivityStore
{
    private const string FileName = "activity.json";
    private const int HistoryLimit = 100;
    private static readonly object Sync = new();
    private static LibraryActivityData? _data;

    /// <summary>Файл хранилища; миграция переписывает пути прямо в нём.</summary>
    public static string FilePath => AppStorage.PathFor(FileName);

    /// <summary>Сбрасывает кэш: после переписывания файла читаем избранное и историю заново.</summary>
    public static void Reload()
    {
        lock (Sync)
        {
            _data = null;
        }
    }

    public static IReadOnlyList<string> Favorites
    {
        get
        {
            lock (Sync)
            {
                return Load().Favorites.ToList();
            }
        }
    }

    public static IReadOnlyList<string> HistoryPaths
    {
        get
        {
            lock (Sync)
            {
                return Load().History.Select(entry => entry.Path).ToList();
            }
        }
    }

    public static bool IsFavorite(string path)
    {
        lock (Sync)
        {
            return Load().Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool ToggleFavorite(string path)
    {
        lock (Sync)
        {
            var data = Load();
            var existing = data.Favorites.FirstOrDefault(
                favorite => string.Equals(favorite, path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                data.Favorites.Add(path);
            }
            else
            {
                data.Favorites.Remove(existing);
            }

            Save(data);
            return existing is null;
        }
    }

    public static void RecordPlay(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (Sync)
        {
            var data = Load();
            data.History.RemoveAll(entry =>
                string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
            data.History.Insert(0, new LibraryHistoryEntry(path, DateTime.UtcNow.Ticks));
            if (data.History.Count > HistoryLimit)
            {
                data.History.RemoveRange(HistoryLimit, data.History.Count - HistoryLimit);
            }

            Save(data);
        }
    }

    private static LibraryActivityData Load()
    {
        if (_data is not null)
        {
            return _data;
        }

        var loaded = JsonFile.Read(
            AppStorage.PathFor(FileName),
            AppJsonContext.Default.LibraryActivityData);
        _data = loaded ?? new LibraryActivityData();
        return _data;
    }

    private static void Save(LibraryActivityData data)
    {
        try
        {
            JsonFile.Write(
                AppStorage.PathFor(FileName),
                data,
                AppJsonContext.Default.LibraryActivityData);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Не удалось сохранить избранное/историю: {ex.Message}");
        }
    }
}
