using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TempoKaraoke.Diagnostics;
using TempoKaraoke.Networking;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace TempoKaraoke.Services;

/// <summary>Snapshot of the wireless environment used by the pre-flight checks.</summary>
public sealed record WiFiEnvironment(
    bool HasWifiRadio,
    bool WifiEnabled,
    string AdapterName,
    string RadioState,
    bool IsConnected,
    string Ssid,
    string Connectivity)
{
    public bool CanAdvertise => HasWifiRadio && WifiEnabled;
}

/// <summary>
/// Serializes every operation that changes the Wi-Fi adapter state and exposes
/// an explicit state machine to the UI. No code path may start or stop the
/// publisher concurrently; a second click is idempotent instead of restarting.
/// </summary>
public static class WiFiDirectCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly WiFiDirectStateMachine Machine = new();
    private static HotspotResult? _lastResult;

    static WiFiDirectCoordinator()
    {
        WiFiDirectService.HelperExited += OnHelperExited;
    }

    public static event EventHandler? StateChanged;

    public static WiFiDirectState State
    {
        get
        {
            lock (Machine)
            {
                return Machine.State;
            }
        }
    }

    public static string LastError => Machine.LastError;

    public static HotspotResult? LastResult => _lastResult;

    public static string StateDescription =>
        $"{Machine.State}" +
        (string.IsNullOrWhiteSpace(Machine.LastError) ? string.Empty : $": {Machine.LastError}");

    /// <summary>Reads adapter and infrastructure-connection state without touching Wi-Fi Direct.</summary>
    public static async Task<WiFiEnvironment> InspectAsync(CancellationToken cancellationToken = default)
    {
        var hasRadio = false;
        var enabled = false;
        var adapterName = string.Empty;
        var radioState = "unknown";

        try
        {
            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(radio => radio.Kind == RadioKind.WiFi);
            if (wifi is not null)
            {
                hasRadio = true;
                adapterName = wifi.Name;
                radioState = wifi.State.ToString();
                enabled = wifi.State == RadioState.On;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Cannot read Wi-Fi radios: {ex.Message}", "wifi-direct");
        }

        var connected = false;
        var ssid = string.Empty;
        var connectivity = "None";
        try
        {
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (!profile.IsWlanConnectionProfile)
                {
                    continue;
                }

                var level = profile.GetNetworkConnectivityLevel();
                if (level == NetworkConnectivityLevel.None)
                {
                    continue;
                }

                connected = true;
                connectivity = level.ToString();
                ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid() ?? string.Empty;
                break;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Cannot read Wi-Fi connection profiles: {ex.Message}", "wifi-direct");
        }

        return new WiFiEnvironment(hasRadio, enabled, adapterName, radioState, connected, ssid, connectivity);
    }

    /// <summary>
    /// Pre-flight checks, then starts the helper. Safe mode blocks the start when
    /// the computer is connected to an infrastructure network.
    /// </summary>
    public static async Task<HotspotResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (Machine.State is WiFiDirectState.Advertising
            or WiFiDirectState.WaitingForConnection
            or WiFiDirectState.Connected)
        {
            if (_lastResult is { Started: true } existing)
            {
                return existing;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));

        try
        {
            if (!await Gate.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token))
            {
                return new HotspotResult(
                    false,
                    "TempoKaraoke",
                    string.Empty,
                    LocalizationService.Get("Remote_HotspotStarting"),
                    HotspotStatus.Busy);
            }
        }
        catch (OperationCanceledException)
        {
            return new HotspotResult(
                false,
                "TempoKaraoke",
                string.Empty,
                LocalizationService.Get("Remote_HotspotStarting"),
                HotspotStatus.Cancelled);
        }

        try
        {
            // Re-check after taking the lock: a second click may have completed
            // while this call waited.
            if ((Machine.State is WiFiDirectState.Advertising
                    or WiFiDirectState.WaitingForConnection
                    or WiFiDirectState.Connected) &&
                _lastResult is { Started: true } active)
            {
                return active;
            }

            Transition(WiFiDirectState.CheckingSupport);
            var environment = await InspectAsync(timeout.Token);
            if (!environment.CanAdvertise)
            {
                var message = environment.HasWifiRadio
                    ? LocalizationService.Format("Wifi_State_Error") + " " + environment.RadioState
                    : LocalizationService.Get("Ui_WifiBlockedMessage");
                return Fail(HotspotStatus.Unsupported, message);
            }

            if (environment.IsConnected)
            {
                var maskedSsid = PrivacyMasking.MaskSsid(environment.Ssid);
                LogService.Warn(
                    $"Infrastructure Wi-Fi is active (SSID {maskedSsid}); safe mode={SettingsService.WifiDirectSafeMode}",
                    "wifi-direct");

                if (SettingsService.WifiDirectSafeMode)
                {
                    var message = LocalizationService.Format("Ui_WifiBlockedMessage", maskedSsid);
                    return Fail(HotspotStatus.BlockedByActiveWifi, message);
                }

                LogService.Warn(
                    "Safe mode is off: starting Wi-Fi Direct over an active Wi-Fi connection. " +
                    "A driver conflict or system crash may occur; this is recorded in the log.",
                    "wifi-direct");
            }

            Transition(WiFiDirectState.Starting);
            var result = await WiFiDirectService.StartAsync(timeout.Token);
            if (result.Started)
            {
                _lastResult = result;
                Transition(WiFiDirectState.Advertising);
            }
            else
            {
                _lastResult = null;
                Machine.Fail(result.Message);
                RaiseStateChanged();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            var result = new HotspotResult(
                false,
                "TempoKaraoke",
                string.Empty,
                "Запуск Wi-Fi Direct отменён.",
                HotspotStatus.Cancelled);
            Machine.Fail(result.Message);
            RaiseStateChanged();
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error("Wi-Fi Direct coordinator failed", ex, "wifi-direct");
            var result = new HotspotResult(false, "TempoKaraoke", string.Empty, ex.Message, HotspotStatus.Failed);
            Machine.Fail(ex.Message);
            RaiseStateChanged();
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await Gate.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
            {
                LogService.Warn("Stop request timed out waiting for the Wi-Fi Direct start operation.", "wifi-direct");
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (Machine.State == WiFiDirectState.Idle && !WiFiDirectService.IsRunning)
            {
                return;
            }

            Transition(WiFiDirectState.Stopping);
            await WiFiDirectService.StopAsync(cancellationToken);
            _lastResult = null;
            Machine.Reset();
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            LogService.Error("Wi-Fi Direct stop failed", ex, "wifi-direct");
            Machine.Fail(ex.Message);
            RaiseStateChanged();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>The remote server reports connected phones; the UI follows the state machine.</summary>
    public static void NotifyClientCount(int count)
    {
        if (count > 0 && Machine.State is WiFiDirectState.Advertising or WiFiDirectState.WaitingForConnection)
        {
            Transition(WiFiDirectState.Connected);
        }
        else if (count == 0 && Machine.State == WiFiDirectState.Connected)
        {
            Transition(WiFiDirectState.WaitingForConnection);
        }
    }

    private static HotspotResult Fail(HotspotStatus status, string message)
    {
        _lastResult = null;
        Machine.Fail(message);
        RaiseStateChanged();
        return new HotspotResult(false, "TempoKaraoke", string.Empty, message, status);
    }

    private static void OnHelperExited(int exitCode)
    {
        if (Machine.State is WiFiDirectState.Advertising
            or WiFiDirectState.WaitingForConnection
            or WiFiDirectState.Connected
            or WiFiDirectState.Starting)
        {
            _lastResult = null;
            Machine.Fail($"Wi-Fi Direct helper exited with code {exitCode}.");
            RaiseStateChanged();
        }
    }

    private static void Transition(WiFiDirectState next)
    {
        if (Machine.TryTransition(next))
        {
            WiFiDirectService.StateDescription = StateDescription;
            RaiseStateChanged();
        }
    }

    private static void RaiseStateChanged() => StateChanged?.Invoke(null, EventArgs.Empty);
}
