using System.Text.Json;

namespace TempoKaraoke.Locales;

/// <summary>A resolved string together with the information a caller needs for diagnostics.</summary>
public readonly record struct LocalizationLookup(string Value, bool Found, bool FromFallback);

/// <summary>A language that is present in the manifest and has a readable catalog.</summary>
public sealed class LocalizationLanguage
{
    public LocalizationLanguage(
        string tag,
        string nativeName,
        string englishName,
        string file,
        IReadOnlyDictionary<string, string> strings)
    {
        Tag = tag;
        NativeName = nativeName;
        EnglishName = englishName;
        File = file;
        Strings = strings;
    }

    public string Tag { get; }

    public string NativeName { get; }

    public string EnglishName { get; }

    public string File { get; }

    public IReadOnlyDictionary<string, string> Strings { get; }
}

/// <summary>
/// Loads localization/manifest.json and the flat JSON catalogs next to it.
/// The catalog never throws: malformed entries are reported through
/// <see cref="Issues"/> and the remaining languages keep working.
/// </summary>
public sealed class LocalizationCatalog
{
    public const string DefaultLanguage = "en-US";

    private static readonly StringComparer TagComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, LocalizationLanguage> _languages;

    private LocalizationCatalog(
        string directory,
        LocalizationManifest? manifest,
        string fallbackTag,
        IReadOnlyList<LocalizationLanguage> languages,
        IReadOnlyList<LocalizationIssue> issues)
    {
        Directory = directory;
        Manifest = manifest;
        FallbackTag = fallbackTag;
        Languages = languages;
        Issues = issues;
        _languages = languages.ToDictionary(language => language.Tag, TagComparer);
    }

    public string Directory { get; }

    public LocalizationManifest? Manifest { get; }

    public string FallbackTag { get; }

    /// <summary>Enabled languages with a readable catalog, in manifest order.</summary>
    public IReadOnlyList<LocalizationLanguage> Languages { get; }

    public IReadOnlyList<LocalizationIssue> Issues { get; }

    public bool IsUsable => _languages.ContainsKey(FallbackTag);

    public static LocalizationCatalog Load(string directory)
    {
        var result = LocalizationManifestLoader.Load(directory);
        var issues = new List<LocalizationIssue>(result.Issues);
        var languages = new List<LocalizationLanguage>();

        if (result.Manifest is null)
        {
            return new LocalizationCatalog(directory, null, DefaultLanguage, languages, issues);
        }

        var fallbackTag = result.Manifest.Fallback;
        foreach (var entry in result.Manifest.Languages)
        {
            if (!entry.Enabled)
            {
                continue;
            }

            var path = Path.Combine(directory, entry.File);
            if (!File.Exists(path))
            {
                continue;
            }

            if (!TryReadFlatJson(path, out var strings, out var error))
            {
                issues.Add(new LocalizationIssue("catalog_invalid_json", error, entry.Tag));
                continue;
            }

            if (strings.Count == 0)
            {
                issues.Add(new LocalizationIssue("catalog_empty", "Catalog has no strings.", entry.Tag));
                continue;
            }

            languages.Add(new LocalizationLanguage(
                entry.Tag,
                entry.NativeName,
                entry.EnglishName,
                entry.File,
                strings));
        }

        if (!languages.Any(language => TagComparer.Equals(language.Tag, fallbackTag)))
        {
            issues.Add(new LocalizationIssue(
                "fallback_catalog_missing",
                $"Fallback catalog '{fallbackTag}.json' could not be loaded.",
                fallbackTag));
        }

        return new LocalizationCatalog(
            directory,
            result.Manifest,
            fallbackTag,
            languages,
            issues);
    }

    /// <summary>Resolves an arbitrary tag to an enabled manifest tag, or the fallback.</summary>
    public string ResolveTag(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var trimmed = preferred.Trim();
            var exact = Languages.FirstOrDefault(language => TagComparer.Equals(language.Tag, trimmed));
            if (exact is not null)
            {
                return exact.Tag;
            }

            var primary = LanguageTag.PrimaryLanguage(trimmed);
            if (primary.Length > 0)
            {
                var group = Languages.FirstOrDefault(language =>
                    string.Equals(LanguageTag.PrimaryLanguage(language.Tag), primary, StringComparison.OrdinalIgnoreCase));
                if (group is not null)
                {
                    return group.Tag;
                }
            }
        }

        return FallbackTag;
    }

    public bool TryGetLanguage(string? tag, out LocalizationLanguage language)
    {
        if (!string.IsNullOrWhiteSpace(tag) && _languages.TryGetValue(tag.Trim(), out var found))
        {
            language = found;
            return true;
        }

        if (_languages.TryGetValue(FallbackTag, out var fallback))
        {
            language = fallback;
            return true;
        }

        language = null!;
        return false;
    }

    /// <summary>
    /// Per-key fallback: current language first, then the fallback language.
    /// A missing key is reported as not found; the caller decides how to degrade.
    /// </summary>
    public LocalizationLookup Lookup(string? tag, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new LocalizationLookup(string.Empty, true, false);
        }

        if (TryGetLanguage(tag, out var current) &&
            current.Strings.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return new LocalizationLookup(value, true, false);
        }

        if (_languages.TryGetValue(FallbackTag, out var fallback) &&
            fallback.Strings.TryGetValue(key, out var fallbackValue) &&
            !string.IsNullOrWhiteSpace(fallbackValue))
        {
            return new LocalizationLookup(fallbackValue, true, true);
        }

        return new LocalizationLookup(key, false, false);
    }

    /// <summary>
    /// Plural lookup with per-key fallback. If the requested category is absent,
    /// the "Other" form of the same language is used, then the fallback language.
    /// </summary>
    public LocalizationLookup LookupPlural(string? tag, string keyBase, int count)
    {
        var resolved = ResolveTag(tag);
        var category = PluralCategory.For(resolved, count);
        var candidates = new[] { $"{keyBase}_{category}", $"{keyBase}_{PluralCategory.Other}" };

        if (_languages.TryGetValue(resolved, out var current))
        {
            foreach (var candidate in candidates)
            {
                if (current.Strings.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return new LocalizationLookup(value, true, false);
                }
            }
        }

        if (_languages.TryGetValue(FallbackTag, out var fallback))
        {
            var fallbackCategory = PluralCategory.For(FallbackTag, count);
            foreach (var candidate in new[] { $"{keyBase}_{fallbackCategory}", $"{keyBase}_{PluralCategory.Other}" })
            {
                if (fallback.Strings.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return new LocalizationLookup(value, true, true);
                }
            }
        }

        return new LocalizationLookup($"{keyBase}_{category}", false, false);
    }

    public string Get(string? tag, string key) => Lookup(tag, key).Value;

    public string GetPlural(string? tag, string keyBase, int count) => LookupPlural(tag, keyBase, count).Value;

    private static bool TryReadFlatJson(
        string path,
        out IReadOnlyDictionary<string, string> strings,
        out string error)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                strings = values;
                error = "Root element must be an object of key/value pairs.";
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                values[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            strings = values;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            strings = values;
            error = ex.Message;
            return false;
        }
    }
}
