using System;
using TempoKaraoke.Services;

namespace TempoKaraoke.Models;

/// <summary>One quick-search row with the query needed for inline highlighting.</summary>
public sealed class QuickSearchResult
{
    public QuickSearchResult(MediaItem item, string query, int versionCount = 1)
    {
        Item = item;
        Query = query;
        VersionCount = versionCount;
    }

    public MediaItem Item { get; }

    public string Query { get; }

    public int VersionCount { get; }

    public string DisplayTitle => Item.DisplayTitle;

    public string Folder => Item.Folder;

    public string DurationText => Item.DurationText;

    public string VersionsText => VersionCount > 1
        ? LocalizationService.Format("QuickSearch_Versions", VersionCount)
        : string.Empty;

    public bool HasMultipleVersions => VersionCount > 1;
}
