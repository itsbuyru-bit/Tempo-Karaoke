using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Собирает целевые имена файлов и папок по стандарту каталога и приводит их
/// к виду, допустимому в NTFS.
/// </summary>
public static class MediaNameComposer
{
    /// <summary>Папка для песен, у которых исполнителя нет по природе.</summary>
    public const string FilmsFolder = "_Из фильмов";

    /// <summary>Папка для файлов, где исполнителя не удалось определить.</summary>
    public const string UnknownArtistFolder = "_Без исполнителя";

    private const int MaxStemLength = 150;
    private const int MaxFolderLength = 80;

    private static readonly char[] InvalidNameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Тег версии в имени файла. Оригинал тега не получает.</summary>
    public static string VersionSuffix(VersionKind version) => version switch
    {
        VersionKind.Cover => " (Кавер)",
        VersionKind.Extreme => " (Экстрим)",
        VersionKind.Russian => " (Рус)",
        VersionKind.Transcription => " (Транскр)",
        VersionKind.Remix => " (Ремикс)",
        VersionKind.Live => " (Live)",
        VersionKind.BackingVocal => " (Бэк)",
        _ => string.Empty,
    };

    /// <summary>Короткая подпись версии для бейджа в интерфейсе.</summary>
    public static string VersionBadge(VersionKind version) => version switch
    {
        VersionKind.Cover => "Кавер",
        VersionKind.Extreme => "Экстрим",
        VersionKind.Russian => "Рус",
        VersionKind.Transcription => "Транскр",
        VersionKind.Remix => "Ремикс",
        VersionKind.Live => "Live",
        VersionKind.BackingVocal => "Бэк",
        _ => string.Empty,
    };

    /// <summary>
    /// Имя файла по стандарту: «Исполнитель - Название (Версия) (vN) [720p].mkv».
    /// </summary>
    public static string BuildFileName(
        ParsedMediaName parsed,
        QualityClass quality,
        string extension)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var builder = new StringBuilder();
        var artist = Sanitize(parsed.Artist, MaxStemLength);
        if (artist.Length > 0)
        {
            builder.Append(artist).Append(" - ");
        }

        builder.Append(Sanitize(parsed.Title, MaxStemLength));
        builder.Append(VersionSuffix(parsed.Version));

        if (parsed.Ordinal > 1)
        {
            builder.Append(" (v").Append(parsed.Ordinal).Append(')');
        }

        var qualityTag = quality.FileTag();
        if (qualityTag is not null)
        {
            builder.Append(' ').Append(qualityTag);
        }

        var stem = TrimName(builder.ToString());
        if (stem.Length > MaxStemLength)
        {
            stem = TrimName(stem[..MaxStemLength]);
        }

        var suffix = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.Trim();
        if (suffix.Length > 0 && suffix[0] != '.')
        {
            suffix = "." + suffix;
        }

        return stem + suffix.ToLowerInvariant();
    }

    /// <summary>
    /// Имя папки исполнителя. Ведущий «#» сохраняется: «#2Маши» — так группа
    /// и называется. Пустой исполнитель уезжает в отдельную папку.
    /// </summary>
    public static string BuildArtistFolder(string? artist)
    {
        var folder = Sanitize(artist, MaxFolderLength);
        if (folder.Length == 0)
        {
            return UnknownArtistFolder;
        }

        if (folder.Length > MaxFolderLength)
        {
            folder = TrimName(folder[..MaxFolderLength]);
        }

        if (ReservedNames.Contains(folder))
        {
            folder = "_" + folder;
        }

        return folder;
    }

    /// <summary>Папка для песни: артист, песня из фильма или сирота.</summary>
    public static string BuildFolder(ParsedMediaName parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (!parsed.HasArtist)
        {
            return parsed.FilmSource is not null ? FilmsFolder : UnknownArtistFolder;
        }

        return BuildArtistFolder(parsed.Artist);
    }

    /// <summary>
    /// Приводит произвольную строку к допустимому имени: запрещённые символы
    /// в пробел, никаких точек и пробелов на концах, без пустых скобок.
    /// </summary>
    public static string Sanitize(string? value, int maxLength = MaxStemLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsControl(ch) || Array.IndexOf(InvalidNameChars, ch) >= 0)
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(ch);
            }
        }

        var result = Spaces.Replace(builder.ToString(), " ").Trim();
        result = EmptyBrackets.Replace(result, string.Empty);
        result = Spaces.Replace(result, " ").Trim();

        while (result.Length > 0 && (result[^1] == '.' || result[^1] == ' '))
        {
            result = result[..^1];
        }

        return result.Length > maxLength ? result[..maxLength] : result;
    }

    private static readonly Regex EmptyBrackets = new(
        @"\(\s*\)|\[\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string TrimName(string value)
    {
        var trimmed = value.Trim().TrimEnd('.', ' ');
        return Spaces.Replace(trimmed, " ").TrimEnd('.', ' ');
    }

    /// <summary>Расширение файла в нижнем регистре, с точкой.</summary>
    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var suffix = extension.Trim();
        return (suffix[0] == '.' ? suffix : "." + suffix).ToLowerInvariant();
    }

    /// <summary>Полный путь к файлу внутри корня каталога.</summary>
    public static string BuildTargetPath(
        string catalogRoot,
        ParsedMediaName parsed,
        QualityClass quality,
        string extension)
    {
        var folder = BuildFolder(parsed);
        var fileName = BuildFileName(parsed, quality, NormalizeExtension(extension));
        return Path.Combine(catalogRoot, folder, fileName);
    }
}
