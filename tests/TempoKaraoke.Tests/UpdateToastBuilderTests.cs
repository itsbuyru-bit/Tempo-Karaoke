using System;
using System.Linq;
using TempoKaraoke.Services;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class UpdateToastBuilderTests
{
    private static UpdateToastContent Content(
        string title = "Доступно обновление TempoKaraoke",
        string body = "Установлена 1.10.1.0 → 1.10.2.0. Что нового: Исправления • 110 MB",
        string? attribution = "Опубликовано 07.11.2026") => new(
        title,
        body,
        attribution,
        "Установить",
        "Позже",
        "Что нового",
        UpdateToastBuilder.OpenUpdatePageArgument,
        "file:///C:/App/Assets/AppIcon-192.png",
        new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void BuildXmlContainsLaunchActionsAndAttribution()
    {
        var xml = UpdateToastBuilder.BuildXml(Content());

        Assert.Contains("launch=\"action=open_update_page\"", xml);
        Assert.Contains("template=\"ToastGeneric\"", xml);
        Assert.Contains("placement=\"attribution\"", xml);
        Assert.Contains("arguments=\"action=install\"", xml);
        Assert.Contains("arguments=\"action=postpone\"", xml);
        Assert.Contains("arguments=\"action=changelog\"", xml);
        Assert.Contains("displayTimestamp=\"2026-11-07T12:00:00Z\"", xml);
        Assert.Contains("placement=\"appLogoOverride\"", xml);
        Assert.DoesNotContain("Update_Toast", xml);
        Assert.DoesNotContain("ms-resource:", xml);
    }

    [Fact]
    public void BuildXmlEscapesUserControlledText()
    {
        var xml = UpdateToastBuilder.BuildXml(Content(
            title: "A & B <C>",
            body: "1.0 → 2.0 \"quoted\" & <tag>",
            attribution: "O'Reilly & Sons"));

        Assert.Contains("A &amp; B &lt;C&gt;", xml);
        Assert.Contains("&quot;quoted&quot; &amp; &lt;tag&gt;", xml);
        Assert.Contains("O&apos;Reilly &amp; Sons", xml);
        Assert.DoesNotContain("<tag>", xml);
    }

    [Fact]
    public void BuildXmlOmitsEmptyOptionalParts()
    {
        var xml = UpdateToastBuilder.BuildXml(new UpdateToastContent(
            "Title",
            "Body",
            null,
            string.Empty,
            string.Empty,
            null,
            UpdateToastBuilder.OpenUpdatePageArgument,
            null,
            DateTimeOffset.UnixEpoch));

        Assert.DoesNotContain("<actions>", xml);
        Assert.DoesNotContain("appLogoOverride", xml);
        Assert.DoesNotContain("attribution", xml);
    }

    [Theory]
    [InlineData("action=install", "install")]
    [InlineData("action=postpone", "postpone")]
    [InlineData("action=later", "postpone")]
    [InlineData("action=open_update_page", "open_update_page")]
    [InlineData("action=changelog", "open_update_page")]
    [InlineData("action=unknown", "open_update_page")]
    [InlineData("tempokaraoke:updates-install", "install")]
    [InlineData("tempokaraoke:updates-postpone", "postpone")]
    [InlineData("tempokaraoke:updates-open", "open_update_page")]
    [InlineData("key=value&action=install", "install")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseActionMapsKnownAndUnknownArguments(string? argument, string? expected)
    {
        Assert.Equal(expected, UpdateActivationArguments.ParseAction(argument));
    }

    [Fact]
    public void ExtractHighlightsRemovesMarkdownAndLimitsCount()
    {
        const string body = """
            ## Новое

            - **Плашка UPDATE/NEW** у пункта «Параметры».
            * Исправлен [тост](https://example.com) об обновлении.
            + Добавлено подтверждение установки.
            - Четвёртый пункт не должен попасть в тост.

            Не список — игнорируется.
            """;

        var highlights = UpdateToastBuilder.ExtractHighlights(body);

        Assert.Equal(3, highlights.Count);
        Assert.Equal("Плашка UPDATE/NEW у пункта «Параметры».", highlights[0]);
        Assert.Equal("Исправлен тост об обновлении.", highlights[1]);
        Assert.Equal("Добавлено подтверждение установки.", highlights[2]);
    }

    [Fact]
    public void ExtractHighlightsShortensLongLines()
    {
        var line = "- " + new string('я', 200);
        var highlights = UpdateToastBuilder.ExtractHighlights(line, max: 1);

        Assert.Single(highlights);
        Assert.EndsWith("…", highlights[0]);
        Assert.True(highlights[0].Length <= 120);
    }

    [Fact]
    public void ExtractHighlightsHandlesEmptyInput()
    {
        Assert.Empty(UpdateToastBuilder.ExtractHighlights(null));
        Assert.Empty(UpdateToastBuilder.ExtractHighlights("   "));
        Assert.Empty(UpdateToastBuilder.ExtractHighlights("- пункт", max: 0));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(800, "800 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(114843756, "110 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2 GB")]
    public void FormatSizeUsesReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, UpdateToastBuilder.FormatSize(bytes));
    }
}
