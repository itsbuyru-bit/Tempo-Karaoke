using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TempoKaraoke.Diagnostics;

namespace TempoKaraoke.Services;

public enum HotspotStatus
{
    Started,
    BlockedByActiveWifi,
    Unsupported,
    Busy,
    Failed,
    Cancelled,
}

public sealed record HotspotResult(
    bool Started,
    string Ssid,
    string Password,
    string Message,
    HotspotStatus Status);

/// <summary>
/// Launches the small Wi-Fi Direct helper in a separate process. The WinRT
/// advertisement publisher is stable in a plain console host; running it in
/// the WinUI process can fail inside CoreMessagingXP on some drivers.
///
/// All state changes are serialized by <see cref="WiFiDirectCoordinator"/>;
/// this class only owns the child process and its protocol.
/// </summary>
public static class WiFiDirectService
{
    private static readonly object Gate = new();
    private static Process? _process;
    private static HotspotResult? _lastResult;

    /// <summary>Notified when the helper exits unexpectedly; the coordinator turns it into Error.</summary>
    public static event Action<int>? HelperExited;

    /// <summary>Last known coordinator state, used by diagnostics and reports.</summary>
    public static string StateDescription { get; internal set; } = "Idle";

    public static bool IsRunning
    {
        get
        {
            lock (Gate)
            {
                return _process is { HasExited: false } && _lastResult is { Started: true };
            }
        }
    }

    public static HotspotResult? LastResult
    {
        get
        {
            lock (Gate)
            {
                return _lastResult;
            }
        }
    }

    public static async Task<HotspotResult> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            if (_process is { HasExited: false } && _lastResult is { Started: true } result)
            {
                // Idempotent start: a second click must not recreate the publisher
                // or reset the QR code.
                return result;
            }
        }

        var operationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            var failure = new HotspotResult(
                false,
                "TempoKaraoke",
                string.Empty,
                "Не удалось определить исполняемый файл приложения.",
                HotspotStatus.Failed);
            LogService.Error(failure.Message, category: "wifi-direct", operationId: operationId);
            return failure;
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
        startInfo.ArgumentList.Add("--operation");
        startInfo.ArgumentList.Add(operationId);

        LogService.Info(
            $"Starting Wi-Fi Direct: op={operationId}, state={StateDescription}, ssid={PrivacyMasking.MaskSsid(ssid)}",
            "wifi-direct");

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                var failure = new HotspotResult(
                    false,
                    ssid,
                    password,
                    "Не удалось запустить компонент Wi-Fi Direct.",
                    HotspotStatus.Failed);
                LogService.Error(failure.Message, category: "wifi-direct", operationId: operationId);
                return failure;
            }

            process.EnableRaisingEvents = true;
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    LogService.Warn($"helper: {args.Data}", "wifi-direct");
                }
            };
            process.BeginErrorReadLine();
            process.Exited += (_, _) =>
            {
                var code = -1;
                try
                {
                    code = process.ExitCode;
                }
                catch
                {
                    // Process may already be disposed.
                }

                LogService.Warn($"Wi-Fi Direct helper exited with code {code}.", "wifi-direct");
                HelperExited?.Invoke(code);
            };

            lock (Gate)
            {
                _process = process;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null)
                {
                    var error = await process.StandardError.ReadToEndAsync(timeout.Token);
                    return Fail(
                        ssid,
                        password,
                        string.IsNullOrWhiteSpace(error)
                            ? "Компонент Wi-Fi Direct завершился."
                            : error.Trim(),
                        operationId);
                }

                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                var status = root.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : null;

                if (string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Info("Helper is starting the publisher.", "wifi-direct");
                    continue;
                }

                if (string.Equals(status, "warning", StringComparison.OrdinalIgnoreCase))
                {
                    var warning = root.TryGetProperty("message", out var warningElement)
                        ? warningElement.GetString()
                        : "unknown warning";
                    LogService.Warn($"Helper warning: {warning}", "wifi-direct");
                    continue;
                }

                if (string.Equals(status, "started", StringComparison.Ordinal))
                {
                    var success = new HotspotResult(true, ssid, password, "Wi-Fi Direct включён.", HotspotStatus.Started);
                    lock (Gate)
                    {
                        _lastResult = success;
                    }

                    LogService.Info(
                        $"Wi-Fi Direct started: op={operationId}, ssid={PrivacyMasking.MaskSsid(ssid)}",
                        "wifi-direct");
                    return success;
                }

                if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    var message = root.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : "Windows не разрешил запуск Wi-Fi Direct.";
                    return Fail(ssid, password, message ?? "Ошибка Wi-Fi Direct.", operationId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            await KillAsync(process);
            var cancelled = new HotspotResult(
                false,
                ssid,
                password,
                "Запуск Wi-Fi Direct отменён.",
                HotspotStatus.Cancelled);
            LogService.Warn($"Wi-Fi Direct start cancelled: op={operationId}", "wifi-direct");
            return cancelled;
        }
        catch (Exception ex)
        {
            await KillAsync(process);
            lock (Gate)
            {
                _lastResult = null;
            }

            LogService.Error(
                $"Wi-Fi Direct start failed: op={operationId}, HRESULT=0x{ex.HResult:X8}",
                ex,
                "wifi-direct",
                operationId);
            return new HotspotResult(false, ssid, password, ex.Message, HotspotStatus.Failed);
        }
    }

    public static async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (Gate)
        {
            process = _process;
            _process = null;
            _lastResult = null;
        }

        if (process is null)
        {
            StateDescription = "Idle";
            return;
        }

        LogService.Info($"Stopping Wi-Fi Direct helper (pid={SafePid(process)}).", "wifi-direct");
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("stop");
                await process.StandardInput.FlushAsync();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    LogService.Warn("Wi-Fi Direct helper did not stop in time; killing it.", "wifi-direct");
                    try { process.Kill(true); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Wi-Fi Direct stop error: {ex.Message}", "wifi-direct");
            try { process.Kill(true); } catch { }
        }
        finally
        {
            process.Dispose();
            StateDescription = "Idle";
        }
    }

    private static HotspotResult Fail(string ssid, string password, string message, string operationId)
    {
        lock (Gate)
        {
            _lastResult = null;
        }

        LogService.Error($"Wi-Fi Direct failed: op={operationId}: {message}", category: "wifi-direct", operationId: operationId);
        return new HotspotResult(false, ssid, password, message, HotspotStatus.Failed);
    }

    private static async Task KillAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            process.Dispose();
            lock (Gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }
        }
    }

    private static int SafePid(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
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
