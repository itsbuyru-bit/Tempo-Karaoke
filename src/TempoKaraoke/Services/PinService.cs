using System.Collections.Generic;
using TempoKaraoke.Security;

namespace TempoKaraoke.Services;

/// <summary>
/// Keeps the per-session random PINs and decides, per verification, whether a
/// persistent hash from settings should be used. Settings are read live, so
/// saving a new PIN applies without restarting the remote server.
/// </summary>
public sealed class PinService
{
    public const string PersistentPinMask = "••••";

    private readonly string _randomHostPin;
    private readonly string _randomAdminPin;

    public PinService(string randomHostPin, string randomAdminPin)
    {
        _randomHostPin = randomHostPin;
        _randomAdminPin = randomAdminPin;
    }

    public string RandomHostPin => _randomHostPin;

    public string RandomAdminPin => _randomAdminPin;

    public string DisplayHostPin => Display(PinRole.Host);

    public string DisplayAdminPin => Display(PinRole.Admin);

    public bool HostUsesPersistentPin =>
        PinAuthentication.RoleHasPersistentPin(SettingsService.GetSecurity(), PinRole.Host);

    public bool AdminUsesPersistentPin =>
        PinAuthentication.RoleHasPersistentPin(SettingsService.GetSecurity(), PinRole.Admin);

    public PinVerification Verify(PinRole role, string? pin) =>
        PinAuthentication.Verify(
            SettingsService.GetSecurity(),
            role,
            pin,
            _randomHostPin,
            _randomAdminPin);

    public IReadOnlyList<PinRole> MissingPersistentRoles() =>
        PinAuthentication.RolesMissingPersistentPin(SettingsService.GetSecurity());

    private string Display(PinRole role)
    {
        var usesPersistent = PinAuthentication.RoleHasPersistentPin(SettingsService.GetSecurity(), role);
        if (usesPersistent)
        {
            return PersistentPinMask;
        }

        return role == PinRole.Host ? _randomHostPin : _randomAdminPin;
    }
}
