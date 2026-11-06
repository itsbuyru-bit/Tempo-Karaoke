namespace TempoKaraoke.Security;

public enum PinRole
{
    Host,
    Admin,
}

/// <summary>
/// Result of a sign-in attempt. The PIN itself is never returned or logged.
/// <see cref="ConfigurationError"/> is true when a persistent PIN is enabled
/// but that role has no usable hash; the caller keeps the random PIN working
/// and shows a configuration warning in the settings.
/// </summary>
public readonly record struct PinVerification(
    bool Success,
    bool UsedPersistentPin,
    bool ConfigurationError);

/// <summary>Chooses between a stored persistent PIN and the per-session random PIN.</summary>
public static class PinAuthentication
{
    public static PinVerification Verify(
        SecuritySettings? settings,
        PinRole role,
        string? pin,
        string randomHostPin,
        string randomAdminPin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            return new PinVerification(false, false, false);
        }

        if (settings?.UsePersistentPin == true)
        {
            var record = role == PinRole.Host ? settings.HostPin : settings.AdminPin;
            if (record is { IsUsable: true })
            {
                return new PinVerification(PinHasher.Verify(pin, record), true, false);
            }

            // The recommended policy is to report the configuration error. The
            // app still accepts the random PIN shown on screen so the host is not
            // locked out; the settings page explains what to fix.
            var random = role == PinRole.Host ? randomHostPin : randomAdminPin;
            return new PinVerification(PinHasher.FixedTimeEquals(pin, random), false, true);
        }

        var expected = role == PinRole.Host ? randomHostPin : randomAdminPin;
        return new PinVerification(PinHasher.FixedTimeEquals(pin, expected), false, false);
    }

    public static bool RoleHasPersistentPin(SecuritySettings? settings, PinRole role)
    {
        if (settings?.UsePersistentPin != true)
        {
            return false;
        }

        var record = role == PinRole.Host ? settings.HostPin : settings.AdminPin;
        return record is { IsUsable: true };
    }

    public static IReadOnlyList<PinRole> RolesMissingPersistentPin(SecuritySettings? settings)
    {
        if (settings?.UsePersistentPin != true)
        {
            return Array.Empty<PinRole>();
        }

        var missing = new List<PinRole>();
        if (settings.HostPin is not { IsUsable: true })
        {
            missing.Add(PinRole.Host);
        }

        if (settings.AdminPin is not { IsUsable: true })
        {
            missing.Add(PinRole.Admin);
        }

        return missing;
    }
}
