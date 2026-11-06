using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Devices.WiFiDirect;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace TempoKaraoke.Services;

/// <summary>
/// Runs in a child instance of the app executable, before WinUI starts.
/// Keeping Wi-Fi Direct out of the UI process avoids the Windows App SDK
/// CoreMessagingXP crash seen when the publisher is started on the XAML thread.
/// Every step is written to logs/tempo-karaoke-wifi-direct.log.
/// </summary>
public static class WiFiDirectHelperRuntime
{
    public static int Run(string[] args)
    {
        string? GetArg(string name)
        {
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        static void Write(object payload)
        {
            Console.WriteLine(JsonSerializer.Serialize(payload));
            Console.Out.Flush();
        }

        var ssid = GetArg("--ssid") ?? "TempoKaraoke";
        var password = GetArg("--password") ?? "tempo123456";
        var operationId = GetArg("--operation") ?? "-";
        var publisher = new WiFiDirectAdvertisementPublisher();
        var started = false;

        HelperLog(operationId, $"helper started; pid={Environment.ProcessId}; ssid={MaskSsid(ssid)}");
        Write(new { status = "starting" });

        try
        {
            var activeWifi = HasActiveWifi();
            if (activeWifi)
            {
                HelperLog(operationId, "active infrastructure Wi-Fi detected");
                Write(new { status = "warning", message = "active-wifi" });
            }

            publisher.Advertisement.IsAutonomousGroupOwnerEnabled = true;
            publisher.Advertisement.ListenStateDiscoverability =
                WiFiDirectAdvertisementListenStateDiscoverability.Normal;
            publisher.Advertisement.LegacySettings.IsEnabled = true;
            publisher.Advertisement.LegacySettings.Ssid = ssid;
            publisher.Advertisement.LegacySettings.Passphrase =
                new PasswordCredential("WiFiDirect", ssid, password);

            HelperLog(operationId, "publisher.Start()");
            publisher.Start();

            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (publisher.Status == WiFiDirectAdvertisementPublisherStatus.Started)
                {
                    started = true;
                    HelperLog(operationId, "publisher status=Started");
                    Write(new { status = "started", ssid, password });
                    break;
                }

                if (publisher.Status == WiFiDirectAdvertisementPublisherStatus.Aborted)
                {
                    HelperLog(operationId, "publisher status=Aborted");
                    Write(new { status = "failed", message = "Publisher was aborted by the adapter." });
                    return 2;
                }

                Task.Delay(250).GetAwaiter().GetResult();
            }

            if (!started)
            {
                HelperLog(operationId, $"publisher did not start; status={publisher.Status}");
                Write(new { status = "failed", message = publisher.Status.ToString() });
                return 2;
            }

            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                if (string.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    HelperLog(operationId, "stop requested");
                    break;
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            HelperLog(operationId, $"unauthorized: HRESULT=0x{ex.HResult:X8}");
            Write(new { status = "failed", message = "Windows не разрешил запуск Wi-Fi Direct." });
            return 3;
        }
        catch (Exception ex)
        {
            HelperLog(operationId, $"{ex.GetType().Name}: {ex.Message}; HRESULT=0x{ex.HResult:X8}");
            Write(new { status = "failed", message = ex.Message });
            return 4;
        }
        finally
        {
            if (started)
            {
                try { publisher.Stop(); } catch (Exception ex) { HelperLog(operationId, $"stop error: {ex.Message}"); }
            }

            HelperLog(operationId, "helper finished");
            Write(new { status = "stopped" });
        }

        return 0;
    }

    private static bool HasActiveWifi()
    {
        try
        {
            return NetworkInformation.GetConnectionProfiles()
                .Any(profile => profile.IsWlanConnectionProfile &&
                                profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
        }
        catch
        {
            return false;
        }
    }

    private static string MaskSsid(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return "<none>";
        }

        return ssid.Length <= 2 ? "**" : ssid[..1] + new string('*', Math.Min(12, ssid.Length - 1));
    }

    private static void HelperLog(string operationId, string message)
    {
        try
        {
            var folder = Path.Combine(AppStorage.Folder, "logs");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "tempo-karaoke-wifi-direct.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [helper:{operationId}] {message}{Environment.NewLine}";
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch
        {
            // The helper must not fail because diagnostics are unavailable.
        }
    }
}
