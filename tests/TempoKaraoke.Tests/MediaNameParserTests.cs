using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class MediaNameParserTests
{
    [Theory]
    // Калинка Караоке: «Арт - Песня (Караоке)»
    [InlineData("#2Маши - Босая (Караоке).mkv", "", "#2Маши", "Босая")]
    // karaoke_archive: «Арт - Песня»
    [InlineData("10AGE - Нету интереса.mp4", "", "10AGE", "Нету интереса")]
    // Karaoke4U: разделитель — три пробела
    [InlineData("2Маши   Босая   Караоке (Кавер минус).mkv", "", "2Маши", "Босая")]
    // Karaoke online: обратный порядок, папка переворачивает
    [InlineData("Босая - 2Маши (Karaoke version).mkv", "Karaoke online", "2Маши", "Босая")]
    // Karaoke4U: ведущая пометка «КАРАОКЕ»
    [InlineData("КАРАОКЕ   ANIVAR   Капля кислорода (Кавер минус).mkv", "Karaoke4U", "ANIVAR", "Капля кислорода")]
    // Калинка Live: мусорный хвост из пометок
    [InlineData(
        "JONY - Комета [ ПОЙ ПОД БИТ ] КАВЕР МИНУС   ТЕКСТ ПЕСНИ   КАРАОКЕ.mkv",
        "Калинка Live",
        "JONY",
        "Комета")]
    // Дефис без пробела перед ним
    [InlineData("TERNOVOY- Меркурий.mp4", "", "TERNOVOY", "Меркурий")]
    // Двойной дефис как разделитель
    [InlineData("ANNA ASTI и Филипп Киркоров -- Хобби.mp4", "", "ANNA ASTI и Филипп Киркоров", "Хобби")]
    // Дефис внутри имени артиста не должен разрывать строку
    [InlineData("Hi-Fi - Беспризорник.mp4", "", "Hi-Fi", "Беспризорник")]
    // Тег качества в имени — не часть названия
    [InlineData("50 Cent - In Da Club 4K.mp4", "", "50 Cent", "In Da Club")]
    // Подчёркивания вместо пробелов и амперсанда
    [InlineData("Hi Fi - Беспризорник.mp4", "", "Hi Fi", "Беспризорник")]
    public void SplitsArtistAndTitle(string fileName, string folder, string artist, string title)
    {
        var parsed = MediaNameParser.Parse(fileName, folder);

        Assert.Equal(artist, parsed.Artist);
        Assert.Equal(title, parsed.Title);
    }

    [Theory]
    [InlineData("Mariah Carey - All I Want For Christmas Is You (Караоке на русском).mkv", VersionKind.Russian)]
    [InlineData("Mariah Carey - All I Want For Christmas Is You (Караоке Транскрипция).mkv", VersionKind.Transcription)]
    [InlineData("ARTIK & ASTI - Кто я тебе (Караоке Ремикс).mkv", VersionKind.Remix)]
    [InlineData("2Маши   Босая   Кавер минус.mkv", VersionKind.Cover)]
    [InlineData("#2Маши - Босая (Караоке).mkv", VersionKind.Original)]
    public void DetectsVersionKind(string fileName, VersionKind expected)
    {
        Assert.Equal(expected, MediaNameParser.Parse(fileName, "Калинка Караоке").Version);
    }

    [Fact]
    public void FolderDrivesLiveAndExtremeVersions()
    {
        Assert.Equal(
            VersionKind.Live,
            MediaNameParser.Parse("Асия - Как ты там (Караоке).mkv", "Калинка Live").Version);

        Assert.Equal(
            VersionKind.Extreme,
            MediaNameParser.Parse("Lx24   Не было печали   ЭКСТРЕМАЛЬНОЕ КАРАОКЕ.mkv", "Karaoke4U Экстремальное").Version);
    }

    [Fact]
    public void RussianAndTranscriptionStayDifferentSongs()
    {
        var russian = MediaNameParser.Parse(
            "Mariah Carey - All I Want For Christmas Is You (Караоке на русском).mkv",
            "Калинка Караоке");
        var transcription = MediaNameParser.Parse(
            "Mariah Carey - All I Want For Christmas Is You (Караоке Транскрипция).mkv",
            "Калинка Караоке");

        Assert.Equal(russian.Title, transcription.Title);
        Assert.NotEqual(russian.SongKey, transcription.SongKey);
    }

    [Fact]
    public void FilmSongKeepsSourceInTitleForSearch()
    {
        var parsed = MediaNameParser.Parse("Буратино из фильма _Приключения Буратино_.mp4", "karaoke_archive");

        Assert.Empty(parsed.Artist);
        Assert.Equal("Приключения Буратино", parsed.FilmSource);
        Assert.Equal("Буратино (из фильма Приключения Буратино)", parsed.Title);
    }

    [Fact]
    public void FilmSongsWithBrokenQuotesShareOneKey()
    {
        var quoted = MediaNameParser.Parse("Три белых коня из фильма _Чародеи_.mp4", "karaoke_archive");
        var plain = MediaNameParser.Parse("Три белых коня из кинофильма Чародеи.mp4", "karaoke_archive");

        Assert.Equal(quoted.TitleKey, plain.TitleKey);
        Assert.Equal(quoted.SongKey, plain.SongKey);
    }

    [Fact]
    public void KeepsTrailingNumberWhenItIsPartOfTheTitle()
    {
        // «№ 5» — часть названия, а не номер версии.
        var parsed = MediaNameParser.Parse("ВиаГра - Попытка № 5.mp4", "karaoke_archive");

        Assert.Equal(1, parsed.Ordinal);
        Assert.Equal("Попытка № 5", parsed.Title);
    }

    [Fact]
    public void ExtractsOrdinalFromParenthesizedSuffix()
    {
        var parsed = MediaNameParser.Parse("Егор Крид - Что они знают (Караоке) (2).mp4", "Калинка Караоке");

        Assert.Equal(2, parsed.Ordinal);
        Assert.Equal("Что они знают", parsed.Title);
    }

    [Theory]
    [InlineData("ЛАРИН - ТЕКСТ ПЕСНИ.mp4")]
    [InlineData("Пьяная любовь  Дима Билан и Полина   Кавер минус.mkv")]
    public void FlagsUnparseableNames(string fileName)
    {
        Assert.True(MediaNameParser.Parse(fileName, "karaoke_archive").NeedsAttention);
    }

    [Theory]
    [InlineData("Karaoke.online.mkv")]
    [InlineData("Гимн СССР (Караоке).mkv")]
    public void OrphansAreNotFlaggedButHaveNoArtist(string fileName)
    {
        // Сирота — не ошибка разбора: у него просто нет исполнителя, и он едет
        // в отдельную папку каталога.
        var parsed = MediaNameParser.Parse(fileName, "karaoke_archive");

        Assert.Empty(parsed.Artist);
        Assert.False(parsed.NeedsAttention);
        Assert.Equal("_Без исполнителя", MediaNameComposer.BuildFolder(parsed));
    }

    [Fact]
    public void ReturnsEmptyForBlankName()
    {
        Assert.Equal(string.Empty, MediaNameParser.Parse(null).Title);
        Assert.Equal(string.Empty, MediaNameParser.Parse("   ").Title);
    }

    [Fact]
    public void OnlineFolderDoesNotInvertNamesWithoutSeparator()
    {
        var parsed = MediaNameParser.Parse("Мурка (Karaoke version).mkv", "Karaoke online");

        Assert.Empty(parsed.Artist);
        Assert.Equal("Мурка", parsed.Title);
    }
}
