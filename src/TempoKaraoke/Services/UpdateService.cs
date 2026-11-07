using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TempoKaraoke.Services;

public enum UpdateCheckStatus
{
    /// <summary>В сборке не задан репозиторий с релизами.</summary>
    NotConfigured,

    UpToDate,

    UpdateAvailable,

    Failed,
}

public sealed record UpdateInfo(
    string Version,
    string InstallerUrl,
    string ReleaseUrl,
    long InstallerSize,
    string? Changelog = null,
    DateTimeOffset? PublishedAt = null);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info = null, string? Error = null)
{
    public static readonly UpdateCheckResult NotConfigured = new(UpdateCheckStatus.NotConfigured);

    public static readonly UpdateCheckResult UpToDate = new(UpdateCheckStatus.UpToDate);

    public static UpdateCheckResult Available(UpdateInfo info) => new(UpdateCheckStatus.UpdateAvailable, info);

    public static UpdateCheckResult Failed(string error) => new(UpdateCheckStatus.Failed, null, error);
}

/// <summary>
/// Проверка обновлений через GitHub Releases и запуск установщика Inno Setup.
/// Приложение скачивает Setup.exe и запускает его молча; установщик закрывает
/// приложение, заменяет файлы и запускает новую версию.
/// </summary>
public static class UpdateService
{
    /// <summary>Владелец репозитория GitHub с релизами.</summary>
    public const string RepositoryOwner = "itsbuyru-bit";

    /// <summary>Имя репозитория GitHub с релизами.</summary>
    public const string RepositoryName = "Tempo-Karaoke";

    private const string InstallerPrefix = "TempoKaraoke-Setup-";
    private const string InstallerExtension = ".exe";

    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>Самообновление доступно: приложение всегда ставится Inno Setup.</summary>
    public static bool IsSupported => true;

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RepositoryOwner) && !string.IsNullOrWhiteSpace(RepositoryName);

    /// <summary>Спрашивает у GitHub последний релиз и сравнивает версии.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return UpdateCheckResult.NotConfigured;
        }

        try
        {
            var url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
            var release = await FetchLatestReleaseAsync(url, cancellationToken).ConfigureAwait(false);

            if (release is null)
            {
                // 404 / пустой ответ: у репозитория ещё нет релизов — обновляться не до чего.
                return UpdateCheckResult.UpToDate;
            }

            var version = ParseVersion(release.TagName);
            var asset = release.Assets?.FirstOrDefault(IsInstallerAsset);
            if (version is null || asset is null || string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                return UpdateCheckResult.Failed("release has no installer asset");
            }

            if (!IsTrustedDownload(asset.BrowserDownloadUrl))
            {
                return UpdateCheckResult.Failed("untrusted download url");
            }

            var info = new UpdateInfo(
                version.ToString(3),
                asset.BrowserDownloadUrl!,
                release.HtmlUrl!,
                asset.Size,
                release.Body,
                release.PublishedAt);

            return version.CompareTo(AppInfo.CurrentVersion) > 0
                ? UpdateCheckResult.Available(info)
                : UpdateCheckResult.UpToDate;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Запрашивает последний релиз. Один повтор на случай обрыва соединения:
    /// в логах пользователей встречается «Unable to read data from the transport connection».
    /// </summary>
    private static async Task<GitHubRelease?> FetchLatestReleaseAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Нет опубликованных релизов: считаем установленную версию актуальной.
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
                }

                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await JsonSerializer
                    .DeserializeAsync(stream, AppJsonContext.Default.GitHubRelease, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (attempt == 0)
            {
                LogService.Warn("Update check failed once, retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Путь, куда скачивается установщик конкретной версии.</summary>
    public static string GetInstallerPath(string version) =>
        Path.Combine(AppStorage.Folder, "updates", $"{InstallerPrefix}{version}{InstallerExtension}");

    /// <summary>Установщик уже скачан и готов к запуску (для состояния «NEW»).</summary>
    public static bool IsInstallerDownloaded(string? version) =>
        !string.IsNullOrWhiteSpace(version) && File.Exists(GetInstallerPath(version));

    /// <summary>Скачивает установщик обновления и возвращает путь к нему.</summary>
    public static async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsTrustedDownload(info.InstallerUrl))
        {
            throw new InvalidOperationException("Небезопасный адрес загрузки обновления.");
        }

        var directory = Path.Combine(AppStorage.Folder, "updates");
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, $"{InstallerPrefix}{info.Version}{InstallerExtension}");
        var partial = target + ".part";

        using var response = await Http
            .GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? info.InstallerSize;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(partial))
        {
            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                if (total > 0)
                {
                    progress?.Report(Math.Clamp((double)copied / total, 0, 1));
                }
            }
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Запускает установщик в тихом режиме. Inno сам закроет приложение и
    /// перезапустит его после установки. Область установки сохраняется.
    /// </summary>
    public static bool LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            return false;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath)
        {
            Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART" + GetInstallScopeArgument(),
            UseShellExecute = true,
        });

        return true;
    }

    /// <summary>
    /// Область установки: явный флаг нужен только когда она известна.
    /// Если неизвестна — флаг не передаём, и Inno повторяет область предыдущей
    /// установки (UsePreviousPrivileges=yes).
    /// </summary>
    private static string GetInstallScopeArgument()
    {
        try
        {
            var marker = Path.Combine(AppContext.BaseDirectory, "install-scope.txt");
            if (File.Exists(marker))
            {
                var scope = File.ReadAllText(marker).Trim();
                return scope.Equals("all", StringComparison.OrdinalIgnoreCase) ? " /ALLUSERS" : " /CURRENTUSER";
            }

            // Старые установки без маркера: Program Files на любом диске значит
            // установку для всех пользователей (путь мог быть выбран вручную).
            return AppContext.BaseDirectory.Contains(@"\Program Files", StringComparison.OrdinalIgnoreCase)
                ? " /ALLUSERS"
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsInstallerAsset(GitHubReleaseAsset asset)
    {
        return !string.IsNullOrWhiteSpace(asset.Name)
            && asset.Name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
            && asset.Name.EndsWith(InstallerExtension, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl);
    }

    /// <summary>Скачиваем только с GitHub по HTTPS: установщик запускается от имени пользователя.</summary>
    private static bool IsTrustedDownload(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));
    }

    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var text = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var parsed))
        {
            return null;
        }

        // GitHub-тег может быть 1.8.22, а версия сборки — 1.8.22.0. Приводим к
        // четырём компонентам, иначе одинаковые версии сравнятся как разные.
        return new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("TempoKaraoke", AppInfo.CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public GitHubReleaseAsset[]? Assets { get; set; }
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
