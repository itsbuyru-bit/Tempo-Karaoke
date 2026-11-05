using System;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Devices.WiFiDirect;
using Windows.Security.Credentials;

namespace TempoKaraoke.Services;

/// <summary>
/// Runs in a child instance of the app executable, before WinUI starts.
/// Keeping Wi-Fi Direct out of the UI process avoids the Windows App SDK
/// CoreMessagingXP crash seen when the publisher is started on the XAML thread.
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
        var publisher = new WiFiDirectAdvertisementPublisher();
        try
        {
            publisher.Advertisement.IsAutonomousGroupOwnerEnabled = true;
            publisher.Advertisement.ListenStateDiscoverability =
                WiFiDirectAdvertisementListenStateDiscoverability.Normal;
            publisher.Advertisement.LegacySettings.IsEnabled = true;
            publisher.Advertisement.LegacySettings.Ssid = ssid;
            publisher.Advertisement.LegacySettings.Passphrase =
                new PasswordCredential("WiFiDirect", ssid, password);

            publisher.Start();
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (publisher.Status == WiFiDirectAdvertisementPublisherStatus.Started)
                {
                    Write(new { status = "started", ssid, password });
                    break;
                }

                Task.Delay(250).GetAwaiter().GetResult();
            }

            if (publisher.Status != WiFiDirectAdvertisementPublisherStatus.Started)
            {
                Write(new { status = "failed", message = publisher.Status.ToString() });
                return 2;
            }

            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                if (string.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            Write(new { status = "failed", message = "Windows не разрешил запуск Wi-Fi Direct." });
            return 3;
        }
        catch (Exception ex)
        {
            Write(new { status = "failed", message = ex.Message });
            return 4;
        }
        finally
        {
            try { publisher.Stop(); } catch { }
            Write(new { status = "stopped" });
        }

        return 0;
    }
}
