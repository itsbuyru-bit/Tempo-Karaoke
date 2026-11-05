using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class NameKeyTests
{
    [Theory]
    [InlineData("Ramil'", "Ramil’")]
    [InlineData("Роти", "Роти")]
    [InlineData("ЛЮБЭ", "Любэ")]
    [InlineData("Ёлка", "Елка")]
    [InlineData("ARTIK & ASTI", "Artik & Asti")]
    [InlineData("#2Маши", "2Маши")]
    [InlineData("A'STUDIO", "A-Studio")]
    [InlineData("Егор Крид & MOLLY", "Егор Крид и MOLLY")]
    [InlineData("Егор Крид feat. MOLLY", "Егор Крид и MOLLY")]
    [InlineData("Алла Пугачёва", "Алла Пугачева")]
    [InlineData("HammAli & Navai", "HammAli _ Navai")]
    public void SameArtistGivesSameKey(string left, string right)
    {
        Assert.Equal(NameKey.Artist(left), NameKey.Artist(right));
    }

    [Theory]
    [InlineData("Баста", "Баста (Ноггано)")]
    public void DifferentArtistsKeepDifferentKeys(string left, string right)
    {
        Assert.NotEqual(NameKey.Artist(left), NameKey.Artist(right));
    }

    [Fact]
    public void SongKeyDependsOnVersionAndOrdinal()
    {
        var original = NameKey.ForSong("матис", "босая", VersionKind.Original, 1);
        var cover = NameKey.ForSong("матис", "босая", VersionKind.Cover, 1);
        var second = NameKey.ForSong("матис", "босая", VersionKind.Original, 2);

        Assert.NotEqual(original, cover);
        Assert.NotEqual(original, second);
    }

    [Fact]
    public void DisplayKeyIgnoresVersion()
    {
        var a = NameKey.ForDisplay("jony", "комета");
        var b = NameKey.ForDisplay("jony", "комета");

        Assert.Equal(a, b);
    }
}

public sealed class QualityClassTests
{
    [Theory]
    [InlineData(3840, 2160, QualityClass.Uhd4K)]
    [InlineData(1920, 1080, QualityClass.Hd1080)]
    [InlineData(1280, 720, QualityClass.Hd720)]
    [InlineData(854, 480, QualityClass.Sd)]
    [InlineData(640, 360, QualityClass.Low)]
    [InlineData(0, 0, QualityClass.Unknown)]
    // Вертикальное видео: 1080×1920 должно остаться 1080p
    [InlineData(1080, 1920, QualityClass.Hd1080)]
    public void ClassifiesByShortSide(int width, int height, QualityClass expected)
    {
        Assert.Equal(expected, QualityClassExtensions.FromDimensions(width, height));
    }

    [Theory]
    [InlineData(QualityClass.Uhd4K, null)]
    [InlineData(QualityClass.Hd1080, null)]
    [InlineData(QualityClass.Hd720, "[720p]")]
    [InlineData(QualityClass.Sd, "[480p]")]
    public void TagsOnlyBelowFullHd(QualityClass quality, string? expected)
    {
        Assert.Equal(expected, quality.FileTag());
    }
}

public sealed class MediaNameComposerTests
{
    [Fact]
    public void BuildsStandardName()
    {
        var parsed = MediaNameParser.Parse("2Маши   Босая   Караоке (Кавер минус).mkv", "Karaoke4U");
        var name = MediaNameComposer.BuildFileName(parsed, QualityClass.Hd1080, ".MKV");

        Assert.Equal("2Маши - Босая (Кавер).mkv", name);
    }

    [Fact]
    public void AddsQualityTagOnlyForLowQuality()
    {
        var parsed = MediaNameParser.Parse("10AGE - Нету интереса.mp4", "karaoke_archive");

        Assert.Equal("10AGE - Нету интереса [720p].mp4", MediaNameComposer.BuildFileName(parsed, QualityClass.Hd720, ".mp4"));
        Assert.Equal("10AGE - Нету интереса.mp4", MediaNameComposer.BuildFileName(parsed, QualityClass.Uhd4K, ".mp4"));
    }

    [Fact]
    public void AddsOrdinalSuffixAfterVersion()
    {
        var parsed = MediaNameParser.Parse("Егор Крид - Что они знают (Караоке) (2).mp4", "Калинка Караоке");

        Assert.Equal("Егор Крид - Что они знают (v2) [720p].mp4",
            MediaNameComposer.BuildFileName(parsed, QualityClass.Hd720, ".mp4"));
    }

    [Fact]
    public void FilmSongGoesToFilmFolder()
    {
        var parsed = MediaNameParser.Parse("Буратино из фильма _Приключения Буратино_.mp4", "karaoke_archive");

        Assert.Equal("_Из фильмов", MediaNameComposer.BuildFolder(parsed));
    }

    [Fact]
    public void UnknownArtistGoesToUnknownFolder()
    {
        var parsed = MediaNameParser.Parse("Гимн СССР (Караоке).mkv", "Калинка Караоке");

        Assert.Equal("_Без исполнителя", MediaNameComposer.BuildFolder(parsed));
    }

    [Theory]
    [InlineData("AC/DC", "AC DC")]
    [InlineData("Что?: да", "Что да")]
    [InlineData("  Точка.  ", "Точка")]
    public void SanitizesInvalidCharacters(string source, string expected)
    {
        Assert.Equal(expected, MediaNameComposer.Sanitize(source));
    }

    [Fact]
    public void KeepsHashInArtistFolder()
    {
        Assert.Equal("#2Маши", MediaNameComposer.BuildArtistFolder("#2Маши"));
    }

    [Fact]
    public void BuildsPathInsideCatalog()
    {
        var parsed = MediaNameParser.Parse("Асия - Как ты там (Караоке).mkv", "Калинка Караоке");
        var path = MediaNameComposer.BuildTargetPath(@"G:\Karaoke\Каталог", parsed, QualityClass.Uhd4K, ".mkv");

        Assert.Equal(@"G:\Karaoke\Каталог\Асия\Асия - Как ты там.mkv", path);
    }
}
