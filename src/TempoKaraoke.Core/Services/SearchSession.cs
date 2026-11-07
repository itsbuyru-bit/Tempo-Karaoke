using System;
using System.Collections.Generic;
using System.Linq;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// App-independent search state: owns the built index and returns bounded pages.
/// Keeping this out of MainPage makes search behavior testable and prevents
/// rebuilding the index for every keystroke.
/// </summary>
public sealed class SearchSession
{
    private const int CacheLimit = 256;

    private readonly object _sync = new();
    private readonly Dictionary<string, SearchPage> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _cacheOrder = new();
    private List<SearchService.Entry> _index = new();
    private int _indexVersion;
    private int _activityVersion;

    public string Query { get; private set; } = string.Empty;

    public int IndexedCount => _index.Count;

    public void SetItems(IEnumerable<MediaItem> items)
    {
        lock (_sync)
        {
            _index = SearchService.Build(items);
            _indexVersion++;
            Query = string.Empty;
            ClearCache();
        }
    }

    /// <summary>Called after a favorite/play-count change so ranking is not stale.</summary>
    public void InvalidateActivityCache()
    {
        lock (_sync)
        {
            _activityVersion++;
            ClearCache();
        }
    }

    /// <summary>Point update for a changed file; a full Build remains the fallback.</summary>
    public void UpdateEntry(MediaItem item)
    {
        lock (_sync)
        {
            var index = _index.FindIndex(entry =>
                string.Equals(entry.Item.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _index[index] = new SearchService.Entry(item);
            }
            else
            {
                _index.Add(new SearchService.Entry(item));
            }

            _indexVersion++;
            ClearCache();
        }
    }

    public void RemoveEntry(string path)
    {
        lock (_sync)
        {
            _index.RemoveAll(entry =>
                string.Equals(entry.Item.Path, path, StringComparison.OrdinalIgnoreCase));
            _indexVersion++;
            ClearCache();
        }
    }

    public SearchPage Search(
        string? query,
        int limit = 8,
        SearchFilter? filter = null,
        bool groupResults = false)
    {
        Query = query?.Trim() ?? string.Empty;
        if (Query.Length == 0)
        {
            return new SearchPage(Query, Array.Empty<MediaItem>(), 0);
        }

        var key = BuildCacheKey(Query, filter, groupResults);
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                TouchCache(key);
                return cached;
            }
        }

        List<SearchService.Entry> index;
        lock (_sync)
        {
            index = _index;
        }

        var all = SearchService.Search(index, Query);
        if (filter is not null)
        {
            all = all.Where(filter.Matches).ToList();
        }

        if (groupResults)
        {
            all = MediaGroupingService.Group(all)
                .Select(group => group.Best)
                .ToList();
        }

        var visible = limit <= 0
            ? all
            : all.GetRange(0, Math.Min(limit, all.Count));
        var page = new SearchPage(
            Query,
            visible,
            all.Count,
            SearchService.IsStopWordOnlyQuery(Query) && all.Count == 0);

        lock (_sync)
        {
            _cache[key] = page;
            _cacheOrder.AddLast(key);
            while (_cacheOrder.Count > CacheLimit)
            {
                var oldest = _cacheOrder.First!.Value;
                _cacheOrder.RemoveFirst();
                _cache.Remove(oldest);
            }
        }

        return page;
    }

    public IReadOnlyList<MediaItem> SearchAll(string? query)
    {
        var page = Search(query, limit: 0);
        return page.Items;
    }

    /// <summary>Prefix suggestions from indexed artist/title tokens.</summary>
    public IReadOnlyList<string> Suggest(string? prefix, int limit = 6)
    {
        var normalized = SearchService.Normalize(prefix);
        if (normalized.Length < 2 || limit <= 0)
        {
            return Array.Empty<string>();
        }

        lock (_sync)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in _index)
            {
                foreach (var token in entry.ArtistTokens.Concat(entry.TitleTokens))
                {
                    if (!token.StartsWith(normalized, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    counts[token] = counts.TryGetValue(token, out var count) ? count + 1 : 1;
                }
            }

            return counts
                .OrderByDescending(pair => pair.Value)
                .ThenByDescending(pair => pair.Key.Length)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(limit)
                .Select(pair => pair.Key)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            Query = string.Empty;
            ClearCache();
        }
    }

    private string BuildCacheKey(string query, SearchFilter? filter, bool groupResults) =>
        $"{SearchService.Normalize(query)}|{filter?.ToString() ?? string.Empty}|{groupResults}|{_indexVersion}|{_activityVersion}";

    private void TouchCache(string key)
    {
        _cacheOrder.Remove(key);
        _cacheOrder.AddLast(key);
    }

    private void ClearCache()
    {
        _cache.Clear();
        _cacheOrder.Clear();
    }
}

public sealed record SearchPage(
    string Query,
    IReadOnlyList<MediaItem> Items,
    int TotalCount,
    bool NeedsClarification = false);
