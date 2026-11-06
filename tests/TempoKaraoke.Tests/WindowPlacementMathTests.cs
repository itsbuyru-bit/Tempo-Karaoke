using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class WindowPlacementMathTests
{
    [Fact]
    public void DefaultSizeAt1080pIsThreeQuartersAndCentered()
    {
        var work = new PlacementRect(0, 0, 1920, 1040);

        var rect = WindowPlacementMath.ComputeDefault(work, 1.0);

        Assert.Equal(1440, rect.Width);
        Assert.Equal(780, rect.Height);
        Assert.Equal(240, rect.X);
        Assert.Equal(130, rect.Y);
    }

    [Fact]
    public void DefaultSizeAt4KWith150PercentStaysBelowMaxDip()
    {
        var work = new PlacementRect(0, 0, 3840, 2080);

        var rect = WindowPlacementMath.ComputeDefault(work, 1.5);

        // 75% ширины = 2880 px = MaxWidthDip(1920) * 1.5 — ровно предел.
        Assert.Equal(2880, rect.Width);
        // 75% высоты = 1560 px, максимум 1200 DIP * 1.5 = 1800 — не срабатывает.
        Assert.Equal(1560, rect.Height);
        Assert.Equal(480, rect.X);
        Assert.Equal(260, rect.Y);
    }

    [Fact]
    public void DefaultSizeRespectsMinimumsOnSmallScreens()
    {
        var work = new PlacementRect(0, 0, 1280, 720);

        var rect = WindowPlacementMath.ComputeDefault(work, 1.0);

        Assert.Equal(1024, rect.Width);
        Assert.Equal(700, rect.Height);
        Assert.Equal(128, rect.X);
        Assert.Equal(10, rect.Y);
    }

    [Fact]
    public void ClampKeepsWindowInsideWorkArea()
    {
        var work = new PlacementRect(0, 0, 1920, 1040);
        var rect = new PlacementRect(5000, 5000, 800, 600);

        var clamped = WindowPlacementMath.Clamp(rect, work);

        Assert.Equal(1120, clamped.X);
        Assert.Equal(440, clamped.Y);
        Assert.Equal(800, clamped.Width);
        Assert.Equal(600, clamped.Height);
    }

    [Fact]
    public void OffscreenSavedRectFallsBackToDefault()
    {
        var work = new PlacementRect(0, 0, 1920, 1040);
        var saved = new PlacementRect(3000, 2000, 1200, 800);

        var rect = WindowPlacementMath.RestoreOrDefault(saved, work, 1.0, out var clamped, out var reset, out var reason);

        Assert.True(reset);
        Assert.False(clamped);
        Assert.Equal("saved-rect-offscreen", reason);
        Assert.Equal(WindowPlacementMath.ComputeDefault(work, 1.0), rect);
    }

    [Fact]
    public void PartiallyVisibleSavedRectIsKeptButClamped()
    {
        var work = new PlacementRect(0, 0, 1920, 1040);
        var saved = new PlacementRect(1000, 500, 1200, 800);

        var rect = WindowPlacementMath.RestoreOrDefault(saved, work, 1.0, out var clamped, out var reset, out var reason);

        Assert.False(reset);
        Assert.True(clamped);
        Assert.Equal("saved-rect-clamped", reason);
        Assert.Equal(720, rect.X);
        Assert.Equal(240, rect.Y);
        Assert.Equal(1200, rect.Width);
        Assert.Equal(800, rect.Height);
    }

    [Fact]
    public void OversizedSavedRectIsShrunkToWorkArea()
    {
        var work = new PlacementRect(0, 0, 1920, 1040);
        var saved = new PlacementRect(0, 0, 3000, 2000);

        var rect = WindowPlacementMath.RestoreOrDefault(saved, work, 1.0, out var clamped, out var reset, out _);

        Assert.False(reset);
        Assert.True(clamped);
        Assert.Equal(1920, rect.Width);
        Assert.Equal(1040, rect.Height);
        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
    }

    [Fact]
    public void EmptySavedRectFallsBackToDefault()
    {
        var work = new PlacementRect(0, 0, 1600, 900);

        var rect = WindowPlacementMath.RestoreOrDefault(null, work, 1.0, out _, out var reset, out var reason);

        Assert.True(reset);
        Assert.Equal("no-saved-placement", reason);
        Assert.Equal(WindowPlacementMath.ComputeDefault(work, 1.0), rect);
    }

    [Fact]
    public void TinySavedRectGrowsToMinimumSize()
    {
        var work = new PlacementRect(0, 0, 1920, 1040);
        var saved = new PlacementRect(100, 100, 200, 150);

        var rect = WindowPlacementMath.RestoreOrDefault(saved, work, 1.0, out var clamped, out var reset, out _);

        Assert.False(reset);
        Assert.True(clamped);
        Assert.Equal(WindowPlacementMath.MinWidthDip, rect.Width);
        Assert.Equal(WindowPlacementMath.MinHeightDip, rect.Height);
        Assert.Equal(100, rect.X);
        Assert.Equal(100, rect.Y);
    }

    [Fact]
    public void VisibleFractionIsZeroWhenRectIsOutside()
    {
        var work = new PlacementRect(0, 0, 1000, 800);
        var rect = new PlacementRect(2000, 1200, 300, 200);

        Assert.Equal(0, WindowPlacementMath.VisibleFraction(rect, work));
        Assert.False(WindowPlacementMath.IsVisibleEnough(rect, work, 1.0));
    }
}
