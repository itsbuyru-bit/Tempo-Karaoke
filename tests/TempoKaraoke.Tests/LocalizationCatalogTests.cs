using System.Text.Json;
using TempoKaraoke.Locales;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class LocalizationCatalogTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string SourceDirectory = Path.Combine(Root, "localization");

    [Fact]
    public void ManifestIsValidAndListsFullBcp47Tags()
    {
        var result = LocalizationManifestLoader.Load(SourceDirectory);

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Issues);
        Assert.Equal("en-US", result.Manifest!.Fallback);
        Assert.Contains(result.Manifest.Languages, entry => entry.Tag == "en-US" && entry.Enabled);
        Assert.Contains(result.Manifest.Languages, entry => entry.Tag == "ru-RU" && entry.Enabled);
        Assert.All(result.Manifest.Languages, entry =>
        {
            Assert.True(LanguageTag.IsFullTag(entry.Tag), entry.Tag);
            Assert.False(string.IsNullOrWhiteSpace(entry.NativeName), entry.Tag);
            Assert.True(File.Exists(Path.Combine(SourceDirectory, entry.File)), entry.File);
        });
    }

    [Fact]
    public void CatalogLoadsEveryLanguageWithoutIssues()
    {
        var catalog = LocalizationCatalog.Load(SourceDirectory);
        var issues = LocalizationValidator.Validate(catalog);

        Assert.True(catalog.IsUsable);
        Assert.True(catalog.Languages.Count >= 2);
        Assert.Equal("en-US", catalog.FallbackTag);
        Assert.Empty(issues);
    }

    [Fact]
    public void MissingKeyFallsBackToEnglishPerKey()
    {
        using var temp = new TemporaryCatalog();
        temp.Write("manifest.json", """
            {
              "version": 1,
              "fallback": "en-US",
              "languages": [
                { "tag": "en-US", "file": "en-US.json", "nativeName": "English" },
                { "tag": "ru-RU", "file": "ru-RU.json", "nativeName": "Русский" }
              ]
            }
            """);
        temp.Write("en-US.json", """{ "Only_English": "English only", "Shared": "Hello {0}" }""");
        temp.Write("ru-RU.json", """{ "Shared": "Привет {0}" }""");

        var catalog = LocalizationCatalog.Load(temp.Directory);
        var lookup = catalog.Lookup("ru-RU", "Only_English");

        Assert.True(lookup.Found);
        Assert.True(lookup.FromFallback);
        Assert.Equal("English only", lookup.Value);
        Assert.Equal("Привет {0}", catalog.Lookup("ru-RU", "Shared").Value);
    }

    [Fact]
    public void MissingKeyInBothLanguagesReturnsSafeKey()
    {
        using var temp = new TemporaryCatalog();
        temp.Write("manifest.json", """
            {
              "version": 1,
              "fallback": "en-US",
              "languages": [
                { "tag": "en-US", "file": "en-US.json", "nativeName": "English" }
              ]
            }
            """);
        temp.Write("en-US.json", """{ "Known": "Known" }""");

        var catalog = LocalizationCatalog.Load(temp.Directory);
        var lookup = catalog.Lookup("en-US", "Unknown.Key");

        Assert.False(lookup.Found);
        Assert.Equal("Unknown.Key", lookup.Value);
    }

    [Fact]
    public void RussianPluralCategoriesAndFallbackAreCorrect()
    {
        var catalog = LocalizationCatalog.Load(SourceDirectory);

        Assert.Equal("One", PluralCategory.For("ru-RU", 1));
        Assert.Equal("Few", PluralCategory.For("ru-RU", 2));
        Assert.Equal("Many", PluralCategory.For("ru-RU", 5));
        Assert.Equal("Many", PluralCategory.For("ru-RU", 11));
        Assert.Equal("One", PluralCategory.For("ru-RU", 21));
        Assert.Equal("Other", PluralCategory.For("en-US", 2));
        Assert.Equal("One", PluralCategory.For("en-US", 1));

        Assert.Equal("1 песня", string.Format(catalog.GetPlural("ru-RU", "Plural_SongCount", 1), 1));
        Assert.Equal("2 песни", string.Format(catalog.GetPlural("ru-RU", "Plural_SongCount", 2), 2));
        Assert.Equal("5 песен", string.Format(catalog.GetPlural("ru-RU", "Plural_SongCount", 5), 5));
    }

    [Fact]
    public void MissingPluralCategoryFallsBackToEnglish()
    {
        using var temp = new TemporaryCatalog();
        temp.Write("manifest.json", """
            {
              "version": 1,
              "fallback": "en-US",
              "languages": [
                { "tag": "en-US", "file": "en-US.json", "nativeName": "English" },
                { "tag": "de-DE", "file": "de-DE.json", "nativeName": "Deutsch" }
              ]
            }
            """);
        temp.Write("en-US.json", """{ "Plural_X_One": "{0} item", "Plural_X_Other": "{0} items" }""");
        temp.Write("de-DE.json", """{ "Plural_X_One": "{0} Ding" }""");

        var catalog = LocalizationCatalog.Load(temp.Directory);

        Assert.Equal("{0} Ding", catalog.GetPlural("de-DE", "Plural_X", 1));
        // German "Other" is missing, so the English fallback is used.
        Assert.Equal("{0} items", catalog.GetPlural("de-DE", "Plural_X", 3));
    }

    [Fact]
    public void ResolverPrefersSavedThenSystemThenFallback()
    {
        var catalog = LocalizationCatalog.Load(SourceDirectory);

        var saved = LanguageResolver.Resolve(catalog, "ru-RU", new[] { "en-US" });
        Assert.Equal("ru-RU", saved.Tag);
        Assert.Equal(LanguageMatch.Saved, saved.Match);
        Assert.False(saved.ShouldPersist);

        var shortSaved = LanguageResolver.Resolve(catalog, "ru", new[] { "en-US" });
        Assert.Equal("ru-RU", shortSaved.Tag);
        Assert.Equal(LanguageMatch.Saved, shortSaved.Match);

        var system = LanguageResolver.Resolve(catalog, null, new[] { "ru" });
        Assert.Equal("ru-RU", system.Tag);
        Assert.Equal(LanguageMatch.SystemLanguage, system.Match);
        Assert.True(system.ShouldPersist);

        var unknown = LanguageResolver.Resolve(catalog, null, new[] { "ja-JP" });
        Assert.Equal("en-US", unknown.Tag);
        Assert.Equal(LanguageMatch.Fallback, unknown.Match);
    }

    [Fact]
    public void AddingLanguageOnlyNeedsManifestAndFile()
    {
        using var temp = new TemporaryCatalog();
        temp.Write("manifest.json", """
            {
              "version": 1,
              "fallback": "en-US",
              "languages": [
                { "tag": "en-US", "file": "en-US.json", "nativeName": "English" },
                { "tag": "de-DE", "file": "de-DE.json", "nativeName": "Deutsch", "englishName": "German", "enabled": true }
              ]
            }
            """);
        temp.Write("en-US.json", """{ "Greeting": "Hello" }""");
        temp.Write("de-DE.json", """{ "Greeting": "Hallo" }""");

        var catalog = LocalizationCatalog.Load(temp.Directory);

        Assert.Contains(catalog.Languages, language => language.Tag == "de-DE" && language.NativeName == "Deutsch");
        Assert.Equal("de-DE", catalog.ResolveTag("de"));
        Assert.Equal("de-DE", catalog.ResolveTag("de-AT"));
        Assert.Equal("Hallo", catalog.Lookup("de-DE", "Greeting").Value);
    }

    [Fact]
    public void InvalidManifestEntriesAreReportedAndSkipped()
    {
        using var temp = new TemporaryCatalog();
        temp.Write("manifest.json", """
            {
              "version": 1,
              "fallback": "en-US",
              "languages": [
                { "tag": "en-US", "file": "en-US.json", "nativeName": "English" },
                { "tag": "de", "file": "de.json", "nativeName": "" },
                { "tag": "fr-FR", "file": "missing.json", "nativeName": "Français" },
                { "tag": "es-ES", "file": "../escape.json", "nativeName": "Español" }
              ]
            }
            """);
        temp.Write("en-US.json", """{ "Greeting": "Hello" }""");

        var result = LocalizationManifestLoader.Load(temp.Directory);

        Assert.NotNull(result.Manifest);
        Assert.Contains(result.Issues, issue => issue.Code == "language_invalid_tag");
        Assert.Contains(result.Issues, issue => issue.Code == "language_missing_native_name");
        Assert.Contains(result.Issues, issue => issue.Code == "language_missing_file");
        Assert.Contains(result.Issues, issue => issue.Code == "language_invalid_file");

        var catalog = LocalizationCatalog.Load(temp.Directory);
        Assert.DoesNotContain(catalog.Languages, language => language.Tag == "de");
        Assert.DoesNotContain(catalog.Languages, language => language.Tag == "fr-FR");
        Assert.Equal("en-US", catalog.ResolveTag("de"));
    }

    [Fact]
    public void GeneratedSurfacesKeepEverySourceKey()
    {
        var source = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(SourceDirectory, "en-US.json")))!;
        var desktopKeys = source.Keys.Where(key => !LocalizationValidator.IsWebKey(key)).ToList();
        var webKeys = source.Keys.Where(LocalizationValidator.IsWebKey).ToList();

        var reswKeys = ReadReswKeys("en-US");
        var localeKeys = ReadLocaleKeys("en-US");

        Assert.Equal(desktopKeys.OrderBy(key => key), reswKeys.OrderBy(key => key));
        Assert.Equal(webKeys.OrderBy(key => key), localeKeys.OrderBy(key => key));

        var webManifest = JsonSerializer.Deserialize<WebManifest>(
            File.ReadAllText(Path.Combine(Root, "src", "TempoKaraoke", "public", "locale-manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var manifest = LocalizationManifestLoader.Load(SourceDirectory).Manifest!;
        Assert.Equal(
            manifest.Languages.Where(language => language.Enabled).Select(language => language.Tag),
            webManifest.Languages.Select(language => language.Tag));
    }

    [Fact]
    public void EveryCatalogPlaceholderMatchesEnglish()
    {
        var catalog = LocalizationCatalog.Load(SourceDirectory);
        var fallback = catalog.Languages.First(language => language.Tag == catalog.FallbackTag);
        var issues = new List<string>();

        foreach (var language in catalog.Languages.Where(language => language.Tag != catalog.FallbackTag))
        {
            foreach (var pair in language.Strings)
            {
                if (!fallback.Strings.TryGetValue(pair.Key, out var fallbackValue))
                {
                    issues.Add($"orphan {language.Tag}:{pair.Key}");
                    continue;
                }

                var expected = LocalizationValidator.Placeholders(fallbackValue);
                var actual = LocalizationValidator.Placeholders(pair.Value);
                if (!expected.SequenceEqual(actual))
                {
                    issues.Add($"placeholder {language.Tag}:{pair.Key}");
                }
            }
        }

        Assert.Empty(issues);
    }

    [Fact]
    public void EveryWebKeyUsedByRemotePageExistsInSource()
    {
        var page = File.ReadAllText(Path.Combine(Root, "src", "TempoKaraoke", "Services", "RemotePage.cs"));
        var used = System.Text.RegularExpressions.Regex
            .Matches(page, @"data-i18n(?:-placeholder)?=""([a-z0-9_]+)""")
            .Select(match => match.Groups[1].Value)
            .Concat(System.Text.RegularExpressions.Regex
                .Matches(page, @"\bt\('([a-z0-9_]+)'")
                .Select(match => match.Groups[1].Value))
            .Concat(System.Text.RegularExpressions.Regex
                .Matches(page, @"\btp\('([a-z0-9_]+)'")
                .Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var source = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(SourceDirectory, "en-US.json")))!;

        foreach (var key in used)
        {
            Assert.True(source.ContainsKey(key), $"localization/en-US.json missing web key {key}");
        }
    }

    private static HashSet<string> ReadReswKeys(string language)
    {
        var document = System.Xml.Linq.XDocument.Load(
            Path.Combine(Root, "src", "TempoKaraoke", "Strings", language, "Resources.resw"));
        return document.Root!.Elements("data")
            .Select(element => (string)element.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ReadLocaleKeys(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(Root, "src", "TempoKaraoke", "public", "locales", $"{language}.json")))!
            .Keys
            .ToHashSet(StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "localization")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "TempoKaraoke")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class TemporaryCatalog : IDisposable
    {
        public TemporaryCatalog()
        {
            Directory = Path.Combine(Path.GetTempPath(), "tempo-l10n-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public void Write(string file, string content) =>
            File.WriteAllText(Path.Combine(Directory, file), content);

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Best effort in the test temp folder.
            }
        }
    }

    private sealed class WebManifest
    {
        public string Fallback { get; set; } = string.Empty;

        public List<WebLanguage> Languages { get; set; } = new();
    }

    private sealed class WebLanguage
    {
        public string Tag { get; set; } = string.Empty;

        public string NativeName { get; set; } = string.Empty;
    }
}
