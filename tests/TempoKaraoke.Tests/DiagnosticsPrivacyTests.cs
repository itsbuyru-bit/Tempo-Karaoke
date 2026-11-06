using TempoKaraoke.Diagnostics;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class DiagnosticsPrivacyTests
{
    [Fact]
    public void SsidIsPartiallyMasked()
    {
        var masked = PrivacyMasking.MaskSsid("HomeNetwork");

        Assert.StartsWith("H", masked);
        Assert.DoesNotContain("omeNetwork", masked);
        Assert.Equal("<none>", PrivacyMasking.MaskSsid(null));
    }

    [Fact]
    public void MacAddressKeepsOnlyVendorAndLastOctet()
    {
        Assert.Equal("AA:BB:**:**:**:FF", PrivacyMasking.MaskMac("AA:BB:CC:DD:EE:FF"));
        Assert.Equal("**:**:**:**:**:**", PrivacyMasking.MaskMac("garbage"));
    }

    [Fact]
    public void MacAddressesInsideTextAreMasked()
    {
        var masked = PrivacyMasking.MaskMacAddresses("adapter AA-BB-CC-DD-EE-FF connected");

        Assert.Equal("adapter AA:BB:**:**:**:FF connected", masked);
    }

    [Fact]
    public void MediaPathsAndPinsAreNeverExposed()
    {
        Assert.Equal("<path hidden>", PrivacyMasking.MaskPath(@"G:\Karaoke\Song.mkv"));
        Assert.Equal("<none>", PrivacyMasking.MaskPath(null));
        Assert.Equal("<pin hidden>", PrivacyMasking.MaskPin("1234"));
        Assert.Equal(string.Empty, PrivacyMasking.MaskPin(null));
    }
}
