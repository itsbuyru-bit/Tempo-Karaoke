using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Экспорт и импорт простых M3U/M3U8-списков. Пути пишутся абсолютными,
/// чтобы список открывался в VLC и других плеерах без дополнительных настроек.
/// </summary>
public static class M3uService
{
    public static async Task WriteAsync(string playlistPath, IEnumerable<MediaItem> items)
    {
        var lines = new List<string> { "#EXTM3U" };
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
            {
                continue;
            }

            lines.Add($"#EXTINF:-1,{item.DisplayTitle}");
            lines.Add(item.Path);
        }

        var folder = Path.GetDirectoryName(playlistPath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await File.WriteAllLinesAsync(playlistPath, lines, new UTF8Encoding(false));
    }

    /// <summary>
    /// Переписывает пути внутри m3u/m3u8 по карте миграции. Комментарии и
    /// строки, которых нет в карте, остаются как были; относительные пути
    /// разрешаются от папки списка и записываются уже абсолютными.
    /// </summary>
    public static int RewritePaths(
        string playlistPath,
        IReadOnlyDictionary<string, string> pathMap)
    {
        ArgumentNullException.ThrowIfNull(pathMap);

        if (pathMap.Count == 0 || !File.Exists(playlistPath))
        {
            return 0;
        }

        var baseDirectory = Path.GetDirectoryName(playlistPath) ?? string.Empty;
        var lines = File.ReadAllLines(playlistPath);
        var replaced = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var value = lines[index].Trim();
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }

            var absolute = value;
            if (!Path.IsPathRooted(absolute) && baseDirectory.Length > 0)
            {
                try
                {
                    absolute = Path.GetFullPath(Path.Combine(baseDirectory, absolute));
                }
                catch
                {
                    continue;
                }
            }

            if (pathMap.TryGetValue(absolute, out var replacement))
            {
                lines[index] = replacement;
                replaced++;
            }
        }

        if (replaced > 0)
        {
            File.WriteAllLines(playlistPath, lines, new UTF8Encoding(false));
        }

        return replaced;
    }

    public static async Task<List<string>> ReadAsync(string playlistPath)
    {
        var result = new List<string>();
        var baseDirectory = Path.GetDirectoryName(playlistPath) ?? string.Empty;
        var lines = await File.ReadAllLinesAsync(playlistPath);

        foreach (var line in lines)
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }

            var path = value;
            if (!Path.IsPathRooted(path) && baseDirectory.Length > 0)
            {
                path = Path.GetFullPath(Path.Combine(baseDirectory, path));
            }

            if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(path);
            }
        }

        return result;
    }
}
