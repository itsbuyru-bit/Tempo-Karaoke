using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TempoKaraoke.Services;

/// <summary>
/// Structured diagnostic log. Normal events require the LoggingEnabled setting;
/// crashes and reports are always written. Files rotate by size so a long
/// session cannot fill the disk.
/// </summary>
public static class LogService
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int RetainedFiles = 5;
    public const string FilePrefix = "tempo-karaoke-";

    private static readonly object Sync = new();
    private static readonly string SessionId =
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    private static string? _sessionHeader;

    public static string SessionIdentifier => SessionId;

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
        Path.Combine(LogDirectory, $"{FilePrefix}{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Writes the environment header once per session, even if logging is off later.</summary>
    public static void BeginSession(string header)
    {
        lock (Sync)
        {
            _sessionHeader = header;
            try
            {
                var path = CurrentLogPath;
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                {
                    File.AppendAllText(
                        path,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [SESSION {SessionId}]{Environment.NewLine}{header}{Environment.NewLine}",
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
            catch
            {
                // Logging must never break startup.
            }
        }
    }

    public static void Info(string message, string category = "app") =>
        Write("INFO", message, null, always: false, category: category);

    /// <summary>
    /// Строка диагностики запуска: пишется даже при выключенном ведении логов,
    /// чтобы по логу можно было разобрать медленный старт и размещение окна.
    /// </summary>
    public static void Startup(string message) =>
        Write("INFO", message, null, always: true, category: "startup");

    public static void Warn(string message, string category = "app") =>
        Write("WARN", message, null, always: false, category: category);

    public static void Error(string message, Exception? exception = null, string category = "app", string? operationId = null) =>
        Write("ERROR", message, exception, always: true, category: category, operationId: operationId);

    /// <summary>Падение пишется в файл независимо от настройки логирования.</summary>
    public static void Crash(Exception? exception, string context = "") =>
        Write("FATAL", string.IsNullOrWhiteSpace(context) ? "Unhandled exception" : context, exception, always: true, category: "crash");

    public static void Write(
        string level,
        string message,
        Exception? exception = null,
        bool always = false,
        string category = "app",
        string? operationId = null)
    {
        if (!always && !SettingsService.LoggingEnabled)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                var path = CurrentLogPath;
                RotateIfNeeded(path);

                var builder = new StringBuilder();
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(level).Append(']')
                    .Append(" [").Append(category).Append(']')
                    .Append(" [session:").Append(SessionId).Append(']');
                if (!string.IsNullOrWhiteSpace(operationId))
                {
                    builder.Append(" [op:").Append(operationId).Append(']');
                }

                builder.Append(' ').Append(message);

                if (exception is not null)
                {
                    AppendException(builder, exception, depth: 0);
                }

                var fileExists = File.Exists(path) && new FileInfo(path).Length > 0;
                if (!fileExists && !string.IsNullOrWhiteSpace(_sessionHeader))
                {
                    builder.Insert(0, _sessionHeader + Environment.NewLine);
                }

                File.AppendAllText(path, builder.AppendLine().ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Логирование не должно ломать приложение.
        }
    }

    /// <summary>Lists log files newest first, including rotated parts.</summary>
    public static IReadOnlyList<string> GetLogFiles()
    {
        try
        {
            return Directory.GetFiles(LogDirectory, FilePrefix + "*.log*")
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxFileBytes)
        {
            return;
        }

        for (var index = RetainedFiles - 1; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            var target = $"{path}.{index + 1}";
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }

        File.Move(path, path + ".1", overwrite: true);
    }

    private static void AppendException(StringBuilder builder, Exception exception, int depth)
    {
        var indent = new string(' ', depth * 2);
        builder.AppendLine()
            .Append(indent)
            .Append(exception.GetType().FullName)
            .Append(": ")
            .Append(exception.Message);

        try
        {
            if (exception.HResult != 0)
            {
                builder.Append(" (HRESULT 0x").Append(exception.HResult.ToString("X8")).Append(')');
            }
        }
        catch
        {
            // Some exception types throw from HResult.
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.AppendLine().Append(indent).Append(exception.StackTrace);
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                AppendException(builder, inner, depth + 1);
            }
        }
        else if (exception.InnerException is not null)
        {
            AppendException(builder, exception.InnerException, depth + 1);
        }
    }
}
