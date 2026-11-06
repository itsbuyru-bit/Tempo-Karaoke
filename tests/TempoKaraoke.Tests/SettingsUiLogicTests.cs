using TempoKaraoke.Core.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class SettingsUiLogicTests
{
    [Theory]
    [InlineData("1.8.23.0", "", "1.8.22.1", true)]
    [InlineData("1.8.23.0", "1.8.22.1", "1.8.22.1", true)]
    [InlineData("1.8.23.0", "1.8.23.0", "1.8.22.1", false)]
    [InlineData("1.8.23.0", "1.9.0.0", "1.8.22.1", true)]
    [InlineData("1.8.22.1", "", "1.8.22.1", false)]
    [InlineData("", "", "1.8.22.1", false)]
    public void BadgeVisibleUntilInstalledOrDismissed(string pending, string dismissed, string installed, bool expected)
    {
        Assert.Equal(expected, UpdateBadgePolicy.ShouldShow(pending, dismissed, installed));
    }
}
