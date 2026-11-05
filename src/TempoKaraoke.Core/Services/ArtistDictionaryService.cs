using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempoKaraoke.Services;

/// <summary>Один исполнитель: как он пишется в каталоге и какие варианты к нему относятся.</summary>
public sealed record ArtistEntry
{
    /// <summary>Победившее написание — оно попадёт в имя папки.</summary>
    public required string Canonical { get; init; }

    /// <summary>Все встреченные варианты, включая канонический.</summary>
    public List<string> Aliases { get; init; } = new();
}

/// <summary>
/// Канонический словарь исполнителей. Хранится в _Служебное\artists.json рядом
/// с библиотекой: он описывает её состояние, а не настройки приложения.
/// </summary>
public sealed class ArtistDictionary
{
    public int Version { get; init; } = 1;

    public List<ArtistEntry> Entries { get; init; } = new();

    /// <summary>
    /// Пары написаний, которые нельзя сливать, даже если ключ совпал.
    /// Пример: разные группы с одинаковым названием.
    /// </summary>
    public List<string[]> DoNotMerge { get; init; } = new();

    /// <summary>Ищет каноническое написание для любого варианта.</summary>
    public string Canonicalize(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return string.Empty;
        }

        var key = NameKey.Artist(artist);
        foreach (var entry in Entries)
        {
            if (NameKey.Artist(entry.Canonical) == key)
            {
                return entry.Canonical;
            }
        }

        return artist.Trim();
    }

    public bool IsForbiddenToMerge(string left, string right)
    {
        var a = NameKey.Artist(left);
        var b = NameKey.Artist(right);
        return DoNotMerge.Any(pair => pair.Length == 2 &&
            ((NameKey.Artist(pair[0]) == a && NameKey.Artist(pair[1]) == b) ||
             (NameKey.Artist(pair[0]) == b && NameKey.Artist(pair[1]) == a)));
    }
}

/// <summary>
/// Чтение, запись и построение словаря. Первый запуск инициализирует его
/// алгоритмическими группами из <see cref="NameKey"/>, дальше правки человека
/// перекрывают алгоритм.
/// </summary>
public static class ArtistDictionaryService
{
    public const string FileName = "artists.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string serviceRoot) => Path.Combine(serviceRoot, FileName);

    /// <summary>Строит словарь по встречаемости: канон — самое частое написание.</summary>
    public static ArtistDictionary BuildFrom(IEnumerable<string> artistNames)
    {
        ArgumentNullException.ThrowIfNull(artistNames);

        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var spellings = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var raw in artistNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            var key = NameKey.Artist(trimmed);
            if (key.Length == 0)
            {
                continue;
            }

            if (!counts.TryGetValue(key, out var variants))
            {
                variants = new Dictionary<string, int>(StringComparer.Ordinal);
                counts[key] = variants;
            }

            variants[trimmed] = variants.TryGetValue(trimmed, out var seen) ? seen + 1 : 1;
            spellings[trimmed] = spellings.TryGetValue(trimmed, out var total) ? total + 1 : 1;
        }

        var entries = new List<ArtistEntry>();
        foreach (var (_, variants) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            // Канон: частота, затем длина (полное написание информативнее), затем алфавит.
            var canonical = variants
                .OrderByDescending(pair => pair.Value)
                .ThenByDescending(pair => pair.Key.Length)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First().Key;

            entries.Add(new ArtistEntry
            {
                Canonical = canonical,
                Aliases = variants.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            });
        }

        return new ArtistDictionary { Entries = entries };
    }

    public static ArtistDictionary Load(string serviceRoot)
    {
        var path = PathFor(serviceRoot);
        if (!File.Exists(path))
        {
            return new ArtistDictionary();
        }

        return JsonSerializer.Deserialize<ArtistDictionary>(File.ReadAllText(path), Options)
            ?? new ArtistDictionary();
    }

    public static void Save(ArtistDictionary dictionary, string serviceRoot)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var path = PathFor(serviceRoot);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(dictionary, Options));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Готовит словарь к работе: если файла нет — строит по библиотеке и сразу
    /// сохраняет, чтобы ручные правки было куда записывать.
    /// </summary>
    public static ArtistDictionary Ensure(string serviceRoot, IEnumerable<string> artistNames)
    {
        var existing = Load(serviceRoot);
        if (existing.Entries.Count > 0)
        {
            return existing;
        }

        var built = BuildFrom(artistNames);
        if (built.Entries.Count > 0)
        {
            Save(built, serviceRoot);
        }

        return built;
    }

    /// <summary>Сливает написание <paramref name="alias"/> в группу канона.</summary>
    public static ArtistDictionary Merge(ArtistDictionary dictionary, string canonical, string alias)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        if (dictionary.IsForbiddenToMerge(canonical, alias))
        {
            return dictionary;
        }

        var aliasKey = NameKey.Artist(alias);
        var target = dictionary.Entries.FirstOrDefault(entry => NameKey.Artist(entry.Canonical) == NameKey.Artist(canonical));
        if (target is null || aliasKey.Length == 0)
        {
            return dictionary;
        }

        foreach (var entry in dictionary.Entries.ToList())
        {
            if (NameKey.Artist(entry.Canonical) != aliasKey)
            {
                continue;
            }

            dictionary.Entries.Remove(entry);
            foreach (var name in entry.Aliases)
            {
                if (!target.Aliases.Contains(name, StringComparer.Ordinal))
                {
                    target.Aliases.Add(name);
                }
            }
        }

        if (!target.Aliases.Contains(alias, StringComparer.Ordinal))
        {
            target.Aliases.Add(alias);
        }

        return dictionary;
    }

    /// <summary>Запрещает слияние двух имён: они похожи, но это разные исполнители.</summary>
    public static ArtistDictionary ForbidMerge(ArtistDictionary dictionary, string left, string right)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        dictionary.DoNotMerge.Add(new[] { left, right });
        return dictionary;
    }

    /// <summary>Заменяет каноническое написание группы (например, возвращает «ё»).</summary>
    public static ArtistDictionary SetCanonical(ArtistDictionary dictionary, string current, string canonical)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var key = NameKey.Artist(current);
        var entry = dictionary.Entries.FirstOrDefault(item => NameKey.Artist(item.Canonical) == key);
        if (entry is null || string.IsNullOrWhiteSpace(canonical))
        {
            return dictionary;
        }

        if (!entry.Aliases.Contains(entry.Canonical, StringComparer.Ordinal))
        {
            entry.Aliases.Add(entry.Canonical);
        }

        var index = dictionary.Entries.IndexOf(entry);
        dictionary.Entries[index] = entry with { Canonical = canonical.Trim() };
        if (!dictionary.Entries[index].Aliases.Contains(canonical.Trim(), StringComparer.Ordinal))
        {
            dictionary.Entries[index].Aliases.Add(canonical.Trim());
        }

        return dictionary;
    }
}
