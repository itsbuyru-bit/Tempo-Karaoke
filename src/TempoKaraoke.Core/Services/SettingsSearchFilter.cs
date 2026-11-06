using System;

namespace TempoKaraoke.Core.Services;

/// <summary>Фильтр поиска по настройкам: общий для UI и тестов.</summary>
public static class SettingsSearchFilter
{
    /// <summary>Пустой запрос показывает всё; иначе — подстрочное совпадение без учёта регистра.</summary>
    public static bool Matches(string? title, string? description, string? query)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return true;
        }

        return Contains(title, text) || Contains(description, text);
    }

    private static bool Contains(string? source, string query) =>
        !string.IsNullOrEmpty(source) &&
        source.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
