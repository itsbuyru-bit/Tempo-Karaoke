using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace TempoKaraoke.Models;

/// <summary>
/// Именованный список воспроизведения. Хранит пути к видео; добавление песен
/// и экспорт M3U развиваются отдельными шагами.
/// </summary>
public sealed class Playlist : INotifyPropertyChanged
{
    private List<string> _items = new();
    private string _name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set
        {
            if (string.Equals(_name, value, StringComparison.Ordinal))
            {
                return;
            }

            _name = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<string> Items
    {
        get => _items;
        set
        {
            _items = value ?? new List<string>();
        }
    }

    [JsonIgnore]
    public string CountText
    {
        get
        {
            var english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
            if (_items.Count == 0)
            {
                return english ? "Empty playlist" : "Пустой список";
            }

            return english
                ? (_items.Count == 1 ? "1 video" : $"{_items.Count} videos")
                : $"{_items.Count} видео";
        }
    }

    public bool AddItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            _items.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _items.Add(path);
        NotifyCountChanged();
        return true;
    }

    public bool RemoveItem(string path)
    {
        var removed = _items.RemoveAll(item =>
            string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return false;
        }

        NotifyCountChanged();
        return true;
    }

    public void ReplaceItems(IEnumerable<string> paths)
    {
        _items = paths.ToList();
        NotifyCountChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
    }

    private void NotifyCountChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
    }
}
