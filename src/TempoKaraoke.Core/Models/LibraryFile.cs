using TempoKaraoke.Models;

namespace TempoKaraoke.Models;

/// <summary>
/// Файл библиотеки в том виде, в каком его видит сканер: путь, размер, дата и
/// разрешение. Имя не разобрано — это делает <see cref="Services.MediaNameParser"/>.
/// </summary>
public sealed record LibraryFile
{
    public required string Path { get; init; }

    /// <summary>Имя папки-источника, откуда файл приехал.</summary>
    public required string SourceFolder { get; init; }

    public long SizeBytes { get; init; }

    public DateTime ModifiedUtc { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Длительность из кэша метаданных. 0 — ещё не известна.</summary>
    public long DurationMs { get; init; }

    public string Extension { get; init; } = string.Empty;

    /// <summary>Probed at least once. Unknown quality + IsProbed means broken header, not unmeasured.</summary>
    public bool IsProbed { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public QualityClass Quality => QualityClassExtensions.FromDimensions(Width, Height);
}

/// <summary>Одна запись отчёта: что не так и где.</summary>
public sealed record AuditItem(string Path, string Category, string Detail);

/// <summary>Группа технических дублей: одна запись, найденная в нескольких местах.</summary>
public sealed record DuplicateGroupInfo(
    string SongKey,
    string DisplayName,
    IReadOnlyList<string> Paths,
    string WinnerPath);
