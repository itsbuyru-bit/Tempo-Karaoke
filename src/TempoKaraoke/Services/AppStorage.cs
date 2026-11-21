using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TempoKaraoke.Services;

/// <summary>
/// Единое место для локальных файлов приложения.
/// Приложение поставляется только как unpackaged-сборка Inno Setup,
/// поэтому данные всегда лежат в %LocalAppData%\TempoKaraoke.
/// </summary>
public static class AppStorage
{
    private static readonly Lazy<string> LazyFolder = new(CreateFolder);

    private const string PortableMarker = "portable.marker";

    public static string Folder => LazyFolder.Value;

    /// <summary>True when the app runs from a portable folder created by the installer.</summary>
    public static bool IsPortable =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    public static string PathFor(string fileName) => Path.Combine(Folder, fileName);

    private static string CreateFolder()
    {
        if (IsPortable)
        {
            var portable = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(portable);
            return portable;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, "TempoKaraoke"),
            Path.Combine(Path.GetTempPath(), "TempoKaraoke")
        };

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

        // Degraded mode: вернём последний кандидат, чтобы приложение стартовало.
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
