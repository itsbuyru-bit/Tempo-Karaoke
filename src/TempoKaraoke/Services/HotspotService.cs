using System.Threading;
using System.Threading.Tasks;

namespace TempoKaraoke.Services;

/// <summary>
/// Compatibility facade for the remote UI. All calls go through
/// <see cref="WiFiDirectCoordinator"/>, which serializes adapter operations and
/// applies the safe-mode pre-check.
/// </summary>
public static class HotspotService
{
    public static Task<HotspotResult> StartAsync(CancellationToken cancellationToken = default) =>
        WiFiDirectCoordinator.StartAsync(cancellationToken);

    public static Task StopAsync() => WiFiDirectCoordinator.StopAsync();
}
