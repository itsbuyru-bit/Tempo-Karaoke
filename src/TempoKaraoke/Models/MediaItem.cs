using System;
using System.IO;

namespace TempoKaraoke.Models;

/// <summary>
/// Одна видеозапись в библиотеке. Пока это облегчённая модель для каркаса:
/// полноценный сканер и метаданные появятся на следующем шаге.
/// </summary>
public sealed class MediaItem
{
    public MediaItem(string path, string artist, string title, string folder)
    {
        Path = path;
        Artist = artist;
        Title = title;
        Folder = folder;
    }

    public string Path { get; }

    public string Artist { get; }

    public string Title { get; }

    public string Folder { get; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} — {Title}";

    public string Format => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();

    public string Subtitle => string.IsNullOrWhiteSpace(Folder) ? Format : $"{Folder} · {Format}";

    public override string ToString() => DisplayTitle;

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
        return new MediaItem(path, artist, title, folder);
    }
}
