using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TempoKaraoke.Services;

/// <summary>Fully resolved toast strings. Nothing here is a resource key.</summary>
public sealed record UpdateToastContent(
    string Title,
    string Body,
    string? Attribution,
    string InstallLabel,
    string PostponeLabel,
    string? ChangelogLabel,
    string LaunchArgument,
    string? LogoUri,
    DateTimeOffset Timestamp,
    string Tag = "update-available",
    string Group = "updates");

/// <summary>
/// Builds the toast payload as XML. Kept in Core (no WinUI dependency) so the
/// generated markup can be unit-tested without starting the application.
/// </summary>
public static class UpdateToastBuilder
{
    public const string OpenUpdatePageArgument = "action=open_update_page";
    public const string InstallArgument = "action=install";
    public const string PostponeArgument = "action=postpone";
    public const string ChangelogArgument = "action=changelog";

    private static readonly Regex ListMarker = new(@"^\s*(?:[-*+]|\d+[.)])\s+", RegexOptions.Compiled);
    private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownEmphasis = new(@"\*\*|__|`", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string BuildXml(UpdateToastContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var xml = new StringBuilder(1024);
        xml.Append("<toast");
        AppendAttribute(xml, "launch", content.LaunchArgument);
        AppendAttribute(xml, "activationType", "foreground");
        AppendAttribute(xml, "displayTimestamp", content.Timestamp.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        xml.AppendLine(">");
        xml.AppendLine("  <visual>");
        xml.AppendLine("    <binding template=\"ToastGeneric\">");
        AppendText(xml, content.Title, "      <text>", "</text>");
        AppendText(xml, content.Body, "      <text>", "</text>");
        if (!string.IsNullOrWhiteSpace(content.Attribution))
        {
            AppendText(xml, content.Attribution, "      <text placement=\"attribution\">", "</text>");
        }

        if (!string.IsNullOrWhiteSpace(content.LogoUri))
        {
            xml.Append("      <image placement=\"appLogoOverride\" hint-crop=\"circle\"");
            AppendAttribute(xml, "src", content.LogoUri);
            xml.AppendLine(" alt=\"Tempo Karaoke\" />");
        }

        xml.AppendLine("    </binding>");
        xml.AppendLine("  </visual>");

        if (!string.IsNullOrWhiteSpace(content.InstallLabel) ||
            !string.IsNullOrWhiteSpace(content.PostponeLabel) ||
            !string.IsNullOrWhiteSpace(content.ChangelogLabel))
        {
            xml.AppendLine("  <actions>");
            AppendAction(xml, content.InstallLabel, InstallArgument);
            AppendAction(xml, content.PostponeLabel, PostponeArgument);
            AppendAction(xml, content.ChangelogLabel, ChangelogArgument);
            xml.AppendLine("  </actions>");
        }

        xml.AppendLine("</toast>");
        return xml.ToString();
    }

    /// <summary>
    /// Extracts 1..max human-readable bullets from a GitHub release body.
    /// Markdown decorations and links are stripped; long lines are shortened.
    /// </summary>
    public static IReadOnlyList<string> ExtractHighlights(string? changelog, int max = 3)
    {
        var highlights = new List<string>();
        if (string.IsNullOrWhiteSpace(changelog) || max <= 0)
        {
            return highlights;
        }

        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var text = Clean(current.ToString());
            current.Clear();
            if (text.Length == 0 || text.EndsWith(':'))
            {
                return;
            }

            text = Shorten(text);
            if (!highlights.Contains(text, StringComparer.Ordinal))
            {
                highlights.Add(text);
            }
        }

        foreach (var rawLine in changelog.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var indented = rawLine.Length > rawLine.TrimStart().Length;
            if (line.StartsWith('#'))
            {
                Flush();
                if (highlights.Count >= max)
                {
                    break;
                }

                continue;
            }

            if (ListMarker.IsMatch(line))
            {
                Flush();
                if (highlights.Count >= max)
                {
                    break;
                }

                current.Append(ListMarker.Replace(line, string.Empty));
            }
            else if (current.Length > 0 && indented)
            {
                // Многострочный пункт changelog: склеиваем продолжение.
                current.Append(' ').Append(line);
            }
            else if (current.Length > 0)
            {
                // Абзац без отступа завершает список.
                Flush();
            }
        }

        Flush();
        if (highlights.Count > max)
        {
            highlights.RemoveRange(max, highlights.Count - max);
        }

        return highlights;
    }

    private static string Shorten(string text)
    {
        const int limit = 90;
        if (text.Length <= limit)
        {
            return text;
        }

        // Для тоста достаточно первого предложения пункта changelog.
        var end = text.IndexOfAny(new[] { '.', '!', '?', ';' });
        if (end >= 20 && end < limit)
        {
            return text[..(end + 1)].Trim();
        }

        return text[..(limit - 3)].TrimEnd() + "…";
    }

    private static string Clean(string value)
    {
        value = MarkdownLink.Replace(value, "$1");
        value = MarkdownEmphasis.Replace(value, string.Empty);
        return Whitespace.Replace(value, " ").Trim();
    }

    /// <summary>Human-readable size for the toast body (1 GB / 110 MB / 800 KB).</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        const double Kilobyte = 1024d;
        const double Megabyte = Kilobyte * 1024d;
        const double Gigabyte = Megabyte * 1024d;

        return bytes switch
        {
            >= (long)Gigabyte => (bytes / Gigabyte).ToString("0.#", CultureInfo.InvariantCulture) + " GB",
            >= (long)Megabyte => Math.Round(bytes / Megabyte).ToString("0", CultureInfo.InvariantCulture) + " MB",
            >= (long)Kilobyte => Math.Round(bytes / Kilobyte).ToString("0", CultureInfo.InvariantCulture) + " KB",
            _ => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        };
    }

    private static void AppendText(StringBuilder xml, string? value, string open, string close)
    {
        xml.Append(open);
        xml.Append(Escape(value));
        xml.AppendLine(close);
    }

    private static void AppendAction(StringBuilder xml, string? label, string argument)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        xml.Append("    <action");
        AppendAttribute(xml, "content", label);
        AppendAttribute(xml, "arguments", argument);
        AppendAttribute(xml, "activationType", "foreground");
        xml.AppendLine(" />");
    }

    private static void AppendAttribute(StringBuilder xml, string name, string? value)
    {
        xml.Append(' ').Append(name).Append("=\"").Append(Escape(value)).Append('"');
    }

    private static string Escape(string? value) => SecurityElement.Escape(value) ?? string.Empty;
}
