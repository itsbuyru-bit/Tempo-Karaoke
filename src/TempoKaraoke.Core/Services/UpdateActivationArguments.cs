using System;

namespace TempoKaraoke.Services;

/// <summary>
/// Parses activation arguments of update notifications. Accepts both WinAppSDK
/// app notifications (action=install) and legacy toast URIs
/// (tempokaraoke:updates-install). Unknown values fall back to opening the
/// update page, per the notification contract.
/// </summary>
public static class UpdateActivationArguments
{
    public const string OpenUpdatePage = "open_update_page";
    public const string Install = "install";
    public const string Postpone = "postpone";

    public static string? ParseAction(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return null;
        }

        var text = argument.Trim();
        if (text.StartsWith("tempokaraoke:", StringComparison.OrdinalIgnoreCase))
        {
            return Normalize(text["tempokaraoke:".Length..]);
        }

        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals("action", StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(parts[1].Trim());
            }
        }

        return Normalize(text);
    }

    private static string Normalize(string action)
    {
        return action.Trim().ToLowerInvariant() switch
        {
            "install" or "updates-install" => Install,
            "postpone" or "later" or "updates-postpone" => Postpone,
            _ => OpenUpdatePage,
        };
    }
}
