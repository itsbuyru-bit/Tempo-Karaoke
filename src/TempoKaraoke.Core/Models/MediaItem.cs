using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using TempoKaraoke.Services;

namespace TempoKaraoke.Models;

/// <summary>
/// Одна видеозапись в библиотеке. Пока это облегчённая модель для каркаса:
/// полноценный сканер и метаданные появятся на следующем шаге.
/// </summary>
public sealed class MediaItem : INotifyPropertyChanged
{
    /// <summary>Подстановка локализованных строк из приложения (Core не знает ресурсов).</summary>
    public static Func<string, string?>? TextResolver { get; set; }

    private readonly string _displayTitle;
    private long _durationMs;
    private int _width;
    private int _height;
    private string? _thumbnailPath;
    private bool _isSelected;
    private bool _isPlaylistSelected;
    private bool _isPlaying;
    private bool _isAlternateRow;
    private bool _isHovered;
    private bool _isSelectionActive;
    private string? _queuedBy;

    public MediaItem(
        string path,
        string artist,
        string title,
        string folder,
        DateTime modifiedUtc = default,
        long sizeBytes = 0)
    {
        Path = path;
        Artist = artist;
        Title = title;
        Folder = folder;
        ModifiedUtc = modifiedUtc;
        SizeBytes = sizeBytes;
        _displayTitle = MediaTitleCleaner.Clean(
            string.IsNullOrWhiteSpace(artist) ? title : $"{artist} — {title}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Уникальный идентификатор записи в очереди (даже для дублей одного файла).</summary>
    public Guid QueueEntryId { get; } = Guid.NewGuid();

    public string Path { get; }

    public string Artist { get; }

    public string Title { get; }

    public string Folder { get; }

    public DateTime ModifiedUtc { get; }

    public long SizeBytes { get; }

    public long DurationMs => _durationMs;

    public int Width => _width;

    public int Height => _height;

    public string? ThumbnailPath => _thumbnailPath;

    /// <summary>Бонус к поисковой выдаче: трек добавлен в избранное.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Сколько раз трек запускали; используется как мягкий тай-брейкер.</summary>
    public int PlayCount { get; set; }

    /// <summary>Выделен ли элемент в очереди (состояние чекбокса).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNowIcon)));
        }
    }

    /// <summary>Выделен ли элемент в плейлисте (независимо от очереди).</summary>
    public bool IsPlaylistSelected
    {
        get => _isPlaylistSelected;
        set
        {
            if (_isPlaylistSelected == value)
            {
                return;
            }

            _isPlaylistSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaylistSelected)));
        }
    }

    /// <summary>Играет ли сейчас этот трек (подсветка строки в очереди).</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNowIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowMoviesIcon)));
        }
    }

    /// <summary>Наведён ли курсор на строку очереди.</summary>
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered == value)
            {
                return;
            }

            _isHovered = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHovered)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNowIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowMoviesIcon)));
        }
    }

    /// <summary>Иконка Video видна только у играющего трека, пока он не подсвечен и не выбран.</summary>
    public bool ShowNowIcon => _isPlaying && !_isHovered && !_isSelected && !_isSelectionActive;

    /// <summary>Обычная иконка Video видна, когда трек не играет и курсор не наведён.</summary>
    public bool ShowMoviesIcon => !_isPlaying && !_isHovered;

    /// <summary>В очереди включён режим отметки чекбоксами.</summary>
    public bool IsSelectionActive
    {
        get => _isSelectionActive;
        set
        {
            if (_isSelectionActive == value)
            {
                return;
            }

            _isSelectionActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNowIcon)));
        }
    }

    /// <summary>Чётная/нечётная строка очереди — для чередования фона карточек.</summary>
    public bool IsAlternateRow
    {
        get => _isAlternateRow;
        set
        {
            if (_isAlternateRow == value)
            {
                return;
            }

            _isAlternateRow = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAlternateRow)));
        }
    }

    public string ResolutionText => _width > 0 && _height > 0 ? $"{_width}×{_height}" : "—";

    public string DisplayTitle => _displayTitle;

    public string Format => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();

    public string Subtitle => string.IsNullOrWhiteSpace(Folder) ? Format : $"{Folder} · {Format}";

    /// <summary>Кто добавил трек в очередь (имя устройства или «Компьютер»).</summary>
    public string? QueuedBy => _queuedBy;

    public string QueueSubtitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_queuedBy))
            {
                return Subtitle;
            }

            var english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
            var addedBy = TextResolver?.Invoke("Queue_AddedBy") ?? (english ? "added by" : "добавил");
            var requester = string.Equals(_queuedBy, "Компьютер", StringComparison.OrdinalIgnoreCase)
                ? TextResolver?.Invoke("Queue_Computer") ?? (english ? "PC" : "Компьютер")
                : _queuedBy;
            return $"{Subtitle} · {addedBy}: {requester}";
        }
    }

    /// <summary>Пересчитывает локализованные подписи после смены языка.</summary>
    public void RefreshLocalizedText()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueueSubtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
    }

    public void SetQueuedBy(string? value)
    {
        if (string.Equals(_queuedBy, value, StringComparison.Ordinal))
        {
            return;
        }

        _queuedBy = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueuedBy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueueSubtitle)));
    }

    public string SizeText => SizeBytes <= 0
        ? "—"
        : SizeBytes >= 1024L * 1024 * 1024
            ? $"{SizeBytes / 1024d / 1024 / 1024:0.0} {(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "GB" : "ГБ")}"
            : $"{SizeBytes / 1024d / 1024:0} {(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "MB" : "МБ")}";

    public string DurationText
    {
        get
        {
            if (_durationMs <= 0)
            {
                return "--:--";
            }

            var span = TimeSpan.FromMilliseconds(_durationMs);
            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        }
    }

    public void SetDuration(long milliseconds)
    {
        if (milliseconds <= 0 || milliseconds == _durationMs)
        {
            return;
        }

        _durationMs = milliseconds;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationMs)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationText)));
    }

    /// <summary>Применяет данные сканера: длительность, разрешение и превью.</summary>
    public void ApplyMetadata(long durationMs, int width, int height, string? thumbnailPath)
    {
        var durationChanged = durationMs > 0 && durationMs != _durationMs;
        if (durationChanged)
        {
            _durationMs = durationMs;
        }

        var resolutionChanged = width != _width || height != _height;
        _width = width;
        _height = height;
        _thumbnailPath = thumbnailPath;

        if (durationChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationMs)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationText)));
        }

        if (resolutionChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Height)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolutionText)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailPath)));
    }

    public override string ToString() => DisplayTitle;

    public static MediaItem FromUrl(string url) => new(url, string.Empty, url, "URL");

    public static MediaItem FromPath(string path)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
        var parsed = MediaNameParser.Parse(System.IO.Path.GetFileName(path), folder);
        var artist = parsed.Artist;
        var title = parsed.Title;

        // A malformed or unsupported file name must still produce a usable item.
        if (string.IsNullOrWhiteSpace(title))
        {
            artist = string.Empty;
            title = fileName;
            var separator = fileName.IndexOf(" - ", StringComparison.Ordinal);
            if (separator > 0)
            {
                artist = fileName[..separator].Trim();
                title = fileName[(separator + 3)..].Trim();
            }
        }

        var modifiedUtc = default(DateTime);
        var sizeBytes = 0L;
        try
        {
            var info = new FileInfo(path);
            modifiedUtc = info.LastWriteTimeUtc;
            sizeBytes = info.Length;
        }
        catch
        {
            // A file can disappear between enumeration and metadata lookup.
        }

        return new MediaItem(path, artist, title, folder, modifiedUtc, sizeBytes);
    }

    /// <summary>
    /// Creates a distinct object for a queue slot. Sharing one MediaItem between
    /// duplicate rows makes IsPlaying and selection state leak across duplicates.
    /// </summary>
    public MediaItem CreateQueueCopy()
    {
        var copy = new MediaItem(Path, Artist, Title, Folder, ModifiedUtc, SizeBytes);
        copy.ApplyMetadata(DurationMs, Width, Height, ThumbnailPath);
        copy.SetQueuedBy(QueuedBy);
        copy.IsFavorite = IsFavorite;
        copy.PlayCount = PlayCount;
        return copy;
    }
}
