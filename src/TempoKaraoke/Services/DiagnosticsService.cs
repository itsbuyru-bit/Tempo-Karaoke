using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Devices.WiFiDirect;
using Windows.Networking.Connectivity;
using TempoKaraoke.Diagnostics;

namespace TempoKaraoke.Services;

/// <summary>
/// Collects environment and network information for the log session header and
/// the user-facing error report. User data (song titles, media paths, PINs) is
/// never included.
/// </summary>
public static class DiagnosticsService
{
    /// <summary>Synchronous subset used when the log session starts.</summary>
    public static string BuildSessionHeader()
    {
        var builder = new StringBuilder();
        AppendApplication(builder);
        builder.AppendLine($"Culture: {CultureInfo.CurrentCulture.Name}; UI culture: {CultureInfo.CurrentUICulture.Name}");
        builder.AppendLine($"Language: {LocalizationService.CurrentLanguage}");
        builder.AppendLine($"Log directory: {LogService.LogDirectory}");
        return builder.ToString();
    }

    /// <summary>Full report with adapter and connection details (no private data).</summary>
    public static async Task<string> BuildSystemReportAsync()
    {
        var builder = new StringBuilder();
        AppendApplication(builder);
        builder.AppendLine($"Culture: {CultureInfo.CurrentCulture.Name}; UI culture: {CultureInfo.CurrentUICulture.Name}");
        builder.AppendLine($"Language: {LocalizationService.CurrentLanguage}");
        builder.AppendLine($"Session: {LogService.SessionIdentifier}");
        builder.AppendLine($"Log directory: {LogService.LogDirectory}");
        builder.AppendLine($"Localization directory: {LocalizationService.Languages.Count} language(s) loaded");
        builder.AppendLine($"Wi-Fi Direct: {WiFiDirectService.StateDescription}");
        builder.AppendLine();

        builder.AppendLine("Wi-Fi adapters:");
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var wifiRadios = radios.Where(radio => radio.Kind == RadioKind.WiFi).ToList();
            if (wifiRadios.Count == 0)
            {
                builder.AppendLine("  none reported by Windows");
            }

            foreach (var radio in wifiRadios)
            {
                builder.AppendLine($"  {radio.Name}: {radio.State}");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"  unavailable: {ex.GetType().Name}: {ex.Message} (HRESULT 0x{ex.HResult:X8})");
        }

        builder.AppendLine("Wi-Fi connections:");
        try
        {
            var found = false;
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (!profile.IsWlanConnectionProfile)
                {
                    continue;
                }

                found = true;
                var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
                builder.AppendLine(
                    $"  SSID: {PrivacyMasking.MaskSsid(ssid)}; " +
                    $"connectivity: {profile.GetNetworkConnectivityLevel()}");
            }

            if (!found)
            {
                builder.AppendLine("  no active Wi-Fi profile");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"  unavailable: {ex.GetType().Name}: {ex.Message} (HRESULT 0x{ex.HResult:X8})");
        }

        builder.AppendLine();
        builder.AppendLine("Localization issues:");
        var issues = LocalizationService.Issues;
        if (issues.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            foreach (var issue in issues)
            {
                builder.AppendLine($"  {issue}");
            }
        }

        return builder.ToString();
    }

    /// <summary>Adapter, connection and Wi-Fi Direct state for the settings diagnostics card.</summary>
    public static async Task<string> BuildWifiDirectDiagnosticsAsync()
    {
        var environment = await WiFiDirectCoordinator.InspectAsync();
        var adapter = environment.HasWifiRadio
            ? environment.AdapterName
            : LocalizationService.Get("Wifi_None");
        var driver = await GetDriverVersionAsync();
        var connection = environment.IsConnected
            ? $"{PrivacyMasking.MaskSsid(environment.Ssid)} ({environment.Connectivity})"
            : LocalizationService.Get("Wifi_None");
        var state = LocalizationService.Get($"Wifi_State_{WiFiDirectCoordinator.State}");
        var error = string.IsNullOrWhiteSpace(WiFiDirectCoordinator.LastError)
            ? LocalizationService.Get("Wifi_None")
            : WiFiDirectCoordinator.LastError;

        return LocalizationService.Format(
            "Wifi_DiagnosticsTemplate",
            adapter,
            string.IsNullOrWhiteSpace(driver) ? environment.RadioState : driver,
            connection,
            state,
            error);
    }

    /// <summary>Sanitized settings summary: values that help diagnosis, no paths or secrets.</summary>
    public static string BuildSettingsSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Settings summary (sanitized)");
        builder.AppendLine($"Theme: {SettingsService.Theme}");
        builder.AppendLine($"Language: {SettingsService.Language}");
        builder.AppendLine($"LanguageConfigured: {SettingsService.LanguageConfigured}");
        builder.AppendLine($"LoggingEnabled: {SettingsService.LoggingEnabled}");
        builder.AppendLine($"CountdownEnabled: {SettingsService.CountdownEnabled}");
        builder.AppendLine($"CountdownSeconds: {SettingsService.CountdownSeconds}");
        builder.AppendLine($"RemoteAutoStopEnabled: {SettingsService.RemoteAutoStopEnabled}");
        builder.AppendLine($"RemoteAutoStopMinutes: {SettingsService.RemoteAutoStopMinutes}");
        builder.AppendLine($"RequestLimitPerGuest: {SettingsService.RequestLimitPerGuest}");
        builder.AppendLine($"MaxPendingRequests: {SettingsService.MaxPendingRequests}");
        builder.AppendLine($"VideoRootsCount: {SettingsService.VideoRoots.Count}");
        builder.AppendLine($"EqualizerEnabled: {SettingsService.EqualizerEnabled}");
        builder.AppendLine("Media paths, recent files, song titles, guest names and PIN material are intentionally omitted.");
        return builder.ToString();
    }

    private static async Task<string> GetDriverVersionAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(
                WiFiDirectDevice.GetDeviceSelector(),
                new[] { "System.Devices.DriverVersion" });
            foreach (var device in devices)
            {
                if (device.Properties.TryGetValue("System.Devices.DriverVersion", out var value) &&
                    value is string text &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Cannot read the Wi-Fi driver version: {ex.Message}", "wifi-direct");
        }

        return string.Empty;
    }

    private static void AppendApplication(StringBuilder builder)
    {
        builder.AppendLine($"Application: Tempo Karaoke {AppInfo.DisplayVersion}");
        builder.AppendLine("Install mode: Inno Setup");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Machine: {Environment.MachineName}");
        builder.AppendLine($"Processors: {Environment.ProcessorCount}");
    }
}
