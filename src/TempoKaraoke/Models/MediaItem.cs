using System;
using System.ComponentModel;
using System.IO;

namespace TempoKaraoke.Models;

/// <summary>
/// Одна видеозапись в библиотеке. Пока это облегчённая модель для каркаса:
/// полноценный сканер и метаданные появятся на следующем шаге.
/// </summary>
public sealed class MediaItem : INotifyPropertyChanged
{
    private long _durationMs;

    public MediaItem(string path, string artist, string title, string folder, DateTime modifiedUtc = default)
    {
        Path = path;
        Artist = artist;
        Title = title;
        Folder = folder;
        ModifiedUtc = modifiedUtc;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public string Artist { get; }

    public string Title { get; }

    public string Folder { get; }

    public DateTime ModifiedUtc { get; }

    public long DurationMs => _durationMs;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} — {Title}";

    public string Format => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();

    public string Subtitle => string.IsNullOrWhiteSpace(Folder) ? Format : $"{Folder} · {Format}";

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

    public override string ToString() => DisplayTitle;

    public static MediaItem FromUrl(string url) => new(url, string.Empty, url, "URL");

    public static MediaItem FromPath(string path)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var artist = string.Empty;
        var title = fileName;

        var separator = fileName.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0)
        {
            artist = fileName[..separator].Trim();
            title = fileName[(separator + 3)..].Trim();
        }

        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
        var modifiedUtc = default(DateTime);
        try
        {
            modifiedUtc = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            // A file can disappear between enumeration and metadata lookup.
        }

        return new MediaItem(path, artist, title, folder, modifiedUtc);
    }
}
