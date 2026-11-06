namespace TempoKaraoke.Locales;

/// <summary>Plural categories used by the JSON catalogs.</summary>
public static class PluralCategory
{
    public const string One = "One";
    public const string Few = "Few";
    public const string Many = "Many";
    public const string Other = "Other";

    /// <summary>
    /// Returns the CLDR category for the language. Russian requires the full
    /// One/Few/Many set; the remaining supported languages only need One/Other.
    /// </summary>
    public static string For(string? tag, int count)
    {
        var primary = LanguageTag.PrimaryLanguage(tag);
        var absolute = Math.Abs(count);

        if (string.Equals(primary, "ru", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(primary, "uk", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(primary, "be", StringComparison.OrdinalIgnoreCase))
        {
            var mod100 = absolute % 100;
            if (mod100 is >= 11 and <= 14)
            {
                return Many;
            }

            return (absolute % 10) switch
            {
                1 => One,
                2 or 3 or 4 => Few,
                _ => Many,
            };
        }

        return absolute == 1 ? One : Other;
    }

    /// <summary>Convenience helper used for the category a fallback language needs.</summary>
    public static bool RequiresFullSet(string? tag) =>
        string.Equals(LanguageTag.PrimaryLanguage(tag), "ru", StringComparison.OrdinalIgnoreCase);
}
