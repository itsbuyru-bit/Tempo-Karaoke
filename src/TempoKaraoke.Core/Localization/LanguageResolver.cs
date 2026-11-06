namespace TempoKaraoke.Locales;

public enum LanguageMatch
{
    Saved,
    SystemExact,
    SystemLanguage,
    Fallback,
}

public readonly record struct LanguageResolution(string Tag, LanguageMatch Match)
{
    /// <summary>True when the app picked the language itself and should persist it.</summary>
    public bool ShouldPersist => Match != LanguageMatch.Saved;
}

/// <summary>
/// Startup language resolution order:
/// saved user choice → operating-system language → best language-group match →
/// fallback (en-US). Kept UI-independent so it can be unit tested.
/// </summary>
public static class LanguageResolver
{
    public static LanguageResolution Resolve(
        LocalizationCatalog catalog,
        string? savedLanguage,
        IEnumerable<string>? systemLanguages)
    {
        if (!string.IsNullOrWhiteSpace(savedLanguage))
        {
            var saved = catalog.ResolveTag(savedLanguage);
            if (LanguageTag.Equals(saved, savedLanguage) ||
                LanguageTag.SameLanguage(saved, savedLanguage))
            {
                // A short saved value ("ru") also counts as a user choice when the
                // manifest contains a matching full tag.
                return new LanguageResolution(saved, LanguageMatch.Saved);
            }
        }

        foreach (var systemLanguage in systemLanguages ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(systemLanguage))
            {
                continue;
            }

            var match = catalog.ResolveTag(systemLanguage);
            if (LanguageTag.Equals(match, catalog.FallbackTag) &&
                !LanguageTag.SameLanguage(systemLanguage, catalog.FallbackTag))
            {
                continue;
            }

            return new LanguageResolution(
                match,
                LanguageTag.Equals(match, systemLanguage)
                    ? LanguageMatch.SystemExact
                    : LanguageMatch.SystemLanguage);
        }

        return new LanguageResolution(catalog.FallbackTag, LanguageMatch.Fallback);
    }
}
