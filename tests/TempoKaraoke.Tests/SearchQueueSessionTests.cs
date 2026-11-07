using System.Linq;
using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class SearchSessionTests
{
    private static MediaItem Item(string title, string folder = "Karaoke") =>
        new($@"G:\Karaoke\{folder}\{title}.mkv", string.Empty, title, folder);

    [Fact]
    public void BuildsIndexOnceAndReturnsBoundedPage()
    {
        var session = new SearchSession();
        session.SetItems(new[]
        {
            Item("Конь"),
            Item("Конь — Любэ"),
            Item("Три белых коня"),
            Item("Другая песня"),
        });

        var page = session.Search("Конь", limit: 2);

        Assert.Equal("Конь", page.Query);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(4, session.IndexedCount);
    }

    [Fact]
    public void EmptyQueryReturnsNoResults()
    {
        var session = new SearchSession();
        session.SetItems(new[] { Item("Конь") });

        var page = session.Search("   ");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public void SearchAllReturnsUnboundedResults()
    {
        var session = new SearchSession();
        session.SetItems(Enumerable.Range(0, 12).Select(i => Item($"Трек {i}")).ToList());

        var results = session.SearchAll("Трек");

        Assert.Equal(12, results.Count);
    }

    [Fact]
    public void GroupsDuplicateVersionsBeforeApplyingTheLimit()
    {
        var session = new SearchSession();
        session.SetItems(new[]
        {
            new MediaItem(@"G:\Karaoke\A\Конь.mkv", "Любэ", "Конь", "A"),
            new MediaItem(@"G:\Karaoke\B\Конь.mp4", "Любэ", "Конь", "B"),
            new MediaItem(@"G:\Karaoke\A\Другая.mkv", "Любэ", "Другая", "A"),
        });

        var page = session.Search("Конь", limit: 1, groupResults: true);

        Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Конь", page.Items[0].Title);
    }

    [Fact]
    public void AppliesFolderAndFormatFilters()
    {
        var session = new SearchSession();
        var mkv = Item("Конь");
        var mp4 = new MediaItem(@"G:\Karaoke\Other\Конь.mp4", string.Empty, "Конь", "Other");
        session.SetItems(new[] { mkv, mp4 });

        var page = session.Search("Конь", limit: 8, new SearchFilter("Karaoke", "mkv"));

        Assert.Single(page.Items);
        Assert.Same(mkv, page.Items[0]);
        Assert.Equal(1, page.TotalCount);
    }
}

public sealed class QueueEngineTests
{
    private static MediaItem Item(string title) =>
        new($@"G:\Karaoke\{title}.mkv", string.Empty, title, "Karaoke");

    [Fact]
    public void TracksCurrentIndexWhenRemovingEarlierItem()
    {
        var queue = new QueueEngine();
        queue.Add(Item("A"));
        queue.Add(Item("B"));
        queue.Add(Item("C"));
        queue.SetCurrent(2);

        queue.RemoveAt(0);

        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal("C", queue.Current?.Title);
    }

    [Fact]
    public void MoveKeepsCurrentItem()
    {
        var queue = new QueueEngine();
        queue.Add(Item("A"));
        queue.Add(Item("B"));
        queue.Add(Item("C"));
        queue.SetCurrent(1);

        queue.Move(1, 2);

        Assert.Equal("B", queue.Current?.Title);
        Assert.Equal(2, queue.CurrentIndex);
    }

    [Fact]
    public void ClearResetsCurrent()
    {
        var queue = new QueueEngine();
        queue.Add(Item("A"));
        queue.SetCurrent(0);

        queue.Clear();

        Assert.Empty(queue.Items);
        Assert.Equal(-1, queue.CurrentIndex);
    }
}
