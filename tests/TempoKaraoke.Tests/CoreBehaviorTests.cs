using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TempoKaraoke.Models;
using TempoKaraoke.Services;
using TempoKaraoke.Text;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class CoreBehaviorTests
{
    [Theory]
    [InlineData(1, "песня")]
    [InlineData(2, "песни")]
    [InlineData(5, "песен")]
    [InlineData(11, "песен")]
    [InlineData(21, "песня")]
    [InlineData(22, "песни")]
    [InlineData(25, "песен")]
    public void PluralizesRussianCounts(int count, string expected) =>
        Assert.Equal($"{count} {expected}", RussianPlural.Format(count, "песня", "песни", "песен"));

    [Fact]
    public void QueueCopiesHaveDistinctEntryIdsAndPlayingState()
    {
        var source = new MediaItem(@"G:\Karaoke\A\Song.mkv", "Artist", "Song", "A");
        var first = source.CreateQueueCopy();
        var second = source.CreateQueueCopy();

        Assert.NotEqual(first.QueueEntryId, second.QueueEntryId);
        Assert.NotSame(first, second);

        first.IsPlaying = true;
        Assert.True(first.IsPlaying);
        Assert.False(second.IsPlaying);
    }

    [Fact]
    public void GroupsVersionsOfTheSameSongAndPicksLargest()
    {
        // Core форматирует подписи по текущей UI-культуре — фиксируем её,
        // иначе тест падает на англоязычном CI.
        using var culture = CultureScope.For("ru-RU");
        var small = new MediaItem(@"G:\Karaoke\A\Алла Пугачёва - Свеча горела.mkv", "Алла Пугачёва", "Свеча горела", "A", sizeBytes: 100);
        var large = new MediaItem(@"G:\Karaoke\B\Алла Пугачёва - Свеча горела.mkv", "Алла Пугачёва", "Свеча горела", "B", sizeBytes: 200);

        var groups = MediaGroupingService.Group(new[] { small, large });

        var group = Assert.Single(groups);
        Assert.Equal(2, group.VersionCount);
        Assert.Same(large, group.Best);
        Assert.Equal("2 версии", group.VersionCountText);
    }

    [Fact]
    public void DoesNotMergeUntitledFilesFromDifferentFolders()
    {
        var first = new MediaItem(@"G:\Karaoke\A\Track 01.mkv", string.Empty, "Track 01", "A");
        var second = new MediaItem(@"G:\Karaoke\B\Track 01.mkv", string.Empty, "Track 01", "B");

        var groups = MediaGroupingService.Group(new[] { first, second });

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public async Task M3uRoundTripsAndDeduplicates()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tempo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var playlistPath = Path.Combine(folder, "list.m3u8");

        try
        {
            var first = new MediaItem(@"G:\Karaoke\A\One.mkv", "Artist", "One", "A");
            var second = new MediaItem(@"G:\Karaoke\B\Two.mp4", "Artist", "Two", "B");
            await M3uService.WriteAsync(playlistPath, new[] { first, second });

            var lines = await File.ReadAllLinesAsync(playlistPath);
            Assert.Contains($"#EXTINF:-1,{first.DisplayTitle}", lines);
            Assert.Contains(first.Path, lines);

            var paths = await M3uService.ReadAsync(playlistPath);
            Assert.Equal(new[] { first.Path, second.Path }, paths);

            await File.AppendAllLinesAsync(playlistPath, new[] { first.Path });
            var deduplicated = await M3uService.ReadAsync(playlistPath);
            Assert.Equal(2, deduplicated.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void PlaylistAddsAndDeduplicates()
    {
        using var culture = CultureScope.For("ru-RU");
        var playlist = new Playlist { Name = "Тест" };

        Assert.True(playlist.AddItem(@"G:\Karaoke\a.mkv"));
        Assert.False(playlist.AddItem(@"g:\karaoke\A.MKV"));
        Assert.False(playlist.AddItem(" "));
        Assert.Single(playlist.Items);
        Assert.Equal("1 видео", playlist.CountText);

        Assert.True(playlist.RemoveItem(@"G:\Karaoke\a.mkv"));
        Assert.Empty(playlist.Items);
    }

    [Fact]
    public void RemovesSeveralServiceMarkersInOnePass()
    {
        var cleaned = MediaTitleCleaner.Clean("Исполнитель - Песня (Клип 2024) [Текст песни] Караоке");
        Assert.Equal("Исполнитель - Песня", cleaned);
    }

    /// <summary>Временно переключает культуру потока, чтобы тест не зависел от локали CI.</summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUiCulture;

        private CultureScope(CultureInfo culture)
        {
            _previousCulture = CultureInfo.CurrentCulture;
            _previousUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public static CultureScope For(string name) => new(CultureInfo.GetCultureInfo(name));

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
