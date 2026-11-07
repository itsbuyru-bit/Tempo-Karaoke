using System;
using System.Globalization;

namespace TempoKaraoke.Services;

/// <summary>
/// Resolves toast strings before the XML is built. The toast payload cannot use
/// x:Uid or ms-resource, so every value must already be plain text here.
/// Order: LocalizationService (JSON catalogs, works in portable mode) →
/// Windows ResourceLoader (packaged .resw) → literal fallback. A missing key is
/// never returned to the user.
/// </summary>
public static class UpdateToastLocalization
{
    public static string Get(string key, string ruFallback, string enFallback)
    {
        var fallback = IsRussian ? ruFallback : enFallback;
        return Resolve(key) ?? fallback;
    }

    public static string Format(string key, string ruFallback, string enFallback, params object[] args)
    {
        var fallback = IsRussian ? ruFallback : enFallback;
        var template = Resolve(key) ?? fallback;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException ex)
        {
            // A broken translation must not crash the notification path.
            LogService.Warn($"Invalid toast format for '{key}': {ex.Message}");
            try
            {
                return string.Format(CultureInfo.CurrentCulture, fallback, args);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }
    }

    private static bool IsRussian =>
        LocalizationService.CurrentLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    private static string? Resolve(string key)
    {
        try
        {
            var value = LocalizationService.Get(key);
            if (IsUsable(key, value))
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Toast localization failed for '{key}': {ex.Message}");
        }

        try
        {
            var value = Windows.ApplicationModel.Resources.ResourceLoader
                .GetForViewIndependentUse()
                .GetString(key);
            if (IsUsable(key, value))
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"ResourceLoader toast lookup failed for '{key}': {ex.Message}");
        }

        return null;
    }

    private static bool IsUsable(string key, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, key, StringComparison.Ordinal)
        && !value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase);
}
