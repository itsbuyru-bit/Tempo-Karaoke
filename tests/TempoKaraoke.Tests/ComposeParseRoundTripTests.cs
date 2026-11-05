using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

/// <summary>
/// Круговой тест «собрали имя → разобрали обратно». Ловит главный риск формата:
/// служебный суффикс, который прилипает к названию песни вместо того, чтобы
/// вернуться в структуру. Именно так [720p] превратился бы в часть Title и
/// сломал бы и поиск, и повторный прогон миграции.
/// </summary>
public sealed class ComposeParseRoundTripTests
{
    [Fact]
    public void QualityTagDoesNotLeakIntoTitle()
    {
        var parsed = MediaNameParser.Parse("2Маши   Босая   Кавер минус.mkv", "Karaoke4U");
        var composed = MediaNameComposer.BuildFileName(parsed, QualityClass.Hd720, ".mkv");

        Assert.Equal("2Маши - Босая (Кавер) [720p].mkv", composed);

        var reparsed = MediaNameParser.Parse(composed, "2Маши");

        Assert.Equal("Босая", reparsed.Title);
        Assert.DoesNotContain("720", reparsed.Title, StringComparison.Ordinal);
        Assert.Equal(VersionKind.Cover, reparsed.Version);
        Assert.Equal(parsed.SongKey, reparsed.SongKey);
    }

    [Fact]
    public void QualityTagRoundTripsToSameName()
    {
        var parsed = MediaNameParser.Parse("10AGE - Нету интереса.mp4", "karaoke_archive");
        var first = MediaNameComposer.BuildFileName(parsed, QualityClass.Hd720, ".mp4");
        var second = MediaNameComposer.BuildFileName(MediaNameParser.Parse(first, "10AGE"), QualityClass.Hd720, ".mp4");

        // Второй прогон обязан дать ровно то же имя: иначе миграция не идемпотентна.
        Assert.Equal(first, second);
        Assert.Equal("10AGE - Нету интереса [720p].mp4", first);
    }

    [Theory]
    [InlineData(QualityClass.Uhd4K)]
    [InlineData(QualityClass.Hd1080)]
    public void FullHdAndAboveGetNoTag(QualityClass quality)
    {
        var parsed = MediaNameParser.Parse("Асия - Как ты там (Караоке).mkv", "Калинка Караоке");
        var composed = MediaNameComposer.BuildFileName(parsed, quality, ".mkv");

        Assert.Equal("Асия - Как ты там.mkv", composed);
        Assert.Equal("Как ты там", MediaNameParser.Parse(composed, "Асия").Title);
    }

    [Fact]
    public void BackingVocalRoundTripsAsVersion()
    {
        var parsed = MediaNameParser.Parse("Ленинград - В Питере — пить (Караоке _ Бэк).mkv", "Калинка Караоке");

        Assert.Equal(VersionKind.BackingVocal, parsed.Version);
        Assert.Equal("В Питере — пить", parsed.Title);
        Assert.DoesNotContain("Бэк", parsed.Title, StringComparison.Ordinal);

        var composed = MediaNameComposer.BuildFileName(parsed, QualityClass.Uhd4K, ".mkv");
        Assert.Equal("Ленинград - В Питере — пить (Бэк).mkv", composed);

        var reparsed = MediaNameParser.Parse(composed, "Ленинград");
        Assert.Equal(VersionKind.BackingVocal, reparsed.Version);
        Assert.Equal("В Питере — пить", reparsed.Title);
        Assert.Equal(parsed.SongKey, reparsed.SongKey);
    }

    [Fact]
    public void BackingVocalStaysSeparateFromPlainMinus()
    {
        var plain = MediaNameParser.Parse("Ленинград - В Питере — пить (Караоке).mkv", "Калинка Караоке");
        var backing = MediaNameParser.Parse("Ленинград - В Питере — пить (Караоке _ Бэк).mkv", "Калинка Караоке");

        Assert.NotEqual(plain.SongKey, backing.SongKey);
    }

    [Fact]
    public void FilmSongSurvivesComposeParseCycle()
    {
        var parsed = MediaNameParser.Parse("Буратино из фильма _Приключения Буратино_.mp4", "karaoke_archive");
        var composed = MediaNameComposer.BuildFileName(parsed, QualityClass.Hd720, ".mp4");
        var reparsed = MediaNameParser.Parse(composed, MediaNameComposer.BuildFolder(parsed));

        Assert.Equal("Буратино (из фильма Приключения Буратино) [720p].mp4", composed);
        Assert.Equal(parsed.Title, reparsed.Title);
        Assert.Equal(parsed.FilmSource, reparsed.FilmSource);
        Assert.Equal("_Из фильмов", MediaNameComposer.BuildFolder(reparsed));
    }
}
