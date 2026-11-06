using System.Text.RegularExpressions;

namespace TempoKaraoke.Locales;

/// <summary>
/// Catalog checks shared by application startup and the automated tests.
/// These are deliberately non-fatal: the UI must start even if a translation
/// file is broken.
/// </summary>
public static partial class LocalizationValidator
{
    private static readonly string[] PluralSuffixes =
    {
        "One", "Few", "Many", "Other",
        "one", "few", "many", "other",
    };

    [GeneratedRegex(@"\{[^{}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    public static IReadOnlyList<LocalizationIssue> Validate(LocalizationCatalog catalog)
    {
        var issues = new List<LocalizationIssue>(catalog.Issues);

        if (!catalog.IsUsable)
        {
            return issues;
        }

        if (!catalog.Languages.Any(language => LanguageTag.Equals(language.Tag, catalog.FallbackTag)))
        {
            issues.Add(new LocalizationIssue(
                "fallback_catalog_missing",
                $"Fallback '{catalog.FallbackTag}' could not be loaded.",
                catalog.FallbackTag));
            return issues;
        }

        var fallback = catalog.Languages.First(language => LanguageTag.Equals(language.Tag, catalog.FallbackTag));
        foreach (var pair in fallback.Strings)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                issues.Add(new LocalizationIssue("empty_value", $"Key '{pair.Key}' has an empty value.", fallback.Tag));
            }

            if (ContainsCyrillic(pair.Value))
            {
                issues.Add(new LocalizationIssue(
                    "cyrillic_in_fallback",
                    $"Fallback key '{pair.Key}' contains Cyrillic text.",
                    fallback.Tag));
            }
        }

        foreach (var language in catalog.Languages)
        {
            if (LanguageTag.Equals(language.Tag, catalog.FallbackTag))
            {
                continue;
            }

            foreach (var pair in language.Strings)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    issues.Add(new LocalizationIssue(
                        "empty_value",
                        $"Key '{pair.Key}' has an empty value.",
                        language.Tag));
                    continue;
                }

                if (!fallback.Strings.TryGetValue(pair.Key, out var fallbackValue))
                {
                    issues.Add(new LocalizationIssue(
                        "orphan_key",
                        $"Key '{pair.Key}' does not exist in {fallback.Tag}.",
                        language.Tag));
                    continue;
                }

                var expected = Placeholders(fallbackValue);
                var actual = Placeholders(pair.Value);
                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                {
                    issues.Add(new LocalizationIssue(
                        "placeholder_mismatch",
                        $"Key '{pair.Key}' placeholders [{string.Join(", ", actual)}] differ from " +
                        $"{fallback.Tag} [{string.Join(", ", expected)}].",
                        language.Tag));
                }
            }

            ValidatePluralFamilies(language, fallback, issues);
        }

        return issues;
    }

    public static IReadOnlyList<string> Placeholders(string value) =>
        PlaceholderPattern().Matches(value)
            .Select(match => match.Value)
            .ToList();

    public static bool ContainsCyrillic(string value) =>
        value.Any(character =>
            character is >= 'А' and <= 'я' or 'Ё' or 'ё');

    /// <summary>
    /// Web and desktop expose different key surfaces. Both must keep the full
    /// key set of their own surface when a language is added.
    /// </summary>
    public static IReadOnlyList<string> FindMissingKeys(
        IEnumerable<string> expectedKeys,
        IEnumerable<string> actualKeys)
    {
        var actual = actualKeys.ToHashSet(StringComparer.Ordinal);
        return expectedKeys
            .Where(key => !actual.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A key is a web key when it is lowercase snake_case without a property suffix.</summary>
    public static bool IsWebKey(string key) =>
        !key.Contains('.', StringComparison.Ordinal) &&
        !key.Any(char.IsUpper);

    private static void ValidatePluralFamilies(
        LocalizationLanguage language,
        LocalizationLanguage fallback,
        List<LocalizationIssue> issues)
    {
        var families = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in fallback.Strings.Keys)
        {
            foreach (var suffix in PluralSuffixes)
            {
                var marker = "_" + suffix;
                if (key.EndsWith(marker, StringComparison.Ordinal) && key.Length > marker.Length)
                {
                    families.Add(key[..^marker.Length]);
                }
            }
        }

        foreach (var family in families)
        {
            var hasOne = language.Strings.ContainsKey(family + "_One") ||
                         language.Strings.ContainsKey(family + "_one");
            var hasFew = language.Strings.ContainsKey(family + "_Few") ||
                         language.Strings.ContainsKey(family + "_few");
            var hasMany = language.Strings.ContainsKey(family + "_Many") ||
                          language.Strings.ContainsKey(family + "_many");
            var hasOther = language.Strings.ContainsKey(family + "_Other") ||
                           language.Strings.ContainsKey(family + "_other");

            if (!hasOne || !hasOther)
            {
                issues.Add(new LocalizationIssue(
                    "plural_categories_missing",
                    $"Plural family '{family}' must contain One and Other.",
                    language.Tag));
            }

            if (PluralCategory.RequiresFullSet(language.Tag) && (!hasFew || !hasMany))
            {
                issues.Add(new LocalizationIssue(
                    "plural_categories_missing",
                    $"Plural family '{family}' must contain One, Few, Many and Other for {language.Tag}.",
                    language.Tag));
            }
        }
    }
}
