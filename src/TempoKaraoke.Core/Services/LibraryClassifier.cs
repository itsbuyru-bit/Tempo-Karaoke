using System;
using System.Text.RegularExpressions;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>Куда файл едет в каталоге.</summary>
public enum LibraryKind
{
    /// <summary>Караоке-песенка: в папку исполнителя.</summary>
    Karaoke,

    /// <summary>Длинный сборник: в «_Фоновая музыка\Сборники».</summary>
    BackgroundMix,

    /// <summary>Клип с полным вокалом: в «_Фоновая музыка\Клипы».</summary>
    Clip,

    /// <summary>Аудио без видео: в «_Фоновая музыка\Аудио».</summary>
    Audio,

    /// <summary>Мусор, в корзину.</summary>
    Junk,
}

/// <summary>
/// Определяет, караоке это или фоновая музыка. Признаки два: длительность
/// (сборник длиннее десяти минут) и имя (хиты, топ-50, шазам, mood video).
/// </summary>
public static class LibraryClassifier
{
    private const long MixDurationMs = 10 * 60 * 1000;

    private static readonly Regex MixName = new(
        @"\bхиты\b|\bтоп[\s-]?\d+|\btop[\s-]?\d+|\bшазам|\bshazam|\bсборник|\bплейлист|"
        + @"музыка\s+в\s+тренде|лучшие\s+песни|новинки\s+музыки|русская\s+музыка|russische\s+musik|"
        + @"music\s+mix|deep\s+house|vocal\s+deep|\bхит-парад|сборник\s+песни",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClipName = new(
        @"премьера\s+клипа|mood\s+video|official\s+video|официальное\s+видео|\bклип\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KaraokeMarker = new(
        @"караоке|karaoke|кавер|минус|пой\s+под\s+бит",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AudioExtension = new(
        @"^\.(mp3|flac|wav|m4a|aac|ogg|opus)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LibraryKind Classify(LibraryFile file, ParsedMediaName name)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(name);

        if (LibraryAuditService.IsJunk(file) || file.SizeBytes == 0)
        {
            return LibraryKind.Junk;
        }

        if (AudioExtension.IsMatch(file.Extension))
        {
            return LibraryKind.Audio;
        }

        var raw = file.FileName;

        // Караоке-пометка сильнее длительности: караоке-сборник всё равно караоке.
        if (KaraokeMarker.IsMatch(raw) || name.FilmSource is not null)
        {
            return LibraryKind.Karaoke;
        }

        if (file.DurationMs >= MixDurationMs)
        {
            return LibraryKind.BackgroundMix;
        }

        if (ClipName.IsMatch(raw))
        {
            return LibraryKind.Clip;
        }

        if (MixName.IsMatch(raw))
        {
            return LibraryKind.BackgroundMix;
        }

        return LibraryKind.Karaoke;
    }
}
