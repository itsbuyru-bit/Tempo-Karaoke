using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Проверка библиотеки: разбирает имена, ищет технические дубли, сирот и мусор.
/// Ничего не изменяет — это гарантия, на которой держится вся вкладка «Проверка».
/// </summary>
public static class LibraryAuditService
{
    private static readonly HashSet<string> JunkExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".part", ".crdownload", ".tmp", ".json", ".torrent", ".url", ".nfo", ".db",
    };

    private static readonly HashSet<string> ServiceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "downloads.json", "subscrube.json", "thumbs.db", "desktop.ini",
    };

    public static AuditReport Audit(IEnumerable<LibraryFile> files, string catalogRoot)
    {
        ArgumentNullException.ThrowIfNull(files);

        var all = files.ToList();
        var items = new List<AuditItem>();
        var parsed = new List<(LibraryFile File, ParsedMediaName Name)>();
        var junkCount = 0;
        long junkBytes = 0;

        foreach (var file in all)
        {
            if (IsJunk(file))
            {
                junkCount++;
                junkBytes += file.SizeBytes;
                items.Add(new AuditItem(file.Path, "Мусор", DescribeJunk(file)));
                continue;
            }

            if (file.SizeBytes == 0)
            {
                junkCount++;
                items.Add(new AuditItem(file.Path, "Пустой файл", "0 байт"));
                continue;
            }

            var name = MediaNameParser.Parse(file.Path, file.SourceFolder);
            parsed.Add((file, name));

            if (name.NeedsAttention)
            {
                items.Add(new AuditItem(file.Path, "Разобрать вручную", $"{name.Artist} / {name.Title}"));
            }
        }

        var groups = parsed
            .GroupBy(pair => pair.Name.SongKey, StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();

        var duplicates = new List<DuplicateGroupInfo>();
        var collapsibleFiles = 0;
        long collapsibleBytes = 0;

        foreach (var group in groups.Where(g => g.Count > 1))
        {
            var winner = group
                .OrderByDescending(pair => pair.File.SizeBytes)
                .ThenByDescending(pair => pair.File.ModifiedUtc)
                .ThenBy(pair => pair.File.Path, StringComparer.Ordinal)
                .First();

            foreach (var loser in group.Where(pair => !ReferenceEquals(pair.File, winner.File)))
            {
                collapsibleFiles++;
                collapsibleBytes += loser.File.SizeBytes;
            }

            duplicates.Add(new DuplicateGroupInfo(
                group[0].Name.SongKey,
                DescribeName(winner.Name),
                group.Select(pair => pair.File.Path).ToList(),
                winner.File.Path));
        }

        var renameRequired = parsed.Count(pair =>
        {
            var target = MediaNameComposer.BuildFileName(pair.Name, pair.File.Quality, pair.File.Extension);
            return !string.Equals(target, pair.File.FileName, StringComparison.Ordinal);
        });

        return new AuditReport
        {
            TotalFiles = parsed.Count,
            TotalBytes = parsed.Sum(pair => pair.File.SizeBytes),
            WithArtist = parsed.Count(pair => pair.Name.HasArtist),
            Films = parsed.Count(pair => pair.Name.FilmSource is not null),
            Orphans = parsed.Count(pair => !pair.Name.HasArtist && pair.Name.FilmSource is null),
            NeedsAttention = items.Count(item => item.Category == "Разобрать вручную"),
            RenameRequired = renameRequired,
            VersionVariants = parsed.Count(pair => pair.Name.Version != VersionKind.Original),
            DuplicateGroups = duplicates.Count,
            FilesInDuplicates = duplicates.Sum(group => group.Paths.Count),
            CollapsibleFiles = collapsibleFiles,
            CollapsibleBytes = collapsibleBytes,
            JunkFiles = junkCount,
            JunkBytes = junkBytes,
            Items = items,
            Duplicates = duplicates.OrderByDescending(group => group.Paths.Count).ToList(),
            BySourceFolder = parsed
                .GroupBy(pair => pair.File.SourceFolder, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            ByQuality = parsed
                .GroupBy(pair => pair.File.Quality)
                .ToDictionary(group => group.Key, group => group.Count()),
            ByVersion = parsed
                .GroupBy(pair => pair.Name.Version)
                .ToDictionary(group => group.Key, group => group.Count()),
        };
    }

    public static bool IsJunk(LibraryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (ServiceNames.Contains(Path.GetFileName(file.Path)))
        {
            return true;
        }

        return JunkExtensions.Contains(file.Extension)
            || file.Path.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeJunk(LibraryFile file)
    {
        var extension = file.Extension.ToLowerInvariant();
        return extension switch
        {
            ".part" => "незавершённая загрузка",
            ".json" => "служебный файл загрузчика",
            _ => $"файл {extension}",
        };
    }

    private static string DescribeName(ParsedMediaName name) =>
        name.HasArtist ? $"{name.Artist} — {name.Title}" : name.Title;

    /// <summary>
    /// Собирает файлы библиотеки по корням. Отдельный метод, чтобы проверку можно
    /// было вызвать и из приложения, и из инструментов, не дублируя обход.
    /// </summary>
    public static IEnumerable<LibraryFile> Scan(
        string root,
        Func<string, bool>? include = null,
        Action<string, Exception>? onError = null)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            onError?.Invoke(root, ex);
            yield break;
        }

        foreach (var path in paths)
        {
            if (include is not null && !include(path))
            {
                continue;
            }

            LibraryFile? file = null;
            try
            {
                var info = new FileInfo(path);
                file = new LibraryFile
                {
                    Path = info.FullName,
                    SourceFolder = info.Directory?.Name ?? string.Empty,
                    SizeBytes = info.Length,
                    ModifiedUtc = info.LastWriteTimeUtc,
                    Extension = info.Extension,
                };
            }
            catch (Exception ex)
            {
                onError?.Invoke(path, ex);
            }

            if (file is not null)
            {
                yield return file;
            }
        }
    }
}
