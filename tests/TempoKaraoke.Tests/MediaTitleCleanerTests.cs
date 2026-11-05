using TempoKaraoke.Models;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class MediaTitleCleanerTests
{
    [Theory]
    [InlineData("2Маши - Босая Караоке", "2Маши - Босая")]
    [InlineData("Алла Пугачёва — Свеча горела (Текст песни)", "Алла Пугачёва — Свеча горела")]
    [InlineData("Song (Official Video)", "Song")]
    [InlineData("Трек [Кавер минус]", "Трек")]
    [InlineData("Видео (2023)", "Видео")]
    public void RemovesServiceMarkers(string source, string expected)
    {
        Assert.Equal(expected, MediaTitleCleaner.Clean(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsEmptyForBlank(string? source)
    {
        Assert.Equal(string.Empty, MediaTitleCleaner.Clean(source));
    }

    [Fact]
    public void KeepsNameWhenOnlyMarkers()
    {
        Assert.Equal("Караоке", MediaTitleCleaner.Clean("Караоке"));
    }

    [Fact]
    public void KeepsOriginalWhenEverythingLooksLikeMarker()
    {
        Assert.Equal("Кавер минус", MediaTitleCleaner.Clean("Кавер минус"));
    }
}
