using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TempoKaraoke.Services;

/// <summary>
/// Builds a user-shareable diagnostic ZIP: recent logs, system/network summary,
/// sanitized settings and localization status. The archive never contains media
/// files, media paths or PIN material.
/// </summary>
public static class ReportService
{
    private const long MaxLogBytes = 8 * 1024 * 1024;

    public static string ReportsDirectory
    {
        get
        {
            var folder = Path.Combine(AppStorage.Folder, "Reports");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static async Task<string> CreateAsync(CancellationToken cancellationToken = default)
    {
        var folder = ReportsDirectory;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(folder, $"tempo-karaoke-report-{stamp}.zip");
        var temporary = path + ".tmp";

        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                AddText(archive, "system-info.txt", await DiagnosticsService.BuildSystemReportAsync());
                AddText(archive, "settings-summary.txt", DiagnosticsService.BuildSettingsSummary());
                AddText(archive, "localization.txt", BuildLocalizationReport());

                long copied = 0;
                var index = 0;
                foreach (var logFile in LogService.GetLogFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = new FileInfo(logFile);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    if (copied + info.Length > MaxLogBytes)
                    {
                        break;
                    }

                    copied += info.Length;
                    index++;
                    archive.CreateEntryFromFile(
                        logFile,
                        $"logs/{index:00}-{info.Name}",
                        CompressionLevel.Optimal);
                }

                if (index == 0)
                {
                    AddText(archive, "logs/README.txt", "No log files were available.");
                }
            }

            File.Move(temporary, path, overwrite: true);
            LogService.Info($"Diagnostic report created: {Path.GetFileName(path)}", "report");
            return path;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch
            {
                // The report itself is more important than a leftover temp file.
            }
        }
    }

    private static string BuildLocalizationReport()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"Fallback: {LocalizationService.FallbackLanguage}");
        builder.AppendLine($"Active: {LocalizationService.CurrentLanguage}");
        foreach (var language in LocalizationService.Languages)
        {
            builder.AppendLine(
                $"{language.Tag}: {language.NativeName} ({language.Strings.Count} keys)");
        }

        builder.AppendLine();
        builder.AppendLine("Issues:");
        if (LocalizationService.Issues.Count == 0)
        {
            builder.AppendLine("none");
        }
        else
        {
            foreach (var issue in LocalizationService.Issues)
            {
                builder.AppendLine(issue.ToString());
            }
        }

        return builder.ToString();
    }

    private static void AddText(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
