using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TempoKaraoke.Services;

public sealed record CachedMedia(
    long ModifiedUtcTicks,
    long SizeBytes,
    long DurationMs,
    int Width,
    int Height,
    string? ThumbnailPath,
    bool Probed = false);

/// <summary>
/// Кэш метаданных библиотеки. Хранит только сведения о файлах, не сами видео.
/// </summary>
public sealed class MediaCache : IDisposable
{
    private readonly string _databasePath;
    private readonly object _sync = new();
    private SqliteConnection? _connection;
    private bool _disposed;

    public MediaCache()
    {
        SQLitePCL.Batteries_V2.Init();
        _databasePath = AppStorage.PathFor("media.db");
    }

    private SqliteConnection Connection
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MediaCache));
            }

            if (_connection is not null)
            {
                return _connection;
            }

            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
            _connection.Open();

            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS media (
                    path TEXT PRIMARY KEY,
                    mtime_utc INTEGER NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    duration_ms INTEGER NOT NULL DEFAULT 0,
                    width INTEGER NOT NULL DEFAULT 0,
                    height INTEGER NOT NULL DEFAULT 0,
                    thumbnail_path TEXT NULL,
                    last_seen_utc INTEGER NOT NULL,
                    probed INTEGER NOT NULL DEFAULT 0
                );
                """;
            command.ExecuteNonQuery();
            MigrateSchema(_connection);
            return _connection;
        }
    }

    /// <summary>
    /// Schema version 2 adds <c>probed</c>. The migration is idempotent: on a
    /// fresh database the column already exists (CREATE above), on an old one it
    /// is added once, and the version is never downgraded.
    /// </summary>
    private static void MigrateSchema(SqliteConnection connection)
    {
        try
        {
            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(versionCommand.ExecuteScalar());
            if (version >= 2)
            {
                return;
            }

            if (!HasColumn(connection, "media", "probed"))
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE media ADD COLUMN probed INTEGER NOT NULL DEFAULT 0;";
                alter.ExecuteNonQuery();
            }

            using var setVersion = connection.CreateCommand();
            setVersion.CommandText = "PRAGMA user_version=2;";
            setVersion.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // A migration failure must not hide the whole cache: Load() falls
            // back to the version-1 projection when the column is absent.
            LogService.Warn($"Не удалось обновить схему media.db: {ex.Message}");
        }
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public Dictionary<string, CachedMedia> Load()
    {
        var result = new Dictionary<string, CachedMedia>(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            try
            {
                try
                {
                    ReadRows(Connection, includeProbed: true, result);
                }
                catch (SqliteException ex) when (ex.Message.Contains("probed", StringComparison.OrdinalIgnoreCase))
                {
                    // A database that could not be migrated (read-only file,
                    // another process holding an old schema) still loads.
                    LogService.Warn($"media.db без колонки probed: {ex.Message}");
                    ReadRows(Connection, includeProbed: false, result);
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось прочитать media.db: {ex.Message}");
            }
        }

        return result;
    }

    private static void ReadRows(
        SqliteConnection connection,
        bool includeProbed,
        Dictionary<string, CachedMedia> result)
    {
        using var command = connection.CreateCommand();
        command.CommandText = includeProbed
            ? "SELECT path, mtime_utc, size_bytes, duration_ms, width, height, thumbnail_path, probed FROM media;"
            : "SELECT path, mtime_utc, size_bytes, duration_ms, width, height, thumbnail_path FROM media;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            result[path] = new CachedMedia(
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                includeProbed && reader.GetInt64(7) != 0);
        }
    }

    /// <summary>Пакетная запись в одной транзакции — вместо одного fsync на файл.</summary>
    public void SaveBatch(IReadOnlyList<(string Path, CachedMedia Media, long SeenUtc)> records)
    {
        if (records.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                var connection = Connection;
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO media (path, mtime_utc, size_bytes, duration_ms, width, height, thumbnail_path, last_seen_utc, probed)
                    VALUES ($path, $mtime, $size, $duration, $width, $height, $thumbnail, $seen, $probed)
                    ON CONFLICT(path) DO UPDATE SET
                        mtime_utc = excluded.mtime_utc,
                        size_bytes = excluded.size_bytes,
                        duration_ms = excluded.duration_ms,
                        width = excluded.width,
                        height = excluded.height,
                        thumbnail_path = excluded.thumbnail_path,
                        last_seen_utc = excluded.last_seen_utc,
                        probed = MAX(media.probed, excluded.probed);
                    """;
                var pathParameter = command.Parameters.Add("$path", SqliteType.Text);
                var mtimeParameter = command.Parameters.Add("$mtime", SqliteType.Integer);
                var sizeParameter = command.Parameters.Add("$size", SqliteType.Integer);
                var durationParameter = command.Parameters.Add("$duration", SqliteType.Integer);
                var widthParameter = command.Parameters.Add("$width", SqliteType.Integer);
                var heightParameter = command.Parameters.Add("$height", SqliteType.Integer);
                var thumbnailParameter = command.Parameters.Add("$thumbnail", SqliteType.Text);
                var seenParameter = command.Parameters.Add("$seen", SqliteType.Integer);
                var probedParameter = command.Parameters.Add("$probed", SqliteType.Integer);

                foreach (var (path, media, seenUtc) in records)
                {
                    pathParameter.Value = path;
                    mtimeParameter.Value = media.ModifiedUtcTicks;
                    sizeParameter.Value = media.SizeBytes;
                    durationParameter.Value = media.DurationMs;
                    widthParameter.Value = media.Width;
                    heightParameter.Value = media.Height;
                    thumbnailParameter.Value = (object?)media.ThumbnailPath ?? DBNull.Value;
                    seenParameter.Value = seenUtc;
                    probedParameter.Value = media.Probed ? 1 : 0;
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось записать media.db: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Пакетно записывает результаты быстрой проверки заголовков: разрешение и
    /// признак «проверен». Длительность и превью не трогает; файлы, которых уже
    /// нет на диске, пропускает. Одна транзакция на пакет.
    /// </summary>
    public void SaveProbe(IReadOnlyDictionary<string, (int Width, int Height)?> probes)
    {
        if (probes.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                var connection = Connection;
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO media (path, mtime_utc, size_bytes, duration_ms, width, height, thumbnail_path, last_seen_utc, probed)
                    VALUES ($path, $mtime, $size, 0, COALESCE($width, 0), COALESCE($height, 0), NULL, $seen, 1)
                    ON CONFLICT(path) DO UPDATE SET
                        mtime_utc = excluded.mtime_utc,
                        size_bytes = excluded.size_bytes,
                        width = COALESCE($width, media.width),
                        height = COALESCE($height, media.height),
                        probed = 1,
                        last_seen_utc = excluded.last_seen_utc;
                    """;
                var pathParameter = command.Parameters.Add("$path", SqliteType.Text);
                var mtimeParameter = command.Parameters.Add("$mtime", SqliteType.Integer);
                var sizeParameter = command.Parameters.Add("$size", SqliteType.Integer);
                var widthParameter = command.Parameters.Add("$width", SqliteType.Integer);
                var heightParameter = command.Parameters.Add("$height", SqliteType.Integer);
                var seenParameter = command.Parameters.Add("$seen", SqliteType.Integer);
                var now = DateTime.UtcNow.Ticks;

                foreach (var (path, dimensions) in probes)
                {
                    long size;
                    long modified;
                    try
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists)
                        {
                            continue;
                        }

                        size = info.Length;
                        modified = info.LastWriteTimeUtc.Ticks;
                    }
                    catch (Exception ex)
                    {
                        LogService.Warn($"Не удалось прочитать файл для media.db: {path}: {ex.Message}");
                        continue;
                    }

                    pathParameter.Value = path;
                    mtimeParameter.Value = modified;
                    sizeParameter.Value = size;
                    widthParameter.Value = dimensions is { } value ? value.Width : DBNull.Value;
                    heightParameter.Value = dimensions is { } value2 ? value2.Height : DBNull.Value;
                    seenParameter.Value = now;
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось записать проверку в media.db: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Удаляет записи о файлах, которых больше нет в библиотеке, и возвращает
    /// пути их превью, чтобы вызывающая сторона удалила и файлы.
    /// </summary>
    public IReadOnlyList<string> Prune(IEnumerable<string> validPaths)
    {
        var valid = new HashSet<string>(validPaths, StringComparer.OrdinalIgnoreCase);
        var stale = new List<(string Path, string? Thumbnail)>();
        var thumbnails = new List<string>();

        lock (_sync)
        {
            try
            {
                var connection = Connection;
                using (var select = connection.CreateCommand())
                {
                    select.CommandText = "SELECT path, thumbnail_path FROM media;";
                    using var reader = select.ExecuteReader();
                    while (reader.Read())
                    {
                        var path = reader.GetString(0);
                        if (valid.Contains(path))
                        {
                            continue;
                        }

                        stale.Add((path, reader.IsDBNull(1) ? null : reader.GetString(1)));
                    }
                }

                if (stale.Count == 0)
                {
                    return thumbnails;
                }

                using var transaction = connection.BeginTransaction();
                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM media WHERE path = $path;";
                var pathParameter = delete.Parameters.Add("$path", SqliteType.Text);
                foreach (var (path, thumbnail) in stale)
                {
                    pathParameter.Value = path;
                    delete.ExecuteNonQuery();
                    if (!string.IsNullOrWhiteSpace(thumbnail))
                    {
                        thumbnails.Add(thumbnail);
                    }
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось очистить media.db: {ex.Message}");
            }
        }

        return thumbnails;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
