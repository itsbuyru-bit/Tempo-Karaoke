using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace TempoKaraoke.Tests;

/// <summary>
/// Крошки настроек переиспользуют существующие строки: ссылка-родитель обязана
/// совпадать с заголовком страницы «Параметры», иначе переводы разъедутся.
/// </summary>
public sealed class SettingsLocalizationTests
{
    public static IEnumerable<object[]> Languages => new[]
    {
        new object[] { "en-US" },
        new object[] { "ru-RU" },
    };

    [Theory]
    [MemberData(nameof(Languages))]
    public void BreadcrumbRootMatchesSettingsPageTitle(string language)
    {
        var catalog = ReadCatalog(language);

        Assert.True(catalog.ContainsKey("Settings_BreadcrumbRoot.Content"), "нет ключа крошек");
        Assert.True(catalog.ContainsKey("Ui_Settings_123.Text"), "нет ключа заголовка страницы");
        Assert.Equal(catalog["Ui_Settings_123.Text"], catalog["Settings_BreadcrumbRoot.Content"]);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EverySectionCardHasHeaderAndDescription(string language)
    {
        var catalog = ReadCatalog(language);

        foreach (var id in new[] { "Interface", "Libraries", "Playback", "Remote", "Security", "Diagnostics", "Updates" })
        {
            Assert.True(catalog.ContainsKey($"Settings_Section_{id}.Header"), $"нет заголовка раздела {id}");
            Assert.True(catalog.ContainsKey($"Settings_Section_{id}.Description"), $"нет описания раздела {id}");
        }
    }

    [Fact]
    public void EnglishCatalogHasNoCyrillic()
    {
        foreach (var value in ReadCatalog("en-US").Values)
        {
            Assert.DoesNotContain(value, character => character >= 'А' && character <= 'я');
        }
    }

    private static Dictionary<string, string> ReadCatalog(string language)
    {
        var file = Path.Combine(FindRepositoryRoot(), "localization", $"{language}.json");
        using var stream = File.OpenRead(file);
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        Assert.NotNull(catalog);
        return catalog!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoKaraoke.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Не найден корень репозитория (TempoKaraoke.slnx).");
    }
}
