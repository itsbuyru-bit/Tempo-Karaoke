using System.Text.RegularExpressions;

namespace TempoKaraoke.Locales;

/// <summary>
/// BCP 47 helpers. The application only accepts "full" tags that carry both a
/// language and a region subtag (en-US, ru-RU, zh-CN). Short tags such as
/// "de", "fr" or "zh" are rejected by the manifest validator: the project uses
/// them neither as file names nor as saved settings values.
/// </summary>
public static partial class LanguageTag
{
    [GeneratedRegex(
        @"^[A-Za-z]{2,3}(-[A-Za-z]{4})?(-([A-Za-z]{2}|\d{3}))(-([A-Za-z0-9]{5,8}|\d[A-Za-z0-9]{3}))*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FullTagPattern();

    /// <summary>True when the value is a syntactically valid full BCP 47 tag.</summary>
    public static bool IsFullTag(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && FullTagPattern().IsMatch(tag.Trim());

    /// <summary>Primary language subtag: "ru-RU" → "ru", "zh-Hans-CN" → "zh".</summary>
    public static string PrimaryLanguage(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var trimmed = tag.Trim();
        var dash = trimmed.IndexOf('-');
        return dash < 0 ? trimmed : trimmed[..dash];
    }

    /// <summary>True when two tags describe the same language group (ru vs ru-RU).</summary>
    public static bool SameLanguage(string? left, string? right) =>
        !string.IsNullOrEmpty(PrimaryLanguage(left)) &&
        string.Equals(PrimaryLanguage(left), PrimaryLanguage(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>Ordinal-ignore-case tag comparison.</summary>
    public static bool Equals(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
