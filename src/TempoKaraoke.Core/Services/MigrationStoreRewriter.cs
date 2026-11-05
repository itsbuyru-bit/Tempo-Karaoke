using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TempoKaraoke.Services;

/// <summary>Итог переписывания сохранённых путей в JSON-хранилищах и m3u-файлах.</summary>
public sealed record MigrationStoreRewriteResult(
    int FilesRewritten,
    int PathsReplaced,
    IReadOnlyList<string> Backups,
    IReadOnlyList<string> Errors);

/// <summary>
/// Переписывает сохранённые абсолютные пути по карте миграции. Карта строится из
/// <see cref="MigrationManifest.BuildPathMap"/>: старый путь → актуальный.
///
/// Работает на уровне JSON: заменяет только строковые значения, которые являются
/// абсолютными путями и есть в карте. Это сохраняет структуру файлов, не требует
/// знать схемы хранилищ и безопасно пропускает всё, чего нет в карте.
///
/// Перед перезаписью исходные файлы копируются в
/// <c>_Служебное\migration\&lt;run&gt;\backup_stores</c> — откат всегда может
/// опереться на них, даже если обратная карта потеряна.
/// </summary>
public static class MigrationStoreRewriter
{
    public const string BackupFolderName = "backup_stores";

    private const string M3uBackupSubfolder = "m3u";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string BackupDirectoryFor(string serviceRoot, string runId) =>
        Path.Combine(MigrationJournal.DirectoryFor(serviceRoot, runId), BackupFolderName);

    /// <summary>
    /// Переписывает пути в переданных хранилищах. Файлы, которых нет на диске,
    /// пропускаются; ошибка одного файла не мешает остальным.
    /// </summary>
    public static MigrationStoreRewriteResult Rewrite(
        string serviceRoot,
        string runId,
        IReadOnlyDictionary<string, string> pathMap,
        IEnumerable<string> jsonFiles,
        IEnumerable<string> m3uFiles,
        bool createBackup = true)
    {
        ArgumentNullException.ThrowIfNull(pathMap);
        ArgumentNullException.ThrowIfNull(jsonFiles);
        ArgumentNullException.ThrowIfNull(m3uFiles);

        var backups = new List<string>();
        var errors = new List<string>();
        var filesRewritten = 0;
        var pathsReplaced = 0;

        if (pathMap.Count == 0 || string.IsNullOrWhiteSpace(serviceRoot) || string.IsNullOrWhiteSpace(runId))
        {
            return new MigrationStoreRewriteResult(0, 0, backups, errors);
        }

        var backupDirectory = BackupDirectoryFor(serviceRoot, runId);

        foreach (var path in jsonFiles)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                var original = File.ReadAllText(path);
                var (rewritten, replaced) = RewriteJson(original, pathMap);
                if (replaced == 0)
                {
                    continue;
                }

                if (createBackup)
                {
                    var backup = BackupFile(path, backupDirectory);
                    if (backup is not null)
                    {
                        backups.Add(backup);
                    }
                }

                File.WriteAllText(path, rewritten, Utf8NoBom);
                filesRewritten++;
                pathsReplaced += replaced;
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        foreach (var path in m3uFiles)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                if (createBackup)
                {
                    var backup = BackupFile(
                        path,
                        Path.Combine(backupDirectory, M3uBackupSubfolder));
                    if (backup is not null)
                    {
                        backups.Add(backup);
                    }
                }

                var replaced = M3uService.RewritePaths(path, pathMap);
                if (replaced > 0)
                {
                    filesRewritten++;
                    pathsReplaced += replaced;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        return new MigrationStoreRewriteResult(filesRewritten, pathsReplaced, backups, errors);
    }

    /// <summary>
    /// Аварийное восстановление JSON-хранилищ из backup_stores. Используется,
    /// если обратная карта отката пуста, а резервные копии остались.
    /// </summary>
    public static int RestoreBackups(
        string serviceRoot,
        string runId,
        IEnumerable<string> jsonFiles)
    {
        ArgumentNullException.ThrowIfNull(jsonFiles);

        var backupDirectory = BackupDirectoryFor(serviceRoot, runId);
        var restored = 0;
        foreach (var path in jsonFiles)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var backup = Path.Combine(backupDirectory, Path.GetFileName(path));
            if (!File.Exists(backup))
            {
                continue;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(backup, path, overwrite: true);
                restored++;
            }
            catch
            {
                // Восстановление — последний шанс; следующий файл может уцелеть.
            }
        }

        return restored;
    }

    /// <summary>Чистая замена путей в JSON-строке; возвращает новый текст и число замен.</summary>
    public static (string Json, int Replaced) RewriteJson(
        string json,
        IReadOnlyDictionary<string, string> pathMap)
    {
        ArgumentNullException.ThrowIfNull(pathMap);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return (json, 0);
        }

        if (root is null)
        {
            return (json, 0);
        }

        var replaced = RewriteNode(root, pathMap);
        return replaced == 0
            ? (json, 0)
            : (root.ToJsonString(JsonOptions), replaced);
    }

    private static int RewriteNode(JsonNode node, IReadOnlyDictionary<string, string> pathMap)
    {
        var replaced = 0;
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var key in new List<string>(jsonObject.Select(pair => pair.Key)))
                {
                    var value = jsonObject[key];
                    if (value is null)
                    {
                        continue;
                    }

                    if (TryMapValue(value, pathMap, out var mapped))
                    {
                        jsonObject[key] = mapped;
                        replaced++;
                    }
                    else
                    {
                        replaced += RewriteNode(value, pathMap);
                    }
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var value = jsonArray[index];
                    if (value is null)
                    {
                        continue;
                    }

                    if (TryMapValue(value, pathMap, out var mapped))
                    {
                        jsonArray[index] = mapped;
                        replaced++;
                    }
                    else
                    {
                        replaced += RewriteNode(value, pathMap);
                    }
                }

                break;
        }

        return replaced;
    }

    private static bool TryMapValue(
        JsonNode node,
        IReadOnlyDictionary<string, string> pathMap,
        out string mapped)
    {
        mapped = string.Empty;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!LooksLikeAbsolutePath(text))
        {
            return false;
        }

        if (!pathMap.TryGetValue(text, out var replacement))
        {
            return false;
        }

        mapped = replacement;
        return true;
    }

    private static bool LooksLikeAbsolutePath(string value)
    {
        // Только абсолютные Windows-пути: диск или UNC. Ссылки и служебные
        // строки (URL, идентификаторы) не трогаем.
        if (value.Length < 3)
        {
            return false;
        }

        if (value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return char.IsLetter(value[0]) &&
               value[1] == ':' &&
               (value[2] == '\\' || value[2] == '/');
    }

    private static string? BackupFile(string source, string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);
        var target = Path.Combine(backupDirectory, Path.GetFileName(source));
        if (File.Exists(target))
        {
            // Повторный проход в том же прогоне не должен затирать исходник,
            // снятый до первой перезаписи.
            return target;
        }

        File.Copy(source, target, overwrite: false);
        return target;
    }
}
