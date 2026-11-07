using System.Collections.Generic;
using System.Linq;
using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class SearchServiceTests
{
    private static List<SearchService.Entry> Library() => SearchService.Build(new[]
    {
        new MediaItem(@"G:\Karaoke\Караоке\Алла Пугачёва - Свеча горела.mkv", "Алла Пугачёва", "Свеча горела", "Караоке"),
        new MediaItem(@"G:\Karaoke\Караоке\Алла Пугачёва - Старинные часы.mkv", "Алла Пугачёва", "Старинные часы", "Караоке"),
        new MediaItem(@"G:\Karaoke\Karaoke4U\2Маши - Босая.mkv", "2Маши", "Босая", "Karaoke4U"),
        new MediaItem(@"G:\Karaoke\Karaoke4U\Конь - Любэ.mkv", "Конь", "Любэ", "Karaoke4U"),
    });

    private static string[] Titles(IEnumerable<MediaItem> items) =>
        items.Select(item => item.DisplayTitle).ToArray();

    [Fact]
    public void FindsByInitialsWithPartialPrefixes()
    {
        var result = SearchService.Search(Library(), "алп све");

        Assert.Contains("Алла Пугачёва — Свеча горела", Titles(result));
    }

    [Fact]
    public void FindsByPureInitials()
    {
        var result = SearchService.Search(Library(), "ап сг");

        Assert.Contains("Алла Пугачёва — Свеча горела", Titles(result));
    }

    [Fact]
    public void FindsByLatinInitials()
    {
        var result = SearchService.Search(Library(), "ap sg");

        Assert.Contains("Алла Пугачёва — Свеча горела", Titles(result));
    }

    [Fact]
    public void IgnoresDashesAndExtraSpaces()
    {
        var result = SearchService.Search(Library(), "  алла   -  пугачёва   свеча  ");

        Assert.Contains("Алла Пугачёва — Свеча горела", Titles(result));
    }

    [Fact]
    public void FindsLatinTransliteration()
    {
        var result = SearchService.Search(Library(), "alla pugacheva svecha");

        Assert.Contains("Алла Пугачёва — Свеча горела", Titles(result));
    }

    [Fact]
    public void RanksExactTitleFirst()
    {
        var result = SearchService.Search(Library(), "свеча горела");

        Assert.Equal("Алла Пугачёва — Свеча горела", result.First().DisplayTitle);
    }

    [Fact]
    public void ReturnsNothingForUnknownQuery()
    {
        var result = SearchService.Search(Library(), "zzzzqqq");

        Assert.Empty(result);
    }

    [Fact]
    public void DoesNotMatchShortNoiseAsAbbreviation()
    {
        var result = SearchService.Search(Library(), "ъх");

        Assert.Empty(result);
    }

    [Fact]
    public void IgnoresTechnicalTagsAndKaraokeStopWords()
    {
        var library = SearchService.Build(new[]
        {
            new MediaItem(
                @"G:\Karaoke\01. Любэ - Конь (Karaoke 1080p).mkv",
                "Любэ",
                "Конь",
                "Karaoke"),
            new MediaItem(
                @"G:\Karaoke\Баста - Медлячок (minus).mkv",
                "Баста",
                "Медлячок",
                "Karaoke"),
        });

        Assert.Contains("Любэ — Конь", Titles(SearchService.Search(library, "любе конь караоке")));
        Assert.Contains("Баста — Медлячок", Titles(SearchService.Search(library, "баста медлячок минус")));
    }

    [Fact]
    public void ParsesLeadingTrackNumberAndTechnicalTags()
    {
        var item = MediaItem.FromPath(@"G:\Karaoke\01. Любэ - Конь (Karaoke 1080p).mkv");

        Assert.Equal("Любэ", item.Artist);
        Assert.Equal("Конь", item.Title);
    }

    [Fact]
    public void FindsTransposedNeighborLetters()
    {
        var library = SearchService.Build(new[]
        {
            new MediaItem(
                @"G:\Karaoke\Nirvana - Smells Like Teen Spirit.mkv",
                "Nirvana",
                "Smells Like Teen Spirit",
                "Karaoke"),
        });

        var result = SearchService.Search(library, "nirvnaa");

        Assert.Contains("Nirvana — Smells Like Teen Spirit", Titles(result));
    }

    [Theory]
    [InlineData("квин", "Queen — Bohemian Rhapsody")]
    [InlineData("модерн токинг", "Modern Talking — Cheri Cheri Lady")]
    [InlineData("металлика", "Metallica — Nothing Else Matters")]
    [InlineData("бони эм", "Boney M — Rasputin")]
    [InlineData("нирвана", "Nirvana — Smells Like Teen Spirit")]
    public void FindsForeignArtistsTypedInCyrillic(string query, string expected)
    {
        var library = SearchService.Build(new[]
        {
            new MediaItem(@"G:\Karaoke\Queen - Bohemian Rhapsody.mkv", "Queen", "Bohemian Rhapsody", "Karaoke"),
            new MediaItem(@"G:\Karaoke\Modern Talking - Cheri Cheri Lady.mkv", "Modern Talking", "Cheri Cheri Lady", "Karaoke"),
            new MediaItem(@"G:\Karaoke\Metallica - Nothing Else Matters.mkv", "Metallica", "Nothing Else Matters", "Karaoke"),
            new MediaItem(@"G:\Karaoke\Boney M - Rasputin.mkv", "Boney M", "Rasputin", "Karaoke"),
            new MediaItem(@"G:\Karaoke\Nirvana - Smells Like Teen Spirit.mkv", "Nirvana", "Smells Like Teen Spirit", "Karaoke"),
        });

        var result = SearchService.Search(library, query);

        Assert.Contains(expected, Titles(result));
    }

    [Fact]
    public void ExpandsKaraokeArtistAliases()
    {
        var library = SearchService.Build(new[]
        {
            new MediaItem(@"G:\Karaoke\Король и Шут - Лесник.mkv", "Король и Шут", "Лесник", "Karaoke"),
            new MediaItem(@"G:\Karaoke\2Маши - Босая.mkv", "2Маши", "Босая", "Karaoke"),
            new MediaItem(@"G:\Karaoke\Би-2 - Полковнику никто не пишет.mkv", "Би-2", "Полковнику никто не пишет", "Karaoke"),
        });

        Assert.Contains("Король и Шут — Лесник", Titles(SearchService.Search(library, "киш лесник")));
        Assert.Contains("2Маши — Босая", Titles(SearchService.Search(library, "две маши босая")));
        Assert.Contains(
            "Би-2 — Полковнику никто не пишет",
            Titles(SearchService.Search(library, "би2")));
    }

    [Fact]
    public void FallsBackToNMinusOneForLongQueries()
    {
        var library = SearchService.Build(new[]
        {
            new MediaItem(@"G:\Karaoke\Баста - Медлячок.mkv", "Баста", "Медлячок", "Karaoke"),
        });

        var result = SearchService.Search(library, "баста медлячок лишнее");

        Assert.Contains("Баста — Медлячок", Titles(result));
    }

    [Fact]
    public void UsesFavoriteAndPlayCountOnlyAsTieBreakers()
    {
        var favorite = new MediaItem(@"G:\Karaoke\Лепс - Любимый хит.mkv", "Лепс", "Любимый хит", "Karaoke")
        {
            IsFavorite = true,
        };
        var popular = new MediaItem(@"G:\Karaoke\Лепс - Популярный хит.mkv", "Лепс", "Популярный хит", "Karaoke")
        {
            PlayCount = 5,
        };
        var plain = new MediaItem(@"G:\Karaoke\Лепс - Обычный хит.mkv", "Лепс", "Обычный хит", "Karaoke");

        var result = SearchService.Search(SearchService.Build(new[] { plain, popular, favorite }), "лепс");

        Assert.Equal("Лепс — Любимый хит", result[0].DisplayTitle);
    }
}
