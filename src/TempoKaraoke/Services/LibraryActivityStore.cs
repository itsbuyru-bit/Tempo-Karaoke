using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

public sealed class LibraryActivityData
{
    public List<string> Favorites { get; set; } = new();

    public List<LibraryHistoryEntry> History { get; set; } = new();

    /// <summary>Количество запусков по пути; переживает перезапуски и используется поиском.</summary>
    public Dictionary<string, int> PlayCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Отпечаток файла без имени: размер + время изменения в секундах.</summary>
    public Dictionary<string, string> Fingerprints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Количество запусков трека по пути.</summary>
    public static int GetPlayCount(string path)
    {
        lock (Sync)
        {
            var counts = Load().PlayCounts;
            return counts is not null && counts.TryGetValue(path, out var count)
                ? count
                : 0;
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
            RememberFingerprint(data, path);
            var count = data.PlayCounts is not null &&
                        data.PlayCounts.TryGetValue(path, out var existingCount)
                ? existingCount + 1
                : 1;
            data.PlayCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            data.PlayCounts[path] = count;

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

    /// <summary>
    /// Moves favorites/play counts when a file was renamed but its size and
    /// modification time stayed the same.
    /// </summary>
    public static void Reconcile(IEnumerable<MediaItem> items)
    {
        lock (Sync)
        {
            var data = Load();
            data.Fingerprints ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changed = false;

            foreach (var item in items)
            {
                var fingerprint = FileFingerprint(item.SizeBytes, item.ModifiedUtc);
                if (data.Fingerprints.TryGetValue(item.Path, out var existing) &&
                    string.Equals(existing, fingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                var oldPath = data.Fingerprints
                    .Where(pair => string.Equals(pair.Value, fingerprint, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .FirstOrDefault(path => !File.Exists(path));
                if (oldPath is not null &&
                    !string.Equals(oldPath, item.Path, StringComparison.OrdinalIgnoreCase))
                {
                    Migrate(data, oldPath, item.Path);
                }

                data.Fingerprints[item.Path] = fingerprint;
                changed = true;
            }

            if (changed)
            {
                Save(data);
            }
        }
    }

    public static string FileFingerprint(long sizeBytes, DateTime modifiedUtc)
    {
        var seconds = new DateTimeOffset(modifiedUtc.ToUniversalTime()).ToUnixTimeSeconds();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sizeBytes}:{seconds}"));
        return Convert.ToHexString(bytes);
    }

    private static void RememberFingerprint(LibraryActivityData data, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }

            data.Fingerprints ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            data.Fingerprints[path] = FileFingerprint(info.Length, info.LastWriteTimeUtc);
        }
        catch
        {
            // A missing/inaccessible file simply has no fingerprint.
        }
    }

    private static void Migrate(LibraryActivityData data, string oldPath, string newPath)
    {
        var favorite = data.Favorites.FirstOrDefault(
            path => string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase));
        if (favorite is not null)
        {
            data.Favorites.Remove(favorite);
            data.Favorites.Add(newPath);
        }

        if (data.PlayCounts is not null &&
            data.PlayCounts.TryGetValue(oldPath, out var count))
        {
            data.PlayCounts.Remove(oldPath);
            data.PlayCounts[newPath] = count;
        }

        for (var index = 0; index < data.History.Count; index++)
        {
            if (string.Equals(data.History[index].Path, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                data.History[index] = data.History[index] with { Path = newPath };
            }
        }

        data.Fingerprints?.Remove(oldPath);
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
