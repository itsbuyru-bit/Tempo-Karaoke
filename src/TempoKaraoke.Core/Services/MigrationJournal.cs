using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempoKaraoke.Services;

/// <summary>Одна файловая операция. Пишется в append-only журнал после выполнения.</summary>
public sealed record MigrationOperation
{
    public int Sequence { get; init; }

    /// <summary>move | quarantine_duplicate | quarantine_junk | rollback.</summary>
    public required string Action { get; init; }

    public required string Source { get; init; }

    public required string Target { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string? SongKey { get; init; }

    /// <summary>
    /// Для проигравшего дубля — путь к победителю. Плейлисты и избранное должны
    /// переехать на него, а не в корзину.
    /// </summary>
    public string? ReplacedBy { get; init; }

    /// <summary>moved — выполнено, rolled_back — возвращено на место.</summary>
    public string Status { get; init; } = "moved";

    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Журнал миграции. Итоговый manifest.json пишется дважды — в начале и в конце,
/// а каждая операция дописывается строкой в operations.ndjson. Перезапись
/// растущего JSON на каждом шаге недопустима на четырёх тысячах файлов.
/// </summary>
public sealed class MigrationManifest
{
    public int Version { get; init; } = 1;

    public required string RunId { get; init; }

    public required string CatalogRoot { get; init; }

    public required string QuarantineRoot { get; init; }

    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    public DateTime? CompletedUtc { get; set; }

    /// <summary>planned | running | completed | failed | rolledback.</summary>
    public string Status { get; set; } = "planned";

    /// <summary>Заполняется при чтении из журнала, в файл не пишется.</summary>
    [JsonIgnore]
    public List<MigrationOperation> Operations { get; init; } = new();

    /// <summary>
    /// Карта «старый путь → актуальный путь» для переписывания плейлистов,
    /// избранного и истории. Проигравшие дубли ведут на победителя, а если
    /// файл потом откатили — на исходный путь.
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildPathMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in Operations)
        {
            if (string.Equals(operation.Status, "rolled_back", StringComparison.Ordinal))
            {
                map.Remove(operation.Source);
            }
            else if (string.Equals(operation.Action, "move", StringComparison.Ordinal))
            {
                map[operation.Source] = operation.Target;
            }
            else if (operation.ReplacedBy is not null)
            {
                map[operation.Source] = operation.ReplacedBy;
            }
        }

        return map;
    }
}

public static class MigrationJournal
{
    public const string ManifestFileName = "manifest.json";
    public const string OperationsFileName = "operations.ndjson";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DirectoryFor(string serviceRoot, string runId) =>
        Path.Combine(serviceRoot, "migration", runId);

    public static string ManifestPath(string directory) => Path.Combine(directory, ManifestFileName);

    public static string OperationsPath(string directory) => Path.Combine(directory, OperationsFileName);

    public static void SaveManifest(MigrationManifest manifest, string directory)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        Directory.CreateDirectory(directory);
        var path = ManifestPath(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, Options));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Дописывает одну строку журнала. O(1) независимо от длины истории.</summary>
    public static void AppendOperation(string directory, MigrationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Directory.CreateDirectory(directory);
        File.AppendAllText(
            OperationsPath(directory),
            JsonSerializer.Serialize(operation, LineOptions) + Environment.NewLine);
    }

    public static MigrationManifest? LoadRun(string directory)
    {
        var manifestPath = ManifestPath(directory);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<MigrationManifest>(File.ReadAllText(manifestPath), Options);
        if (manifest is null)
        {
            return null;
        }

        manifest.Operations.AddRange(ReadOperations(directory));
        return manifest;
    }

    public static IReadOnlyList<MigrationOperation> ReadOperations(string directory)
    {
        var path = OperationsPath(directory);
        if (!File.Exists(path))
        {
            return Array.Empty<MigrationOperation>();
        }

        var operations = new List<MigrationOperation>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var operation = JsonSerializer.Deserialize<MigrationOperation>(line, LineOptions);
                if (operation is not null)
                {
                    operations.Add(operation);
                }
            }
            catch (JsonException)
            {
                // Оборванная последняя строка после падения: пропускаем её,
                // остальной журнал остаётся пригодным для отката.
            }
        }

        return operations;
    }

    /// <summary>Журналы в порядке от новых к старым.</summary>
    public static IReadOnlyList<string> ListRuns(string serviceRoot)
    {
        var directory = Path.Combine(serviceRoot, "migration");
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var runs = new List<string>();
        foreach (var run in Directory.EnumerateDirectories(directory))
        {
            if (File.Exists(ManifestPath(run)))
            {
                runs.Add(run);
            }
        }

        runs.Sort(StringComparer.OrdinalIgnoreCase);
        runs.Reverse();
        return runs;
    }
}
