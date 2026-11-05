using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>Результат применения или отката плана.</summary>
public sealed record MigrationResult(
    MigrationManifest Manifest,
    int Moved,
    int Quarantined,
    int Failed,
    IReadOnlyList<string> Errors);

/// <summary>
/// Применение и откат плана. Итоговый манифест пишется дважды, а операции
/// дописываются в append-only журнал — оборванный прогон не теряет историю.
/// </summary>
public static class MigrationApplier
{
    public static MigrationResult Apply(
        MigrationPlan plan,
        string serviceRoot,
        Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var directory = MigrationJournal.DirectoryFor(serviceRoot, plan.RunId);
        var manifest = new MigrationManifest
        {
            RunId = plan.RunId,
            CatalogRoot = plan.CatalogRoot,
            QuarantineRoot = plan.QuarantineRoot,
            Status = "running",
        };

        MigrationJournal.SaveManifest(manifest, directory);

        var errors = new List<string>();
        var failed = 0;
        var moved = 0;
        var quarantined = 0;
        var sequence = 0;
        var total = plan.Entries.Count;

        foreach (var entry in plan.Entries)
        {
            if (entry.Action == MigrationAction.Skip)
            {
                continue;
            }

            sequence++;
            var operation = new MigrationOperation
            {
                Sequence = sequence,
                Action = entry.Action switch
                {
                    MigrationAction.Move => "move",
                    MigrationAction.QuarantineDuplicate => "quarantine_duplicate",
                    _ => "quarantine_junk",
                },
                Source = entry.Source,
                Target = entry.Target,
                Reason = entry.Reason,
                SongKey = entry.SongKey,
                ReplacedBy = entry.ReplacedBy,
                Status = "moved",
            };

            try
            {
                MoveFile(entry.Source, entry.Target);
                MigrationJournal.AppendOperation(directory, operation);
                if (operation.Action == "move")
                {
                    moved++;
                }
                else
                {
                    quarantined++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{entry.Source}: {ex.Message}");
            }

            progress?.Invoke(sequence, total);
        }

        manifest.CompletedUtc = DateTime.UtcNow;
        manifest.Status = failed == 0 ? "completed" : "failed";
        MigrationJournal.SaveManifest(manifest, directory);

        manifest.Operations.AddRange(MigrationJournal.ReadOperations(directory));
        return new MigrationResult(manifest, moved, quarantined, failed, errors);
    }

    /// <summary>Полный откат: возвращает файлы на исходные места в обратном порядке.</summary>
    public static MigrationResult Rollback(string serviceRoot, string runId, Action<int, int>? progress = null)
    {
        var directory = MigrationJournal.DirectoryFor(serviceRoot, runId);
        var manifest = MigrationJournal.LoadRun(directory)
            ?? throw new InvalidOperationException($"Журнал прогона «{runId}» не найден.");

        var toUndo = manifest.Operations
            .Where(operation => operation.Status == "moved")
            .OrderByDescending(operation => operation.Sequence)
            .ToList();

        var errors = new List<string>();
        var failed = 0;
        var restored = 0;

        foreach (var operation in toUndo)
        {
            try
            {
                MoveFile(operation.Target, operation.Source);
                MigrationJournal.AppendOperation(directory, new MigrationOperation
                {
                    Sequence = operation.Sequence,
                    Action = "rollback",
                    Source = operation.Source,
                    Target = operation.Target,
                    Reason = operation.Action,
                    SongKey = operation.SongKey,
                    ReplacedBy = operation.ReplacedBy,
                    Status = "rolled_back",
                });
                restored++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{operation.Target}: {ex.Message}");
            }

            progress?.Invoke(restored + failed, toUndo.Count);
        }

        manifest.CompletedUtc = DateTime.UtcNow;
        manifest.Status = failed == 0 ? "rolledback" : "failed";
        MigrationJournal.SaveManifest(manifest, directory);

        return new MigrationResult(manifest, restored, 0, failed, errors);
    }

    private static void MoveFile(string source, string target)
    {
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Исходный файл не найден", source);
        }

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(source, target, overwrite: false);
    }
}
