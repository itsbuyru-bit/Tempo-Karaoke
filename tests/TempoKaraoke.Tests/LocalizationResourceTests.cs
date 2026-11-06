using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void DesktopResourcesHaveIdenticalKeys()
    {
        var english = ReadResw("en-US");
        var russian = ReadResw("ru-RU");

        Assert.Equal(english.Keys.OrderBy(key => key), russian.Keys.OrderBy(key => key));
        Assert.All(english, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.All(russian, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Fact]
    public void EnglishDesktopResourcesContainNoCyrillic()
    {
        foreach (var (key, value) in ReadResw("en-US"))
        {
            Assert.False(value.Any(character => character >= 'А' && character <= 'я' || character is 'Ё' or 'ё'), $"en-US:{key}");
        }
    }

    [Fact]
    public void RussianPluralCategoriesExist()
    {
        var english = ReadResw("en-US");
        var russian = ReadResw("ru-RU");

        foreach (var suffix in new[] { "One", "Other" })
        {
            Assert.True(english.ContainsKey($"Plural_SongCount_{suffix}"), $"en-US Plural_SongCount_{suffix}");
        }

        foreach (var suffix in new[] { "One", "Few", "Many", "Other" })
        {
            Assert.True(russian.ContainsKey($"Plural_SongCount_{suffix}"), $"ru-RU Plural_SongCount_{suffix}");
            Assert.True(russian.ContainsKey($"Selection_Count_{suffix}"), $"ru-RU Selection_Count_{suffix}");
        }
    }

    [Fact]
    public void WebLocalesHaveIdenticalKeysAndEnglishHasNoCyrillic()
    {
        var english = ReadJson("en-US");
        var russian = ReadJson("ru-RU");

        Assert.Equal(english.Keys.OrderBy(key => key), russian.Keys.OrderBy(key => key));
        foreach (var (key, value) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"en-US:{key}");
            Assert.False(value.Any(character => character >= 'А' && character <= 'я' || character is 'Ё' or 'ё'), $"en-US:{key}");
        }
    }

    [Fact]
    public void WebStaticAndDynamicKeysExistInBothLocales()
    {
        var html = File.ReadAllText(Path.Combine(Root, "src", "TempoKaraoke", "Services", "RemotePage.cs"));
        var keys = Regex.Matches(html, @"data-i18n(?:-placeholder)?=""([a-z0-9_]+)""")
            .Select(match => match.Groups[1].Value)
            .Concat(Regex.Matches(html, @"\bt\('([a-z0-9_]+)'").Select(match => match.Groups[1].Value))
            .Concat(Regex.Matches(html, @"\btp\('([a-z0-9_]+)'").Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var english = ReadJson("en-US");
        var russian = ReadJson("ru-RU");
        foreach (var key in keys)
        {
            Assert.True(english.ContainsKey(key), $"en-US missing web key {key}");
            Assert.True(russian.ContainsKey(key), $"ru-RU missing web key {key}");
        }
    }

    [Fact]
    public void XamlDoesNotContainHardcodedRussianUiText()
    {
        foreach (var file in new[] { "MainPage.xaml", "PlayerWindow.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(Root, "src", "TempoKaraoke", file));
            var matches = Regex.Matches(
                xaml,
                @"(?:Text|Content|Header|Description|PlaceholderText|OffContent|OnContent|ToolTipService\.ToolTip|AutomationProperties\.Name)=""[^""]*[А-Яа-яЁё][^""]*""");
            Assert.True(matches.Count == 0, $"{file} has {matches.Count} hardcoded Russian UI attribute(s)");
        }
    }

    [Fact]
    public void EveryXamlUidHasAtLeastOneResource()
    {
        var keys = ReadResw("en-US").Keys.ToList();
        foreach (var file in new[] { "MainPage.xaml", "PlayerWindow.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(Root, "src", "TempoKaraoke", file));
            var uids = Regex.Matches(xaml, @"x:Uid=""([^""]+)""")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal);

            foreach (var uid in uids)
            {
                Assert.True(
                    keys.Any(key => key.StartsWith(uid + ".", StringComparison.Ordinal)),
                    $"{file}: x:Uid=\"{uid}\" has no matching resource");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoKaraoke.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, "src", "TempoKaraoke")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static Dictionary<string, string> ReadResw(string language)
    {
        var path = Path.Combine(Root, "src", "TempoKaraoke", "Strings", language, "Resources.resw");
        var document = XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadJson(string language)
    {
        var path = Path.Combine(Root, "src", "TempoKaraoke", "public", "locales", $"{language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }
}
