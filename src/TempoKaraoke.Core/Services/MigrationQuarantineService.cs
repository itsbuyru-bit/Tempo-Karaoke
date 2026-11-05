using System;
using System.Collections.Generic;
using System.IO;

namespace TempoKaraoke.Services;

/// <summary>Текущее состояние корзины: размер, число файлов и признак наличия.</summary>
public sealed record QuarantineInfo(string Root, bool Exists, long Bytes, int Files);

/// <summary>Итог очистки корзины.</summary>
public sealed record QuarantineClearResult(int Deleted, long BytesFreed, IReadOnlyList<string> Errors);

/// <summary>
/// Работа с корзиной миграции. Удаляются только файлы внутри корня корзины;
/// сам корень и посторонние элементы не трогаются.
/// </summary>
public static class MigrationQuarantineService
{
    public static QuarantineInfo Inspect(string quarantineRoot)
    {
        if (string.IsNullOrWhiteSpace(quarantineRoot) || !Directory.Exists(quarantineRoot))
        {
            return new QuarantineInfo(quarantineRoot ?? string.Empty, false, 0, 0);
        }

        long bytes = 0;
        var files = 0;
        foreach (var path in SafeEnumerateFiles(quarantineRoot))
        {
            try
            {
                var info = new FileInfo(path);
                bytes += info.Length;
                files++;
            }
            catch
            {
                // Файл исчез между перечислением и чтением размера.
            }
        }

        return new QuarantineInfo(quarantineRoot, true, bytes, files);
    }

    /// <summary>
    /// Удаляет все файлы в корзине и пустые подпапки. Корень остаётся на месте.
    /// </summary>
    public static QuarantineClearResult Clear(string quarantineRoot)
    {
        if (string.IsNullOrWhiteSpace(quarantineRoot) || !Directory.Exists(quarantineRoot))
        {
            return new QuarantineClearResult(0, 0, Array.Empty<string>());
        }

        var errors = new List<string>();
        var deleted = 0;
        long bytesFreed = 0;

        foreach (var path in SafeEnumerateFiles(quarantineRoot))
        {
            try
            {
                var length = new FileInfo(path).Length;
                File.Delete(path);
                deleted++;
                bytesFreed += length;
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        // Убираем опустевшие подпапки, но не сам корень корзины.
        try
        {
            DeleteEmptyDirectories(quarantineRoot, isRoot: true, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"{quarantineRoot}: {ex.Message}");
        }

        return new QuarantineClearResult(deleted, bytesFreed, errors);
    }

    private static void DeleteEmptyDirectories(string directory, bool isRoot, List<string> errors)
    {
        foreach (var child in Directory.GetDirectories(directory))
        {
            try
            {
                DeleteEmptyDirectories(child, isRoot: false, errors);
                if (!isRoot && !Directory.EnumerateFileSystemEntries(child).Any())
                {
                    Directory.Delete(child);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{child}: {ex.Message}");
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        string[] paths;
        try
        {
            paths = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var path in paths)
        {
            yield return path;
        }
    }
}
