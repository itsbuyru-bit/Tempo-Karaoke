using TempoKaraoke.Models;

namespace TempoKaraoke.Models;

/// <summary>Итог проверки библиотеки. Только чтение: ничего не двигает и не пишет.</summary>
public sealed class AuditReport
{
    public int TotalFiles { get; init; }

    public long TotalBytes { get; init; }

    public int WithArtist { get; init; }

    public int Films { get; init; }

    public int Orphans { get; init; }

    public int NeedsAttention { get; init; }

    public int RenameRequired { get; init; }

    public int VersionVariants { get; init; }

    public int DuplicateGroups { get; init; }

    public int FilesInDuplicates { get; init; }

    public int CollapsibleFiles { get; init; }

    public long CollapsibleBytes { get; init; }

    public int JunkFiles { get; init; }

    public long JunkBytes { get; init; }

    public IReadOnlyList<AuditItem> Items { get; init; } = Array.Empty<AuditItem>();

    public IReadOnlyList<DuplicateGroupInfo> Duplicates { get; init; } = Array.Empty<DuplicateGroupInfo>();

    public IReadOnlyDictionary<string, int> BySourceFolder { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<QualityClass, int> ByQuality { get; init; } = new Dictionary<QualityClass, int>();

    public IReadOnlyDictionary<VersionKind, int> ByVersion { get; init; } = new Dictionary<VersionKind, int>();
}
