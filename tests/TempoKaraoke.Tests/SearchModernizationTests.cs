using System;
using System.IO;
using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class SearchModernizationTests
{
    private static MediaItem Item(string title, string artist, DateTime? modified = null, bool favorite = false, int plays = 0) =>
        new($@"G:\Karaoke\{artist} - {title}.mkv", artist, title, "Karaoke", modified ?? DateTime.UtcNow)
        {
            IsFavorite = favorite,
            PlayCount = plays,
        };

    [Fact]
    public void ParsesArtistInTrailingBrackets()
    {
        var parsed = MediaNameParser.Parse("Конь (Любэ).mkv");
        Assert.Equal("Любэ", parsed.Artist);
        Assert.Equal("Конь", parsed.Title);
    }

    [Fact]
    public void RecencyDoesNotBeatFavorite()
    {
        var oldFavorite = Item("Хит", "Лепс", DateTime.UtcNow.AddDays(-60), favorite: true);
        var fresh = Item("Новый", "Лепс", DateTime.UtcNow);
        var old = Item("Старый", "Лепс", DateTime.UtcNow.AddDays(-60));
        var result = SearchService.Search(SearchService.Build(new[] { old, fresh, oldFavorite }), "лепс");
        Assert.Equal("Лепс — Хит", result[0].DisplayTitle);
        Assert.True(result.IndexOf(fresh) < result.IndexOf(old));
    }

    [Fact]
    public void SuggestReturnsArtistPrefix()
    {
        var session = new SearchSession();
        session.SetItems(new[] { Item("Хит", "Лепс"), Item("Другая", "Ленинград") });
        Assert.Contains("лепс", session.Suggest("леп"));
    }

    [Fact]
    public void ExternalAliasesAreLoaded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tempo-aliases-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"groups\":[[\"тестовый алиас\",\"tsa\"]]}");
        try
        {
            SearchService.ReloadAliases(path);
            var item = Item("Хит", "Тестовый алиас");
            var result = SearchService.Search(SearchService.Build(new[] { item }), "tsa");
            Assert.NotEmpty(result);
        }
        finally
        {
            SearchService.ReloadAliases();
            File.Delete(path);
        }
    }

    [Fact]
    public void ArtistIntentSortsByPopularity()
    {
        var items = new[]
        {
            Item("Хит 1", "Григорий Лепс", plays: 1),
            Item("Хит 2", "Григорий Лепс", plays: 5),
            Item("Хит 3", "Григорий Лепс", favorite: true),
            Item("Хит 4", "Григорий Лепс"),
        };

        var result = SearchService.Search(SearchService.Build(items), "лепс");

        Assert.Equal(4, result.Count);
        Assert.Equal("Григорий Лепс — Хит 3", result[0].DisplayTitle);
    }

    [Fact]
    public void DiversityCapLimitsConsecutiveArtistTracks()
    {
        var items = new List<MediaItem>();
        for (var i = 0; i < 5; i++)
        {
            items.Add(Item($"Любовь {i}", "Артист А"));
        }

        for (var i = 0; i < 2; i++)
        {
            items.Add(Item($"Любовь {i}", "Артист Б"));
        }

        var result = SearchService.Search(SearchService.Build(items), "любовь");
        var maxRun = 0;
        var run = 0;
        var last = string.Empty;
        foreach (var item in result)
        {
            if (string.Equals(item.Artist, last, StringComparison.Ordinal))
            {
                run++;
            }
            else
            {
                last = item.Artist;
                run = 1;
            }

            maxRun = Math.Max(maxRun, run);
        }

        Assert.True(maxRun <= 3);
    }

    [Fact]
    public void LoadsCustomScoringOptionsFromFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tempo-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"titleExact\":1234,\"fuzzyLongErrors\":3}");
        try
        {
            SearchService.ReloadScoringOptions(path);
            Assert.Equal(1234, SearchService.Options.TitleExact);
            Assert.Equal(3, SearchService.Options.FuzzyLongErrors);
        }
        finally
        {
            SearchService.ReloadScoringOptions();
            File.Delete(path);
        }
    }

    [Fact]
    public void FallsBackToDefaultsForBrokenOrMissingConfig()
    {
        var broken = Path.Combine(Path.GetTempPath(), $"tempo-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(broken, "{not-json");
        try
        {
            SearchService.ReloadScoringOptions(broken);
            Assert.Equal(SearchScoringOptions.Default.TitleExact, SearchService.Options.TitleExact);

            SearchService.ReloadScoringOptions(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
            Assert.Equal(SearchScoringOptions.Default.FavoriteBonus, SearchService.Options.FavoriteBonus);
        }
        finally
        {
            SearchService.ReloadScoringOptions();
            File.Delete(broken);
        }
    }
}
