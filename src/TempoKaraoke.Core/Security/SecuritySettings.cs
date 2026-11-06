namespace TempoKaraoke.Security;

/// <summary>Security block persisted inside settings.json.</summary>
public sealed class SecuritySettings
{
    public bool UsePersistentPin { get; set; }

    public PinHashRecord? HostPin { get; set; }

    public PinHashRecord? AdminPin { get; set; }

    public SecuritySettings Clone() => new()
    {
        UsePersistentPin = UsePersistentPin,
        HostPin = HostPin?.Clone(),
        AdminPin = AdminPin?.Clone(),
    };
}
