using System;
using System.Text;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Ключи сравнения имён. Ключ — это то, что должно совпасть у двух файлов,
/// чтобы считаться одной записью. Отображаемое имя не трогаем: ключ только
/// для сравнения, поэтому в нём можно смело выбрасывать шум.
/// </summary>
public static class NameKey
{
    private static readonly string[] Conjunctions = { "и", "and", "feat", "ft", "vs", "x" };

    /// <summary>
    /// Базовая нормализация: регистр, ё→е, пунктуация в пробел, схлопывание
    /// пробелов. Совпадает с той, что использует поиск, — иначе индекс и
    /// миграция начнут считать разное.
    /// </summary>
    public static string Normalize(string? value) => SearchService.Normalize(value);

    /// <summary>
    /// Ключ исполнителя. Дополнительно к нормализации: убирает ведущий «#»
    /// и схлопывает союзы, чтобы «Егор Крид &amp; MOLLY», «Егор Крид и MOLLY»
    /// и «Егор Крид feat. MOLLY» дали один и тот же ключ.
    /// </summary>
    public static string Artist(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsConjunction(token))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(token.TrimStart('#'));
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Ключ названия. Порядковый номер версии сюда не входит: он разбирается
    /// отдельно и добавляется в общий ключ песни как самостоятельная часть.
    /// </summary>
    public static string Title(string? value) => Normalize(value);

    /// <summary>
    /// Полный ключ записи. Две записи с одинаковым ключом — технические дубли
    /// одной версии и подлежат схлопыванию.
    /// </summary>
    public static string ForSong(string? artistKey, string? titleKey, VersionKind version, int ordinal) =>
        $"{artistKey ?? string.Empty}|{titleKey ?? string.Empty}|{(int)version}|{Math.Max(1, ordinal)}";

    /// <summary>
    /// Ключ песни без учёта версии: одна строка в поиске на все варианты
    /// (оригинал, кавер, перевод). Используется интерфейсом, не миграцией.
    /// </summary>
    public static string ForDisplay(string? artistKey, string? titleKey) =>
        $"{artistKey ?? string.Empty}|{titleKey ?? string.Empty}";

    private static bool IsConjunction(string token)
    {
        foreach (var conjunction in Conjunctions)
        {
            if (string.Equals(token, conjunction, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
