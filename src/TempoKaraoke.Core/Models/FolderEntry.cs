using System;
using System.Globalization;
using TempoKaraoke.Text;

namespace TempoKaraoke.Models;

/// <summary>Элемент сетки раздела «Папки с видео»: вложенная папка или видеофайл.</summary>
public sealed class FolderEntry
{
    public FolderEntry(string name, string path, bool isFolder, int count, MediaItem? item = null)
    {
        Name = name;
        Path = path;
        IsFolder = isFolder;
        Count = count;
        Item = item;
    }

    public string Name { get; }

    public string Path { get; }

    public bool IsFolder { get; }

    public bool IsVideo => !IsFolder;

    public int Count { get; }

    public MediaItem? Item { get; }

    public object? Thumbnail { get; init; }

    public string Title => IsFolder ? Name : Item?.DisplayTitle ?? Name;

    public string CountText => IsFolder
        ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
            ? (Count == 1 ? "1 item" : $"{Count} items")
            : RussianPlural.Format(Count, "элемент", "элемента", "элементов"))
        : DurationCaption;

    private string DurationCaption
    {
        get
        {
            var milliseconds = Item?.DurationMs ?? 0;
            if (milliseconds <= 0)
            {
                return string.Empty;
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
}
