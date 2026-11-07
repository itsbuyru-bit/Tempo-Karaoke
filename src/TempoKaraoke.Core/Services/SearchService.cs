using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Поиск по медиатеке без внешнего индекса: нормализация, транслитерация,
/// исправление раскладки, устойчивость к опечаткам и ранжирование.
/// </summary>
public static class SearchService
{
    public static SearchScoringOptions Options { get; set; } = SearchScoringOptions.Default;

    public static void ReloadScoringOptions(string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (File.Exists(path))
                {
                    var explicitOptions = JsonSerializer.Deserialize<SearchScoringOptions>(
                        File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (explicitOptions is not null)
                    {
                        Options = explicitOptions;
                        return;
                    }
                }
            }
            catch
            {
                // Fall through to defaults for an explicit, broken config.
            }

            Options = SearchScoringOptions.Default;
            return;
        }

        var candidates = new List<string>();
        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TempoKaraoke",
            "search_config.json"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "search_config.json"));

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<SearchScoringOptions>(
                    File.ReadAllText(candidate),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded is not null)
                {
                    Options = loaded;
                    return;
                }
            }
            catch
            {
                // A malformed user config never disables search defaults.
            }
        }

        Options = SearchScoringOptions.Default;
    }

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

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "караоке", "минус", "минусовка", "песня", "клип", "текст",
        "karaoke", "minus", "бэк", "бэквокал", "вокал", "back", "live", "лайв",
    };

    private static readonly string[] RussianSuffixes =
    {
        "ого", "ему", "ых", "их", "ой", "ей", "ом", "ов", "ам", "ах",
        "ая", "яя", "ы", "и", "а", "у", "ю",
    };

    // Canonical form is the first item. Every alias is normalized before matching.
    public static readonly string[][] AliasGroups =
    {
        new[] { "король и шут", "киш", "киша" },
        new[] { "bi 2", "би 2", "би2", "б2", "bi2" },
        new[] { "2маши", "две маши" },
        new[] { "tatu", "тату" },
        new[] { "rammstein", "рамштайн" },
        new[] { "little big", "литл биг" },
        new[] { "ac dc", "эйси диси", "ас дс" },
        new[] { "prodigy", "продиджи" },
        new[] { "boney m", "бони эм", "бони м" },
        new[] { "modern talking", "модерн токинг", "модерн токин" },
    };

    private static (Regex Pattern, string Canonical)[] _aliasPatterns =
        BuildAliasPatterns(AliasGroups);

    public static void ReloadAliases(string? path = null)
    {
        var groups = AliasGroups.Select(group => group.ToArray()).ToList();
        var file = path ?? FindAliasFile();
        if (file is not null && File.Exists(file))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                if (document.RootElement.TryGetProperty("groups", out var external) &&
                    external.ValueKind == JsonValueKind.Array)
                {
                    foreach (var group in external.EnumerateArray())
                    {
                        if (group.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        var values = group.EnumerateArray()
                            .Where(value => value.ValueKind == JsonValueKind.String)
                            .Select(value => Normalize(value.GetString()))
                            .Where(value => value.Length > 0)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();
                        if (values.Length > 1)
                        {
                            groups.Add(values);
                        }
                    }
                }
            }
            catch
            {
                // A broken external dictionary never disables the built-in aliases.
            }
        }

        _aliasPatterns = BuildAliasPatterns(groups);
    }

    private static (Regex Pattern, string Canonical)[] BuildAliasPatterns(
        IEnumerable<string[]> groups) =>
        groups
            .Where(group => group.Length > 1)
            .SelectMany(group => group.Skip(1).Select(alias => (
                Pattern: BuildAliasRegex(Normalize(alias)),
                Canonical: Normalize(group[0]))))
            .OrderByDescending(item => item.Pattern.ToString().Length)
            .ToArray();

    private static string? FindAliasFile()
    {
        var local = AliasFilePath;
        if (File.Exists(local))
        {
            return local;
        }

        var nearExe = Path.Combine(AppContext.BaseDirectory, "search_aliases.json");
        return File.Exists(nearExe) ? nearExe : null;
    }

    public static string AliasFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TempoKaraoke",
        "search_aliases.json");

    public static string ScoringConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TempoKaraoke",
        "search_config.json");

    /// <summary>Creates a documented, editable scoring config once.</summary>
    public static void EnsureScoringTemplate()
    {
        try
        {
            var directory = Path.GetDirectoryName(ScoringConfigPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(ScoringConfigPath))
            {
                File.WriteAllText(
                    ScoringConfigPath,
                    JsonSerializer.Serialize(
                        SearchScoringOptions.Default,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        }));
            }

            ReloadScoringOptions();
        }
        catch
        {
            // The hardcoded defaults remain active.
        }
    }

    /// <summary>Creates a documented user-editable alias template once.</summary>
    public static void EnsureAliasTemplate()
    {
        try
        {
            var directory = Path.GetDirectoryName(AliasFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(AliasFilePath))
            {
                File.WriteAllText(
                    AliasFilePath,
                    "{\n  \"groups\": [\n    [\"киш\", \"киша\", \"король и шут\"],\n    [\"би2\", \"б2\", \"би 2\", \"bi 2\"]\n  ]\n}\n");
            }

            ReloadAliases();
        }
        catch
        {
            // The built-in aliases remain active when the file cannot be created.
        }
    }

    private static readonly Regex TechnicalTag = new(
        @"\b(?:karaoke|караоке|back\s*vocal|бэк[\s-]*вокал|бэк|минусовка|minus|"
        + @"original|оригинал|full\s*hd|uhd|4k|1080p|720p|480p|hd)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingTrackNumber = new(
        @"^\s*\d{1,4}\s*[._-]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RepeatedConsonant = new(
        @"([bcdfghjklmnpqrstvwxyz])\1+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Одна запись индекса с заранее нормализованными полями.</summary>
    public sealed class Entry
    {
        public Entry(MediaItem item)
        {
            Item = item;

            var fileName = Path.GetFileName(item.Path);
            var parsed = !string.IsNullOrWhiteSpace(fileName)
                ? MediaNameParser.Parse(fileName, item.Folder)
                : ParsedMediaName.Empty;

            var title = !string.IsNullOrWhiteSpace(parsed.Title) ? parsed.Title : item.Title;
            var artist = !string.IsNullOrWhiteSpace(parsed.Artist) ? parsed.Artist : item.Artist;
            var rawFileName = Path.GetFileNameWithoutExtension(item.Path);

            RawName = PrepareRaw(rawFileName);
            Title = PrepareField(title);
            Artist = PrepareField(artist);
            Folder = PrepareField(item.Folder);
            LatinTitle = TransliterateToLatin(Title);
            LatinArtist = TransliterateToLatin(Artist);
            LatinFolder = TransliterateToLatin(Folder);
            RawTokens = CreateTokens(RawName);
            TitleTokens = CreateTokens(Title);
            LatinTitleTokens = CreateTokens(LatinTitle);
            ArtistTokens = CreateTokens(Artist);
            LatinArtistTokens = CreateTokens(LatinArtist);
            FolderTokens = CreateTokens(Folder);
            LatinFolderTokens = CreateTokens(LatinFolder);
            RawStems = CreateStems(RawName);
            TitleStems = CreateStems(Title);
            ArtistStems = CreateStems(Artist);
            FolderStems = CreateStems(Folder);
            PhoneticTitleTokens = CreateTokens(LatinTitle).Select(PhoneticKey).ToArray();
            PhoneticArtistTokens = CreateTokens(LatinArtist).Select(PhoneticKey).ToArray();
            PhoneticFolderTokens = CreateTokens(LatinFolder).Select(PhoneticKey).ToArray();
            JoinedTitleTokens = JoinTokens(CreateTokens(Title));
            JoinedArtistTokens = JoinTokens(CreateTokens(Artist));
            JoinedRawTokens = JoinTokens(CreateTokens(RawName));
            CyrillicPhoneticTitleTokens = CreateTokens(Title).Select(CyrillicPhoneticKey).ToArray();
            CyrillicPhoneticArtistTokens = CreateTokens(Artist).Select(CyrillicPhoneticKey).ToArray();
        }

        public MediaItem Item { get; }

        public string RawName { get; }

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

        internal string[] RawTokens { get; }

        internal string[] RawStems { get; }

        internal string[] TitleStems { get; }

        internal string[] ArtistStems { get; }

        internal string[] FolderStems { get; }

        internal string[] JoinedTitleTokens { get; }

        internal string[] JoinedArtistTokens { get; }

        internal string[] JoinedRawTokens { get; }

        internal string[] CyrillicPhoneticTitleTokens { get; }

        internal string[] CyrillicPhoneticArtistTokens { get; }

        internal string[] PhoneticTitleTokens { get; }

        internal string[] PhoneticArtistTokens { get; }

        internal string[] PhoneticFolderTokens { get; }
    }

    public static List<Entry> Build(IEnumerable<MediaItem> items) =>
        items.Select(item => new Entry(item)).ToList();

    public static List<MediaItem> Search(IEnumerable<Entry> entries, string query)
    {
        var variants = BuildVariants(query);
        if (variants.Count == 0)
        {
            var raw = Normalize(query);
            return raw.Length > 2
                ? SearchRaw(entries, raw)
                : new List<MediaItem>();
        }

        var strict = ScoreEntries(entries, variants, allowPartial: false);
        var results = strict;
        var maxTokens = variants.Max(variant => CreateTokens(variant.Value).Length);

        // Long queries often contain one extra word from a file name or a guest's
        // description. Keep strict results first, then add N-1 matches.
        if (strict.Count < Options.StrictResultThreshold && maxTokens >= 3)
        {
            var relaxed = ScoreEntries(entries, variants, allowPartial: true);
            var known = new HashSet<string>(
                results.Select(result => result.Item.Path),
                StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in relaxed)
            {
                if (!known.Add(candidate.Item.Path))
                {
                    continue;
                }

                results.Add(new Scored(
                    candidate.Item,
                    Math.Max(1, (int)Math.Round(
                        candidate.Score *
                        (maxTokens >= Options.FreeTextMinTokens
                            ? Options.FreeTextMultiplier
                            : Options.PartialFallbackMultiplier)))));
            }
        }

        return FinalizeResults(results, query);
    }

    private static List<MediaItem> FinalizeResults(List<Scored> results, string query)
    {
        var queryTokens = CreateTokens(NormalizeQuery(query));
        if (queryTokens.Length == 0)
        {
            queryTokens = CreateTokens(Normalize(query));
        }

        var artistMode = queryTokens.Length is > 0 and <= 2 &&
                         results.Count(result => ArtistMatches(result.Item, queryTokens)) >= 3;

        var ordered = artistMode
            ? results
                .OrderByDescending(result => PopularityBonus(result.Item))
                .ThenBy(result => result.Item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
                .Select(result => result.Item)
                .ToList()
            : results
                .OrderByDescending(result => result.Score)
                .ThenByDescending(result => PopularityBonus(result.Item))
                .ThenBy(result => result.Item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
                .Select(result => result.Item)
                .ToList();

        return artistMode ? ordered : ApplyDiversityCap(ordered);
    }

    private static bool ArtistMatches(MediaItem item, string[] queryTokens)
    {
        var artistTokens = CreateTokens(PrepareField(item.Artist));
        return queryTokens.All(query =>
            artistTokens.Any(token => string.Equals(token, query, StringComparison.Ordinal)));
    }

    private static List<MediaItem> ApplyDiversityCap(List<MediaItem> items)
    {
        var result = new List<MediaItem>(items.Count);
        var deferred = new List<MediaItem>();
        var lastArtist = string.Empty;
        var run = 0;

        foreach (var item in items)
        {
            var artist = PrepareField(item.Artist);
            if (artist.Length > 0 &&
                string.Equals(artist, lastArtist, StringComparison.Ordinal) &&
                run >= Options.DiversityCap)
            {
                deferred.Add(item);
                continue;
            }

            if (artist.Length > 0 && string.Equals(artist, lastArtist, StringComparison.Ordinal))
            {
                run++;
            }
            else
            {
                lastArtist = artist;
                run = 1;
            }

            result.Add(item);
        }

        result.AddRange(deferred);
        return result;
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
            if (char.IsControl(ch) || ch == '\uFEFF' || ch == '\u200B' || ch == '\u200C' || ch == '\u200D')
            {
                continue;
            }

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

    /// <summary>True when a non-empty query consists only of karaoke stop words.</summary>
    public static bool IsStopWordOnlyQuery(string? query)
    {
        var normalized = Normalize(query);
        return normalized.Length > 0 && NormalizeQuery(query).Length == 0;
    }

    private static List<MediaItem> SearchRaw(IEnumerable<Entry> entries, string query)
    {
        var tokens = CreateTokens(query);
        if (tokens.Length == 0)
        {
            return new List<MediaItem>();
        }

        var results = new List<Scored>();
        foreach (var entry in entries)
        {
            var phrase = FieldScore(entry.RawName, entry.RawName, query, Options.RawPhraseExact, Options.RawPhrasePrefix, Options.RawPhraseContains);
            var token = 0;
            foreach (var item in tokens)
            {
                token = Math.Max(
                    token,
                    Math.Max(
                        TokenFieldScore(entry.RawTokens, entry.RawTokens, item, Options.RawTokenExact, Options.RawTokenPrefix, Options.RawTokenContains),
                        TokenFieldScore(entry.FolderTokens, entry.LatinFolderTokens, item, Options.FolderTokenExact, Options.FolderTokenPrefix, Options.FolderTokenContains)));
            }

            var score = Math.Max(phrase, token);
            if (score > 0)
            {
                results.Add(new Scored(entry.Item, score));
            }
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => PopularityBonus(result.Item))
            .ThenBy(result => result.Item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(result => result.Item)
            .ToList();
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

    private static string PrepareField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = TechnicalTag.Replace(MediaTitleCleaner.Clean(value), " ");
        var normalized = ApplyAliases(Normalize(cleaned));
        return normalized.Length > Options.MaxFieldLength
            ? normalized[..Options.MaxFieldLength].TrimEnd()
            : normalized;
    }

    private static string PrepareRaw(string? value)
    {
        var normalized = ApplyAliases(Normalize(value));
        return normalized.Length > Options.MaxFieldLength
            ? normalized[..Options.MaxFieldLength].TrimEnd()
            : normalized;
    }

    private static string[] CreateTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static string[] CreateStems(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(StemToken)
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static string[] JoinTokens(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            return Array.Empty<string>();
        }

        var joined = new string[tokens.Length - 1];
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            joined[index] = tokens[index] + tokens[index + 1];
        }

        return joined;
    }

    private static string StemToken(string token)
    {
        if (token.Length < Options.StemMinLength || !ContainsCyrillic(token))
        {
            return token;
        }

        foreach (var suffix in RussianSuffixes)
        {
            if (token.EndsWith(suffix, StringComparison.Ordinal) &&
                token.Length - suffix.Length >= Options.StemMinLength)
            {
                return token[..^suffix.Length];
            }
        }

        return token;
    }

    private static string NormalizeQuery(string? query)
    {
        var normalized = Normalize(query);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var tokens = SplitTokens(normalized)
            .Where(token => !StopWords.Contains(token))
            .ToArray();
        return ApplyAliases(string.Join(" ", tokens));
    }

    private static string ApplyAliases(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        foreach (var (pattern, canonical) in _aliasPatterns)
        {
            value = pattern.Replace(value, canonical);
        }

        return value;
    }

    private static Regex BuildAliasRegex(string alias) =>
        new(
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static List<QueryVariant> BuildVariants(string query)
    {
        var variants = new List<QueryVariant>();
        var normalized = NormalizeQuery(query);
        if (normalized.Length == 0)
        {
            return variants;
        }

        void Add(string? value, double fuzzyMultiplier)
        {
            var candidate = NormalizeQuery(value);
            if (candidate.Length == 0)
            {
                return;
            }

            var index = variants.FindIndex(item =>
                string.Equals(item.Value, candidate, StringComparison.Ordinal));
            if (index < 0)
            {
                variants.Add(new QueryVariant(candidate, fuzzyMultiplier));
            }
            else if (fuzzyMultiplier > variants[index].FuzzyMultiplier)
            {
                variants[index] = new QueryVariant(candidate, fuzzyMultiplier);
            }
        }

        Add(normalized, 1.0);
        Add(TransliterateToLatin(normalized), Options.FuzzyVariantMultiplier);
        Add(TransliterateToCyrillic(normalized), Options.FuzzyVariantMultiplier);
        Add(ConvertLayout(normalized, RussianLayoutToLatin), Options.FuzzyVariantMultiplier);
        Add(ConvertLayout(normalized, LatinLayoutToRussian), Options.FuzzyVariantMultiplier);

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

    private static List<Scored> ScoreEntries(
        IEnumerable<Entry> entries,
        IReadOnlyList<QueryVariant> variants,
        bool allowPartial)
    {
        var results = new Dictionary<string, Scored>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var best = 0;
            foreach (var variant in variants)
            {
                var score = Score(entry, variant, allowPartial);
                if (score > best)
                {
                    best = score;
                }
            }

            if (best > 0)
            {
                results[entry.Item.Path] = new Scored(entry.Item, best);
            }
        }

        return results.Values.ToList();
    }

    private static int Score(Entry entry, QueryVariant variant, bool allowPartial)
    {
        var tokens = CreateTokens(variant.Value);
        if (tokens.Length == 0)
        {
            return 0;
        }

        var options = Options;
        var phraseScore = Math.Max(
            Math.Max(
                FieldScore(entry.Title, entry.LatinTitle, variant.Value, options.TitleExact, options.TitlePrefix, options.TitleContains),
                FieldScore(entry.Artist, entry.LatinArtist, variant.Value, options.ArtistExact, options.ArtistPrefix, options.ArtistContains)),
            FieldScore(entry.Folder, entry.LatinFolder, variant.Value, options.FolderExact, options.FolderPrefix, options.FolderContains));

        var tokenScore = TokenScore(entry, tokens, variant.FuzzyMultiplier, allowPartial);
        return Math.Max(phraseScore, tokenScore);
    }

    private static int TokenScore(
        Entry entry,
        string[] tokens,
        double fuzzyMultiplier,
        bool allowPartial)
    {
        var total = Options.TokenBase;
        var maxMissing = allowPartial
            ? (tokens.Length >= Options.FreeTextMinTokens
                ? Math.Max(Options.FallbackMaxMissing, tokens.Length - (int)Math.Ceiling(tokens.Length * Options.FreeTextMinRatio))
                : tokens.Length >= Options.StrictResultThreshold ? Options.FallbackMaxMissing : 0)
            : 0;
        var missing = 0;

        foreach (var token in tokens)
        {
            var options = Options;
            var score = Math.Max(
                Math.Max(
                    PositionalTokenFieldScore(entry.TitleTokens, entry.LatinTitleTokens, token, options.TitleTokenExact, options.TitleTokenPrefix, options.TitleTokenContains, tokens.Length),
                    PositionalTokenFieldScore(entry.ArtistTokens, entry.LatinArtistTokens, token, options.ArtistTokenExact, options.ArtistTokenPrefix, options.ArtistTokenContains, tokens.Length)),
                TokenFieldScore(entry.FolderTokens, entry.LatinFolderTokens, token, options.FolderTokenExact, options.FolderTokenPrefix, options.FolderTokenContains));

            var phoneticToken = PhoneticKey(token);
            if (phoneticToken.Length > 0)
            {
                score = Math.Max(
                    score,
                    Math.Max(
                        TokenFieldScore(
                            entry.PhoneticTitleTokens,
                            entry.PhoneticTitleTokens,
                            phoneticToken,
                            options.PhoneticTitleExact, options.PhoneticTitlePrefix, options.PhoneticTitleContains),
                        TokenFieldScore(
                            entry.PhoneticArtistTokens,
                            entry.PhoneticArtistTokens,
                            phoneticToken,
                            options.PhoneticArtistExact, options.PhoneticArtistPrefix, options.PhoneticArtistContains)));
            }

            score = Math.Max(
                score,
                TokenFieldScore(entry.RawTokens, entry.RawTokens, token, options.RawTokenExact, options.RawTokenPrefix, options.RawTokenContains));
            score = Math.Max(
                score,
                Math.Max(
                    TokenFieldScore(entry.JoinedTitleTokens, entry.JoinedTitleTokens, token, options.JoinedTitleExact, options.JoinedTitlePrefix, options.JoinedTitleContains),
                    TokenFieldScore(entry.JoinedArtistTokens, entry.JoinedArtistTokens, token, options.JoinedArtistExact, options.JoinedArtistPrefix, options.JoinedArtistContains)));

            var cyrillicPhonetic = CyrillicPhoneticKey(token);
            if (cyrillicPhonetic.Length > 0)
            {
                var cyrillicScore = Math.Max(
                    TokenFieldScore(
                        entry.CyrillicPhoneticTitleTokens,
                        entry.CyrillicPhoneticTitleTokens,
                        cyrillicPhonetic,
                        options.PhoneticTitleExact, options.PhoneticTitlePrefix, options.PhoneticTitleContains),
                    TokenFieldScore(
                        entry.CyrillicPhoneticArtistTokens,
                        entry.CyrillicPhoneticArtistTokens,
                        cyrillicPhonetic,
                        options.PhoneticArtistExact, options.PhoneticArtistPrefix, options.PhoneticArtistContains));
                if (cyrillicScore > 0)
                {
                    score = Math.Max(score, Scale(cyrillicScore, options.CyrillicPhoneticScale));
                }
            }

            if (score == 0)
            {
                var stem = StemToken(token);
                if (!string.Equals(stem, token, StringComparison.Ordinal))
                {
                    var stemScore = Math.Max(
                        Math.Max(
                            TokenFieldScore(entry.TitleStems, entry.TitleStems, stem, 500, 400, 300),
                            TokenFieldScore(entry.ArtistStems, entry.ArtistStems, stem, 400, 300, 200)),
                        TokenFieldScore(entry.FolderStems, entry.FolderStems, stem, 120, 90, 60));
                    if (stemScore > 0)
                    {
                        score = Scale(stemScore, Options.StemScale);
                    }
                }
            }

            if (score == 0 && token.Length >= Options.FuzzyMinLength)
            {
                var fuzzyOptions = Options;
                score = Math.Max(
                    Math.Max(
                        FuzzyFieldScore(entry.TitleTokens, entry.LatinTitleTokens, entry.PhoneticTitleTokens, token, fuzzyOptions.FuzzyTitle, fuzzyMultiplier),
                        FuzzyFieldScore(entry.ArtistTokens, entry.LatinArtistTokens, entry.PhoneticArtistTokens, token, fuzzyOptions.FuzzyArtist, fuzzyMultiplier)),
                    FuzzyFieldScore(entry.FolderTokens, entry.LatinFolderTokens, entry.PhoneticFolderTokens, token, fuzzyOptions.FuzzyFolder, fuzzyMultiplier));
            }

            if (score == 0 && token.Length >= Options.AbbreviationMinLength)
            {
                score = AbbreviationScore(entry, token);
            }

            if (score == 0)
            {
                missing++;
                if (missing > maxMissing)
                {
                    return 0;
                }

                continue;
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
            (entry.ArtistTokens, entry.LatinArtistTokens, Options.AbbreviationArtist),
            (entry.TitleTokens, entry.LatinTitleTokens, Options.AbbreviationTitle),
            (entry.FolderTokens, entry.LatinFolderTokens, Options.AbbreviationFolder),
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

    private static int PositionalTokenFieldScore(
        string[] tokens,
        string[] latinTokens,
        string queryToken,
        int exact,
        int prefix,
        int contains,
        int queryTokenCount)
    {
        var best = 0;
        var count = Math.Max(1, tokens.Length);

        for (var index = 0; index < tokens.Length; index++)
        {
            var raw = TokenMatch(tokens[index], queryToken, exact, prefix, contains);
            if (raw > 0)
            {
                best = Math.Max(best, ApplyFieldPenalty(raw, index, count, queryTokenCount));
            }
        }

        for (var index = 0; index < latinTokens.Length; index++)
        {
            var raw = TokenMatch(latinTokens[index], queryToken, exact, prefix, contains);
            if (raw > 0)
            {
                best = Math.Max(best, ApplyFieldPenalty(raw, index, Math.Max(1, latinTokens.Length), queryTokenCount));
            }
        }

        return best;
    }

    private static int ApplyFieldPenalty(int score, int index, int fieldCount, int queryTokenCount)
    {
        var positional = 1.0 - (index / (double)Math.Max(1, fieldCount)) * Options.PositionalMax;
        var sparsity = fieldCount > Options.SparsityThreshold && fieldCount > queryTokenCount
            ? 1.0 - Math.Min(Options.SparsityMax, (fieldCount - queryTokenCount) * Options.SparsityPerToken)
            : 1.0;
        var adjusted = (int)Math.Round(score * positional * sparsity);
        return score == Options.TitleTokenExact || score == Options.ArtistTokenExact
            ? Math.Max(adjusted, (int)Math.Round(score * Options.ExactFloor))
            : adjusted;
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

        // Word-boundary guard: a token match starts at the beginning of the
        // indexed token; arbitrary substrings belong to phrase matching only.
        return 0;
    }

    private static int FuzzyFieldScore(
        string[] tokens,
        string[] latinTokens,
        string[] phoneticTokens,
        string queryToken,
        int score,
        double multiplier)
    {
        if (queryToken.Length < Options.FuzzyMinLength)
        {
            return 0;
        }

        var threshold = queryToken.Length >= Options.FuzzyLongLength
            ? Options.FuzzyLongErrors
            : Options.FuzzyShortErrors;
        var candidates = ContainsCyrillic(queryToken) ? tokens : latinTokens;
        if (HasFuzzyMatch(candidates, queryToken, threshold))
        {
            return Scale(score, multiplier);
        }

        var phoneticQuery = PhoneticKey(queryToken);
        if (phoneticQuery.Length > 0 &&
            HasFuzzyMatch(phoneticTokens, phoneticQuery, threshold))
        {
            return Scale((int)Math.Round(score * Options.PhoneticFuzzyScale), multiplier);
        }

        return 0;
    }

    private static bool HasFuzzyMatch(string[] candidates, string queryToken, int threshold)
    {
        foreach (var token in candidates)
        {
            if (Math.Abs(token.Length - queryToken.Length) > threshold)
            {
                continue;
            }

            if (DamerauLevenshteinDistance(token, queryToken, threshold) <= threshold)
            {
                return true;
            }
        }

        return false;
    }

    private static int Scale(int score, double multiplier) =>
        Math.Max(1, (int)Math.Round(score * multiplier));

    private static string CyrillicPhoneticKey(string value)
    {
        if (!ContainsCyrillic(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var raw in value.ToLowerInvariant())
        {
            var ch = raw switch
            {
                'ж' or 'ч' or 'щ' => 'ш',
                'ц' => 'с',
                'й' => 'и',
                'ъ' or 'ь' => '\0',
                _ => raw,
            };

            if (ch != '\0')
            {
                builder.Append(ch);
            }
        }

        return Regex.Replace(builder.ToString(), @"([а-я])\1+", "$1");
    }

    private static string PhoneticKey(string value)
    {
        var latin = TransliterateToLatin(value.ToLowerInvariant());        var builder = new StringBuilder(latin.Length);

        for (var index = 0; index < latin.Length; index++)
        {
            var ch = latin[index];
            var remaining = latin.Length - index;

            string replacement;
            if (remaining >= 3 && string.CompareOrdinal(latin, index, "sch", 0, 3) == 0)
            {
                replacement = "sh";
                index += 2;
            }
            else if (remaining >= 2 && string.CompareOrdinal(latin, index, "ck", 0, 2) == 0)
            {
                replacement = "k";
                index++;
            }
            else if (remaining >= 2 && string.CompareOrdinal(latin, index, "qu", 0, 2) == 0)
            {
                replacement = "kv";
                index++;
            }
            else if (remaining >= 2 && string.CompareOrdinal(latin, index, "ph", 0, 2) == 0)
            {
                replacement = "f";
                index++;
            }
            else if (remaining >= 2 && string.CompareOrdinal(latin, index, "ch", 0, 2) == 0)
            {
                replacement = "sh";
                index++;
            }
            else if (remaining >= 2 && string.CompareOrdinal(latin, index, "sh", 0, 2) == 0)
            {
                replacement = "sh";
                index++;
            }
            else if (remaining >= 2 && string.CompareOrdinal(latin, index, "oo", 0, 2) == 0)
            {
                replacement = "u";
                index++;
            }
            else if (remaining >= 2 &&
                     (string.CompareOrdinal(latin, index, "ee", 0, 2) == 0 ||
                      string.CompareOrdinal(latin, index, "ea", 0, 2) == 0))
            {
                replacement = "i";
                index++;
            }
            else
            {
                replacement = ch switch
                {
                    'x' => "ks",
                    'w' => "v",
                    'c' => "k",
                    'q' => "k",
                    _ => ch.ToString(),
                };
            }

            builder.Append(replacement);
        }

        var phonetic = builder.ToString();
        return RepeatedConsonant.Replace(phonetic, "$1");
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

    private static int DamerauLevenshteinDistance(string left, string right, int limit)
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

        var previousPrevious = new int[right.Length + 1];
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

                if (row > 1 &&
                    column > 1 &&
                    left[row - 1] == right[column - 2] &&
                    left[row - 2] == right[column - 1])
                {
                    current[column] = Math.Min(current[column], previousPrevious[column - 2] + 1);
                }

                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > limit)
            {
                return limit + 1;
            }

            var swap = previousPrevious;
            previousPrevious = previous;
            previous = current;
            current = swap;
        }

        return previous[right.Length];
    }

    private static int PopularityBonus(MediaItem item)
    {
        var options = Options;
        var favoriteBonus = item.IsFavorite ? options.FavoriteBonus : 0;
        var playBonus = Math.Min(Math.Clamp(item.PlayCount, 0, 10) * 10, options.PlayCountMax);
        var ageDays = Math.Max(0, (DateTime.UtcNow - item.ModifiedUtc.ToUniversalTime()).TotalDays);
        var recencyBonus = ageDays < options.RecencyDays
            ? (int)Math.Round(options.RecencyMax * Math.Max(0.0, 1.0 - ageDays / options.RecencyDays))
            : 0;
        return favoriteBonus + playBonus + recencyBonus;
    }

    private sealed record QueryVariant(string Value, double FuzzyMultiplier);

    private sealed record Scored(MediaItem Item, int Score);
}
