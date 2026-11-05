using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TempoKaraoke.Models;

/// <summary>
/// Убирает из отображаемого названия служебные пометки караоке-файлов:
/// «караоке», «кавер минус», «текст песни», «mood video» и похожие.
/// Исходное имя файла и путь не меняются.
/// </summary>
public static class MediaTitleCleaner
{
    private static readonly string[] MarkerPhrases =
    {
        "экстремальное караоке",
        "караоке версия",
        "karaoke version",
        "официальное видео",
        "official video",
        "пой под бит",
        "кавер минус",
        "кавер версия",
        "текст песни",
        "mood video",
        "караоке",
        "кавер",
        "минус",
        "remix",
        "клип",
        "live",
        "version",
        "lyrics",
        "хиты 2023",
        "хиты 2024",
    };

    // Одна регулярка вместо 20 проходов по строке. Длинные фразы идут первыми,
    // чтобы «кавер минус» не был съеден коротким «кавер».
    private static readonly Regex Markers = new(
        $@"\b(?:{string.Join("|", MarkerPhrases.OrderByDescending(p => p.Length).Select(Regex.Escape))})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex YearInBrackets = new(
        @"\(\s*(?:клип\s*)?(?:19|20)\d{2}\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmptyBrackets = new(
        @"[\[\(]\s*[\]\)]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Spaces = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = YearInBrackets.Replace(value, " ");
        result = Markers.Replace(result, " ");
        result = EmptyBrackets.Replace(result, " ");
        result = Spaces.Replace(result, " ").Trim(' ', '—', '-', '|', '·', ',', ':');

        return result.Length > 0 ? result : value.Trim();
    }
}
