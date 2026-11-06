using TempoKaraoke.Core.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class SettingsUiLogicTests
{
    [Theory]
    [InlineData("Интерфейс", "Язык, тема и положение окна", "", true)]
    [InlineData("Интерфейс", "Язык, тема и положение окна", "   ", true)]
    [InlineData("Интерфейс", "Язык, тема и положение окна", "язык", true)]
    [InlineData("Интерфейс", "Язык, тема и положение окна", "ТЕМА", true)]
    [InlineData("Интерфейс", "Язык, тема и положение окна", "окна", true)]
    [InlineData("Интерфейс", "Язык, тема и положение окна", "диагностика", false)]
    [InlineData(null, null, "тема", false)]
    public void SearchMatchesTitleAndDescription(string? title, string? description, string query, bool expected)
    {
        Assert.Equal(expected, SettingsSearchFilter.Matches(title, description, query));
    }

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
