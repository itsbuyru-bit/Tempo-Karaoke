using System.Threading.Tasks;

namespace TempoKaraoke.Services;

/// <summary>
/// Compatibility facade for the remote UI. The actual autonomous group is
/// owned by the isolated Wi-Fi Direct helper process, not by the Windows
/// mobile-hotspot API.
/// </summary>
public static class HotspotService
{
    public static Task<HotspotResult> StartAsync() => WiFiDirectService.StartAsync();

    public static Task StopAsync() => WiFiDirectService.StopAsync();
}
