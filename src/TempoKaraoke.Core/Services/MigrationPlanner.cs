using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

public enum MigrationAction
{
    /// <summary>Файл переезжает в каталог под новым именем.</summary>
    Move,

    /// <summary>Технический дубль уезжает в корзину; ссылки ведут на победителя.</summary>
    QuarantineDuplicate,

    /// <summary>Мусор: обрывки загрузки, служебные файлы, пустышки.</summary>
    QuarantineJunk,

    /// <summary>Файл остаётся на месте, причина указана.</summary>
    Skip,
}

/// <summary>Одна строка плана: откуда, куда и почему.</summary>
public sealed record MigrationPlanEntry(
    string Source,
    string Target,
    MigrationAction Action,
    string Reason,
    string? SongKey,
    string? ReplacedBy,
    long SizeBytes)
{
    /// <summary>Quality of the winner. Unknown means not probed yet.</summary>
    public QualityClass Quality { get; init; } = QualityClass.Unknown;
}

/// <summary>Полный план миграции. Составляется без изменения диска.</summary>
public sealed class MigrationPlan
{
    public required string RunId { get; init; }

    public required string CatalogRoot { get; init; }

    public required string QuarantineRoot { get; init; }

    public IReadOnlyList<MigrationPlanEntry> Entries { get; init; } = Array.Empty<MigrationPlanEntry>();

    public int Moves => Entries.Count(entry => entry.Action == MigrationAction.Move);

    public int Duplicates => Entries.Count(entry => entry.Action == MigrationAction.QuarantineDuplicate);

    public int Junk => Entries.Count(entry => entry.Action == MigrationAction.QuarantineJunk);

    public int Skipped => Entries.Count(entry => entry.Action == MigrationAction.Skip);

    public long FreedBytes => Entries
        .Where(entry => entry.Action is MigrationAction.QuarantineDuplicate or MigrationAction.QuarantineJunk)
        .Sum(entry => entry.SizeBytes);

    public int UnknownQualityWinners { get; init; }

    public bool IsReadyToApply => UnknownQualityWinners == 0;
}

public sealed record MigrationOptions(
    string CatalogRoot,
    string QuarantineRoot,
    string BackgroundRoot,
    string RunId);

/// <summary>
/// Строит план переноса. Правила: одна запись — один файл-победитель; победитель
/// по размеру, затем по дате, затем по пути. Версии не сливаются, фоновая музыка
/// идёт мимо каталога, неоднозначные имена не двигаются автоматически.
/// </summary>
public static class MigrationPlanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".webm", ".mov", ".wmv", ".m4v", ".mpg", ".mpeg", ".ts",
    };

    public const string MixFolder = "Сборники";
    public const string ClipFolder = "Клипы";
    public const string AudioFolder = "Аудио";

    /// <summary>«Пой под бит» — аудио-караоке без видео, в индекс не входит.</summary>
    private static bool IsSongUnderBeat(LibraryFile file) =>
        file.FileName.Contains("пой под бит", StringComparison.OrdinalIgnoreCase);

    public static MigrationPlan CreatePlan(IEnumerable<LibraryFile> files, MigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);

        var entries = new List<MigrationPlanEntry>();
        var parsed = new List<(LibraryFile File, ParsedMediaName Name)>();
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownQualityWinners = 0;

        foreach (var file in files.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            if (LibraryAuditService.IsJunk(file) || file.SizeBytes == 0)
            {
                entries.Add(QuarantineJunkEntry(file, options));
                continue;
            }

            var name = MediaNameParser.Parse(file.Path, file.SourceFolder);
            var kind = LibraryClassifier.Classify(file, name);

            switch (kind)
            {
                case LibraryKind.Junk:
                    entries.Add(QuarantineJunkEntry(file, options));
                    break;

                case LibraryKind.Audio when IsSongUnderBeat(file):
                    // Аудио-караоке без видео приложение не индексирует: держим
                    // отдельной подпапкой карантина, чтобы не мешало и не потерялось.
                    entries.Add(new MigrationPlanEntry(
                        file.Path,
                        Path.Combine(options.QuarantineRoot, options.RunId, "mp3_пой_под_бит", file.FileName),
                        MigrationAction.QuarantineJunk,
                        "аудио-караоке без видео — вне индекса",
                        null,
                        null,
                        file.SizeBytes));
                    break;

                case LibraryKind.BackgroundMix:
                case LibraryKind.Clip:
                case LibraryKind.Audio:
                    entries.Add(RouteBackground(file, kind, options, usedTargets));
                    break;
                default:
                    if (!VideoExtensions.Contains(file.Extension))
                    {
                        entries.Add(SkipEntry(file, "не видео — караоке-индекс его не содержит"));
                        break;
                    }

                    if (name.NeedsAttention)
                    {
                        entries.Add(SkipEntry(file, "имя разобрано неуверенно — нужен ручной выбор", name.SongKey));
                        break;
                    }

                    parsed.Add((file, name));
                    break;
            }
        }

        var groups = parsed
            .GroupBy(pair => pair.Name.SongKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            // Качество важнее размера: 1080p одной песни всегда бьёт 720p-дубль,
            // даже если тот случайно оказался тяжелее.
            // Readable file always beats an unreadable one, and an unreadable
            // file never blocks the whole plan.
            var ordered = group
                .OrderByDescending(pair => pair.File.Quality)
                .ThenBy(pair => pair.File.IsProbed && pair.File.Quality == QualityClass.Unknown ? 1 : 0)
                .ThenByDescending(pair => pair.File.SizeBytes)
                .ThenByDescending(pair => pair.File.ModifiedUtc)
                .ThenBy(pair => pair.File.Path, StringComparer.Ordinal)
                .ToList();

            var winner = ordered[0];

            // Broken container: mark for manual review instead of moving a
            // file nobody could read. Other groups stay applicable.
            if (winner.File.IsProbed && winner.File.Quality == QualityClass.Unknown)
            {
                foreach (var broken in ordered)
                {
                    entries.Add(SkipEntry(broken.File, "broken container, manual review", broken.Name.SongKey));
                }

                continue;
            }

            // Only "never scanned" blocks apply: the winner could still change.
            if (!winner.File.IsProbed)
            {
                unknownQualityWinners++;
            }

            var winnerTarget = ReserveTarget(winner.File, winner.Name, options, usedTargets);

            entries.Add(string.Equals(winnerTarget, winner.File.Path, StringComparison.OrdinalIgnoreCase)
                ? SkipEntry(winner.File, "уже на месте", winner.Name.SongKey)
                : new MigrationPlanEntry(
                    winner.File.Path, winnerTarget, MigrationAction.Move,
                    DescribeMove(winner.Name), winner.Name.SongKey, null, winner.File.SizeBytes)
                {
                    Quality = winner.File.Quality,
                });

            foreach (var loser in ordered.Skip(1))
            {
                entries.Add(new MigrationPlanEntry(
                    loser.File.Path,
                    BuildQuarantinePath(loser.File, options),
                    MigrationAction.QuarantineDuplicate,
                    $"технический дубль; победитель — {Path.GetFileName(winnerTarget)}",
                    loser.Name.SongKey,
                    winnerTarget,
                    loser.File.SizeBytes));
            }
        }

        return new MigrationPlan
        {
            UnknownQualityWinners = unknownQualityWinners,
            RunId = options.RunId,
            CatalogRoot = options.CatalogRoot,
            QuarantineRoot = options.QuarantineRoot,
            Entries = entries
                .OrderBy(entry => entry.Action)
                .ThenBy(entry => entry.Source, StringComparer.Ordinal)
                .ToList(),
        };
    }

    /// <summary>Сборники, клипы и аудио живут вне каталога и не участвуют в дублях.</summary>
    private static MigrationPlanEntry RouteBackground(
        LibraryFile file,
        LibraryKind kind,
        MigrationOptions options,
        HashSet<string> usedTargets)
    {
        var folder = kind switch
        {
            LibraryKind.BackgroundMix => MixFolder,
            LibraryKind.Clip => ClipFolder,
            _ => AudioFolder,
        };

        var directory = Path.Combine(options.BackgroundRoot, folder);
        var target = ReserveFlatTarget(directory, file.FileName, usedTargets);

        var reason = kind switch
        {
            LibraryKind.BackgroundMix => "фоновая музыка: длинный сборник",
            LibraryKind.Clip => "фоновая музыка: клип с вокалом",
            _ => "фоновая музыка: аудио",
        };

        return string.Equals(target, file.Path, StringComparison.OrdinalIgnoreCase)
            ? SkipEntry(file, "уже на месте")
            : new MigrationPlanEntry(file.Path, target, MigrationAction.Move, reason, null, null, file.SizeBytes);
    }

    private static string ReserveFlatTarget(string directory, string fileName, HashSet<string> usedTargets)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);

        var counter = 2;
        while (!usedTargets.Add(candidate))
        {
            candidate = Path.Combine(directory, $"{stem} ({counter}){extension}");
            counter++;
        }

        return candidate;
    }

    private static MigrationPlanEntry SkipEntry(LibraryFile file, string reason, string? songKey = null) =>
        new(file.Path, string.Empty, MigrationAction.Skip, reason, songKey, null, file.SizeBytes);

    /// <summary>Подбирает свободный целевой путь, добавляя «(v2)», «(v3)» и далее.</summary>
    private static string ReserveTarget(
        LibraryFile file,
        ParsedMediaName name,
        MigrationOptions options,
        HashSet<string> usedTargets)
    {
        var candidate = MediaNameComposer
            .BuildTargetPath(options.CatalogRoot, name, file.Quality, file.Extension)
            .Replace('/', '\\');

        var ordinal = name.Ordinal;
        while (!usedTargets.Add(candidate))
        {
            ordinal++;
            candidate = MediaNameComposer
                .BuildTargetPath(options.CatalogRoot, name with { Ordinal = ordinal }, file.Quality, file.Extension)
                .Replace('/', '\\');
        }

        return candidate;
    }

    private static MigrationPlanEntry QuarantineJunkEntry(LibraryFile file, MigrationOptions options) =>
        new(file.Path,
            BuildQuarantinePath(file, options),
            MigrationAction.QuarantineJunk,
            LibraryAuditService.IsJunk(file) ? "мусор" : "нулевой размер",
            null,
            null,
            file.SizeBytes);

    private static string BuildQuarantinePath(LibraryFile file, MigrationOptions options)
    {
        var folder = string.IsNullOrWhiteSpace(file.SourceFolder) ? "_" : file.SourceFolder;
        var safeFolder = MediaNameComposer.Sanitize(folder, 80);
        return Path.Combine(options.QuarantineRoot, options.RunId, safeFolder, file.FileName);
    }

    private static string DescribeMove(ParsedMediaName name)
    {
        var parts = new List<string> { "переименование и раскладка по исполнителю" };
        if (name.Version != VersionKind.Original)
        {
            parts.Add($"версия: {MediaNameComposer.VersionBadge(name.Version)}");
        }

        if (name.FilmSource is not null)
        {
            parts.Add($"из фильма: {name.FilmSource}");
        }

        return string.Join("; ", parts);
    }
}
