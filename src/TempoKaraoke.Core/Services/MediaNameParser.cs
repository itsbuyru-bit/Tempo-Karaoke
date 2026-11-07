using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Разбирает имя файла на исполнителя, название и тип версии.
/// Библиотека собиралась пятью разными способами, поэтому правил несколько и они
/// применяются по порядку — от самого надёжного разделителя к самому слабому.
/// </summary>
public static class MediaNameParser
{
    // Песня из фильма: «Буратино из фильма Приключения Буратино».
    private static readonly Regex FilmPattern = new(
        @"^\s*(?<title>.+?)\s+из\s+(?:кинофильма|фильма|мультфильма|к/ф|м/ф)\s+(?<film>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Та же песня после первого прогона: «Буратино (из фильма Приключения Буратино)».
    // Без этой формы повторный разбор терял бы источник, и миграция не была бы
    // идемпотентной.
    private static readonly Regex FilmSuffix = new(
        @"^\s*(?<title>.+?)\s*\((?:из\s+)?(?:кинофильма|фильма|мультфильма|к/ф|м/ф)\s+(?<film>.+?)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Ведущее «КАРАОКЕ» в файлах Karaoke4U: «КАРАОКЕ   ANIVAR   Капля кислорода».
    private static readonly Regex LeadingMarker = new(
        @"^\s*(?:караоке|karaoke)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingTrackNumber = new(
        @"^\s*(?<number>\d{1,3})\s*[._-]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex YearTag = new(
        @"[\(\[]\s*(?<year>(?:19|20)\d{2})\s*[\)\]]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArtistMarker = new(
        @"\b(?:feat\.?|ft\.?|featuring|vs\.?|versus|дуэт|группа)\b|&|;|/|,",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownArtists = new(StringComparer.Ordinal)
    {
        "любэ", "лепс", "пугачева", "меладзе", "земфира", "баста", "киш",
        "король и шут", "2маши", "две маши", "queen", "boney m", "metallica",
        "nirvana", "modern talking", "би 2", "bi 2", "tatu", "rammstein",
        "little big", "ac dc", "prodigy", "растеряев",
    };

    private static readonly Regex BracketArtist = new(
        @"^\s*(?<title>.+?)\s*[\(\[](?<artist>[^()\[\]]{1,60})[\)\]]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TechnicalBracket = new(
        @"\b(?:karaoke|караоке|minus|минус|минусовка|live|лайв|concert|концерт|duet|дуэт|"
        + @"version|версия|remix|ремикс|cover|кавер|instrumental|acoustic|"
        + @"back\s*vocal|бэк[\s-]*вокал|бэк|вокал|подпевка|хор|оркестр|"
        + @"hd|uhd|4k)\b|(?:19|20)\d{2}|\d{3,4}p",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Brackets = new(
        @"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Служебные пометки источников и качества. Убираются из названия, но тип
    // версии к этому моменту уже определён по исходной строке.
    private static readonly Regex Noise = new(
        @"\b(?:пой\s+под\s+бит|кавер[\s-]*минус|оригинальный\s+минус|кавер|cover|минус|минусовка|"
        + @"текст\s+песни|караоке|karaoke|back\s*vocal|бэк[\s-]*вокал|бэк|minus|"
        + @"mood\s+video|official\s+video|официальное\s+видео|"
        + @"премьера\s+клипа|premiere|клип|lyrics|транскрипция|на\s+русском|русский\s+текст|"
        + @"экстремальное\s+караоке|full\s*hd|uhd|4k|1080p|720p|480p|hd|original|оригинал)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Номер варианта — только в явной форме: «(2)», «[2]», «#2». Голое « 2»
    // в конце не трогаем: иначе «Попытка № 5» теряет номер из названия.
    private static readonly Regex BareOrdinal = new(
        @"\s+([2-9])\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Папка, где голое число в конце — номер второй минусовки.
    private static readonly Regex BareNumberFolder = new(
        @"^karaoke_archive$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Ordinal = new(
        @"(?:[\(\[]\s*(?:v\s*|#)?([2-9])\s*[\)\]]|#\s*([2-9]))\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Тег качества, который дописывает сам каталог. Снимается до разбора,
    // иначе «Буратино (из фильма X) [720p]» перестаёт быть песней из фильма.
    private static readonly Regex QualityTag = new(
        @"[\[\(]\s*(?:4k|uhd|full\s*hd|1080p|720p|480p|sd)\s*[\]\)]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Дефис с пробелами внутри исполнителя: имя вида «А - Б - Песня» невозможно
    // разобрать однозначно, такие файлы не двигаем автоматически.
    private static readonly Regex SpacedDash = new(
        @"\s[-–—]\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Папки-источники, в которых порядок частей имени обратный.
    private static readonly Regex InvertedFolder = new(
        @"karaoke[ ._-]?online",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LiveFolder = new(@"\blive\b|лайв", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtremeFolder = new(@"экстремальн", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Karaoke4U — источник кавер-минусов. Часть файлов там подписана просто
    // «Караоке», и без этого правила они схлопнулись бы с «Калинкой», хотя
    // аранжировка другая.
    private static readonly Regex CoverFolder = new(@"karaoke4u", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Бэк-вокал — отдельная запись: гостю нужен чистый минус, а этот вариант
    // с подпевкой. Держим его в названии, чтобы он не слился с основным.
    private static readonly Regex BackingVocal = new(
        @"\bбэк\b|\bбек\b|\bдоп\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FilmOnly = new(
        @"^\s*из\s+(?:кинофильма|фильма|мультфильма|к/ф|м/ф)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly char[] TrimChars = { ' ', '-', '–', '—', '·', '|', ',', ':', ';', '.' };

    // Разделители «исполнитель — название». Порядок важен: сначала надёжные,
    // потом вырожденные случаи вроде «TERNOVOY- Меркурий».
    private static readonly Regex[] Separators =
    {
        new(@"\s+--\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"\s+-\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"\s+[–—]\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"(?<=\S)-\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"\s{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    };

    private static readonly Regex DoubleSpace = new(
        @"\s{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Разбирает имя файла. <paramref name="folderName"/> влияет на тип версии
    /// («Калинка Live» → Live) и на инверсию частей («Karaoke online»).
    /// </summary>
    public static ParsedMediaName Parse(string? fileName, string? folderName = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ParsedMediaName.Empty;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        stem = stem.Trim('\uFEFF', '\u200B', '\u200C', '\u200D');
        var rawName = stem;
        var yearMatch = YearTag.Match(stem);
        int? year = yearMatch.Success && int.TryParse(yearMatch.Groups["year"].Value, out var parsedYear)
            ? parsedYear
            : null;

        int? leadingTrack = null;
        var leading = LeadingTrackNumber.Match(stem);
        if (leading.Success &&
            int.TryParse(leading.Groups["number"].Value, out var parsedTrack))
        {
            leadingTrack = parsedTrack;
            stem = stem[leading.Length..].Trim();
        }

        if (stem.Length == 0)
        {
            return ParsedMediaName.Empty;
        }

        var folder = folderName ?? string.Empty;
        var version = DetectVersion(stem, folder);

        var raw = QualityTag.Replace(stem, " ").Trim();
        if (raw.Length == 0)
        {
            raw = stem;
        }

        var ordinal = ExtractOrdinal(ref raw, BareNumberFolder.IsMatch(folder));

        var film = FilmPattern.Match(raw);
        if (!film.Success)
        {
            film = FilmSuffix.Match(raw);
        }

        if (film.Success)
        {
            var filmTitle = CleanTitle(film.Groups["title"].Value);
            var filmName = CleanFilmName(film.Groups["film"].Value);
            if (filmTitle.Length > 0 && filmName.Length > 0)
            {
                return new ParsedMediaName
                {
                    Artist = string.Empty,
                    Title = $"{filmTitle} (из фильма {filmName})",
                    Version = version,
                    Ordinal = ordinal,
                    FilmSource = filmName,
                    RawName = rawName,
                    TrackNumber = leadingTrack,
                    Year = year,
                };
            }
        }

        // «Конь (Любэ)» and «Bohemian Rhapsody [Queen]» are also common.
        var bracketArtist = ExtractBracketArtist(ref raw);

        // Пробелы здесь не схлопываем: у Karaoke4U разделитель — ровно три
        // пробела, и любое сжатие до разбиения его уничтожает.
        var working = LeadingMarker.Replace(raw, string.Empty).Replace('_', ' ').Trim();

        var (artist, title) = SplitParts(working, folder);
        if (!string.IsNullOrWhiteSpace(bracketArtist))
        {
            artist = string.IsNullOrWhiteSpace(artist)
                ? bracketArtist
                : $"{artist} & {bracketArtist}";
        }

        // «Любэ - 01 - Конь»: номер стоит между артистом и названием.
        var middle = LeadingTrackNumber.Match(title);
        if (middle.Success &&
            int.TryParse(middle.Groups["number"].Value, out var middleTrack))
        {
            leadingTrack ??= middleTrack;
            title = title[middle.Length..].Trim();
        }

        // «Конь - Любэ» and «bohemian rhapsody - queen» are common in file names.
        if (ShouldSwap(artist, title))
        {
            (artist, title) = (title, artist);
        }

        artist = CleanArtist(artist);
        var titleRaw = title;
        title = CleanTitle(title);

        // От одной пометки вроде «ЛАРИН - ТЕКСТ ПЕСНИ» остаются только пометки.
        var needsAttention = false;
        if (title.Length == 0)
        {
            title = CleanTitle(titleRaw);
            if (title.Length == 0)
            {
                title = Spaces.Replace(titleRaw, " ").Trim();
            }

            if (title.Length == 0)
            {
                title = Spaces.Replace(raw, " ").Trim();
            }

            needsAttention = true;
        }

        if (title.Length == 0)
        {
            return ParsedMediaName.Empty;
        }


        return new ParsedMediaName
        {
            Artist = artist,
            Title = title,
            Version = version,
            Ordinal = ordinal,
            FilmSource = null,
            RawName = rawName,
            TrackNumber = leadingTrack,
            Year = year,
            // Сироту без исполнителя и без фильма отправляем в отдельную папку
            // каталога, а не оставляем на месте: флаг нужен только для имён,
            // которые нельзя разобрать однозначно.
            NeedsAttention = needsAttention
                || (artist.Length == 0 && LooksLikePileOfMarkers(raw))
                || SpacedDash.IsMatch(artist),
        };
    }

    private static bool ShouldSwap(string artist, string title)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (ArtistMarker.IsMatch(artist))
        {
            return false;
        }

        var artistKey = NormalizeName(artist);
        var titleKey = NormalizeName(title);
        if (KnownArtists.Contains(artistKey))
        {
            return false;
        }

        if (KnownArtists.Contains(titleKey))
        {
            return true;
        }

        // A numeric/trailing tag on one side is almost never an artist.
        if (LooksLikeOnlyNumberOrTag(title))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeName(string value) =>
        SearchService.Normalize(value);

    private static string? ExtractBracketArtist(ref string raw)
    {
        var match = BracketArtist.Match(raw);
        if (!match.Success)
        {
            return null;
        }

        var candidate = Trim(match.Groups["artist"].Value);
        if (candidate.Length == 0 || TechnicalBracket.IsMatch(candidate))
        {
            return null;
        }

        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var known = KnownArtists.Contains(SearchService.Normalize(candidate));
        if (words.Length is < 1 or > 4 || (!known && words.Length > 4))
        {
            return null;
        }

        raw = match.Groups["title"].Value.Trim();
        return candidate;
    }

    private static bool LooksLikeOnlyNumberOrTag(string value) =>
        Regex.IsMatch(
            value,
            @"^\s*(?:\d{1,4}|(?:480p|360p|144p|720p|1080p|4k|hd|uhd))\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static (string Artist, string Title) SplitParts(string working, string folder)
    {
        var inverted = InvertedFolder.IsMatch(folder);

        var pairs = SplitOnce(working);
        if (pairs is not null)
        {
            return inverted ? (pairs.Value.Right, pairs.Value.Left) : (pairs.Value.Left, pairs.Value.Right);
        }

        return (string.Empty, working);
    }

    private static (string Left, string Right)? SplitOnce(string value)
    {
        foreach (var separator in Separators)
        {
            var match = separator.Match(value);
            if (!match.Success)
            {
                continue;
            }

            var left = value[..match.Index].Trim();
            var right = value[(match.Index + match.Length)..].Trim();
            if (left.Length > 0 && right.Length > 0)
            {
                return (left, right);
            }
        }

        // Последняя попытка: два и более пробела, но только если левая часть
        // похожа на исполнителя, а не на обрывок названия.
        var doubleSpace = DoubleSpace.Match(value);
        if (doubleSpace.Success)
        {
            var left = value[..doubleSpace.Index].Trim();
            var right = value[(doubleSpace.Index + doubleSpace.Length)..].Trim();
            if (left.Length is > 0 and <= 40 && right.Length > 0 && !right.StartsWith('#'))
            {
                return (left, right);
            }
        }

        return null;
    }

    private static VersionKind DetectVersion(string raw, string folder)
    {
        if (ExtremeFolder.IsMatch(folder) || Regex.IsMatch(raw, "экстремальн|экстрим", RegexOptions.IgnoreCase))
        {
            return VersionKind.Extreme;
        }

        if (LiveFolder.IsMatch(folder))
        {
            return VersionKind.Live;
        }

        if (Regex.IsMatch(raw, "транскр|transcription", RegexOptions.IgnoreCase))
        {
            return VersionKind.Transcription;
        }

        if (Regex.IsMatch(raw, @"на\s+русском|русский\s+текст|русск\w*\s+верси|rus\.?\s*version|\bрус\b", RegexOptions.IgnoreCase))
        {
            return VersionKind.Russian;
        }

        if (Regex.IsMatch(raw, "ремикс|remix", RegexOptions.IgnoreCase))
        {
            return VersionKind.Remix;
        }

        if (Regex.IsMatch(raw, "кавер|cover", RegexOptions.IgnoreCase) || CoverFolder.IsMatch(folder))
        {
            return VersionKind.Cover;
        }

        if (Regex.IsMatch(raw, @"\blive\b|\bстрим\b|stream", RegexOptions.IgnoreCase))
        {
            return VersionKind.Live;
        }

        if (BackingVocal.IsMatch(raw))
        {
            return VersionKind.BackingVocal;
        }

        return VersionKind.Original;
    }

    private static int ExtractOrdinal(ref string raw, bool allowBareNumber)
    {
        var match = Ordinal.Match(raw);
        string? digits = match.Success
            ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            : null;

        // В karaoke_archive «… 2» — это вторая минусовка той же песни, а не часть
        // названия. В остальных папках голое число не трогаем: «Попытка № 5».
        if (digits is null && allowBareNumber && !raw.Contains('№'))
        {
            match = BareOrdinal.Match(raw);
            if (match.Success)
            {
                digits = match.Groups[1].Value;
            }
        }

        if (digits is null)
        {
            return 1;
        }

        var value = raw[..match.Index].TrimEnd();
        if (value.Length == 0)
        {
            return 1;
        }

        raw = value;
        return int.Parse(digits);
    }

    private static string CleanArtist(string artist)
    {
        if (artist.Length == 0)
        {
            return string.Empty;
        }

        return Trim(Brackets.Replace(artist, " "));
    }

    private static string CleanTitle(string title)
    {
        if (title.Length == 0)
        {
            return string.Empty;
        }

        var cleaned = Brackets.Replace(title, " ");
        cleaned = Noise.Replace(cleaned, " ");
        cleaned = FilmOnly.Replace(cleaned, " ");

        // «Караоке Ремикс» превращается в «Ремикс»: слово «ремикс» само по себе
        // несёт тип версии, но названием быть не может.
        if (cleaned.Trim().Length == 0)
        {
            return string.Empty;
        }

        return Trim(cleaned);
    }

    private static string CleanFilmName(string film)
    {
        var cleaned = film.Trim(' ', '_', '"', '\'', '«', '»');
        cleaned = Brackets.Replace(cleaned, " ");
        return Trim(cleaned);
    }

    private static string Trim(string value)
    {
        var collapsed = Spaces.Replace(value, " ").Trim();
        return collapsed.Trim(TrimChars).Trim();
    }

    private static bool LooksLikePileOfMarkers(string raw) =>
        Noise.Replace(Brackets.Replace(raw, " "), " ").Trim(' ', '-').Length == 0;
}
