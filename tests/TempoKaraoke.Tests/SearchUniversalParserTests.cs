using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class SearchUniversalParserTests
{
    [Theory]
    [InlineData("Любэ - Конь (минус).mkv", "Любэ", "Конь")]
    [InlineData("Конь - Любэ [1080p].mp4", "Любэ", "Конь")]
    [InlineData("01. Любэ - Конь (караоке).avi", "Любэ", "Конь")]
    [InlineData("Любэ - 01 - Конь.mkv", "Любэ", "Конь")]
    [InlineData("Конь.mkv", "", "Конь")]
    [InlineData("Любэ Конь.mp4", "", "Любэ Конь")]
    [InlineData("Любэ feat. Растеряев - Конь.mkv", "Любэ feat. Растеряев", "Конь")]
    [InlineData("Queen - Bohemian Rhapsody (Karaoke Version).mkv", "Queen", "Bohemian Rhapsody")]
    [InlineData("bohemian rhapsody - queen.mkv", "queen", "bohemian rhapsody")]
    [InlineData("Бони М - Райская жизнь (бэк-вокал).avi", "Бони М", "Райская жизнь")]
    [InlineData("Boney M - Rasputin [Minus] [720p].mp4", "Boney M", "Rasputin")]
    [InlineData("киш прыгну со скалы минусовка 1080p.mkv", "", "киш прыгну со скалы")]
    [InlineData("03 - Пугачёва - Миллион алых роз (1998).mp4", "Пугачёва", "Миллион алых роз")]
    [InlineData("Земфира - Аривидерчи (Live).mkv", "Земфира", "Аривидерчи")]
    [InlineData("Меладзе - Сэра (дуэт).avi", "Меладзе", "Сэра")]
    [InlineData("Валерий Меладзе & Григорий Лепс - Две души.mkv", "Валерий Меладзе & Григорий Лепс", "Две души")]
    public void ParsesArbitraryRealWorldNames(string fileName, string artist, string title)
    {
        var parsed = MediaNameParser.Parse(fileName);

        Assert.Equal(artist, parsed.Artist);
        Assert.Equal(title, parsed.Title);
    }

    [Theory]
    [InlineData("Любэ - Конь.mkv", "любэ конь")]
    [InlineData("Конь - Любэ.mkv", "любэ конь")]
    [InlineData("Любэ Конь.mkv", "любэ")]
    [InlineData("Любэ Конь.mkv", "конь")]
    [InlineData("киш прыгну со скалы минусовка 1080p.mkv", "прыгну скалы")]
    [InlineData("Валерий Меладзе & Григорий Лепс - Две души.mkv", "лепс")]
    [InlineData("Валерий Меладзе & Григорий Лепс - Две души.mkv", "две души")]
    public void FindsEveryUsefulWordInFileName(string fileName, string query)
    {
        var item = new MediaItem($@"G:\Karaoke\{fileName}", "", "", "Karaoke");
        var index = SearchService.Build(new[] { item });

        Assert.NotEmpty(SearchService.Search(index, query));
    }
}
