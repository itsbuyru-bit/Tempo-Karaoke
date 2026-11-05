using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using TempoKaraoke.Text;

namespace TempoKaraoke.Models;

/// <summary>
/// Одна песня в библиотеке и все найденные её версии. Карточка показывает
/// лучшую версию, остальные доступны через счётчик версий.
/// </summary>
public sealed class MediaGroup : INotifyPropertyChanged
{
    private readonly List<MediaItem> _versions = new();
    private MediaItem _best;
    private object? _thumbnail;

    public MediaGroup(string key, MediaItem first)
    {
        Key = key;
        _versions.Add(first);
        _best = first;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public IReadOnlyList<MediaItem> Versions => _versions;

    /// <summary>Лучшая версия: сначала по размеру, затем по дате изменения.</summary>
    public MediaItem Best => _best;

    public string DisplayTitle => _best.DisplayTitle;

    public string Subtitle => _best.Subtitle;

    public string Format => _best.Format;

    /// <summary>
    /// Превью хранится как <see cref="object"/>, чтобы модель не зависела от WinUI
    /// и могла тестироваться в TempoKaraoke.Core. Фактически это ImageSource.
    /// </summary>
    public object? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoThumbnail)));
        }
    }

    public bool HasNoThumbnail => _thumbnail is null;

    public bool HasDuration => _best.DurationMs > 0;

    public string DurationCaption
    {
        get
        {
            var milliseconds = _best.DurationMs;
            if (milliseconds <= 0)
            {
                return "—";
            }

            var span = TimeSpan.FromMilliseconds(milliseconds);
            var english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
            return span.TotalHours >= 1
                ? (english
                    ? $"{(int)span.TotalHours} h {span.Minutes:00} min"
                    : $"{(int)span.TotalHours} ч {span.Minutes:00} мин.")
                : (english
                    ? $"{span.Minutes} min {span.Seconds:00} s"
                    : $"{span.Minutes} мин. {span.Seconds:00} с.");
        }
    }

    public int VersionCount => _versions.Count;

    public bool HasMultipleVersions => _versions.Count > 1;

    public string VersionCountText => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
        ? (VersionCount == 1 ? "1 version" : $"{VersionCount} versions")
        : RussianPlural.Format(VersionCount, "версия", "версии", "версий");

    public void Add(MediaItem item)
    {
        _versions.Add(item);
        var bestChanged = IsBetter(item, _best);
        if (bestChanged)
        {
            _best = item;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMultipleVersions)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionCountText)));

        if (bestChanged)
        {
            RaiseBestChanged();
        }
    }

    /// <summary>Применяет длительность к лучшей версии и обновляет карточку.</summary>
    public void SetDuration(long milliseconds)
    {
        _best.SetDuration(milliseconds);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDuration)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationCaption)));
    }

    private static bool IsBetter(MediaItem candidate, MediaItem current) =>
        candidate.SizeBytes > current.SizeBytes ||
        (candidate.SizeBytes == current.SizeBytes && candidate.ModifiedUtc > current.ModifiedUtc);

    private void RaiseBestChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Best)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Format)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDuration)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationCaption)));
    }
}
