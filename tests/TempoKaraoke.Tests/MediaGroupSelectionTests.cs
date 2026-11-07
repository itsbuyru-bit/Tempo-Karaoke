using TempoKaraoke.Models;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class MediaGroupSelectionTests
{
    private static MediaGroup Group() =>
        new("key", new MediaItem(@"C:\karaoke\song.mkv", "Artist", "Title", "Folder"));

    [Fact]
    public void CheckboxIsHiddenUntilHoverOrSelection()
    {
        var group = Group();

        Assert.False(group.ShowSelectionCheck);

        group.IsHovered = true;
        Assert.True(group.ShowSelectionCheck);

        group.IsHovered = false;
        Assert.False(group.ShowSelectionCheck);

        group.IsSelected = true;
        Assert.True(group.ShowSelectionCheck);
    }

    [Fact]
    public void SelectionStaysVisibleAfterPointerLeaves()
    {
        var group = Group();
        group.IsHovered = true;
        group.IsSelected = true;
        group.IsHovered = false;

        Assert.True(group.ShowSelectionCheck);
    }
}
