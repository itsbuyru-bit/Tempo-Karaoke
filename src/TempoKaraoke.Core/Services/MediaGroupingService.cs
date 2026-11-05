using System;
using System.Collections.Generic;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Собирает файлы одной песни в группу. Ключ строится из очищенного
/// названия без служебных пометок вроде «караоке» и «кавер минус».
/// </summary>
public static class MediaGroupingService
{
    public static List<MediaGroup> Group(IEnumerable<MediaItem> items)
    {
        var groups = new List<MediaGroup>();
        var byKey = new Dictionary<string, MediaGroup>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = BuildKey(item);
            if (byKey.TryGetValue(key, out var group))
            {
                group.Add(item);
            }
            else
            {
                group = new MediaGroup(key, item);
                byKey[key] = group;
                groups.Add(group);
            }
        }

        return groups;
    }

    public static string BuildKey(MediaItem item)
    {
        // DisplayTitle уже очищен в конструкторе MediaItem — повторно не чистим.
        var key = SearchService.Normalize(item.DisplayTitle);
        if (key.Length == 0)
        {
            key = SearchService.Normalize(item.Path);
        }

        // У файлов без исполнителя (например, «Track 01.mkv») название само по себе
        // не уникально: добавляем папку, иначе разные песни склеятся в одну.
        if (string.IsNullOrWhiteSpace(item.Artist))
        {
            key += "|" + SearchService.Normalize(item.Folder);
        }

        return key;
    }
}
