using ChildGuard.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>How the API learns the real client address behind a proxy (no database needed).</summary>
public class ReverseProxySettingsTests
{
    private static ReverseProxySettings From(params (string Key, string Value)[] values) =>
        ReverseProxySettings.From(new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build());

    [Fact]
    public void NothingConfigured_ForwardedHeadersAreIgnored()
    {
        var settings = From();
        Assert.False(settings.Enabled);
    }

    [Fact]
    public void ListedProxiesAndNetworks_AreTheOnlyTrustedHops()
    {
        var settings = From(
            ("ReverseProxy:KnownProxies:0", "10.0.0.5"),
            ("ReverseProxy:KnownNetworks:0", "10.1.0.0/16"));
        var options = new ForwardedHeadersOptions();

        settings.Apply(options);

        Assert.True(settings.Enabled);
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Single(options.KnownProxies, p => p.ToString() == "10.0.0.5");
        Assert.Single(options.KnownIPNetworks);
    }

    [Fact]
    public void TrustAll_AcceptsAnyProxy_ButStillForwardsOnlyOneHopByDefault()
    {
        // For a platform whose proxy addresses are not fixed and whose service is reachable only through it.
        var settings = From(("ReverseProxy:TrustAll", "true"));
        var options = new ForwardedHeadersOptions();

        settings.Apply(options);

        Assert.True(settings.Enabled);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        // One hop: the address the nearest proxy saw. A client-supplied X-Forwarded-For is not believed.
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void ForwardLimit_CanBeRaisedWhenSeveralProxiesSitInFront()
    {
        var settings = From(("ReverseProxy:TrustAll", "true"), ("ReverseProxy:ForwardLimit", "2"));
        var options = new ForwardedHeadersOptions();

        settings.Apply(options);

        Assert.Equal(2, options.ForwardLimit);
    }

    [Fact]
    public void AnInvalidProxyAddress_StopsStartup()
    {
        var settings = From(("ReverseProxy:KnownProxies:0", "not-an-ip"));
        Assert.Throws<FormatException>(() => settings.Apply(new ForwardedHeadersOptions()));
    }
}
