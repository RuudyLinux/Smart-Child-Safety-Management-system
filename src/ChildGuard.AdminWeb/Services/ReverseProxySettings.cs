using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// Same rules as the API's ReverseProxySettings (kept separate: AdminWeb does not reference the API).
/// Nothing configured means forwarded headers are ignored. <c>TrustAll</c> is for platforms such as Render
/// where the proxy address is not fixed and the app is reachable only through it. <c>ForwardLimit</c>
/// (default 1) reads one hop, so a client-supplied X-Forwarded-For further left is not believed.
/// Without this, behind a TLS-terminating proxy the app sees plain http and issues non-Secure cookies.
/// </summary>
public sealed record ReverseProxySettings(string[] KnownProxies, string[] KnownNetworks, bool TrustAll, int ForwardLimit)
{
    public bool Enabled => TrustAll || KnownProxies.Length > 0 || KnownNetworks.Length > 0;

    public static ReverseProxySettings From(IConfiguration config)
    {
        var section = config.GetSection("ReverseProxy");
        return new ReverseProxySettings(
            section.GetSection("KnownProxies").Get<string[]>() ?? [],
            section.GetSection("KnownNetworks").Get<string[]>() ?? [],
            section.GetValue<bool>("TrustAll"),
            Math.Max(1, section.GetValue("ForwardLimit", 1)));
    }

    public void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        if (TrustAll) return;
        foreach (var proxy in KnownProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
        foreach (var network in KnownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
}
