using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TempoKaraoke.Services;

public sealed record HotspotResult(
    bool Started,
    string Ssid,
    string Password,
    string Message);

/// <summary>
/// Launches the small Wi-Fi Direct helper in a separate process. The WinRT
/// advertisement publisher is stable in a plain console host; running it in
/// the WinUI process can fail inside CoreMessagingXP on some drivers.
/// </summary>
public static class WiFiDirectService
{
    private static Process? _process;
    private static readonly object Gate = new();

    public static async Task<HotspotResult> StartAsync()
    {
        await StopAsync();

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return new HotspotResult(
                false,
                "TempoKaraoke",
                string.Empty,
                "Не удалось определить исполняемый файл приложения.");
        }

        var ssid = $"TempoKaraoke_{RandomNumberGenerator.GetInt32(1000, 10000)}";
        var password = CreatePassword(8);

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("--wifi-direct-helper");
        startInfo.ArgumentList.Add("--ssid");
        startInfo.ArgumentList.Add(ssid);
        startInfo.ArgumentList.Add("--password");
        startInfo.ArgumentList.Add(password);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new HotspotResult(false, ssid, password, "Не удалось запустить компонент Wi-Fi Direct.");
            }

            lock (Gate)
            {
                _process = process;
            }

            var deadline = DateTime.UtcNow.AddSeconds(10);
            var lineTask = process.StandardOutput.ReadLineAsync();
            while (DateTime.UtcNow < deadline)
            {
                var completed = await Task.WhenAny(lineTask, Task.Delay(250));
                if (completed != lineTask)
                {
                    if (process.HasExited)
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        return new HotspotResult(false, ssid, password,
                            string.IsNullOrWhiteSpace(error) ? "Компонент Wi-Fi Direct завершился." : error.Trim());
                    }

                    continue;
                }

                var line = await lineTask;
                if (line is null)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    return new HotspotResult(false, ssid, password,
                        string.IsNullOrWhiteSpace(error) ? "Компонент Wi-Fi Direct завершился." : error.Trim());
                }

                lineTask = process.StandardOutput.ReadLineAsync();
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                var status = root.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : null;
                if (string.Equals(status, "started", StringComparison.Ordinal))
                {
                    return new HotspotResult(true, ssid, password, "Wi-Fi Direct включён.");
                }

                if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    var message = root.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : "Windows не разрешил запуск Wi-Fi Direct.";
                    await StopAsync();
                    return new HotspotResult(false, ssid, password, message ?? "Ошибка Wi-Fi Direct.");
                }
            }

            await StopAsync();
            return new HotspotResult(false, ssid, password, "Wi-Fi Direct не запустился за 10 секунд.");
        }
        catch (Exception ex)
        {
            try { process?.Kill(true); } catch { }
            lock (Gate) { _process = null; }
            return new HotspotResult(false, ssid, password, ex.Message);
        }
    }

    public static async Task StopAsync()
    {
        Process? process;
        lock (Gate)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("stop");
                await process.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch
        {
            try { process.Kill(true); } catch { }
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string CreatePassword(int length)
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var builder = new StringBuilder(length);
        foreach (var value in bytes)
        {
            builder.Append(alphabet[value % alphabet.Length]);
        }

        return builder.ToString();
    }
}
