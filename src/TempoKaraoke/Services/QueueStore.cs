using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace TempoKaraoke.Services;

/// <summary>Одна позиция очереди: путь и устройство, которое её добавило.</summary>
public sealed record QueueEntry(string Path, string? RequesterId);

/// <summary>
/// Очередь хранится в отдельном JSON-файле, а не в LocalSettings:
/// у LocalSettings есть лимит 8 КБ на значение, а очередь может быть длинной.
/// </summary>
public static class QueueStore
{
    private const string FileName = "queue.json";

    private static readonly object Sync = new();
    private static List<QueueEntry>? _entries;

    /// <summary>Файл хранилища; миграция переписывает пути прямо в нём.</summary>
    public static string FilePath => AppStorage.PathFor(FileName);

    /// <summary>Сбрасывает кэш: после переписывания файла читаем пути заново.</summary>
    public static void Reload()
    {
        lock (Sync)
        {
            _entries = null;
        }
    }

    public static List<QueueEntry> Load()
    {
        lock (Sync)
        {
            return new List<QueueEntry>(_entries ??= LoadCore());
        }
    }

    public static void Save(IEnumerable<QueueEntry> entries)
    {
        var copy = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .ToList();

        lock (Sync)
        {
            _entries = copy;
            try
            {
                JsonFile.Write(AppStorage.PathFor(FileName), copy, AppJsonContext.Default.ListQueueEntry);
            }
            catch
            {
                // Очередь — вспомогательные данные: сбой записи не должен ломать плеер.
            }
        }
    }

    private static List<QueueEntry> LoadCore()
    {
        var loaded = JsonFile.Read(AppStorage.PathFor(FileName), AppJsonContext.Default.ListQueueEntry);
        if (loaded is not null)
        {
            return loaded;
        }

        // Миграция старой очереди из LocalSettings.
        try
        {
            var container = ApplicationData.Current.LocalSettings;
            if (container.Values.TryGetValue("QueuePaths", out var raw) && raw is string text)
            {
                return text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => path.Length > 0)
                    .Select(path => new QueueEntry(path, null))
                    .ToList();
            }
        }
        catch
        {
            // Портативный режим: LocalSettings недоступны.
        }

        return new List<QueueEntry>();
    }
}
