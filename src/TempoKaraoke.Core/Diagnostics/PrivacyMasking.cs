using System.Text.RegularExpressions;

namespace TempoKaraoke.Diagnostics;

/// <summary>
/// Masking helpers for diagnostic output. Logs must stay useful without
/// collecting private user data: media paths are dropped, SSIDs and MAC
/// addresses are partially masked, PINs are never written at all.
/// </summary>
public static partial class PrivacyMasking
{
    public const string HiddenPath = "<path hidden>";
    public const string HiddenPin = "<pin hidden>";

    [GeneratedRegex(@"(?i)\b([0-9a-f]{2})([:-])([0-9a-f]{2})(?:\2[0-9a-f]{2}){3,4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex MacPattern();

    /// <summary>Keeps the first character, masks the rest (Wi-Fi SSID).</summary>
    public static string MaskSsid(string? ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return "<none>";
        }

        var trimmed = ssid.Trim();
        return trimmed.Length <= 2
            ? new string('*', trimmed.Length)
            : trimmed[..1] + new string('*', Math.Min(12, trimmed.Length - 1));
    }

    /// <summary>Keeps the vendor and last octet of a MAC address.</summary>
    public static string MaskMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return "<none>";
        }

        var parts = mac.Trim().Split(':', '-');
        if (parts.Length < 4)
        {
            return "**:**:**:**:**:**";
        }

        return $"{parts[0]}:{parts[1]}:**:**:**:{parts[^1]}";
    }

    /// <summary>Replaces every MAC address found inside a diagnostic text.</summary>
    public static string MaskMacAddresses(string text) =>
        MacPattern().Replace(text, match =>
        {
            var parts = match.Value.Split(match.Groups[2].Value[0]);
            return parts.Length >= 4
                ? $"{parts[0]}:{parts[1]}:**:**:**:{parts[^1]}"
                : "**:**:**:**:**:**";
        });

    /// <summary>Media paths are never logged.</summary>
    public static string MaskPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "<none>" : HiddenPath;

    public static string MaskPin(string? pin) =>
        string.IsNullOrWhiteSpace(pin) ? string.Empty : HiddenPin;
}
