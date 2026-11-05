using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Windows.Storage;

namespace TempoKaraoke.Services;

/// <summary>
/// Единое место для локальных файлов приложения. В упакованном режиме это
/// LocalState, в портативном (без identity) — %LocalAppData%\TempoKaraoke.
/// </summary>
public static class AppStorage
{
    private static readonly Lazy<string> LazyFolder = new(CreateFolder);

    public static string Folder => LazyFolder.Value;

    public static string PathFor(string fileName) => Path.Combine(Folder, fileName);

    private static string CreateFolder()
    {
        try
        {
            var folder = ApplicationData.Current.LocalFolder.Path;
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            // Портативный режим или ограниченное окружение.
        }

        var candidates = new List<string>();
        try
        {
            candidates.Add(ApplicationData.Current.LocalCacheFolder.Path);
            candidates.Add(ApplicationData.Current.TemporaryFolder.Path);
            candidates.Add(ApplicationData.Current.LocalFolder.Path);
        }
        catch
        {
            // Identity недоступна в этом окружении.
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TempoKaraoke"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "TempoKaraoke"));

        foreach (var folder in candidates)
        {
            try
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch
            {
                // Пробуем следующее безопасное расположение.
            }
        }

        // Degraded mode: every known location is read-only in this environment.
        // Return the last candidate so the app can start; writes are guarded by
        // their callers and will simply be skipped until storage is available.
        return candidates[^1];
    }
}

/// <summary>Атомарное чтение/запись небольших JSON-файлов.</summary>
public static class JsonFile
{
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch
        {
            return default;
        }
    }

    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, typeInfo));

        if (File.Exists(path))
        {
            File.Replace(temporary, path, null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}
