using System.Text.Json;
using TempoKaraoke.Security;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class PinSecurityTests
{
    private const string PlainPin = "48291573";

    [Fact]
    public void CorrectPinPassesAndWrongPinFails()
    {
        var record = PinHasher.Hash(PlainPin);

        Assert.True(PinHasher.Verify(PlainPin, record));
        Assert.False(PinHasher.Verify("48291574", record));
        Assert.False(PinHasher.Verify(string.Empty, record));
        Assert.False(PinHasher.Verify(null, record));
    }

    [Fact]
    public void HashUsesPbkdf2AndUniqueSalts()
    {
        var first = PinHasher.Hash(PlainPin);
        var second = PinHasher.Hash(PlainPin);

        Assert.Equal("PBKDF2-SHA256", first.Algorithm);
        Assert.Equal(PinHasher.DefaultIterations, first.Iterations);
        Assert.False(string.IsNullOrWhiteSpace(first.Salt));
        Assert.False(string.IsNullOrWhiteSpace(first.Hash));
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void PlaintextPinIsNotStoredInSettingsJson()
    {
        var settings = new SecuritySettings
        {
            UsePersistentPin = true,
            HostPin = PinHasher.Hash(PlainPin),
            AdminPin = PinHasher.Hash("73615482"),
        };

        var json = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain(PlainPin, json, StringComparison.Ordinal);
        Assert.DoesNotContain("73615482", json, StringComparison.Ordinal);
        Assert.Contains("PBKDF2-SHA256", json, StringComparison.Ordinal);
        Assert.Contains("Salt", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Iterations", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PinPolicyRequiresFourToEightDigits()
    {
        Assert.True(PinPolicy.Validate("1234").IsValid);
        Assert.True(PinPolicy.Validate("12345678").IsValid);
        Assert.False(PinPolicy.Validate("123").IsValid);
        Assert.False(PinPolicy.Validate("123456789").IsValid);
        Assert.False(PinPolicy.Validate("12ab").IsValid);
        Assert.False(PinPolicy.Validate("12 34").IsValid);
        Assert.False(PinPolicy.Validate(null).IsValid);
        Assert.True(PinPolicy.IsCommon("1234"));
        Assert.False(PinPolicy.IsCommon("73615482"));
    }

    [Fact]
    public void PersistentPinIsUsedWhenEnabled()
    {
        var settings = new SecuritySettings
        {
            UsePersistentPin = true,
            HostPin = PinHasher.Hash("73615482"),
            AdminPin = PinHasher.Hash("29183746"),
        };

        var host = PinAuthentication.Verify(settings, PinRole.Host, "73615482", "1111", "2222");
        var admin = PinAuthentication.Verify(settings, PinRole.Admin, "29183746", "1111", "2222");

        Assert.True(host.Success);
        Assert.True(host.UsedPersistentPin);
        Assert.True(admin.Success);
        Assert.True(admin.UsedPersistentPin);
        Assert.False(host.ConfigurationError);
    }

    [Fact]
    public void DisabledPersistentPinUsesTheRandomValue()
    {
        var settings = new SecuritySettings
        {
            UsePersistentPin = false,
            HostPin = PinHasher.Hash("73615482"),
        };

        Assert.True(PinAuthentication.Verify(settings, PinRole.Host, "1111", "1111", "2222").Success);
        Assert.False(PinAuthentication.Verify(settings, PinRole.Host, "73615482", "1111", "2222").Success);
    }

    [Fact]
    public void MissingRoleRecordFallsBackToRandomAndReportsConfigurationError()
    {
        var settings = new SecuritySettings
        {
            UsePersistentPin = true,
            HostPin = PinHasher.Hash("73615482"),
        };

        var missingAdmin = PinAuthentication.Verify(settings, PinRole.Admin, "2222", "1111", "2222");

        Assert.True(missingAdmin.Success);
        Assert.False(missingAdmin.UsedPersistentPin);
        Assert.True(missingAdmin.ConfigurationError);
        Assert.Equal(new[] { PinRole.Admin }, PinAuthentication.RolesMissingPersistentPin(settings));
    }

    [Fact]
    public void VerificationNeverComparesAgainstAnEmptyConfiguration()
    {
        var verification = PinAuthentication.Verify(
            new SecuritySettings { UsePersistentPin = true },
            PinRole.Host,
            "1234",
            string.Empty,
            string.Empty);

        Assert.False(verification.Success);
        Assert.True(verification.ConfigurationError);
    }
}
