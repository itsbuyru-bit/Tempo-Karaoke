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
    private List<SearchService.Entry> _index = new();

    public string Query { get; private set; } = string.Empty;

    public int IndexedCount => _index.Count;

    public void SetItems(IEnumerable<MediaItem> items)
    {
        _index = SearchService.Build(items);
        Query = string.Empty;
    }

    public SearchPage Search(string? query, int limit = 8, SearchFilter? filter = null)
    {
        Query = query?.Trim() ?? string.Empty;
        if (Query.Length == 0)
        {
            return new SearchPage(Query, Array.Empty<MediaItem>(), 0);
        }

        var all = SearchService.Search(_index, Query);
        if (filter is not null)
        {
            all = all.Where(filter.Matches).ToList();
        }

        var visible = limit <= 0
            ? all
            : all.GetRange(0, Math.Min(limit, all.Count));
        return new SearchPage(Query, visible, all.Count);
    }

    public IReadOnlyList<MediaItem> SearchAll(string? query)
    {
        var page = Search(query, limit: 0);
        return page.Items;
    }

    public void Clear() => Query = string.Empty;
}

public sealed record SearchPage(
    string Query,
    IReadOnlyList<MediaItem> Items,
    int TotalCount);
