using System;
using System.IO;
using System.Text;

namespace TempoKaraoke.Services;

/// <summary>
/// Простой файловый лог приложения. Обычные записи пишутся только при включённой
/// опции в параметрах, а падения — всегда, чтобы причину можно было разобрать.
/// </summary>
public static class LogService
{
    private static readonly object Sync = new();

    public static string LogDirectory
    {
        get
        {
            var folder = Path.Combine(AppStorage.Folder, "logs");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string CurrentLogPath =>
        Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message, always: false);

    public static void Warn(string message) => Write("WARN", message, always: false);

    /// <summary>Падение пишется в файл независимо от настройки логирования.</summary>
    public static void Crash(Exception? exception, string context = "") =>
        Write("FATAL", $"{context} {exception}".Trim(), exception, always: true);

    public static void Write(string level, string message, Exception? exception = null, bool always = false)
    {
        if (!always && !SettingsService.LoggingEnabled)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            lock (Sync)
            {
                File.AppendAllText(CurrentLogPath, builder.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Логирование не должно ломать приложение.
        }
    }
}
