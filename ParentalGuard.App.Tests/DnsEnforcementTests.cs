using ParentalGuard.Service;

namespace ParentalGuard.App.Tests;

public class DnsEnforcementOptionsTests
{
    [Fact]
    public void OpenDns_Preset_HasCorrectPrimaryDns()
    {
        var options = DnsEnforcementOptions.OpenDns;

        Assert.Equal("208.67.222.222", options.PrimaryDns);
        Assert.Equal("208.67.220.220", options.SecondaryDns);
    }

    [Fact]
    public void OpenDnsFamilyShield_Preset_HasCorrectDnsServers()
    {
        var options = DnsEnforcementOptions.OpenDnsFamilyShield;

        Assert.Equal("208.67.222.123", options.PrimaryDns);
        Assert.Equal("208.67.220.123", options.SecondaryDns);
    }

    [Fact]
    public void CloudFlareFamily_Preset_HasCorrectDnsServers()
    {
        var options = DnsEnforcementOptions.CloudFlareFamily;

        Assert.Equal("1.1.1.3", options.PrimaryDns);
        Assert.Equal("1.0.0.3", options.SecondaryDns);
    }

    [Fact]
    public void CleanBrowsingFamily_Preset_HasCorrectDnsServers()
    {
        var options = DnsEnforcementOptions.CleanBrowsingFamily;

        Assert.Equal("185.228.168.168", options.PrimaryDns);
        Assert.Equal("185.228.169.168", options.SecondaryDns);
    }

    [Fact]
    public void CustomOptions_AllowsNullSecondaryDns()
    {
        var options = new DnsEnforcementOptions("1.2.3.4", null);

        Assert.Equal("1.2.3.4", options.PrimaryDns);
        Assert.Null(options.SecondaryDns);
    }
}

public class DnsEnforcementResultTests
{
    [Fact]
    public void AllSucceeded_IsTrue_WhenAllAdaptersUpdated()
    {
        var result = new DnsEnforcementResult(3, 3, true);

        Assert.True(result.AllSucceeded);
        Assert.Equal(3, result.AdaptersUpdated);
        Assert.Equal(3, result.TotalAdapters);
    }

    [Fact]
    public void AllSucceeded_IsFalse_WhenSomeAdaptersFailed()
    {
        var result = new DnsEnforcementResult(2, 3, false);

        Assert.False(result.AllSucceeded);
    }
}

public class DnsBypassResultTests
{
    [Fact]
    public void AllSucceeded_IsTrue_WhenAllRulesApplied()
    {
        var result = new DnsBypassResult(6, 6, true);

        Assert.True(result.AllSucceeded);
        Assert.Equal(6, result.RulesApplied);
    }
}
