using System;
using System.Collections.Generic;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Хранит именованные списки воспроизведения в JSON рядом с локальными данными
/// приложения. Запись атомарная, чтобы падение не оставило битый файл.
/// </summary>
public static class PlaylistService
{
    private const string FileName = "playlists.json";

    /// <summary>Файл хранилища; миграция переписывает пути прямо в нём.</summary>
    public static string FilePath => AppStorage.PathFor(FileName);

    public static List<Playlist> Load()
    {
        try
        {
            return JsonFile.Read(AppStorage.PathFor(FileName), AppJsonContext.Default.ListPlaylist)
                   ?? new List<Playlist>();
        }
        catch (Exception ex)
        {
            LogService.Warn($"Не удалось прочитать плейлисты: {ex.Message}");
            return new List<Playlist>();
        }
    }

    public static void Save(IEnumerable<Playlist> playlists)
    {
        try
        {
            JsonFile.Write(
                AppStorage.PathFor(FileName),
                new List<Playlist>(playlists),
                AppJsonContext.Default.ListPlaylist);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Не удалось сохранить плейлисты: {ex.Message}");
        }
    }
}
