using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Результат разбора имени файла. <see cref="Title"/> уже пригоден для индекса:
/// у песни из фильма он содержит «(из фильма X)», поэтому поиск находит её и по
/// названию, и по фильму.
/// </summary>
public sealed record ParsedMediaName
{
    public static readonly ParsedMediaName Empty = new()
    {
        Artist = string.Empty,
        Title = string.Empty,
        Version = VersionKind.Original,
        Ordinal = 1,
        FilmSource = null,
        NeedsAttention = true,
    };

    /// <summary>Исходное имя файла без расширения, до очистки.</summary>
    public string RawName { get; init; } = string.Empty;

    /// <summary>Номер трека, если он был найден в начале или середине имени.</summary>
    public int? TrackNumber { get; init; }

    /// <summary>Год из скобок; в поисковые токены не входит.</summary>
    public int? Year { get; init; }

    /// <summary>Исполнитель как в имени файла. Пусто, если его нет.</summary>
    public string Artist { get; init; } = string.Empty;

    /// <summary>Название песни без служебных пометок.</summary>
    public string Title { get; init; } = string.Empty;

    public VersionKind Version { get; init; }

    /// <summary>Номер варианта внутри одного типа: 1 — основной, 2 и больше — «(vN)».</summary>
    public int Ordinal { get; init; } = 1;

    /// <summary>Фильм или мультфильм, из которого песня. Пусто, если не оттуда.</summary>
    public string? FilmSource { get; init; }

    /// <summary>Разбор получился подозрительным: стоит показать в отчёте.</summary>
    public bool NeedsAttention { get; init; }

    public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);

    /// <summary>Ключ исполнителя для сравнения.</summary>
    public string ArtistKey => NameKey.Artist(Artist);

    /// <summary>Ключ названия для сравнения.</summary>
    public string TitleKey => NameKey.Title(Title);

    /// <summary>Ключ записи: одинаковый ключ — технический дубль.</summary>
    public string SongKey => NameKey.ForSong(ArtistKey, TitleKey, Version, Ordinal);
}
