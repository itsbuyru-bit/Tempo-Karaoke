using System;
using System.Collections.ObjectModel;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Queue mutations independent of WinUI. MainPage owns UI state, while this
/// engine owns ordering and current-index invariants so they can be tested.
/// </summary>
public sealed class QueueEngine
{
    public ObservableCollection<MediaItem> Items { get; } = new();

    public int CurrentIndex { get; private set; } = -1;

    public MediaItem? Current =>
        CurrentIndex >= 0 && CurrentIndex < Items.Count ? Items[CurrentIndex] : null;

    public int Count => Items.Count;

    public MediaItem this[int index] => Items[index];

    public int IndexOf(MediaItem item) => Items.IndexOf(item);

    public void Add(MediaItem item) => Items.Add(item);

    public void Insert(int index, MediaItem item)
    {
        Items.Insert(Math.Clamp(index, 0, Items.Count), item);
        if (CurrentIndex >= index)
        {
            CurrentIndex++;
        }
    }

    public bool Remove(MediaItem item)
    {
        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            return;
        }

        Items.RemoveAt(index);
        if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (index == CurrentIndex)
        {
            CurrentIndex = Items.Count == 0
                ? -1
                : Math.Min(CurrentIndex, Items.Count - 1);
        }
    }

    public void Clear()
    {
        Items.Clear();
        CurrentIndex = -1;
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Items.Count || to < 0 || to >= Items.Count || from == to)
        {
            return;
        }

        var current = Current;
        Items.Move(from, to);
        if (current is not null)
        {
            CurrentIndex = Items.IndexOf(current);
        }
    }

    public bool SetCurrent(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            return false;
        }

        CurrentIndex = index;
        return true;
    }

    public bool SetCurrent(MediaItem item)
    {
        var index = Items.IndexOf(item);
        return index >= 0 && SetCurrent(index);
    }

    public void ResetCurrent() => CurrentIndex = -1;
}
