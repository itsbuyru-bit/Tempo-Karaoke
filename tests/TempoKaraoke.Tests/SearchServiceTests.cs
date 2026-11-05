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
}
