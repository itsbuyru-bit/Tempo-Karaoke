using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Поиск по медиатеке без внешнего индекса: нормализация, транслитерация,
/// исправление раскладки, устойчивость к опечаткам и ранжирование.
/// </summary>
public static class SearchService
{
    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = string.Empty, ['ы'] = "y",
        ['ь'] = string.Empty, ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    private static readonly (string Latin, string Cyrillic)[] LatinToCyrillic =
    {
        ("shch", "щ"), ("sch", "щ"), ("zh", "ж"), ("kh", "х"), ("ts", "ц"),
        ("ch", "ч"), ("sh", "ш"), ("yu", "ю"), ("ya", "я"), ("yo", "ё"),
        ("ye", "е"), ("iy", "ий"), ("y", "й"), ("j", "й"),
        ("a", "а"), ("b", "б"), ("c", "к"), ("d", "д"), ("e", "е"),
        ("f", "ф"), ("g", "г"), ("h", "х"), ("i", "и"), ("k", "к"),
        ("l", "л"), ("m", "м"), ("n", "н"), ("o", "о"), ("p", "п"),
        ("q", "к"), ("r", "р"), ("s", "с"), ("t", "т"), ("u", "у"),
        ("v", "в"), ("w", "в"), ("x", "кс"), ("z", "з"),
    };

    private static readonly Dictionary<char, char> RussianLayoutToLatin = new()
    {
        ['й'] = 'q', ['ц'] = 'w', ['у'] = 'e', ['к'] = 'r', ['е'] = 't',
        ['н'] = 'y', ['г'] = 'u', ['ш'] = 'i', ['щ'] = 'o', ['з'] = 'p',
        ['х'] = '[', ['ъ'] = ']', ['ф'] = 'a', ['ы'] = 's', ['в'] = 'd',
        ['а'] = 'f', ['п'] = 'g', ['р'] = 'h', ['о'] = 'j', ['л'] = 'k',
        ['д'] = 'l', ['ж'] = ';', ['э'] = '\'', ['я'] = 'z', ['ч'] = 'x',
        ['с'] = 'c', ['м'] = 'v', ['и'] = 'b', ['т'] = 'n', ['ь'] = 'm',
        ['б'] = ',', ['ю'] = '.', ['ё'] = '`',
    };

    private static readonly Dictionary<char, char> LatinLayoutToRussian =
        RussianLayoutToLatin.ToDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>Одна запись индекса с заранее нормализованными полями.</summary>
    public sealed class Entry
    {
        public Entry(MediaItem item)
        {
            Item = item;
            Title = Normalize(item.Title);
            Artist = Normalize(item.Artist);
            Folder = Normalize(item.Folder);
            LatinTitle = TransliterateToLatin(Title);
            LatinArtist = TransliterateToLatin(Artist);
            LatinFolder = TransliterateToLatin(Folder);
            TitleTokens = SplitTokens(Title);
            LatinTitleTokens = SplitTokens(LatinTitle);
            ArtistTokens = SplitTokens(Artist);
            LatinArtistTokens = SplitTokens(LatinArtist);
            FolderTokens = SplitTokens(Folder);
            LatinFolderTokens = SplitTokens(LatinFolder);
        }

        public MediaItem Item { get; }

        public string Title { get; }

        public string Artist { get; }

        public string Folder { get; }

        public string LatinTitle { get; }

        public string LatinArtist { get; }

        public string LatinFolder { get; }

        internal string[] TitleTokens { get; }

        internal string[] LatinTitleTokens { get; }

        internal string[] ArtistTokens { get; }

        internal string[] LatinArtistTokens { get; }

        internal string[] FolderTokens { get; }

        internal string[] LatinFolderTokens { get; }
    }

    public static List<Entry> Build(IEnumerable<MediaItem> items) =>
        items.Select(item => new Entry(item)).ToList();

    public static List<MediaItem> Search(IEnumerable<Entry> entries, string query)
    {
        var variants = BuildVariants(query);
        if (variants.Count == 0)
        {
            return new List<MediaItem>();
        }

        var scored = new List<(MediaItem Item, int Score)>();
        foreach (var entry in entries)
        {
            var best = 0;
            foreach (var (variant, allowFuzzy) in variants)
            {
                var score = Score(entry, variant, allowFuzzy);
                if (score > best)
                {
                    best = score;
                }
            }

            if (best > 0)
            {
                scored.Add((entry.Item, best));
            }
        }

        return scored
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(result => result.Item)
            .ToList();
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var raw in value.ToLowerInvariant())
        {
            var ch = raw == 'ё' ? 'е' : raw;
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(ch);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    public static string TransliterateToLatin(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(CyrillicToLatin.TryGetValue(ch, out var latin) ? latin : ch.ToString());
        }

        return builder.ToString();
    }

    public static string TransliterateToCyrillic(string value)
    {
        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var matched = false;
            foreach (var (latin, cyrillic) in LatinToCyrillic)
            {
                if (index + latin.Length > value.Length ||
                    string.Compare(value, index, latin, 0, latin.Length, StringComparison.Ordinal) != 0)
                {
                    continue;
                }

                builder.Append(cyrillic);
                index += latin.Length;
                matched = true;
                break;
            }

            if (!matched)
            {
                builder.Append(value[index]);
                index++;
            }
        }

        return builder.ToString();
    }

    private static List<(string Value, bool AllowFuzzy)> BuildVariants(string query)
    {
        var variants = new List<(string Value, bool AllowFuzzy)>();

        void Add(string? value, bool allowFuzzy)
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0)
            {
                return;
            }

            var index = variants.FindIndex(item =>
                string.Equals(item.Value, normalized, StringComparison.Ordinal));
            if (index < 0)
            {
                variants.Add((normalized, allowFuzzy));
            }
            else if (allowFuzzy)
            {
                variants[index] = (normalized, true);
            }
        }

        Add(query, true);
        Add(TransliterateToLatin(query), false);
        Add(TransliterateToCyrillic(query), false);
        Add(ConvertLayout(query, RussianLayoutToLatin), false);
        Add(ConvertLayout(query, LatinLayoutToRussian), false);

        return variants;
    }

    private static string ConvertLayout(string value, IReadOnlyDictionary<char, char> map)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            builder.Append(map.TryGetValue(ch, out var mapped) ? mapped : ch);
        }

        return builder.ToString();
    }

    private static string[] SplitTokens(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static int Score(Entry entry, string query, bool allowFuzzy)
    {
        var tokens = SplitTokens(query);
        if (tokens.Length == 0)
        {
            return 0;
        }

        var phraseScore = Math.Max(
            Math.Max(
                FieldScore(entry.Title, entry.LatinTitle, query, 5000, 4000, 3000),
                FieldScore(entry.Artist, entry.LatinArtist, query, 4000, 3000, 2000)),
            FieldScore(entry.Folder, entry.LatinFolder, query, 1200, 900, 600));

        var tokenScore = TokenScore(entry, tokens, allowFuzzy);
        return Math.Max(phraseScore, tokenScore);
    }

    private static int TokenScore(Entry entry, string[] tokens, bool allowFuzzy)
    {
        var total = 100;

        foreach (var token in tokens)
        {
            var score = Math.Max(
                Math.Max(
                    TokenFieldScore(entry.TitleTokens, entry.LatinTitleTokens, token, 500, 400, 300),
                    TokenFieldScore(entry.ArtistTokens, entry.LatinArtistTokens, token, 400, 300, 200)),
                TokenFieldScore(entry.FolderTokens, entry.LatinFolderTokens, token, 120, 90, 60));

            if (allowFuzzy && score == 0 && token.Length >= 4)
            {
                score = Math.Max(
                    Math.Max(
                        FuzzyFieldScore(entry.TitleTokens, entry.LatinTitleTokens, token, 60),
                        FuzzyFieldScore(entry.ArtistTokens, entry.LatinArtistTokens, token, 50)),
                    FuzzyFieldScore(entry.FolderTokens, entry.LatinFolderTokens, token, 20));
            }

            if (score == 0 && token.Length >= 2)
            {
                score = AbbreviationScore(entry, token);
            }

            if (score == 0)
            {
                return 0;
            }

            total += score;
        }

        return total;
    }

    /// <summary>
    /// Поиск по аббревиатурам: «алп све» → «Алла Пугачёва — Свеча».
    /// Запрос режется на куски, каждый кусок — набор префиксов соседних слов (≥1 буквы).
    /// </summary>
    private static int AbbreviationScore(Entry entry, string token)
    {
        var checks = new (string[] Words, string[] LatinWords, int Score)[]
        {
            (entry.ArtistTokens, entry.LatinArtistTokens, 320),
            (entry.TitleTokens, entry.LatinTitleTokens, 260),
            (entry.FolderTokens, entry.LatinFolderTokens, 80),
        };

        var best = 0;
        foreach (var (words, latinWords, score) in checks)
        {
            if (MatchesAbbreviation(words, token) || MatchesAbbreviation(latinWords, token))
            {
                best = Math.Max(best, score);
            }
        }

        return best;
    }

    private static bool MatchesAbbreviation(string[] words, string token)
    {
        for (var start = 0; start < words.Length; start++)
        {
            if (MatchesAbbreviation(words, start, 0, token))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAbbreviation(string[] words, int wordIndex, int tokenIndex, string token)
    {
        if (tokenIndex >= token.Length)
        {
            return true;
        }

        if (wordIndex >= words.Length)
        {
            return false;
        }

        var word = words[wordIndex];
        var limit = Math.Min(word.Length, token.Length - tokenIndex);
        for (var length = 1; length <= limit; length++)
        {
            if (string.CompareOrdinal(word, 0, token, tokenIndex, length) != 0)
            {
                break;
            }

            if (MatchesAbbreviation(words, wordIndex + 1, tokenIndex + length, token))
            {
                return true;
            }
        }

        return false;
    }

    private static int FieldScore(string value, string latinValue, string query, int exact, int prefix, int contains)
    {
        if (value == query || latinValue == query)
        {
            return exact;
        }

        if (value.StartsWith(query, StringComparison.Ordinal) ||
            latinValue.StartsWith(query, StringComparison.Ordinal))
        {
            return prefix;
        }

        if (value.Contains(query, StringComparison.Ordinal) ||
            latinValue.Contains(query, StringComparison.Ordinal))
        {
            return contains;
        }

        return 0;
    }

    private static int TokenFieldScore(
        string[] tokens,
        string[] latinTokens,
        string queryToken,
        int exact,
        int prefix,
        int contains)
    {
        var best = 0;

        foreach (var token in tokens)
        {
            best = Math.Max(best, TokenMatch(token, queryToken, exact, prefix, contains));
        }

        foreach (var token in latinTokens)
        {
            best = Math.Max(best, TokenMatch(token, queryToken, exact, prefix, contains));
        }

        return best;
    }

    private static int TokenMatch(string token, string queryToken, int exact, int prefix, int contains)
    {
        if (token == queryToken)
        {
            return exact;
        }

        if (token.StartsWith(queryToken, StringComparison.Ordinal))
        {
            return prefix;
        }

        return token.Contains(queryToken, StringComparison.Ordinal) ? contains : 0;
    }

    private static int FuzzyFieldScore(string[] tokens, string[] latinTokens, string queryToken, int score)
    {
        if (queryToken.Length < 4)
        {
            return 0;
        }

        var candidates = ContainsCyrillic(queryToken) ? tokens : latinTokens;
        var threshold = queryToken.Length >= 7 ? 2 : 1;

        foreach (var token in candidates)
        {
            if (Math.Abs(token.Length - queryToken.Length) > threshold)
            {
                continue;
            }

            if (LevenshteinDistance(token, queryToken, threshold) <= threshold)
            {
                return score;
            }
        }

        return 0;
    }

    private static bool ContainsCyrillic(string value)
    {
        foreach (var ch in value)
        {
            if (ch is >= '\u0400' and <= '\u04FF')
            {
                return true;
            }
        }

        return false;
    }

    private static int LevenshteinDistance(string left, string right, int limit)
    {
        if (left == right)
        {
            return 0;
        }

        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];

            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);

                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
