using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace TempoKaraoke.Services;

/// <summary>
/// One-time import of settings/queue from a normal installation when the app
/// starts as a portable copy. Runs before MainWindow so SettingsService reads
/// the imported settings on first access.
/// </summary>
public static class PortableMigrationService
{
    private const string SettingsFile = "settings.json";
    private const string QueueFile = "queue.json";
    private const string PlaylistsFile = "playlists.json";
    private const string ClientsFile = "remote-clients.json";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public static void PromptIfNeeded()
    {
        if (!AppStorage.IsPortable)
        {
            return;
        }

        try
        {
            var target = AppStorage.Folder;
            var targetSettings = Path.Combine(target, SettingsFile);
            if (File.Exists(targetSettings))
            {
                return;
            }

            var source = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TempoKaraoke");
            var sourceSettings = Path.Combine(source, SettingsFile);
            if (!File.Exists(sourceSettings))
            {
                return;
            }

            var russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
            var message = russian
                ? "Найдены настройки обычной установки Tempo Karaoke.\n\nСкопировать настройки, очередь и плейлисты в портативную версию?"
                : "Settings from a regular Tempo Karaoke installation were found.\n\nCopy settings, queue, and playlists into the portable version?";
            var caption = russian ? "Tempo Karaoke — портативный режим" : "Tempo Karaoke — portable mode";

            if (MessageBoxW(0, message, caption, 0x00000004 /* MB_YESNO */ | 0x00000040 /* MB_ICONQUESTION */) != 6 /* IDYES */)
            {
                return;
            }

            Directory.CreateDirectory(target);
            CopyIfExists(source, target, SettingsFile);
            CopyIfExists(source, target, QueueFile);
            CopyIfExists(source, target, PlaylistsFile);
            CopyIfExists(source, target, ClientsFile);
            AddPortableLibrary(targetSettings);
            LogService.Info("[portable] settings, queue, and playlists imported from the local installation");
        }
        catch (Exception ex)
        {
            LogService.Warn($"Portable migration failed: {ex.Message}");
        }
    }

    private static void CopyIfExists(string source, string target, string fileName)
    {
        var from = Path.Combine(source, fileName);
        if (File.Exists(from))
        {
            File.Copy(from, Path.Combine(target, fileName), overwrite: false);
        }
    }

    private static void AddPortableLibrary(string settingsPath)
    {
        var library = SettingsService.PortableLibraryPath;
        if (library is null || !File.Exists(settingsPath))
        {
            return;
        }

        var root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
        if (root is null)
        {
            return;
        }

        var roots = root["VideoRoots"] as JsonArray ?? new JsonArray();
        foreach (var existing in roots)
        {
            if (string.Equals(existing?.GetValue<string>(), library, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        roots.Add(library);
        root["VideoRoots"] = roots;
        root["VideoRootsConfigured"] = true;
        File.WriteAllText(settingsPath, root.ToJsonString());
    }
}
